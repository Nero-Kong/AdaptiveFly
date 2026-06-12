// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#if d_UnityURP
#if UNITY_6000_0_OR_NEWER

using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace WaveHarmonic.Crest
{
    static class RenderGraphHelper
    {
        public struct Handle
        {
            RTHandle _RTHandle;
            TextureHandle _TextureHandle;

            public readonly RTHandle Texture { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _RTHandle ?? _TextureHandle; }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static implicit operator Handle(RTHandle handle) => new() { _RTHandle = handle };

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static implicit operator Handle(TextureHandle handle) => new() { _TextureHandle = handle };

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static implicit operator RTHandle(Handle texture) => texture.Texture;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static implicit operator TextureHandle(Handle texture) => texture._TextureHandle;
        }

        static readonly FieldInfo s_WrappedContext = typeof(UnsafeGraphContext).GetField("wrappedContext", BindingFlags.NonPublic | BindingFlags.Instance);

        public static ScriptableRenderContext GetRenderContext(this UnsafeGraphContext unsafeContext)
        {
            return ((InternalRenderGraphContext)s_WrappedContext.GetValue(unsafeContext)).renderContext;
        }

        public static ContextContainer GetFrameData(this ref RenderingData renderingData)
        {
            return renderingData.frameData;
        }

        internal class PassData
        {
#pragma warning disable IDE1006 // Naming Styles
            public UniversalCameraData cameraData;
            public UniversalRenderingData renderingData;
            public Handle colorTargetHandle;
            public Handle depthTargetHandle;
#pragma warning restore IDE1006 // Naming Styles

            public void Init(ContextContainer frameData, IUnsafeRenderGraphBuilder builder = null)
            {
                var resources = frameData.Get<UniversalResourceData>();
                cameraData = frameData.Get<UniversalCameraData>();
                renderingData = frameData.Get<UniversalRenderingData>();

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
                    colorTargetHandle = resources.activeColorTexture;
                    depthTargetHandle = resources.activeDepthTexture;
                    builder.UseTexture(colorTargetHandle, AccessFlags.ReadWrite);
                    builder.UseTexture(depthTargetHandle, AccessFlags.ReadWrite);
                }
            }
        }
    }
}

#endif // UNITY_6000_0_OR_NEWER
#endif // d_UnityURP
