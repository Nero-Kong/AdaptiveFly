// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace WaveHarmonic.Crest
{
    sealed class UnderwaterEffectPass
    {
        readonly UnderwaterRenderer _Renderer;

        RTHandle _ColorTexture;

        RTHandle _ColorTarget;
        RTHandle _DepthTarget;


        readonly System.Action<CommandBuffer> _CopyColorTexture;
        readonly System.Action<CommandBuffer> _SetRenderTargetToBackBuffers;

        bool _AllocatedColor;
        GraphicsFormat _GraphicsFormat;

        public UnderwaterEffectPass(UnderwaterRenderer renderer)
        {
            _Renderer = renderer;
            _CopyColorTexture = new(CopyColorTexture);
            _SetRenderTargetToBackBuffers = new(SetRenderTargetToBackBuffers);
        }

        void CopyColorTexture(CommandBuffer buffer)
        {
            Blitter.BlitCameraTexture(buffer, _ColorTarget, _ColorTexture);
            CoreUtils.SetRenderTarget(buffer, _ColorTarget, _DepthTarget, ClearFlag.None);
        }

        void SetRenderTargetToBackBuffers(CommandBuffer commands)
        {
            CoreUtils.SetRenderTarget(commands, _ColorTarget, _DepthTarget, ClearFlag.None);
        }

        public void Allocate(GraphicsFormat format)
        {
            _GraphicsFormat = format;

            if (_Renderer.RenderBeforeTransparency && !_Renderer.NeedsColorTexture)
            {
                return;
            }

            // TODO: There may other settings we want to set or bring in. Not MSAA since this is a resolved texture.
            _ColorTexture ??= RTHandles.Alloc
            (
                Vector2.one,
                TextureXR.slices,
                dimension: TextureXR.dimension,
                colorFormat: format,
                depthBufferBits: DepthBits.None,
                useDynamicScale: true,
                wrapMode: TextureWrapMode.Clamp,
                name: "_Crest_UnderwaterCameraColorTexture"
            );

            _AllocatedColor = true;
        }

        public void ReAllocate(RenderTextureDescriptor descriptor)
        {
            if (_Renderer.RenderBeforeTransparency && !_Renderer.NeedsColorTexture)
            {
                return;
            }

            // Descriptor will not have MSAA bound.
            RenderPipelineCompatibilityHelper.ReAllocateIfNeeded(ref _ColorTexture, descriptor, name: "_Crest_UnderwaterCameraColorTexture");
        }

        public void Release()
        {
            _ColorTexture?.Release();
            _ColorTexture = null;
        }

        public void Execute(Camera camera, CommandBuffer buffer, RTHandle color, RTHandle depth, MaterialPropertyBlock mpb = null)
        {
            _Renderer.UpdateEffectMaterial(camera);

            _ColorTarget = color;
            _DepthTarget = depth;

            if (!_Renderer.RenderBeforeTransparency || _Renderer.NeedsColorTexture)
            {
                if (!_AllocatedColor)
                {
                    Allocate(_GraphicsFormat);
                }

                buffer.SetGlobalTexture(UnderwaterRenderer.ShaderIDs.s_CameraColorTexture, _ColorTexture);
            }

            if (!_Renderer.RenderBeforeTransparency)
            {
                CopyColorTexture(buffer);
            }
            else
            {
                // TODO: needed for HDRP, but can set it on pass instead.
                CoreUtils.SetRenderTarget(buffer, _ColorTarget, _DepthTarget, ClearFlag.None);
            }

            _Renderer.ExecuteEffect(camera, buffer, _CopyColorTexture, _SetRenderTargetToBackBuffers, mpb);

            // The last pass (uber post) does not resolve the texture.
            // Although, this is wasteful if the pass after this does a resolve.
            // Possibly a bug with Unity?
            buffer.ResolveAntiAliasedSurface(color);
        }
    }
}
