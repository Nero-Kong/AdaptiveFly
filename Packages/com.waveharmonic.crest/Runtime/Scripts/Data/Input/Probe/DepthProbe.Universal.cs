// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#if d_UnityURP

using UnityEngine.Rendering.Universal;

namespace WaveHarmonic.Crest
{
    partial class DepthProbe
    {
        void SetUpCameraURP()
        {
            var additionalCameraData = _Camera.GetUniversalAdditionalCameraData();
            additionalCameraData.renderShadows = false;
            additionalCameraData.requiresColorTexture = false;
            additionalCameraData.requiresDepthTexture = false;
            additionalCameraData.renderPostProcessing = false;
            additionalCameraData.allowXRRendering = false;
        }
    }
}

#endif // d_UnityURP
