// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

// ENABLE_VR is defined if the platform supports XR.
// In Unity 6.5, the XR module replaced the VR module.
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

using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

namespace WaveHarmonic.Crest
{
    static partial class Rendering
    {
        // Adaptor layer for XR module similar to Unity's XRGraphics/XRSRPSettings.
        // We cannot use theirs as they keep on renaming it…

        static readonly GlobalKeyword s_SinglePassInstancedKeyword = new("STEREO_INSTANCING_ON");
        static readonly GlobalKeyword s_SinglePassMultiViewKeyword = new("STEREO_MULTIVIEW_ON");

#if _XR_ENABLED
        internal static GlobalKeyword SinglePassKeyword => XRSettings.stereoRenderingMode switch
        {
            XRSettings.StereoRenderingMode.SinglePassInstanced => s_SinglePassInstancedKeyword,
            XRSettings.StereoRenderingMode.SinglePassMultiview => s_SinglePassMultiViewKeyword,
            _ => throw new System.NotImplementedException(),
        };
#endif

        public static bool EnabledXR
        {
            get
            {
#if _XR_ENABLED
                return XRSettings.enabled;
#else
                return false;
#endif
            }
        }

        static bool SinglePassXR
        {
            get
            {
#if _XR_ENABLED
                return XRSettings.enabled && (XRSettings.stereoRenderingMode is XRSettings.StereoRenderingMode.SinglePassInstanced or XRSettings.StereoRenderingMode.SinglePassMultiview);
#else
                return false;
#endif
            }
        }

        static bool MultiPassXR
        {
            get
            {
#if _XR_ENABLED
                return XRSettings.enabled && XRSettings.stereoRenderingMode is XRSettings.StereoRenderingMode.MultiPass;
#else
                return false;
#endif
            }
        }

        public static partial class BIRP
        {
            [Conditional("_XR_ENABLED")]
            public static void EnableXR(CommandBuffer commands, Camera camera)
            {
#if _XR_ENABLED
                if (!SinglePassXR || !camera.stereoEnabled)
                {
                    return;
                }

                commands.EnableKeyword(SinglePassKeyword);
#endif
            }

            [Conditional("_XR_ENABLED")]
            public static void DisableXR(CommandBuffer commands, Camera camera)
            {
#if _XR_ENABLED
                if (!SinglePassXR || !camera.stereoEnabled)
                {
                    return;
                }

                commands.DisableKeyword(SinglePassKeyword);
#endif
            }
        }


        //
        // Stereo Rendering
        //

#if _XR_ENABLED
        public static partial class BIRP
        {
            // NOTE: This is the same value as Unity, but in the future it could be higher.
            const int k_MaximumViewsXR = 2;

            static partial class ShaderIDs
            {
                public static readonly int s_StereoInverseViewProjection = Shader.PropertyToID("_Crest_StereoInverseViewProjection");
            }

            static readonly List<XRDisplaySubsystem> s_DisplayListXR = new();

            // Unity only supports one display right now.
            static XRDisplaySubsystem DisplayXR => XRSettings.enabled ? s_DisplayListXR[0] : null;

            static Matrix4x4[] InverseViewProjectionMatrixXR { get; set; } = new Matrix4x4[2];

            static Texture2DArray s_WhiteTextureXR = null;
            public static Texture2DArray WhiteTextureXR
            {
                get
                {
                    if (s_WhiteTextureXR == null)
                    {
                        s_WhiteTextureXR = TextureArrayHelpers.CreateTexture2DArray(Texture2D.whiteTexture, k_MaximumViewsXR);
                        s_WhiteTextureXR.name = "_Crest_WhiteTextureXR";
                    }

                    return s_WhiteTextureXR;
                }
            }

            public static void SetMatricesXR(Camera camera)
            {
                if (!camera.stereoEnabled || !SinglePassXR)
                {
                    return;
                }

                SubsystemManager.GetSubsystems(s_DisplayListXR);
                // XR SPI only has one pass by definition.
                DisplayXR.GetRenderPass(renderPassIndex: 0, out var xrPass);
                xrPass.GetRenderParameter(camera, renderParameterIndex: 0, out var xrLeftEye);
                xrPass.GetRenderParameter(camera, renderParameterIndex: 1, out var xrRightEye);
                // We must opt for renderIntoTexture for Unity to handle Y flip.
                InverseViewProjectionMatrixXR[0] = (GL.GetGPUProjectionMatrix(xrLeftEye.projection, true) * xrLeftEye.view).inverse;
                InverseViewProjectionMatrixXR[1] = (GL.GetGPUProjectionMatrix(xrRightEye.projection, true) * xrRightEye.view).inverse;
                Shader.SetGlobalMatrixArray(ShaderIDs.s_StereoInverseViewProjection, InverseViewProjectionMatrixXR);
            }
        }
#endif // _XR_ENABLED
    }
}

#if d_UnityURP
namespace WaveHarmonic.Crest
{
#if !UNITY_6000_0_OR_NEWER
    using UniversalCameraData = CameraData;
#endif

    static partial class Rendering
    {
        public static class URP
        {
            [Conditional("_XR_ENABLED")]
            public static void EnableXR(CommandBuffer commands, UniversalCameraData camera)
            {
#if _XR_ENABLED
                // We need to check the mask or it will cause entire pipeline to output black. Appears to only affect URP.
                if (!SinglePassXR || !camera.xrRendering || camera.camera.stereoTargetEye != StereoTargetEyeMask.Both)
                {
                    return;
                }

                commands.EnableKeyword(SinglePassKeyword);
#endif
            }

            [Conditional("_XR_ENABLED")]
            public static void DisableXR(CommandBuffer commands, UniversalCameraData camera)
            {
#if _XR_ENABLED
                if (!SinglePassXR || !camera.xrRendering || camera.camera.stereoTargetEye != StereoTargetEyeMask.Both)
                {
                    return;
                }

                commands.DisableKeyword(SinglePassKeyword);
#endif
            }
        }
    }
}
#endif // d_UnityURP

#if d_UnityHDRP
namespace WaveHarmonic.Crest
{
    static partial class Rendering
    {
        public static class HDRP
        {
            [Conditional("_XR_ENABLED")]
            public static void EnableXR(CommandBuffer commands, HDAdditionalCameraData camera)
            {
#if _XR_ENABLED
                if (!SinglePassXR || camera == null || !camera.xrRendering)
                {
                    return;
                }

                commands.EnableKeyword(SinglePassKeyword);
#endif
            }

            [Conditional("_XR_ENABLED")]
            public static void DisableXR(CommandBuffer commands, HDAdditionalCameraData camera)
            {
#if _XR_ENABLED
                if (!SinglePassXR || camera == null || !camera.xrRendering)
                {
                    return;
                }

                commands.DisableKeyword(SinglePassKeyword);
#endif
            }

            public static bool SkipPassXR(ref int index, HDAdditionalCameraData data)
            {
#if _XR_ENABLED
                if (MultiPassXR && data != null && data.xrRendering)
                {
                    // Alternate between left and right eye.
                    index += 1;
                    index %= 2;
                }
                else
#endif

                {
                    index = -1;
                }

                // Skip if rendering the right eye.
                return index == 1;
            }
        }
    }
}
#endif // d_UnityHDRP
