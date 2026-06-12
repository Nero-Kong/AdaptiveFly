// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#if d_UnityURP
#if UNITY_6000_0_OR_NEWER

using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace WaveHarmonic.Crest
{
    partial class UnderwaterEffectPassURP
    {
        readonly RenderGraphHelper.PassData _PassData = new();

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
        {
            using (var builder = graph.AddUnsafePass<RenderGraphHelper.PassData>(k_Name, out var data))
            {
                data.Init(frame, builder);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc<RenderGraphHelper.PassData>((data, context) =>
                {
                    var buffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    OnSetup(buffer, data);
                    Execute(context.GetRenderContext(), buffer, data);
                });
            }
        }

#if URP_COMPATIBILITY_MODE
        [System.Obsolete]
        public override void OnCameraSetup(CommandBuffer buffer, ref RenderingData data)
        {
            _PassData.Init(data.GetFrameData());
        }

        [System.Obsolete]
        public override void Execute(ScriptableRenderContext context, ref RenderingData data)
        {
            _PassData.Init(data.GetFrameData());
            var buffer = CommandBufferPool.Get(k_Name);
            OnSetup(buffer, _PassData);
            Execute(context, buffer, _PassData);
            context.ExecuteCommandBuffer(buffer);
            CommandBufferPool.Release(buffer);
        }
#endif
    }

    partial class CopyDepthBufferPassURP
    {
        class PassData
        {
#pragma warning disable IDE1006 // Naming Styles
            public UniversalCameraData cameraData;
            public RenderGraphHelper.Handle colorTargetHandle;
            public RenderGraphHelper.Handle depthTargetHandle;
#pragma warning restore IDE1006 // Naming Styles

            public void Init(ContextContainer frameData, IUnsafeRenderGraphBuilder builder = null)
            {
                var resources = frameData.Get<UniversalResourceData>();
                cameraData = frameData.Get<UniversalCameraData>();

#if URP_COMPATIBILITY_MODE
                if (builder == null)
                {
#pragma warning disable CS0618 // Type or member is obsolete
                    colorTargetHandle = cameraData.renderer.cameraColorTargetHandle;
                    depthTargetHandle = cameraData.renderer.cameraDepthTargetHandle;
#pragma warning restore CS0618 // Type or member is obsolete
                }
                else
#endif
                {
                    // We need reset render targets to these before the next pass, but we do not read
                    // or write to the color target.
                    colorTargetHandle = resources.activeColorTexture;
                    depthTargetHandle = resources.activeDepthTexture;
                    builder.UseTexture(depthTargetHandle, AccessFlags.ReadWrite);
                }
            }
        }

        readonly PassData _PassData = new();

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
        {
            using (var builder = graph.AddUnsafePass<PassData>(k_Name, out var data))
            {
                data.Init(frame, builder);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc<PassData>((data, context) =>
                {
                    var buffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    OnSetup(buffer, data);
                    Execute(context.GetRenderContext(), buffer, data);
                });
            }
        }

#if URP_COMPATIBILITY_MODE
        [System.Obsolete]
        public override void OnCameraSetup(CommandBuffer buffer, ref RenderingData data)
        {
            _PassData.Init(data.GetFrameData());
        }

        [System.Obsolete]
        public override void Execute(ScriptableRenderContext context, ref RenderingData data)
        {
            _PassData.Init(data.GetFrameData());
            var buffer = CommandBufferPool.Get(k_Name);
            OnSetup(buffer, _PassData);
            Execute(context, buffer, _PassData);
            context.ExecuteCommandBuffer(buffer);
            CommandBufferPool.Release(buffer);
        }
#endif
    }
}

#endif // UNITY_6000_0_OR_NEWER
#endif // d_UnityURP
