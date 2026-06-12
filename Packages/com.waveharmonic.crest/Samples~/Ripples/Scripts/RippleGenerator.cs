// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;
using UnityEngine.Serialization;
using WaveHarmonic.Crest.Internal;

namespace WaveHarmonic.Crest.Examples
{
    [RequireComponent(typeof(DynamicWavesLodInput))]
    [@ExecuteDuringEditMode]
    [AddComponentMenu(Constants.k_MenuPrefixSample + "Ripple Generator")]
    sealed class RippleGenerator : ManagedBehaviour<WaterRenderer>
    {
        [Tooltip("Amount of time before it starts.")]
        [FormerlySerializedAs("_WarmUp")]
        [SerializeField]
        float _StartTime = 3f;

        [Tooltip("The time interval to inject a ripple.\n\nTime will loop around by this number (in seconds). Increase to make ripples more frequent.")]
        [SerializeField]
        float _Period = 4f;

        [Tooltip("The length of time in the period the input runs for.\n\nFrom the start of the period until this time, the input will continue to render. The longer it is active, the further the water will be pushed/pulled per period. If it is too long for the period, the surface may never return to rest.")]
        [FormerlySerializedAs("_OnTime")]
        [SerializeField]
        float _Length = 0.2f;

        DynamicWavesLodInput _DynamicWavesLodInput;

        private protected override void Initialize()
        {
            base.Initialize();
            if (_DynamicWavesLodInput == null) _DynamicWavesLodInput = GetComponent<DynamicWavesLodInput>();
            _DynamicWavesLodInput.ForceRenderingOff = true;
        }

        private protected override System.Action<WaterRenderer> OnUpdateMethod => OnUpdate;
        void OnUpdate(WaterRenderer water)
        {
            if (!water.DynamicWavesLod.Enabled || _DynamicWavesLodInput == null)
            {
                return;
            }

            var time = water.CurrentTime;

            if (time < _StartTime)
            {
                _DynamicWavesLodInput.ForceRenderingOff = true;
                return;
            }

            time -= _StartTime;
            time = Mathf.Repeat(time, _Period);
            _DynamicWavesLodInput.ForceRenderingOff = time >= _Length;
        }
    }
}
