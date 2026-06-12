// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEditor.SceneManagement;
using UnityEngine;

namespace WaveHarmonic.Crest.Editor
{
#if !CREST_DEBUG
    [AddComponentMenu("")]
#endif
    sealed class RenderPipelineTerrainPatcher : RenderPipelinePatcher
    {
        [SerializeField]
        Material _Material;

        [SerializeField]
        Material _MaterialHDRP;

        [SerializeField]
        Material _MaterialURP;

#if UNITY_EDITOR
        private protected override void OnEnable()
        {
            base.OnEnable();
            OnActiveRenderPipelineTypeChanged();
        }

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

#if d_Unity_Terrain
            foreach (var terrain in GetComponentsInChildren<Terrain>())
            {
                terrain.materialTemplate = RenderPipelineHelper.RenderPipeline switch
                {
                    RenderPipeline.Legacy => _Material,
                    RenderPipeline.Universal => _MaterialURP,
                    RenderPipeline.HighDefinition => _MaterialHDRP,
                    _ => throw new System.NotImplementedException(),
                };
            }
#endif // d_Unity_Terrain
        }
#endif
    }
}
