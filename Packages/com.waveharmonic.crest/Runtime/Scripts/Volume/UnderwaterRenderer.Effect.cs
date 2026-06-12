// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;
using UnityEngine.Rendering;
using WaveHarmonic.Crest.Internal;

namespace WaveHarmonic.Crest
{
    partial class UnderwaterRenderer
    {
        internal const string k_ShaderNameEffect = "Crest/Underwater";
        internal const string k_DrawVolume = "Crest.DrawWater/Volume";
        const string k_KeywordDebugVisualizeMask = "_DEBUG_VISUALIZE_MASK";
        const string k_KeywordDebugVisualizeStencil = "_DEBUG_VISUALIZE_STENCIL";
        internal const string k_SampleSphericalHarmonicsMarker = "Crest.UnderwaterRenderer.SampleSphericalHarmonics";

        static readonly Unity.Profiling.ProfilerMarker s_SampleSphericalHarmonicsMarker = new(k_SampleSphericalHarmonicsMarker);

        static partial class ShaderIDs
        {
            // Global
            public static readonly int s_CameraColorTexture = Shader.PropertyToID("_Crest_CameraColorTexture");
            public static readonly int s_WaterVolumeStencil = Shader.PropertyToID("_Crest_WaterVolumeStencil");
            public static readonly int s_AmbientLighting = Shader.PropertyToID("_Crest_AmbientLighting");
            public static readonly int s_ExtinctionMultiplier = Shader.PropertyToID("_Crest_ExtinctionMultiplier");
            public static readonly int s_UnderwaterEnvironmentalLightingWeight = Shader.PropertyToID("_Crest_UnderwaterEnvironmentalLightingWeight");

            public static readonly int s_OutScatteringFactor = Shader.PropertyToID("_Crest_OutScatteringFactor");
            public static readonly int s_OutScatteringExtinctionFactor = Shader.PropertyToID("_Crest_OutScatteringExtinctionFactor");
            public static readonly int s_SunBoost = Shader.PropertyToID("_Crest_SunBoost");
            public static readonly int s_DataSliceOffset = Shader.PropertyToID("_Crest_DataSliceOffset");
        }


        // These map to passes in the underwater shader.
        internal enum EffectPass
        {
            FullScreen,
            Reflections,
        }

        CommandBuffer _EffectCommandBuffer;
        Material _CurrentWaterMaterial;
        readonly UnderwaterSphericalHarmonicsData _SphericalHarmonicsData = new();
        System.Action<CommandBuffer> _CopyColor;
        System.Action<CommandBuffer> _SetRenderTargetToBackBuffers;

        RenderTargetIdentifier _ColorTarget = new
        (
            BuiltinRenderTextureType.CameraTarget,
            0,
            CubemapFace.Unknown,
            -1
        );
        RenderTargetIdentifier _DepthStencilTarget = new
        (
            ShaderIDs.s_WaterVolumeStencil,
            0,
            CubemapFace.Unknown,
            -1
        );
        RenderTargetIdentifier _ColorCopyTarget = new
        (
            ShaderIDs.s_CameraColorTexture,
            0,
            CubemapFace.Unknown,
            -1
        );

        sealed class UnderwaterSphericalHarmonicsData
        {
            internal Color[] _AmbientLighting = new Color[1];
            internal Vector3[] _DirectionsSH = { new(0.0f, 0.0f, 0.0f) };
        }

        void SetRenderTargetToBackBuffers(CommandBuffer commands)
        {
            commands.SetRenderTarget(_ColorTarget);
        }

        void CopyColorTexture(CommandBuffer buffer)
        {
            // Use blit instead of CopyTexture as it will smooth out issues with format
            // differences which is very hard to get right for BIRP.
            buffer.Blit(BuiltinRenderTextureType.CameraTarget, _ColorCopyTarget);

            if (UseStencilBuffer)
            {
                _EffectCommandBuffer.SetRenderTarget(_ColorTarget, _DepthStencilTarget);
            }
            else
            {
                _EffectCommandBuffer.SetRenderTarget(_ColorTarget);
            }
        }

        void SetupUnderwaterEffect()
        {
            _EffectCommandBuffer ??= new()
            {
                name = k_DrawVolume,
            };

            _CopyColor ??= new(CopyColorTexture);
            _SetRenderTargetToBackBuffers ??= new(SetRenderTargetToBackBuffers);
        }

