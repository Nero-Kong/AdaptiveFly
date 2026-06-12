// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#if d_UnityHDRP

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace WaveHarmonic.Crest
{
    sealed class UnderwaterEffectPassHDRP : CustomPass
    {
        const string k_Name = "Underwater Effect";

        static UnderwaterRenderer s_Renderer;
        static UnderwaterEffectPass s_UnderwaterEffectPass;
        internal static UnderwaterEffectPassHDRP s_Instance;
        static CopyDepthBufferPassHDRP s_CopyDepthBufferPassHDRP;

        static ShaderTagId[] s_ForwardShaderTags;

        public static void Enable(UnderwaterRenderer renderer)
        {
            var gameObject = CustomPassHelpers.CreateOrUpdate
            (
                parent: renderer._Water.Container.transform,
                k_Name,
                hide: !renderer._Water._Debug._ShowHiddenObjects
            );

            CustomPassHelpers.CreateOrUpdate
            (
                gameObject,
                ref s_CopyDepthBufferPassHDRP,
                UnderwaterRenderer.k_DrawVolume,
                CustomPassInjectionPoint.AfterOpaqueDepthAndNormal
            );

            var isBeforeTransparentPass = renderer.RenderBeforeTransparency;

            CustomPassHelpers.CreateOrUpdate
            (
                gameObject,
                ref s_Instance,
                UnderwaterRenderer.k_DrawVolume,
                GetInjectionPoint(isBeforeTransparentPass),
                // Higher number (priority) means execute earlier. Volume executes first.
                priority: 1
            );

            s_Renderer = renderer;
            s_UnderwaterEffectPass = new(renderer);
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

        static CustomPassInjectionPoint GetInjectionPoint(bool isBeforeTransparentPass)
        {
            return isBeforeTransparentPass
                ? CustomPassInjectionPoint.BeforeTransparent
                : CustomPassInjectionPoint.BeforePostProcess;
        }

        internal void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            s_CopyDepthBufferPassHDRP.enabled = s_Renderer.UseStencilBuffer;
            s_Instance._Volume.injectionPoint = GetInjectionPoint(s_Renderer.RenderBeforeTransparency);
        }

        protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            var asset = GraphicsSettings.currentRenderPipeline as HDRenderPipelineAsset;

            // Developers have a choice with the color buffer format. There is also a custom buffer buffer format but
            // that is not relevant here. This will not cover the format change when scene filtering as Setup/Cleanup is
            // not executed for this change.
            s_UnderwaterEffectPass.Allocate((GraphicsFormat)asset.currentPlatformRenderPipelineSettings.colorBufferFormat);

            // Taken from:
            // https://github.com/Unity-Technologies/Graphics/blob/778ddac6207ade1689999b95380cd835b0669f2d/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/DrawRenderersCustomPass.cs#L136-L142
            s_ForwardShaderTags ??= new[]
            {
                HDShaderPassNames.s_ForwardName,            // HD Lit shader
                HDShaderPassNames.s_ForwardOnlyName,        // HD Unlit shader
                HDShaderPassNames.s_SRPDefaultUnlitName,    // Cross SRP Unlit shader
            };
        }

        protected override void Cleanup()
        {
            s_UnderwaterEffectPass?.Release();
        }

        protected override void Execute(CustomPassContext context)
        {
            var camera = context.hdCamera.camera;

            if (!s_Renderer._Water._ActiveModules.HasFlag(WaterRenderer.ActiveModules.Volume))
            {
                return;
            }

            // Allocate here in case user changes options and we skipped allocation in Setup.
            s_UnderwaterEffectPass.Allocate(context.cameraColorBuffer.rt.graphicsFormat);

            // Create a separate stencil buffer context by using a depth buffer copy if needed.
            var depthBuffer = s_Renderer.UseStencilBuffer
                ? s_CopyDepthBufferPassHDRP._DepthBufferCopy
                : context.cameraDepthBuffer;

            s_UnderwaterEffectPass.Execute(camera, context.cmd, context.cameraColorBuffer, depthBuffer, context.propertyBlock);
        }
    }

    sealed class CopyDepthBufferPassHDRP : CustomPass
    {
        public RTHandle _DepthBufferCopy;

        protected override void Execute(CustomPassContext context)
        {
            // Multiple cameras could have different settings.
            RenderPipelineCompatibilityHelper.ReAllocateIfNeeded
            (
                ref _DepthBufferCopy,
                context.cameraDepthBuffer.rt.descriptor,
                FilterMode.Point,
                name: "_Crest_UnderwaterCopiedDepthBuffer"
            );

            var buffer = context.cmd;

            // NOTE: previously we cleared the target depth first due to artifacts.
            buffer.CopyTexture(context.cameraDepthBuffer.rt, _DepthBufferCopy.rt);

            // Clear the stencil component just in case.
            CoreUtils.SetRenderTarget(buffer, BuiltinRenderTextureType.None, _DepthBufferCopy, ClearFlag.Stencil);
        }

        protected override void Cleanup()
        {
            _DepthBufferCopy?.Release();
            _DepthBufferCopy = null;
        }
    }
}

#endif // d_UnityHDRP
