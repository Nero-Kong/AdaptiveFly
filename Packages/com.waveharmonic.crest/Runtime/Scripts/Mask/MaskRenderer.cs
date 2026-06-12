// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace WaveHarmonic.Crest
{
    abstract partial class MaskRenderer
    {
        protected const string k_MaskColor = "_Crest_MaskColor";
        protected const string k_MaskDepth = "_Crest_MaskDepth";

        public static class ShaderIDs
        {
            public static readonly int s_WaterMaskTexture = Shader.PropertyToID("_Crest_WaterMaskTexture");
            public static readonly int s_WaterMaskDepthTexture = Shader.PropertyToID("_Crest_WaterMaskDepthTexture");
        }

        public static MaskRenderer Instantiate(WaterRenderer water)
        {
#pragma warning disable format
#if d_UnityHDRP
            if (RenderPipelineHelper.IsHighDefinition)
            {
                return new MaskRendererHDRP(water);
            }
            else
#endif

#if d_UnityURP
            if (RenderPipelineHelper.IsUniversal)
            {
                return new MaskRendererURP(water);
            }
            else
#endif

            {
                return new MaskRendererBIRP(water);
            }
#pragma warning restore format
        }

        // For PortalRenderer.
        public static System.Action s_OnAllocate;
        public static System.Action s_OnRelease;
        public static System.Action<RenderTextureDescriptor> s_OnReAllocate;

        public MaskRenderer(WaterRenderer water)
        {
            _Water = water;
        }

        public bool Enabled => true; //_Water.Underwater.Enabled;

        internal RenderTargetIdentifier _ColorRTI;
        internal RenderTargetIdentifier _DepthRTI;

        public RenderTextureDescriptor ColorDescriptor => ColorRT.descriptor;
        public RenderTextureDescriptor DepthDescriptor => DepthRT.descriptor;

        public abstract void OnBeginCameraRendering(Camera camera);
        public abstract void OnEndCameraRendering(Camera camera);


        public virtual void Enable()
        {

        }

        public virtual void Disable()
        {

        }

        public virtual void Destroy()
        {
            Release();
        }

        protected void UpdateColor(Texture color)
        {
            _ColorRTI = new(color, mipLevel: 0, CubemapFace.Unknown, depthSlice: -1);
            Shader.SetGlobalTexture(ShaderIDs.s_WaterMaskTexture, color);
        }

        protected void UpdateDepth(Texture depth)
        {
            _DepthRTI = new(depth, mipLevel: 0, CubemapFace.Unknown, depthSlice: -1);
            Shader.SetGlobalTexture(ShaderIDs.s_WaterMaskDepthTexture, depth);
        }


        //
        // Pub/Sub
        //

        [System.Flags]
        public enum MaskInput
        {
            None,
            Zero = 1 << 0,
            Color = 1 << 1,

            /// <summary>
            /// Depth is used for the legacy mask or for stencil operations.
            /// </summary>
            Depth = 1 << 2,
            Both = Color | Depth,
        }

        protected MaskInput _Inputs;

        protected readonly WaterRenderer _Water;

        internal readonly Utility.SortedList<int, IMaskProvider> _Providers = new(Helpers.DuplicateComparison);
        internal readonly List<IMaskReceiver> _Receivers = new();

        public interface IMaskProvider
        {
            MaskInput Allocate();
            MaskInput Write(Camera camera);
            void OnMaskPass(CommandBuffer commands, Camera camera, MaskRenderer mask);
        }

        public interface IMaskReceiver
        {
            MaskInput Allocate();
        }

        void Initialize()
        {
            _Inputs = MaskInput.None;

            foreach (var receiver in _Receivers)
            {
                _Inputs |= receiver.Allocate();
            }
        }

        internal void Add(IMaskReceiver receiver)
        {
            if (_Receivers.Contains(receiver))
            {
                return;
            }

            _Receivers.Add(receiver);

            Initialize();
        }

        internal void Remove(IMaskReceiver receiver)
        {
            if (!_Receivers.Remove(receiver))
            {
                return;
            }

            Initialize();
        }

        internal void Add(int queue, IMaskProvider provider)
        {
            if (_Providers.Contains(provider))
            {
                return;
            }

            _Providers.Add(queue, provider);

            Initialize();
        }

        internal void Remove(IMaskProvider provider)
        {
            if (!_Providers.Remove(provider))
            {
                return;
            }

            Initialize();
        }

        public void Execute(Camera camera, CommandBuffer commands)
        {
            foreach (var provider in _Providers)
            {
                if (provider.Value.Write(camera) == MaskInput.None)
                {
                    continue;
                }

                provider.Value.OnMaskPass(commands, camera, this);
            }
        }

        internal bool ShouldExecute(Camera camera)
        {
            var input = MaskInput.None;

            foreach (var providers in _Providers)
            {
                input |= providers.Value.Write(camera);
            }

            return input != MaskInput.None;
        }
    }

    // Holds common stuff for SRPs
    abstract partial class MaskRenderer
    {
        internal RTHandle _ColorRTH;
        internal RTHandle _DepthRTH;


        // Null check due to U6 not being able to cast if null (Unity bug?).
        public Texture ColorT => _ColorRTH?.rt;
        public Texture DepthT => _DepthRTH?.rt;
        public RTHandle ColorRTH => _ColorRTH;
        public RTHandle DepthRTH => _DepthRTH;
        public RenderTexture ColorRT => _ColorRTH;
        public RenderTexture DepthRT => _DepthRTH;

        public void ResetRenderTarget(CommandBuffer commands)
        {
            CoreUtils.SetRenderTarget(commands, ColorRTH, DepthRTH);
        }

        public void Allocate()
        {
            if (_Inputs.HasFlag(MaskInput.Color) && _ColorRTH == null)
            {
                _ColorRTH = RTHandles.Alloc
                (
                    scaleFactor: Vector2.one,
                    slices: TextureXR.slices,
                    dimension: TextureXR.dimension,
                    depthBufferBits: DepthBits.None,
                    colorFormat: Helpers.GetCompatibleTextureFormat(GraphicsFormat.R16_SFloat, randomWrite: true),
                    enableRandomWrite: true,
                    useDynamicScale: true,
                    name: k_MaskColor
                );

                UpdateColor(_ColorRTH);
            }

            if (_Inputs.HasFlag(MaskInput.Depth) && _DepthRTH == null)
            {
                _DepthRTH = RTHandles.Alloc
                (
                    scaleFactor: Vector2.one,
                    slices: TextureXR.slices,
                    dimension: TextureXR.dimension,
                    depthBufferBits: Rendering.GetDefaultDepthBufferBits(),
                    colorFormat: GraphicsFormat.None,
                    enableRandomWrite: false,
                    useDynamicScale: true,
                    name: k_MaskDepth
                );

                UpdateDepth(_DepthRTH);
            }

            s_OnAllocate?.Invoke();
        }

        public void ReAllocate(RenderTextureDescriptor descriptor)
        {
            // Shared settings. Enabling MSAA might be a good idea except cannot enable random
            // writes. Having a raster shader to remove artifacts is a workaround.
            // This looks safe to do as Unity's CopyDepthPass does the same.
            descriptor.bindMS = false;
            descriptor.msaaSamples = 1;

            s_OnReAllocate?.Invoke(descriptor);

            if (_Inputs.HasFlag(MaskInput.Depth))
            {
                descriptor.graphicsFormat = GraphicsFormat.None;
                descriptor.depthBufferBits = (int)Rendering.GetDefaultDepthBufferBits();

                if (RenderPipelineCompatibilityHelper.ReAllocateIfNeeded(ref _DepthRTH, descriptor, name: k_MaskDepth))
                {
                    UpdateDepth(_DepthRTH);
                }
            }

            if (_Inputs.HasFlag(MaskInput.Color))
            {
                // NOTE: Intel iGPU for Metal and DirectX both had issues with R16 (2021.11.18).
                descriptor.graphicsFormat = Helpers.GetCompatibleTextureFormat(GraphicsFormat.R16_SFloat, randomWrite: true);
                descriptor.depthStencilFormat = GraphicsFormat.None;
                descriptor.depthBufferBits = 0;
                descriptor.enableRandomWrite = true;

                if (RenderPipelineCompatibilityHelper.ReAllocateIfNeeded(ref _ColorRTH, descriptor, name: k_MaskColor))
                {
                    UpdateColor(_ColorRTH);
                }
            }
        }

        public void Release()
        {
            _ColorRTH?.Release();
            _DepthRTH?.Release();

            // Set to null possibly due to Initialize/Destroy overlap.
            _ColorRTH = null;
            _DepthRTH = null;

            s_OnRelease?.Invoke();
        }
    }
}
