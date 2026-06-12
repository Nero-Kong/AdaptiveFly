// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;
using WaveHarmonic.Crest.Internal;

namespace WaveHarmonic.Crest.Examples
{
    [AddComponentMenu(Constants.k_MenuPrefixSample + "Waterfall (Sample)")]
    [ExecuteAlways]
    sealed class Waterfall : CustomBehaviour
    {
        public Transform _Crest;
        public float _CrestRadius = 1f;

        public Transform _Face;
        public float _FaceAngle = 5f;

        public Transform _Waves;

        [Tooltip("The bottom of the waterfall. If unset, it will use sea level.")]
        public Transform _PlungePool;

        public Transform _Foam;
        public float _FoamOffset;
        public float _FoamPadding;

        public Transform _Flow;
        public float _FlowPadding = 10f;

        float HeightFromPlungePool => transform.position.y - (_PlungePool != null
            ? _PlungePool.position.y
            : WaterRenderer.Instance != null
            ? WaterRenderer.Instance.SeaLevel
            : 0f);

        private protected override void OnEnable()
        {
            base.OnEnable();
            Update();
        }

        void Update()
        {
            UpdateFace();
            UpdateCrest();
            UpdateWaves();
            UpdateFoam();
            UpdateFlow();
        }

        void UpdateCrest()
        {
            if (_Crest == null)
            {
                return;
            }

            var crest = _Crest;

            var position = crest.localPosition;
            position.y = -_CrestRadius;
            crest.localPosition = position;

            var scale = crest.localScale;
            scale.x = _CrestRadius * 2f;
            scale.z = _CrestRadius * 2f;
            crest.localScale = scale;
        }

        void UpdateFace()
        {
            if (_Face == null)
            {
                return;
            }

            var face = _Face;
            var angle = _FaceAngle * Mathf.Deg2Rad;
            var ratio = 1f / Mathf.Cos(angle);
            var height = HeightFromPlungePool - _CrestRadius;
            var center = height * 0.5f;

            var rotation = face.localRotation.eulerAngles;
            rotation.x = 90f - _FaceAngle;
            face.localRotation = Quaternion.Euler(rotation);

            // Plane is 10m.
            var scale = face.localScale;
            scale.z = height * 0.1f * ratio;
            face.localScale = scale;

            var position = face.localPosition;
            position.y = -center;
            position.z = center * ratio * Mathf.Sin(angle);

            position.y += -_CrestRadius;
            position.z += _CrestRadius;

            face.localPosition = position;
        }

        void UpdateWaves()
        {
            if (_Waves == null)
            {
                return;
            }

            var waves = _Waves;

            var feather = 0f;

            if (_Waves.TryGetComponent<Renderer>(out var renderer) && renderer.sharedMaterial != null && renderer.sharedMaterial.IsKeywordEnabled("d_Feather"))
            {
                feather = renderer.sharedMaterial.GetFloat(ShaderIDs.s_FeatherWidth);
            }

            var position = waves.localPosition;

            var scale = waves.localScale.x;

            position.z = scale * 0.5f + (_Face.localPosition.z * 2f - _CrestRadius) - feather * scale;

            position.y = -HeightFromPlungePool;

            waves.localPosition = position;
        }

        void UpdateFoam()
        {
            if (_Foam == null)
            {
                return;
            }

            var foam = _Foam;

            var size = _Face.localPosition.z * 2f - _CrestRadius;

            var scale = foam.localScale;
            scale.y = size + _FoamPadding;
            foam.localScale = scale;

            var position = _Foam.localPosition;
            position.z = size * 0.5f + _FoamOffset;
            _Foam.localPosition = position;
        }

        void UpdateFlow()
        {
            if (_Flow == null)
            {
                return;
            }

            var flow = _Flow;

            var size = _Face.localPosition.z * 2f - _CrestRadius;

            var scale = flow.localScale;
            scale.y = size + _FlowPadding;
            flow.localScale = scale;

            var position = _Flow.localPosition;
            position.z = size * 0.5f;
            _Flow.localPosition = position;
        }
    }
}
