// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;
using UnityEngine.Rendering;
using WaveHarmonic.Crest.Internal;

namespace WaveHarmonic.Crest.Editor
{
    [ExecuteAlways]
    abstract class RenderPipelinePatcher : CustomBehaviour
    {
#if UNITY_EDITOR
        private protected override void OnEnable()
        {
            base.OnEnable();

            RenderPipelineManager.activeRenderPipelineTypeChanged -= OnActiveRenderPipelineTypeChanged;
            RenderPipelineManager.activeRenderPipelineTypeChanged += OnActiveRenderPipelineTypeChanged;
        }

        protected virtual void OnDisable()
        {
            RenderPipelineManager.activeRenderPipelineTypeChanged -= OnActiveRenderPipelineTypeChanged;
        }

        protected abstract void OnActiveRenderPipelineTypeChanged();
#endif
    }
}
