// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#if UNITY_EDITOR
#if d_UnityHDRP

using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace WaveHarmonic.Crest
{
    partial class SurfaceRenderer
    {
        sealed class WaterLevelDepthTextureHDRP : CustomPass
        {
            static WaterLevelDepthTextureHDRP s_Instance;
            WaterRenderer _Water;
            SurfaceRenderer _Surface;

            internal static void Enable(WaterRenderer water, SurfaceRenderer surface)
            {
                var gameObject = CustomPassHelpers.CreateOrUpdate
                (
                    parent: water.Container.transform,
                    k_WaterLevelDepthTextureName,
                    hide: !water._Debug._ShowHiddenObjects
                );

                CustomPassHelpers.CreateOrUpdate
                (
                    gameObject,
                    ref s_Instance,
                    k_WaterLevelDepthTextureName,
                    CustomPassInjectionPoint.BeforeRendering
                );

                s_Instance._Water = water;
                s_Instance._Surface = surface;
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

            protected override void Execute(CustomPassContext context)
            {
                var camera = context.hdCamera.camera;

                if (Application.isPlaying)
                {
                    return;
                }

                if (!_Water._CurrentSceneCameraHasMouseHover)
                {
                    return;
                }

                if (camera.cameraType != CameraType.SceneView || (_Water.IsSingleViewpointMode && camera != _Water.Viewer))
                {
                    return;
                }

                _Surface.ExecuteWaterLevelDepthTexture(camera, context.cmd);
            }
        }
    }
}

#endif // d_UnityHDRP
#endif
