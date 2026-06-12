// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#if UNITY_EDITOR

using UnityEngine;
using WaveHarmonic.Crest.Editor;

namespace WaveHarmonic.Crest
{
    // Edit Mode.
    partial class UnderwaterRenderer
    {
        internal bool ShouldRenderForSceneView(Camera camera)
        {
            if (GL.wireframe)
            {
                return false;
            }

            // Check if scene view has disabled fog rendering.
            if (camera.cameraType == CameraType.SceneView)
            {
                var sceneView = EditorHelpers.GetSceneViewFromSceneCamera(camera);
                // Skip rendering if fog is disabled or for some reason we could not find the scene view.
                if (sceneView == null || !sceneView.sceneViewState.fogEnabled)
                {
                    return false;
                }
            }

            return true;
        }
    }
}

#endif
