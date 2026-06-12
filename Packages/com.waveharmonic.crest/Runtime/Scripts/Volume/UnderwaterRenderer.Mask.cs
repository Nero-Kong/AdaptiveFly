// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;
using UnityEngine.Rendering;

namespace WaveHarmonic.Crest
{
    partial class UnderwaterRenderer : MaskRenderer.IMaskReceiver, MaskRenderer.IMaskProvider
    {
        internal const string k_DrawMask = "Crest.DrawMask";
        const string k_DrawMaskHorizon = "Horizon";
        const string k_DrawMaskSurface = "Surface";

        internal const int k_VolumeMaskQueue = 1000;

        internal const int k_ShaderPassWaterSurfaceMask = 0;
        internal const int k_ShaderPassWaterSurfaceDepth = 1;
        internal const int k_ShaderPassWaterHorizonMask = 0;

        internal const string k_ComputeShaderKernelFillMaskArtefacts = "FillMaskArtefacts";

        static partial class ShaderIDs
        {
            // Local
            public static readonly int s_FarPlaneOffset = Shader.PropertyToID("_Crest_FarPlaneOffset");
        }

        internal Material _MaskMaterial;
        internal Material _HorizonMaskMaterial;

        ComputeShader _ArtifactsShader;
        bool _ArtifactsShaderInitialized;
        int _ArtifactsKernel;
        uint _ArtifactsThreadGroupSizeX;
        uint _ArtifactsThreadGroupSizeY;

        internal void OnEnableMask()
        {
            _Water._Mask.Add(this);
            _Water._Mask.Add(k_VolumeMaskQueue, this);

            SetUpArtifactsShader();
        }

        internal void OnDisableMask()
        {
            if (_Water == null) return;
            _Water._Mask?.Remove(this as MaskRenderer.IMaskReceiver);
            _Water._Mask?.Remove(this as MaskRenderer.IMaskProvider);
        }

        internal void SetUpArtifactsShader()
        {
            if (_ArtifactsShaderInitialized)
            {
                return;
            }

            _ArtifactsKernel = _ArtifactsShader.FindKernel(k_ComputeShaderKernelFillMaskArtefacts);
            _ArtifactsShader.GetKernelThreadGroupSizes
            (
                _ArtifactsKernel,
                out _ArtifactsThreadGroupSizeX,
                out _ArtifactsThreadGroupSizeY,
                out _
            );

            _ArtifactsShaderInitialized = true;
        }

        void MaskRenderer.IMaskProvider.OnMaskPass(CommandBuffer commands, Camera camera, MaskRenderer mask)
        {
            var color = mask.ColorRTH;
            var depth = mask.DepthRTH;

            var size = color.GetScaledSize(color.rtHandleProperties.currentViewportSize);
            var descriptor = color.rt.descriptor;
            descriptor.width = size.x; descriptor.height = size.y;

            if (UseLegacyMask)
            {
                // Portals changes the target.
                // When using the stencil we are already clearing depth and do not want to clear the stencil too. Clear
                // color only when using the stencil as the horizon effectively clears it when not using it.
                CoreUtils.SetRenderTarget(commands, color, depth, UseStencilBuffer ? ClearFlag.Color : ClearFlag.DepthStencil);
                Helpers.ScaleViewport(camera, commands, color);

                PopulateMask(commands, camera);
                FixMaskArtefacts(commands, descriptor, mask._ColorRTI);
            }
            // Portals have their own fitted to the portal bounds.
            else if (RequiresFullScreenMask)
            {
                RenderLineMask(commands, camera, mask.ColorRT.descriptor, mask._ColorRTI);
            }
        }

        internal void RenderLineMask(CommandBuffer buffer, Camera camera, RenderTextureDescriptor descriptor, RenderTargetIdentifier target)
        {
            if (!_Water.Surface.Enabled)
            {
                return;
            }

            var keep = false;

#if d_CrestPortals
            if (_Water._ActiveModules.HasFlag(WaterRenderer.ActiveModules.Portal))
            {
                keep = _Water.Portals.RenderLineMask(buffer, target);
            }
#endif

            var wrapper = new PropertyWrapperCompute(buffer, WaterResources.Instance.Compute._Mask, (int)RenderPipelineHelper.RenderPipeline);

            var parameters = _Water.Surface._SurfaceDataParameters;

            wrapper.SetTexture(SurfaceRenderer.ShaderIDs.s_WaterLine, _Water.Surface.HeightRT);
            wrapper.SetVector(SurfaceRenderer.ShaderIDs.s_WaterLineSnappedPosition, parameters._SnappedPosition);
            wrapper.SetVector(SurfaceRenderer.ShaderIDs.s_WaterLineResolution, parameters._Resolution);
            wrapper.SetFloat(SurfaceRenderer.ShaderIDs.s_WaterLineTexel, parameters._Texel);

            // Only write if not written (ie zero).
            wrapper.SetKeyword(new(WaterResources.Instance.Compute._Mask, "d_KeepValue"), keep);

            // Setting this sets unity_CameraToWorld.
            wrapper.SetMatrix(Crest.ShaderIDs.Unity.s_CameraToWorld, camera.cameraToWorldMatrix);

            // Viewport sizes are not perfect so round up to cover.
            wrapper.Dispatch(Mathf.CeilToInt(descriptor.width / 8f), Mathf.CeilToInt(descriptor.height / 8f), descriptor.volumeDepth);
        }

