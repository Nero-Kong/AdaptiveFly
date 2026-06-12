// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#if d_UnityHDRP

using UnityEngine.Rendering.HighDefinition;

namespace WaveHarmonic.Crest
{
    sealed class SampleShadowsHDRP : CustomPass
    {
        static SampleShadowsHDRP s_Instance;
        static readonly string s_Name = "Sample Shadows";

        WaterRenderer _Water;
        int _XrTargetEyeIndex = -1;

        protected override void Execute(CustomPassContext context)
        {
            var water = _Water;

            if (!water._ActiveModules.HasFlag(WaterRenderer.ActiveModules.Shadows))
            {
                return;
            }

            var camera = context.hdCamera.camera;

            // TODO: bail when not executing for main light or when no main light exists?
            // if (renderingData.lightData.mainLightIndex == -1) return;

            camera.TryGetComponent<HDAdditionalCameraData>(out var cameraData);

            // Skip the right eye for multi-pass as data is not stereo.
            if (Rendering.HDRP.SkipPassXR(ref _XrTargetEyeIndex, cameraData))
            {
                return;
            }

            // Disable for XR SPI otherwise input will not have correct world position.
            Rendering.HDRP.DisableXR(context.cmd, cameraData);

            water._ShadowLod.BuildCommandBuffer(water, context.cmd);

            // Restore XR SPI as we cannot rely on remaining pipeline to do it for us.
            Rendering.HDRP.EnableXR(context.cmd, cameraData);
        }

        internal static void Enable(WaterRenderer water)
        {
            var gameObject = CustomPassHelpers.CreateOrUpdate
            (
                parent: water.Container.transform,
                s_Name,
                hide: !water._Debug._ShowHiddenObjects
            );

            CustomPassHelpers.CreateOrUpdate
            (
                gameObject,
                ref s_Instance,
                WaterRenderer.k_DrawLodData,
                // Earliest point after shadow maps have populated.
                CustomPassInjectionPoint.AfterOpaqueAndSky
            );

            s_Instance._Water = water;
        }

        internal static void Disable()
        {
            // It should be safe to rely on this reference for this reference to fail.
            if (s_Instance != null && s_Instance._GameObject != null)
            {
                // Will also trigger Cleanup below.
                s_Instance._GameObject.SetActive(false);
            }
        }
    }
}

#endif // d_UnityHDRP
