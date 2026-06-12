// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;
using UnityEngine.Rendering;

namespace WaveHarmonic.Crest
{
    // Built-in Render Pipeline
    partial class WaterRenderer
    {
        internal const string k_DrawWater = "Crest.DrawWater";
        internal const string k_DrawCopyColor = "CopyColor";
        internal const string k_DrawCopyDepth = "CopyDepth";

        partial class ShaderIDs
        {
            public static readonly int s_ScreenSpaceShadowTexture = Shader.PropertyToID("_Crest_ScreenSpaceShadowTexture");
            public static readonly int s_TemporaryDepthTexture = Shader.PropertyToID("_Crest_TemporaryDepthTexture");
            public static readonly int s_PrimaryLightHasCookie = Shader.PropertyToID("g_Crest_PrimaryLightHasCookie");
        }

        bool _DoneMatrices;

        CommandBuffer _ScreenSpaceShadowMapBuffer;
        CommandBuffer _UpdateColorDepthTexturesBuffer;

        internal Rendering.BIRP.FrameBufferFormatOverride FrameBufferFormatOverride =>
            !_OverrideRenderHDR
            ? Rendering.BIRP.FrameBufferFormatOverride.None
            : _RenderHDR
            ? Rendering.BIRP.FrameBufferFormatOverride.HDR
            : Rendering.BIRP.FrameBufferFormatOverride.LDR;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#endif
        static void InitializeOnLoad()
        {
            // Fixes error on first frame.
            Shader.SetGlobalTexture(Crest.ShaderIDs.Unity.s_ShadowMapTexture, Texture2D.whiteTexture);
        }

        internal void UpdateMatrices(Camera camera)
        {
            if (_DoneMatrices)
            {
                return;
            }

            Rendering.BIRP.SetMatrices(camera);

            _DoneMatrices = true;
        }

        void OnBeginCameraRenderingLegacy(Camera camera)
        {
            if (PrimaryLight == null)
            {
                return;
            }

            // Force shadow map to remain for transparent pass and beyond.
            {
                _ScreenSpaceShadowMapBuffer ??= new() { name = "Crest.CausticsOcclusion" };
                _ScreenSpaceShadowMapBuffer.Clear();

                // Make the screen-space shadow texture available for the water shader for caustic occlusion.
                _ScreenSpaceShadowMapBuffer.SetGlobalTexture(ShaderIDs.s_ScreenSpaceShadowTexture, BuiltinRenderTextureType.CurrentActive);
                PrimaryLight.AddCommandBuffer(LightEvent.AfterScreenspaceMask, _ScreenSpaceShadowMapBuffer);

                // Always set these in case shadow maps are disabled in the graphics settings which
                // we cannot check at runtime.
                // Black for shadowed. White for unshadowed.
                Shader.SetGlobalTexture(ShaderIDs.s_ScreenSpaceShadowTexture, Rendering.BIRP.GetWhiteTexture(camera));
            }

            Helpers.SetGlobalBoolean(ShaderIDs.s_PrimaryLightHasCookie, PrimaryLight.cookie != null);
        }

        void OnEndCameraRenderingLegacy(Camera camera)
        {
            _DoneMatrices = false;
            _DoneCameraOpaqueTexture = false;

            if (_UpdateColorDepthTexturesBuffer != null)
            {
                camera.RemoveCommandBuffer(RenderBeforeTransparency ? CameraEvent.BeforeForwardAlpha : CameraEvent.AfterForwardAlpha, _UpdateColorDepthTexturesBuffer);
            }

            if (QualitySettings.shadows != ShadowQuality.Disable && PrimaryLight != null)
            {
                if (_ScreenSpaceShadowMapBuffer != null)
                {
                    PrimaryLight.RemoveCommandBuffer(LightEvent.AfterScreenspaceMask, _ScreenSpaceShadowMapBuffer);
                }
            }

            Shader.SetGlobalTexture(Crest.ShaderIDs.Unity.s_CameraOpaqueTexture, Texture2D.grayTexture);
        }