        internal void FixMaskArtefacts(CommandBuffer buffer, RenderTextureDescriptor descriptor, RenderTargetIdentifier target)
        {
            if (_Debug._DisableArtifactCorrection)
            {
                return;
            }

            if (!_Water.Surface.Enabled && Portaled)
            {
                return;
            }

            // This will not be set automatically unless we use they RP variant approach
            // similar to Mask.compute.
            _ArtifactsShader.SetKeyword(new LocalKeyword(_ArtifactsShader, "STEREO_INSTANCING_ON"), descriptor.dimension == TextureDimension.Tex2DArray);

            buffer.SetComputeTextureParam(_ArtifactsShader, _ArtifactsKernel, MaskRenderer.ShaderIDs.s_WaterMaskTexture, target);

            buffer.DispatchCompute
            (
                _ArtifactsShader,
                _ArtifactsKernel,
                // Viewport sizes are not perfect so round up to cover.
                Mathf.CeilToInt((float)descriptor.width / _ArtifactsThreadGroupSizeX),
                Mathf.CeilToInt((float)descriptor.height / _ArtifactsThreadGroupSizeY),
                descriptor.volumeDepth
            );
        }

        // Populates a screen space mask which will inform the underwater postprocess. As a future optimisation we may
        // be able to avoid this pass completely if we can reuse the camera depth after transparents are rendered.
        internal void PopulateMask(CommandBuffer commandBuffer, Camera camera)
        {
            if (!_Water.Surface.Enabled && Portaled)
            {
                return;
            }

            // Render horizon into mask using a fullscreen triangle at the far plane. Horizon must be rendered first or
            // it will overwrite the mask with incorrect values.
            {
                var zBufferParameters = Helpers.GetZBufferParameters(camera);
                // Take 0-1 linear depth and convert non-linear depth.
                _HorizonMaskMaterial.SetFloat(ShaderIDs.s_FarPlaneOffset, Helpers.LinearDepthToNonLinear(_FarPlaneMultiplier, zBufferParameters));

                // Render fullscreen triangle with horizon mask pass.
                commandBuffer.BeginSample(k_DrawMaskHorizon);
                commandBuffer.DrawProcedural(Matrix4x4.identity, _HorizonMaskMaterial, shaderPass: k_ShaderPassWaterHorizonMask, MeshTopology.Triangles, 3, 1);
                commandBuffer.EndSample(k_DrawMaskHorizon);
            }

            // Get all water chunks and render them using cmd buffer, but with mask shader.
            if (!_Debug._DisableMask)
            {
                commandBuffer.BeginSample(k_DrawMaskSurface);
                _Water.Surface.Render(camera, commandBuffer, _MaskMaterial, k_ShaderPassWaterSurfaceMask);
                commandBuffer.EndSample(k_DrawMaskSurface);
            }
        }

        internal bool _MaskRead;

        MaskRenderer.MaskInput MaskRenderer.IMaskProvider.Allocate()
        {
            return UseLegacyMask || UseStencilBuffer ? MaskRenderer.MaskInput.Both : MaskRenderer.MaskInput.Color;
        }

        MaskRenderer.MaskInput MaskRenderer.IMaskReceiver.Allocate()
        {
            return UseLegacyMask || UseStencilBuffer ? MaskRenderer.MaskInput.Both : MaskRenderer.MaskInput.Color;
        }

        MaskRenderer.MaskInput MaskRenderer.IMaskProvider.Write(Camera camera)
        {
            _MaskRead = _Water._ActiveModules.HasFlag(WaterRenderer.ActiveModules.SurfaceAndVolume);

            // We only need the mask if rendering both surface and volume.
            return _MaskRead
                ? (UseLegacyMask || UseStencilBuffer ? MaskRenderer.MaskInput.Both : MaskRenderer.MaskInput.Color)
                : MaskRenderer.MaskInput.None;
        }
    }
}
