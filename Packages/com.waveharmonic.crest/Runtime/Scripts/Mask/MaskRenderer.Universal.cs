// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#if d_UnityURP

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WaveHarmonic.Crest
{
    sealed class MaskRendererURP : MaskRenderer
    {
        readonly MaskRenderPass _MaskRenderPass = new();

        public MaskRendererURP(WaterRenderer water) : base(water) { }

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

        sealed partial class MaskRenderPass : ScriptableRenderPass
        {
            const string k_Name = "Crest.DrawMask";

            internal MaskRenderer _Renderer;

            public MaskRenderPass()
            {
                // Will always execute and matrices will be ready.
                renderPassEvent = RenderPassEvent.BeforeRenderingPrePasses;
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

                _Renderer.Allocate();

                // Enqueue the pass. This happens every frame.
                renderer.EnqueuePass(this);
            }

#if UNITY_6000_0_OR_NEWER
            class PassData
            {
                public UniversalCameraData _CameraData;
                public MaskRenderer _Renderer;
            }

            public override void RecordRenderGraph(UnityEngine.Rendering.RenderGraphModule.RenderGraph graph, ContextContainer frame)
            {
                using (var builder = graph.AddUnsafePass<PassData>(k_Name, out var data))
                {
                    builder.AllowPassCulling(false);

                    data._CameraData = frame.Get<UniversalCameraData>();
                    data._Renderer = _Renderer;

                    builder.SetRenderFunc<PassData>((data, context) =>
                    {
                        var buffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        data._Renderer.ReAllocate(data._CameraData.cameraTargetDescriptor);
                        data._Renderer.Execute(data._CameraData.camera, buffer);
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
                _Renderer.ReAllocate(data.cameraData.cameraTargetDescriptor);
                _Renderer.Execute(data.cameraData.camera, buffer);
                context.ExecuteCommandBuffer(buffer);
                CommandBufferPool.Release(buffer);
            }
#endif
        }
    }
}

#endif // d_UnityURP