        // Needs to be separate, as it needs to run after the water volume pass.
        [System.Diagnostics.Conditional(Constants.Symbols.k_Refraction)]
        void OnLegacyCopyPass(Camera camera)
        {
            if (!SurfaceRenderer.IsTransparent(Surface.Material))
            {
                return;
            }

            _UpdateColorDepthTexturesBuffer ??= new() { name = k_DrawWater };
            _UpdateColorDepthTexturesBuffer.Clear();

            var buffer = _UpdateColorDepthTexturesBuffer;

            if (WriteToColorTexture)
            {
                UpdateCameraOpaqueTexture(camera, buffer);
            }

            if (WriteToDepthTexture && Shader.GetGlobalTexture(Crest.ShaderIDs.Unity.s_CameraDepthTexture) is RenderTexture depthRT)
            {
                buffer.BeginSample(k_DrawCopyDepth);

                // There is no way to update the depth texture with the depth buffer, as we cannot
                // get a reference to it. We have to  render water depth separately and then merge
                // the results.

                var target = new RenderTargetIdentifier(depthRT, 0, CubemapFace.Unknown, -1);

                var id = ShaderIDs.s_TemporaryDepthTexture;

                buffer.GetTemporaryRT(id, depthRT.descriptor);
                CoreUtils.SetRenderTarget(buffer, id, ClearFlag.Depth);

                Surface.Render(camera, buffer, pass: Surface.Material.FindPass("DepthOnly"), culled: true);

                buffer.SetGlobalTexture(Helpers.ShaderIDs.s_MainTexture, id);

                Helpers.Blit(buffer, target, Rendering.BIRP.UtilityMaterial, (int)Rendering.BIRP.UtilityPass.MergeDepth);

                // TODO: add debug toggle
                // buffer.Blit(target, BuiltinRenderTextureType.CameraTarget);

                buffer.ReleaseTemporaryRT(id);

                buffer.EndSample(k_DrawCopyDepth);
            }

            if (WriteToColorTexture || WriteToDepthTexture)
            {
                camera.AddCommandBuffer(RenderBeforeTransparency ? CameraEvent.BeforeForwardAlpha : CameraEvent.AfterForwardAlpha, buffer);
            }
        }
    }

    // Camera Opaque Texture.
    partial class WaterRenderer
    {
        bool _DoneCameraOpaqueTexture;
        RTHandle _CameraOpaqueTexture;
        CommandBuffer _CameraOpaqueTextureCommands;

        internal void UpdateCameraOpaqueTexture(Camera camera, CommandBuffer commands)
        {
            commands.BeginSample(k_DrawCopyColor);

            var target = new RenderTargetIdentifier(_CameraOpaqueTexture, 0, CubemapFace.Unknown, -1);

            // Use blit instead of CopyTexture as it will smooth out issues with format
            // differences which is very hard to get right for BIRP.
            commands.Blit(BuiltinRenderTextureType.CameraTarget, target);

            commands.EndSample(k_DrawCopyColor);
        }

        [System.Diagnostics.Conditional(Constants.Symbols.k_Refraction)]
        internal void OnBeginCameraOpaqueTexture(Camera camera)
        {
            if (_DoneCameraOpaqueTexture)
            {
                return;
            }

            camera.depthTextureMode |= DepthTextureMode.Depth;

            var descriptor = Rendering.BIRP.GetCameraTargetDescriptor(camera, FrameBufferFormatOverride);

            // Required, or depth/stencil format will be chosen.
            descriptor.depthStencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.None;

            // Occurred in a build and caused a black screen.
            if (descriptor.width <= 0)
            {
                return;
            }

            RenderPipelineCompatibilityHelper.ReAllocateIfNeeded(ref _CameraOpaqueTexture, descriptor);

            _CameraOpaqueTextureCommands ??= new()
            {
                name = "Crest.DrawWater",
            };

            _CameraOpaqueTextureCommands.Clear();

            // Do every frame as we set to default texture at end of rendering.
            _CameraOpaqueTextureCommands.SetGlobalTexture(Crest.ShaderIDs.Unity.s_CameraOpaqueTexture, _CameraOpaqueTexture);

            UpdateCameraOpaqueTexture(camera, _CameraOpaqueTextureCommands);

            camera.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, _CameraOpaqueTextureCommands);

            _DoneCameraOpaqueTexture = true;
        }

        internal void OnEndCameraOpaqueTexture(Camera camera)
        {
            if (_CameraOpaqueTextureCommands != null)
            {
                camera.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, _CameraOpaqueTextureCommands);
            }
        }
    }
}
