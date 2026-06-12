// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;
using WaveHarmonic.Crest.Internal;

namespace WaveHarmonic.Crest.Examples
{
    /// <summary>
    /// Shoves the gameobject around random amounts, occasionally useful for debugging where some motion is required to reproduce an issue.
    /// </summary>
#if !CREST_DEBUG
    [AddComponentMenu("")]
#endif
    sealed class RandomMotion : CustomBehaviour
    {
        [SerializeField]
        bool _WorldSpace;

        [Header("Translation")]

        [SerializeField]
        Vector3 _Axis = Vector3.up;

        [@Range(0, 15)]
        [SerializeField]
        float _Amplitude = 1f;

        [@Range(0, 5)]
        [SerializeField]
        float _Frequency = 1f;

        [@Range(0, 1)]
        [SerializeField]
        float _OrthogonalMotion = 0f;


        [Header("Rotation")]

        [@Range(0, 5)]
        [SerializeField]
        float _RotationFrequency = 1f;

        [SerializeField]
        float _RotationVelocity = 0f;


        Vector3 _Origin;
        Vector3 _OrthogonalAxis;

        private protected override void OnStart()
        {
            base.OnStart();

            _Origin = _WorldSpace ? transform.position : transform.localPosition;

            _OrthogonalAxis = Quaternion.AngleAxis(90f, Vector3.up) * _Axis;
        }

        void Update()
        {
            // Translation
            {
                // Do circles in perlin noise
                var rnd = 2f * (Mathf.PerlinNoise(0.5f + 0.5f * Mathf.Cos(_Frequency * Time.time), 0.5f + 0.5f * Mathf.Sin(_Frequency * Time.time)) - 0.5f);

                // Prevent jump at start.
                var amplitude = Mathf.Min(_Amplitude, _Amplitude * Time.timeSinceLevelLoad);

                var orthoPhaseOff = Mathf.PI / 2f;
                var rndOrtho = 2f * (Mathf.PerlinNoise(0.5f + 0.5f * Mathf.Cos(_Frequency * Time.time + orthoPhaseOff), 0.5f + 0.5f * Mathf.Sin(_Frequency * Time.time + orthoPhaseOff)) - 0.5f);
                var position = _Origin + (_Axis * rnd + _OrthogonalMotion * rndOrtho * _OrthogonalAxis) * amplitude;

                if (_WorldSpace)
                {
                    transform.position = position;
                }
                else
                {
                    transform.localPosition = position;
                }
            }

            // Rotation
            {
                var f1 = Mathf.Sin(Time.time * _RotationFrequency * 1.0f);
                var f2 = Mathf.Sin(Time.time * _RotationFrequency * 0.83f);
                var f3 = Mathf.Sin(Time.time * _RotationFrequency * 1.14f);
                transform.rotation *= Quaternion.Euler(
                    f1 * _RotationVelocity * Time.deltaTime,
                    f2 * _RotationVelocity * Time.deltaTime,
                    f3 * _RotationVelocity * Time.deltaTime);
            }
        }
    }
}
