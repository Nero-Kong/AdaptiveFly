// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#if d_UnityURP

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WaveHarmonic.Crest
{
    sealed partial class UnderwaterEffectPassURP : ScriptableRenderPass
    {
        const string k_Name = "Crest.DrawWater/Volume";

        UnderwaterRenderer _Renderer;

        internal static UnderwaterEffectPassURP s_Instance;
        UnderwaterEffectPass _UnderwaterEffectPass;
        CopyDepthBufferPassURP _CopyDepthBufferPass;

        RTHandle _ColorBuffer;
        RTHandle _DepthBuffer;

        public UnderwaterEffectPassURP()
        {
            ConfigureInput(ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth);
        }

        public static void Enable(UnderwaterRenderer renderer)
        {
            if (s_Instance == null)
            {
                s_Instance = new();
                s_Instance._Renderer = renderer;
                s_Instance._CopyDepthBufferPass = new(RenderPassEvent.AfterRenderingOpaques);
            }

            RenderPipelineManager.activeRenderPipelineTypeChanged -= Disable;
            RenderPipelineManager.activeRenderPipelineTypeChanged += Disable;
        }

        public static void Disable()
        {
            RenderPipelineManager.activeRenderPipelineTypeChanged -= Disable;

            s_Instance?._UnderwaterEffectPass?.Release();
            s_Instance?._CopyDepthBufferPass?.Release();
            s_Instance = null;
        }

        internal void EnqueuePass(ScriptableRenderContext context, Camera camera)
        {
            if (!_Renderer._Water._ActiveModules.HasFlag(WaterRenderer.ActiveModules.Volume))
            {
                return;
            }

            s_Instance.renderPassEvent = _Renderer.RenderBeforeTransparency ? WaterRenderer.k_WaterRenderPassEvent : RenderPassEvent.AfterRenderingTransparents;

            var renderer = camera.GetUniversalAdditionalCameraData().scriptableRenderer;

#if UNITY_EDITOR
            if (renderer == null) return;
#endif

            // Copy the depth buffer to create a new depth/stencil context.
            if (_Renderer.UseStencilBuffer)
            {
                renderer.EnqueuePass(_CopyDepthBufferPass);
            }

            // Set up internal pass which houses shared code for SRPs.
            _UnderwaterEffectPass ??= new(_Renderer);

            renderer.EnqueuePass(s_Instance);
        }

#if UNITY_6000_0_OR_NEWER
        bool _ErrorMissingColorTarget;

        void OnSetup(CommandBuffer buffer, RenderGraphHelper.PassData data)
        {
            _ColorBuffer = data.colorTargetHandle.Texture;
            _DepthBuffer = data.depthTargetHandle.Texture;

            // Unity bug
            if (_ColorBuffer?.rt == null)
            {
                if (!_ErrorMissingColorTarget)
                {
                    Debug.LogError($"Crest: Your current URP setup has a Unity bug which prevents underwater from rendering on this camera ({data.cameraData.camera.name}). It is too complicated for us to advise which combination of settings are the issue (sorry), but they will be on either the URP asset or renderer file.");
                    _ErrorMissingColorTarget = true;
                }

                return;
            }

            // TODO: renderingData.cameraData.cameraTargetDescriptor?
            _UnderwaterEffectPass.ReAllocate(_ColorBuffer.rt.descriptor);
        }

        void Execute(ScriptableRenderContext context, CommandBuffer buffer, RenderGraphHelper.PassData data)
        {
            // Unity bug
            if (_ColorBuffer?.rt == null)
            {
                return;
            }

            if (_Renderer.UseStencilBuffer)
            {
                _DepthBuffer = _CopyDepthBufferPass._DepthBufferCopy;
            }

            _UnderwaterEffectPass.Execute(data.cameraData.camera, buffer, _ColorBuffer, _DepthBuffer);
        }
#else
        public override void OnCameraSetup(CommandBuffer buffer, ref RenderingData data)
        {
            _ColorBuffer = data.cameraData.renderer.cameraColorTargetHandle;
            _DepthBuffer = data.cameraData.renderer.cameraDepthTargetHandle;

            // TODO: renderingData.cameraData.cameraTargetDescriptor?
            _UnderwaterEffectPass.ReAllocate(_ColorBuffer.rt.descriptor);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData data)
        {
            var buffer = CommandBufferPool.Get(k_Name);

            if (_Renderer.UseStencilBuffer)
            {
                _DepthBuffer = _CopyDepthBufferPass._DepthBufferCopy;
            }

            _UnderwaterEffectPass.Execute(data.cameraData.camera, buffer, _ColorBuffer, _DepthBuffer);

            context.ExecuteCommandBuffer(buffer);
            CommandBufferPool.Release(buffer);
        }
#endif
    }

    // Copies the depth buffer to avoid conflicts when using the stencil buffer.
    sealed partial class CopyDepthBufferPassURP : ScriptableRenderPass
    {
        const string k_Name = "Crest Copy Depth Buffer";
        RTHandle _ColorBuffer;
        RTHandle _DepthBuffer;
        public RTHandle _DepthBufferCopy;

        public CopyDepthBufferPassURP(RenderPassEvent @event)
        {
            renderPassEvent = @event;
        }

#if UNITY_6000_0_OR_NEWER
        void OnSetup(CommandBuffer buffer, PassData data)
#else
        public override void OnCameraSetup(CommandBuffer buffer, ref RenderingData data)
#endif
        {
            var descriptor = data.cameraData.cameraTargetDescriptor;
            descriptor.graphicsFormat = GraphicsFormat.None;
            descriptor.bindMS = descriptor.msaaSamples > 1;
#if UNITY_6000_0_OR_NEWER
            RenderingUtils.ReAllocateHandleIfNeeded(ref _DepthBufferCopy, descriptor, FilterMode.Point, name: "Crest Copied Depth Buffer");
            _ColorBuffer = data.colorTargetHandle;
            _DepthBuffer = data.depthTargetHandle;
#else
            RenderingUtils.ReAllocateIfNeeded(ref _DepthBufferCopy, descriptor, FilterMode.Point, name: "Crest Copied Depth Buffer");
            _ColorBuffer = data.cameraData.renderer.cameraColorTargetHandle;
            _DepthBuffer = data.cameraData.renderer.cameraDepthTargetHandle;
#endif
        }

#if UNITY_6000_0_OR_NEWER
        void Execute(ScriptableRenderContext context, CommandBuffer buffer, PassData data)
#else
        public override void Execute(ScriptableRenderContext context, ref RenderingData data)
#endif
        {
            // Just in case.
            if (_ColorBuffer == null || _DepthBuffer == null)
            {
                return;
            }

#if !UNITY_6000_0_OR_NEWER
            var buffer = CommandBufferPool.Get(k_Name);
#endif

            // NOTE: previously we cleared the target depth first due to artifacts.
            buffer.CopyTexture(_DepthBuffer.rt, _DepthBufferCopy.rt);

            // Clear the stencil component just in case.
            // Previously we passed BuiltinRenderTextureType.None for color but this made the
            // scene disappear in the scene view on DX11 only.
            CoreUtils.SetRenderTarget(buffer, _ColorBuffer, _DepthBufferCopy, ClearFlag.Stencil);

            // Required for Unity 6+.
            CoreUtils.SetRenderTarget(buffer, _ColorBuffer, _DepthBuffer);

#if !UNITY_6000_0_OR_NEWER
            context.ExecuteCommandBuffer(buffer);
            CommandBufferPool.Release(buffer);
#endif
        }

        public void Release()
        {
            _DepthBuffer = null;
            _DepthBufferCopy?.Release();
        }
    }
}

#endif // d_UnityURP
