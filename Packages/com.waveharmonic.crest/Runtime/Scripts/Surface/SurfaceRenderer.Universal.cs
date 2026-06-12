// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#if d_UnityURP

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.Universal;

namespace WaveHarmonic.Crest
{
    partial class SurfaceRenderer
    {
        internal sealed class WaterSurfaceRenderPass : ScriptableRenderPass
        {
            readonly WaterRenderer _Water;
            public static WaterSurfaceRenderPass Instance { get; set; }

            // We disable the pass we want, so target another.
            ShaderTagId _ShaderTagID = new("DepthOnly");

            public WaterSurfaceRenderPass(WaterRenderer water)
            {
                _Water = water;
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
                ConfigureInput(ScriptableRenderPassInput.None);
            }

            public static void Enable(WaterRenderer water)
            {
#if UNITY_EDITOR
                var data = water.Viewer != null ? water.Viewer.GetUniversalAdditionalCameraData() : null;

                // Type is internal.
                if (data != null && data.scriptableRenderer.GetType().Name == "Renderer2D")
                {
                    UnityEditor.EditorUtility.DisplayDialog
                    (
                        "Crest Error!",
                        "The project has been detected as a URP 2D project. Crest only supports 3D projects. " +
                        "You may see errors from Crest in the console, and other issues.",
                        "Ok"
                    );
                }
#endif

                Instance = new WaterSurfaceRenderPass(water);
            }

            internal void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
            {
                if (!IsTransparent(_Water.Surface.Material))
                {
                    return;
                }

                {
                    // Copy color happens between "after skybox" and "before transparency".
                    var pass = ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth;
#if d_Crest_SimpleTransparency
                    pass = ScriptableRenderPassInput.None;
#endif
                    ConfigureInput(pass);
                }

                camera.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(Instance);
            }

#if UNITY_6000_0_OR_NEWER
            class PassData
            {
                public UnityEngine.Rendering.RenderGraphModule.RendererListHandle _RendererList;
            }

            readonly RenderGraphHelper.PassData _PassData = new();

            public override void RecordRenderGraph(UnityEngine.Rendering.RenderGraphModule.RenderGraph graph, ContextContainer frame)
            {
                if (!_Water.RenderBeforeTransparency)
                {
                    return;
                }

                using (var builder = graph.AddRasterRenderPass<PassData>("Crest.DrawWater/Surface", out var data))
                {

                    var resourceData = frame.Get<UniversalResourceData>();
                    var cameraData = frame.Get<UniversalCameraData>();
                    var renderingData = frame.Get<UniversalRenderingData>();

                    // Make inputs show in RG viewer. We configure them already which makes them
                    // available, but that might change when Unity removes compatibility mode. If that
                    // happens, we also have to reconsider pass culling to ensure inputs are available
                    // when rendering to transparent pass.
                    builder.UseTexture(resourceData.cameraDepthTexture, UnityEngine.Rendering.RenderGraphModule.AccessFlags.Read);
                    builder.UseTexture(resourceData.cameraOpaqueTexture, UnityEngine.Rendering.RenderGraphModule.AccessFlags.Read);

                    // We do not want to use the back buffers, as it will prevent merging?
                    // This is recommended. Back buffers are used at end of frame typically.
                    builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                    builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture);

                    var rld = new RendererListDesc(_ShaderTagID, renderingData.cullResults, cameraData.camera)
                    {
                        layerMask = 1 << _Water.Surface.Layer,
                        // Required to set the pass. Use shader to keep WB material overrides.
                        overrideShader = _Water.Surface.Material.shader,
                        overrideShaderPassIndex = 0, // UniversalForward
                        renderQueueRange = RenderQueueRange.transparent,
                        sortingCriteria = SortingCriteria.CommonOpaque,
                        rendererConfiguration = renderingData.perObjectData,
                    };

                    data._RendererList = graph.CreateRendererList(rld);
                    builder.UseRendererList(data._RendererList);

                    builder.SetRenderFunc<PassData>((data, context) =>
                    {
                        context.cmd.DrawRendererList(data._RendererList);
                    });
                }
            }
#endif

#if URP_COMPATIBILITY_MODE
#if UNITY_6000_0_OR_NEWER
            [System.Obsolete]
#endif
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (!_Water.RenderBeforeTransparency)
                {
                    return;
                }

                var buffer = CommandBufferPool.Get("Crest.DrawWater/Surface");

                var rld = new RendererListDesc(_ShaderTagID, renderingData.cullResults, renderingData.cameraData.camera)
                {
                    layerMask = 1 << _Water.Surface.Layer,
                    // Required to set the pass. Use shader to keep WB material overrides.
                    overrideShader = _Water.Surface.Material.shader,
                    overrideShaderPassIndex = 0, // UniversalForward
                    renderQueueRange = RenderQueueRange.transparent,
                    sortingCriteria = SortingCriteria.CommonOpaque,
                    rendererConfiguration = renderingData.perObjectData,
                };

                buffer.DrawRendererList(context.CreateRendererList(rld));

                context.ExecuteCommandBuffer(buffer);
                CommandBufferPool.Release(buffer);
            }
#endif
        }
    }
}

#endif
