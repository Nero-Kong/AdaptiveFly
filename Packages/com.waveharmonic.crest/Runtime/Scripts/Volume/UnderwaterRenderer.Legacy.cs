// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;
using UnityEngine.Rendering;

namespace WaveHarmonic.Crest
{
    partial class UnderwaterRenderer
    {
        bool _HasEffectCommandBuffersBeenRegistered;

        void OnEnableLegacy()
        {
            SetupUnderwaterEffect();

            RenderPipelineManager.activeRenderPipelineTypeChanged -= OnDisableLegacy;
            RenderPipelineManager.activeRenderPipelineTypeChanged += OnDisableLegacy;
        }

        void OnDisableLegacy()
        {
            RenderPipelineManager.activeRenderPipelineTypeChanged -= OnDisableLegacy;
        }

        // Listening to OnPreCull. Camera must have underwater layer.
        void OnBeforeLegacyRender(Camera camera)
        {
            if (_Water._ActiveModules.HasFlag(WaterRenderer.ActiveModules.Volume))
            {
                _Water.UpdateMatrices(camera);

                _Water.OnBeginCameraOpaqueTexture(camera);

                var @event = RenderBeforeTransparency ? CameraEvent.BeforeForwardAlpha : CameraEvent.AfterForwardAlpha;
                camera.AddCommandBuffer(@event, _EffectCommandBuffer);
                OnPreRenderUnderwaterEffect(camera);
                _HasEffectCommandBuffersBeenRegistered = true;
            }
        }

        void OnAfterLegacyRender(Camera camera)
        {
            if (_HasEffectCommandBuffersBeenRegistered)
            {
                var @event = RenderBeforeTransparency ? CameraEvent.BeforeForwardAlpha : CameraEvent.AfterForwardAlpha;
                camera.RemoveCommandBuffer(@event, _EffectCommandBuffer);
                _EffectCommandBuffer?.Clear();
            }

            _Water.OnEndCameraOpaqueTexture(camera);

            _HasEffectCommandBuffersBeenRegistered = false;
        }
    }
}
