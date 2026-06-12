// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#if d_UnityHDRP

using System.Collections.Generic;
using UnityEngine.Rendering.HighDefinition;

namespace WaveHarmonic.Crest
{
    partial class DepthProbe
    {
        static readonly List<FrameSettingsField> s_FrameSettingsFields = new()
        {
            FrameSettingsField.OpaqueObjects,
            FrameSettingsField.TransparentObjects,
            FrameSettingsField.TransparentPrepass,
            FrameSettingsField.TransparentPostpass,
            FrameSettingsField.AsyncCompute,
        };

        HDAdditionalCameraData _HDAdditionalCameraData;

        void SetUpCameraHD()
        {
            var additionalCameraData = _Camera.gameObject.AddComponent<HDAdditionalCameraData>();

            additionalCameraData.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            additionalCameraData.volumeLayerMask = 0;
            additionalCameraData.probeLayerMask = 0;
            additionalCameraData.xrRendering = false;

            // Override camera frame settings to disable most of the expensive rendering for this camera.
            // Most importantly, disable custom passes and post-processing as third-party stuff might throw
            // errors because of this camera. Even with excluding a lot of HDRP features, it still does a
            // lit pass which is not cheap.
            additionalCameraData.customRenderingSettings = true;

            foreach (FrameSettingsField frameSetting in System.Enum.GetValues(typeof(FrameSettingsField)))
            {
                if (!s_FrameSettingsFields.Contains(frameSetting))
                {
                    // Enable override and then disable the feature.
                    additionalCameraData.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)frameSetting] = true;
                    additionalCameraData.renderingPathCustomFrameSettings.SetEnabled(frameSetting, false);
                }
            }

            _HDAdditionalCameraData = additionalCameraData;
        }
    }
}

#endif // d_UnityHDRP