        void OnPreRenderUnderwaterEffect(Camera camera)
        {
            var descriptor = Rendering.BIRP.GetCameraTargetDescriptor(camera, _Water.FrameBufferFormatOverride);
            descriptor.useDynamicScale = camera.allowDynamicResolution;

            UpdateEffectMaterial(camera);

            _EffectCommandBuffer.Clear();

            if (!RenderBeforeTransparency || NeedsColorTexture)
            {
                // No need to clear as Blit will overwrite everything.
                _EffectCommandBuffer.GetTemporaryRT(ShaderIDs.s_CameraColorTexture, descriptor);
                _EffectCommandBuffer.SetGlobalTexture(ShaderIDs.s_CameraColorTexture, _ColorCopyTarget);
            }

            var sun = RenderSettings.sun;
            if (sun != null)
            {
                // Unity does not set up lighting for us so we will get the last value which could incorrect.
                // SetGlobalColor is just an alias for SetGlobalVector (no color space conversion like Material.SetColor):
                // https://docs.unity3d.com/2017.4/Documentation/ScriptReference/Shader.SetGlobalColor.html
                _EffectCommandBuffer.SetGlobalVector(Crest.ShaderIDs.Unity.s_LightColor0, sun.FinalColor());
                _EffectCommandBuffer.SetGlobalVector(Crest.ShaderIDs.Unity.s_WorldSpaceLightPos0, -sun.transform.forward);
                _EffectCommandBuffer.SetShaderKeyword("DIRECTIONAL_COOKIE", sun.cookie != null);
            }

            // Create a separate stencil buffer context by copying the depth texture.
            if (UseStencilBuffer)
            {
                descriptor.colorFormat = RenderTextureFormat.Depth;
                descriptor.depthBufferBits = (int)Rendering.GetDefaultDepthBufferBits();
                // bindMS is necessary in this case for depth.
                descriptor.SetMSAASamples(camera);
                descriptor.bindMS = descriptor.msaaSamples > 1;

                // No need to clear as Blit will overwrite everything.
                _EffectCommandBuffer.GetTemporaryRT(ShaderIDs.s_WaterVolumeStencil, descriptor);

                // Use blit for MSAA. We should be able to use CopyTexture. Might be the following bug:
                // https://issuetracker.unity3d.com/product/unity/issues/guid/1308132
                if (Helpers.IsMSAAEnabled(camera))
                {
                    // Blit with a depth write shader to populate the depth buffer.
                    Helpers.Blit(_EffectCommandBuffer, _DepthStencilTarget, Rendering.BIRP.UtilityMaterial, (int)Rendering.BIRP.UtilityPass.CopyDepth);
                }
                else
                {
                    // Copy depth texture. Since this is not depth buffer, no need to clear stencil.
                    // SRPs copy the depth buffer, because they can.
                    _EffectCommandBuffer.CopyTexture(BuiltinRenderTextureType.Depth, _DepthStencilTarget);
                    CoreUtils.SetRenderTarget(_EffectCommandBuffer, _DepthStencilTarget);
                }

                if (RenderBeforeTransparency)
                {
                    _EffectCommandBuffer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget, _DepthStencilTarget);
                }
            }

            if (!RenderBeforeTransparency)
            {
                CopyColorTexture(_EffectCommandBuffer);
            }

            ExecuteEffect(camera, _EffectCommandBuffer, _CopyColor, _SetRenderTargetToBackBuffers);

            if (!RenderBeforeTransparency || NeedsColorTexture)
            {
                _EffectCommandBuffer.ReleaseTemporaryRT(ShaderIDs.s_CameraColorTexture);
            }

            if (UseStencilBuffer)
            {
                _EffectCommandBuffer.ReleaseTemporaryRT(ShaderIDs.s_WaterVolumeStencil);
            }
        }

        internal void ExecuteEffect(Camera camera, CommandBuffer buffer, System.Action<CommandBuffer> copyColor, System.Action<CommandBuffer> resetRenderTargets, MaterialPropertyBlock properties = null)
        {
            var isFullScreenRequired = true;

#if d_CrestPortals
            if (_Water._ActiveModules.HasFlag(WaterRenderer.ActiveModules.Portal))
            {
                isFullScreenRequired = _Portals.RenderEffect(camera, buffer, _VolumeMaterial, copyColor, resetRenderTargets, properties);
            }
#endif

            if (!isFullScreenRequired)
            {
                return;
            }

            buffer.DrawProcedural
            (
                Matrix4x4.identity,
                _VolumeMaterial,
                shaderPass: (int)(camera.cameraType == CameraType.Reflection ? EffectPass.Reflections : EffectPass.FullScreen),
                MeshTopology.Triangles,
                vertexCount: 3,
                instanceCount: 1,
                properties
            );
        }

