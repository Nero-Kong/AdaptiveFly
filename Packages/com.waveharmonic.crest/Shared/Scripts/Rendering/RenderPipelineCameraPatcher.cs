// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace WaveHarmonic.Crest.Editor
{
#if !CREST_DEBUG
    [AddComponentMenu("")]
#endif
    [RequireComponent(typeof(Camera))]
    sealed class RenderPipelineCameraPatcher : RenderPipelinePatcher
    {
#if UNITY_EDITOR
        protected override void OnActiveRenderPipelineTypeChanged()
        {
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                return;
            }

            if (!isActiveAndEnabled)
            {
                return;
            }

#if d_UnityHDRP
            if (RenderPipelineHelper.IsHighDefinition)
            {
                if (TryGetComponent<HDAdditionalCameraData>(out var data))
                {
                    // This component will try to modify serialized HDR & MSAA properties every frame. Disgusting.
                    data.enabled = true;
                }
            }
#endif
        }
#endif
    }
}
