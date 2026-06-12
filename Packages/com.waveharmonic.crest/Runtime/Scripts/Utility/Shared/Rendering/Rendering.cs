// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#if ENABLE_VR
#if UNITY_6000_5_OR_NEWER
#if d_UnityModuleXR
#define _XR_ENABLED
#endif
#else
#if d_UnityModuleVR
#define _XR_ENABLED
#endif
#endif
#endif

using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace WaveHarmonic.Crest
{
    static partial class Rendering
    {
        // Taken from Unity 6.5:
        // Packages/com.unity.render-pipelines.core/Runtime/Utilities/CoreUtils.cs

        /// <summary>
        /// Return the GraphicsFormat of DepthStencil RenderTarget preferred for the current platform.
        /// </summary>
        /// <returns>The GraphicsFormat of DepthStencil RenderTarget preferred for the current platform.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GraphicsFormat GetDefaultDepthStencilFormat()
        {
#if UNITY_SWITCH || UNITY_SWITCH2 || UNITY_EMBEDDED_LINUX || UNITY_QNX || UNITY_ANDROID
            return GraphicsFormat.D24_UNorm_S8_UInt;
#else
            return GraphicsFormat.D32_SFloat_S8_UInt;
#endif
        }

        // Taken from Unity 6.5:
        // Packages/com.unity.render-pipelines.core/Runtime/Utilities/CoreUtils.cs

        /// <summary>
        /// Return the GraphicsFormat of Depth-only RenderTarget preferred for the current platform.
        /// </summary>
        /// <returns>The GraphicsFormat of Depth-only RenderTarget preferred for the current platform.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GraphicsFormat GetDefaultDepthOnlyFormat()
        {
#if UNITY_SWITCH || UNITY_SWITCH2 || UNITY_EMBEDDED_LINUX || UNITY_QNX || UNITY_ANDROID
            return GraphicsFormatUtility.GetDepthStencilFormat(24, 0); // returns GraphicsFormat.D24_UNorm when hardware supports it
#else
            return GraphicsFormat.D32_SFloat;
#endif
        }

        // Taken from Unity 6.5:
        // Packages/com.unity.render-pipelines.core/Runtime/Utilities/CoreUtils.cs

        /// <summary>
        /// Return the number of DepthStencil RenderTarget depth bits preferred for the current platform.
        /// </summary>
        /// <returns>The number of DepthStencil RenderTarget depth bits preferred for the current platform.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static DepthBits GetDefaultDepthBufferBits()
        {
#if UNITY_SWITCH || UNITY_SWITCH2 || UNITY_EMBEDDED_LINUX || UNITY_QNX || UNITY_ANDROID
            return DepthBits.Depth24;
#else
            return DepthBits.Depth32;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GraphicsFormat GetDefaultColorFormat(bool hdr)
        {
            return SystemInfo.GetGraphicsFormat(hdr ? DefaultFormat.HDR : DefaultFormat.LDR);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GraphicsFormat GetDefaultDepthFormat(bool stencil)
        {
            return stencil ? GetDefaultDepthStencilFormat() : GetDefaultDepthOnlyFormat();
        }

        // URP_COMPATIBILITY_MODE = URP + UE < 6.4
        public static bool IsRenderGraph => RenderPipelineHelper.IsUniversal
#if URP_COMPATIBILITY_MODE
#if !UNITY_6000_0_OR_NEWER
            && false
#else
            && !GraphicsSettings.GetRenderPipelineSettings<UnityEngine.Rendering.Universal.RenderGraphSettings>().enableRenderCompatibilityMode
#endif
#endif
            ;

        public static partial class BIRP
        {
            static partial class ShaderIDs
            {
                public static readonly int s_InverseViewProjection = Shader.PropertyToID("_Crest_InverseViewProjection");
            }

            public static Texture GetWhiteTexture(Camera camera)
            {
#if _XR_ENABLED
                if (camera.stereoEnabled && SinglePassXR)
                {
                    return WhiteTextureXR;
                }
#endif

                return Texture2D.whiteTexture;
            }

            public static void SetMatrices(Camera camera)
            {
                Shader.SetGlobalMatrix(ShaderIDs.s_InverseViewProjection, (GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix).inverse);

#if _XR_ENABLED
                SetMatricesXR(camera);
#endif
            }

            public enum FrameBufferFormatOverride
            {
                None,
                LDR,
                HDR,
            }

            public static RenderTextureDescriptor GetCameraTargetDescriptor(Camera camera, FrameBufferFormatOverride hdrOverride = FrameBufferFormatOverride.None)
            {
                RenderTextureDescriptor descriptor;

#if _XR_ENABLED
                if (camera.stereoEnabled)
                {
                    // Will not set the following correctly:
                    // - HDR format
                    descriptor = XRSettings.eyeTextureDesc;
                }
                else
#endif
                {
                    // As recommended by Unity, in 2021.2 using SystemInfo.GetGraphicsFormat with DefaultFormat.LDR is
                    // necessary or gamma color space texture is returned:
                    // https://docs.unity3d.com/ScriptReference/Experimental.Rendering.DefaultFormat.html
                    descriptor = new(camera.pixelWidth, camera.pixelHeight, SystemInfo.GetGraphicsFormat(DefaultFormat.LDR), 0);
                }

                // Set HDR format.
                if (camera.allowHDR && QualitySettings.activeColorSpace == ColorSpace.Linear)
                {
                    var format = DefaultFormat.HDR;

                    if (hdrOverride is not FrameBufferFormatOverride.None)
                    {
                        format = hdrOverride is FrameBufferFormatOverride.HDR ? DefaultFormat.HDR : DefaultFormat.LDR;
                    }
#if UNITY_ANDROID || UNITY_IOS || UNITY_TVOS
                    else
                    {
                        format = DefaultFormat.LDR;
                    }
#endif

                    descriptor.graphicsFormat = SystemInfo.GetGraphicsFormat(format);
                }

                return descriptor;
            }
        }
    }

    static partial class Rendering
    {
        // Blit
        public static partial class BIRP
        {
            // Need to cast to int but no conversion cost.
            // https://stackoverflow.com/a/69148528
            internal enum UtilityPass
            {
                CopyDepth,
                Copy,
                MergeDepth,
            }

            static Material s_UtilityMaterial;
            public static Material UtilityMaterial
            {
                get
                {
                    if (s_UtilityMaterial == null)
                    {
                        s_UtilityMaterial = new(Shader.Find("Hidden/Crest/Legacy/Blit"));
                    }

                    return s_UtilityMaterial;
                }
            }
        }
    }
}