        internal static void UpdateGlobals(Material source)
        {
            // We will have the wrong color values if we do not use linear:
            // https://forum.unity.com/threads/fragment-shader-output-colour-has-incorrect-values-when-hardcoded.377657/

            // _CrestAbsorption is already set as global in Water Renderer.
            Shader.SetGlobalColor(WaterRenderer.ShaderIDs.s_Scattering, source.GetColor(WaterRenderer.ShaderIDs.s_Scattering).MaybeLinear());
            Shader.SetGlobalFloat(WaterRenderer.ShaderIDs.s_Anisotropy, source.GetFloat(WaterRenderer.ShaderIDs.s_Anisotropy));
            Shader.SetGlobalFloat(WaterRenderer.ShaderIDs.s_AmbientTerm, source.GetFloat(WaterRenderer.ShaderIDs.s_AmbientTerm));
            Shader.SetGlobalFloat(WaterRenderer.ShaderIDs.s_DirectTerm, source.GetFloat(WaterRenderer.ShaderIDs.s_DirectTerm));
            Shader.SetGlobalFloat(WaterRenderer.ShaderIDs.s_ShadowsAffectsAmbientFactor, source.GetFloat(WaterRenderer.ShaderIDs.s_ShadowsAffectsAmbientFactor));

            Shader.SetGlobalFloat(ShaderIDs.s_ExtinctionMultiplier, source.GetFloat(ShaderIDs.s_ExtinctionMultiplier));
            Shader.SetGlobalFloat(ShaderIDs.s_OutScatteringFactor, source.GetFloat(ShaderIDs.s_OutScatteringFactor));
            Shader.SetGlobalFloat(ShaderIDs.s_OutScatteringExtinctionFactor, source.GetFloat(ShaderIDs.s_OutScatteringExtinctionFactor));
            Shader.SetGlobalFloat(ShaderIDs.s_SunBoost, source.GetFloat(ShaderIDs.s_SunBoost));
            Shader.SetGlobalInteger(ShaderIDs.s_DataSliceOffset, source.GetInteger(ShaderIDs.s_DataSliceOffset));
        }

        internal void UpdateEffectMaterial(Camera camera)
        {
            // Copy water material parameters to underwater material.
            // WBs can change the material per camera, so disable optimization.
            if (_MaterialLastUpdatedFrame < Time.frameCount || WaterBody.WaterBodies.Count > 0)
            {
                if (_CopyWaterMaterialParametersEachFrame || _SurfaceMaterial != _CurrentWaterMaterial)
                {
                    _CurrentWaterMaterial = _SurfaceMaterial;

                    if (_SurfaceMaterial != null)
                    {
                        _VolumeMaterial.CopyMatchingPropertiesFromMaterial(_SurfaceMaterial);

                        AfterCopyMaterial?.Invoke(_Water, _VolumeMaterial);

                        // Make volume properties available to surface and meniscus.
                        if (RenderBeforeTransparency)
                        {
                            UpdateGlobals(_VolumeMaterial);
                        }
                    }
                }

                // Enabling/disabling keywords each frame don't seem to have large measurable overhead
                _VolumeMaterial.SetKeyword(k_KeywordDebugVisualizeMask, _Debug._VisualizeMask);
                _VolumeMaterial.SetKeyword(k_KeywordDebugVisualizeStencil, _Debug._VisualizeStencil);

                // We use this for caustics to get the displacement.
                _VolumeMaterial.SetInteger(Lod.ShaderIDs.s_LodIndex, 0);

                _MaterialLastUpdatedFrame = Time.frameCount;
            }

            // Not applicable to reflection pass.
            if (camera.cameraType != CameraType.Reflection)
            {
                // Skip work if camera is far enough below the surface.
                var forceFullShader = !_MaskRead || (_Water._PerCameraHeightReady && _Water._ViewerHeightAboveWaterPerCamera < -8f && !Portaled);
                _VolumeMaterial.SetKeyword("d_Crest_NoMaskColor", forceFullShader);
                _VolumeMaterial.SetKeyword("d_Crest_NoMaskDepth", !_MaskRead || RenderBeforeTransparency);
            }

            // Compute ambient lighting SH.
            {
                // We could pass in a renderer which would prime this lookup. However it doesnt make sense to use an existing render
                // at different position, as this would then thrash it and negate the priming functionality. We could create a dummy invis GO
                // with a dummy Renderer which might be enough, but this is hacky enough that we'll wait for it to become a problem
                // rather than add a pre-emptive hack.
                s_SampleSphericalHarmonicsMarker.Begin(_Water);
                LightProbes.GetInterpolatedProbe(camera.transform.position, null, out var sphericalHarmonicsL2);
                sphericalHarmonicsL2.Evaluate(_SphericalHarmonicsData._DirectionsSH, _SphericalHarmonicsData._AmbientLighting);
                Helpers.SetShaderVector(_VolumeMaterial, ShaderIDs.s_AmbientLighting, _SphericalHarmonicsData._AmbientLighting[0], RenderBeforeTransparency);
                s_SampleSphericalHarmonicsMarker.End();
            }
        }
    }
}
