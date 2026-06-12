// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

// RTHandles for Built-In Render Pipeline.
// We cannot call dispose ourselves, but it does not seem to be a problem.

using UnityEngine;

namespace WaveHarmonic.Crest.Utility
{
    static class RTHandles
    {
        public static void Initialize()
        {
            if (!RenderPipelineHelper.IsLegacy)
            {
                return;
            }

            // Check whether already initialized.
            if (UnityEngine.Rendering.RTHandles.maxWidth > 1)
            {
                return;
            }

            UnityEngine.Rendering.RTHandles.Initialize(Screen.width, Screen.height);
            UnityEngine.Rendering.RTHandles.SetHardwareDynamicResolutionState(false);
        }

        public static void OnBeginCameraRendering(Camera camera)
        {
            // Forget Dynamic Scaling, as is broken for Shader Graph and Post-Processing anyway.
            // The only foreseeable problem is if a third party calls this with a different size.
            UnityEngine.Rendering.RTHandles.SetReferenceSize(camera.pixelWidth, camera.pixelHeight);
        }
    }
}
