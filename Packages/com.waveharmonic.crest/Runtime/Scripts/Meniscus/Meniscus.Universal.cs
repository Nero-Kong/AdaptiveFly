// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#if d_UnityURP

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WaveHarmonic.Crest
{
    partial class Meniscus
    {
        internal sealed class MeniscusRendererURP : MeniscusRenderer
        {
            readonly MeniscusRenderPass _MaskRenderPass = new();

            public MeniscusRendererURP(WaterRenderer water, Meniscus meniscus) : base(water, meniscus)
            {

            }

            public override void OnBeginCameraRendering(Camera camera)
            {
                if (!ShouldExecute(camera))
                {
                    return;
                }

                _MaskRenderPass._Renderer = this;
                _MaskRenderPass.EnqueuePass(camera);
            }

            public override void OnEndCameraRendering(Camera camera)
            {

            }

            sealed partial class MeniscusRenderPass : ScriptableRenderPass
            {
                const string k_Name = k_Draw;

                internal MeniscusRenderer _Renderer;

                bool _RequiresOpaqueTexture;

                public MeniscusRenderPass()
                {
                    renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
                }

                internal void EnqueuePass(Camera camera)
                {
                    // TODO: check if we need to even enqueue a pass
                    var renderer = camera.GetUniversalAdditionalCameraData().scriptableRenderer;

#if UNITY_EDITOR
                    if (renderer == null)
                    {
                        return;
                    }
#endif

                    _RequiresOpaqueTexture = _Renderer._Meniscus.RequiresOpaqueTexture;

                    ConfigureInput(_RequiresOpaqueTexture ? ScriptableRenderPassInput.Color : ScriptableRenderPassInput.None);

                    // Enqueue the pass. This happens every frame.
                    renderer.EnqueuePass(this);
                }

#if UNITY_6000_0_OR_NEWER
                class PassData
                {
                    public UniversalCameraData _CameraData;
                    public MeniscusRenderer _Renderer;
                }

                public override void RecordRenderGraph(UnityEngine.Rendering.RenderGraphModule.RenderGraph graph, ContextContainer frame)
                {
                    using (var builder = graph.AddRasterRenderPass<PassData>(k_Name, out var data))
                    {
                        builder.AllowPassCulling(false);

                        var resources = frame.Get<UniversalResourceData>();

                        if (_RequiresOpaqueTexture)
                        {
                            builder.UseTexture(resources.cameraOpaqueTexture);
                        }

                        data._CameraData = frame.Get<UniversalCameraData>();
                        data._Renderer = _Renderer;

                        builder.SetRenderAttachment(resources.activeColorTexture, index: 0);

                        builder.SetRenderFunc<PassData>((data, context) =>
                        {
                            data._Renderer.Execute(data._CameraData.camera, new RasterCommandWrapper(context.cmd));
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
                    var buffer = CommandBufferPool.Get(k_Name);
                    _Renderer.Execute(data.cameraData.camera, new CommandWrapper(buffer));
                    context.ExecuteCommandBuffer(buffer);
                    CommandBufferPool.Release(buffer);
                }
#endif
            }
        }
    }
}

#endif
