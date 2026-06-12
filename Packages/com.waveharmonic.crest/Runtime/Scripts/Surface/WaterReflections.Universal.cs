// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#if d_UnityURP
#if UNITY_6000_0_OR_NEWER

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace WaveHarmonic.Crest
{
    partial class WaterReflections
    {
        CopyDepthRenderPass _CopyTargetsRenderPass;

        void CaptureTargetDepth(ScriptableRenderContext context, Camera camera)
        {
            if (camera != ReflectionCamera)
            {
                return;
            }

            if (!RenderPipelineHelper.IsUniversal)
            {
                return;
            }

#if URP_COMPATIBILITY_MODE
#if !UNITY_6000_4_OR_NEWER
            if (GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode)
            {
                return;
            }
#endif
#endif

            _CopyTargetsRenderPass ??= new(this);
            var renderer = camera.GetUniversalAdditionalCameraData().scriptableRenderer;
            renderer.EnqueuePass(_CopyTargetsRenderPass);
        }

        sealed class CopyDepthRenderPass : ScriptableRenderPass
        {
            readonly WaterReflections _Renderer;
            RTHandle _Wrapper;

            class CopyPassData
            {
                public TextureHandle _Source;
                public TextureHandle _Target;
                public int _Slice;
            }

            public CopyDepthRenderPass(WaterReflections renderer)
            {
                _Renderer = renderer;
                renderPassEvent = RenderPassEvent.AfterRendering;
            }

            public void Dispose()
            {
                _Wrapper?.Release();
                _Wrapper = null;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                var resources = frame.Get<UniversalResourceData>();

                var source = resources.cameraDepth;

                if (!source.IsValid())
                {
                    return;
                }

                // Create a wrapper. Does not appear to be anything heavy in here.
                _Wrapper ??= RTHandles.Alloc(_Renderer._DepthTexture);
                _Wrapper.SetRenderTexture(_Renderer._DepthTexture);

                var texture = graph.ImportTexture(_Wrapper);

                using var builder = graph.AddUnsafePass<CopyPassData>("Crest.CopyDepth", out var data);

                data._Source = source;
                data._Target = graph.ImportTexture(_Wrapper);
                data._Slice = _Renderer._ActiveSlice;

                builder.UseTexture(data._Source, AccessFlags.Read);
                builder.UseTexture(data._Target, AccessFlags.Write);

                // Unity's AddCopyPass cannot handle this it seems.
                builder.SetRenderFunc((CopyPassData data, UnsafeGraphContext context) =>
                {
                    RTHandle source = data._Source;
                    RTHandle target = data._Target;

                    // Just in case. Planar Reflections will work mostly without it.
                    if (source.rt == null)
                    {
                        return;
                    }

                    if (source.rt.graphicsFormat == target.rt.graphicsFormat && source.rt.depthStencilFormat == target.rt.depthStencilFormat)
                    {
                        context.cmd.m_WrappedCommandBuffer.CopyTexture(source.rt, 0, 0, target.rt, data._Slice, 0);
                    }
                });
            }
        }
    }
}

#endif
#endif
