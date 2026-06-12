// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#if UNITY_EDITOR

#if d_UnityURP

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WaveHarmonic.Crest
{
    partial class SurfaceRenderer
    {
        sealed class WaterLevelDepthTextureURP : ScriptableRenderPass
        {
            internal static WaterLevelDepthTextureURP s_Instance;
            WaterRenderer _Water;
            SurfaceRenderer _Surface;

            internal WaterLevelDepthTextureURP()
            {
                // Will always execute and matrices will be ready.
                renderPassEvent = RenderPassEvent.BeforeRenderingPrePasses;
            }

            internal static void Enable(WaterRenderer water, SurfaceRenderer surface)
            {
                s_Instance ??= new();
                s_Instance._Water = water;
                s_Instance._Surface = surface;
            }

            internal void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
            {
                if (Application.isPlaying)
                {
                    return;
                }

                if (camera.cameraType != CameraType.SceneView || (_Water.IsSingleViewpointMode && camera != _Water.Viewer))
                {
                    return;
                }

                if (!_Water._CurrentSceneCameraHasMouseHover)
                {
                    return;
                }

                // Enqueue the pass. This happens every frame.
                camera.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(this);
            }

#if UNITY_6000_0_OR_NEWER
            class PassData
            {
                public UniversalCameraData _CameraData;
                public SurfaceRenderer _Surface;
            }

            public override void RecordRenderGraph(UnityEngine.Rendering.RenderGraphModule.RenderGraph graph, ContextContainer frame)
            {
                using (var builder = graph.AddUnsafePass<PassData>(k_WaterLevelDepthTextureName, out var data))
                {
                    builder.AllowPassCulling(false);

                    data._CameraData = frame.Get<UniversalCameraData>();
                    data._Surface = _Surface;

                    builder.SetRenderFunc<PassData>((data, context) =>
                    {
                        var buffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        _Surface.ExecuteWaterLevelDepthTexture(data._CameraData.camera, buffer);
                    });
                }
            }
#endif

#if URP_COMPATIBILITY_MODE
#if UNITY_6000_0_OR_NEWER
            [System.Obsolete]
#endif
            public override void Execute(ScriptableRenderContext context, ref RenderingData data)
            {
                var buffer = CommandBufferPool.Get(k_WaterLevelDepthTextureName);
                _Surface.ExecuteWaterLevelDepthTexture(data.cameraData.camera, buffer);
                context.ExecuteCommandBuffer(buffer);
                CommandBufferPool.Release(buffer);
            }
#endif
        }
    }
}

#endif // d_UnityURP
#endif
