// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#if d_UnityHDRP

using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering.RendererUtils;

namespace WaveHarmonic.Crest
{
    partial class SurfaceRenderer
    {
        internal sealed class WaterSurfaceCustomPass : CustomPass
        {
            const string k_Name = "Water Surface";

            static WaterSurfaceCustomPass s_Instance;

            WaterRenderer _Water;

            // We disable the pass we want, so target another.
            ShaderTagId _ShaderTagID = new("DepthOnly");

            static readonly RenderTargetIdentifier[] s_RenderTargets = new RenderTargetIdentifier[2];

            public static void Enable(WaterRenderer renderer)
            {
                var gameObject = CustomPassHelpers.CreateOrUpdate
                (
                    parent: renderer.Container.transform,
                    k_Name,
                    hide: !renderer._Debug._ShowHiddenObjects
                );

                CustomPassHelpers.CreateOrUpdate
                (
                    gameObject,
                    ref s_Instance,
                    WaterRenderer.k_DrawWater,
                    CustomPassInjectionPoint.BeforeTransparent
                );

                s_Instance._Water = renderer;

                s_Instance.targetColorBuffer = TargetBuffer.Camera;
                s_Instance.targetDepthBuffer = TargetBuffer.Camera;
            }

            public static void Disable()
            {
                // It should be safe to rely on this reference for this reference to fail.
                if (s_Instance != null && s_Instance._GameObject != null)
                {
                    // Will also trigger Cleanup below.
                    s_Instance._GameObject.SetActive(false);
                }
            }

            protected override void Execute(CustomPassContext context)
            {
                var hdCamera = context.hdCamera;
                var camera = hdCamera.camera;

                if (!_Water._ActiveModules.HasFlag(WaterRenderer.ActiveModules.Surface))
                {
                    return;
                }

                if (hdCamera.msaaEnabled)
                {
                    WaterRenderer.s_CameraMSAA = true;
                    return;
                }

                var buffer = context.cmd;

                buffer.BeginSample(k_DrawWaterSurface);

                s_RenderTargets[0] = context.cameraColorBuffer;
                s_RenderTargets[1] = context.cameraMotionVectorsBuffer;

                CoreUtils.SetRenderTarget(buffer, s_RenderTargets, context.cameraDepthBuffer);

                var apv = FrameSettingsField.
#if UNITY_6000_0_OR_NEWER
                    AdaptiveProbeVolume;
#else
                    ProbeVolume;
#endif

                var rendererConfiguration = HDUtils.GetRendererConfiguration
                (
                    context.hdCamera.frameSettings.IsEnabled(apv),
                    context.hdCamera.frameSettings.IsEnabled(FrameSettingsField.Shadowmask)
                );

                if (hdCamera.frameSettings.IsEnabled(FrameSettingsField.MotionVectors))
                {
                    rendererConfiguration |= PerObjectData.MotionVectors;
                }

                var rld = new RendererListDesc(_ShaderTagID, context.cullingResults, camera)
                {
                    layerMask = 1 << _Water.Surface.Layer,
                    // Required to set the pass. Use shader to keep WB material overrides.
                    overrideShader = _Water.Surface.Material.shader,
                    overrideShaderPassIndex = _Water.Surface.Material.FindPass("Forward"),
                    renderQueueRange = RenderQueueRange.transparent,
                    sortingCriteria = SortingCriteria.CommonOpaque,
                    excludeObjectMotionVectors = false,
                    rendererConfiguration = rendererConfiguration,
                };

                buffer.DrawRendererList(context.renderContext.CreateRendererList(rld));

                buffer.EndSample(k_DrawWaterSurface);
            }
        }
    }
}

#endif
