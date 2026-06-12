// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;
using UnityEngine.Rendering;

namespace WaveHarmonic.Crest
{
    /// <summary>
    /// FFT wave shape.
    /// </summary>
    [AddComponentMenu(Constants.k_MenuPrefixInputs + "Shape FFT")]
    public sealed partial class ShapeFFT : ShapeWaves
    {
        // Waves

        [Tooltip("Whether to apply the options shown when \"Show Advanced Controls\" is active.")]
        [@Order(nameof(_EvaluateSpectrumAtRunTimeEveryFrame), Order.Placement.Below)]
        [@DecoratedField]
        [@GenerateAPI]
        [@SerializeField]
        bool _ApplyAdvancedSpectrumControls;

        [Tooltip("Whether to use the wind turbulence on this component rather than the global wind turbulence.\n\nGlobal wind turbulence comes from the Water Renderer component.")]
        [@Order("Waves")]
        [@InlineToggle]
        [@GenerateAPI]
        [@SerializeField]
        bool _OverrideGlobalWindTurbulence;

        [Tooltip("How turbulent/chaotic the waves are.")]
        [@Order("Waves")]
        [@Show(nameof(_OverrideGlobalWindTurbulence))]
        [@ShowComputedProperty(nameof(WindTurbulence))]
        [@Range(0, 1)]
        [@GenerateAPI(Getter.Custom)]
        [SerializeField]
        float _WindTurbulence = 0.145f;

        [Tooltip("How aligned the waves are with wind.")]
        [@Range(0, 1)]
        [@Order("Waves")]
        [@GenerateAPI]
        [SerializeField]
        float _WindAlignment;


        // Generation

        [Tooltip("FFT waves will loop with a period of this many seconds.")]
        [@Order("Generation Settings")]
        [@Range(4f, 128f, Range.Clamp.Minimum)]
        [@GenerateAPI]
        [SerializeField]
        float _TimeLoopLength = Mathf.Infinity;


        [@Heading("Culling")]

        [Tooltip("Whether to override automatic culling based on heuristics.")]
        [@GenerateAPI]
        [@DecoratedField]
        [SerializeField]
        bool _OverrideCulling;

        [Tooltip("Maximum amount the surface will be displaced vertically from sea level.\n\nIncrease this if gaps appear at bottom of screen.")]
        [@GenerateAPI]
        [@DecoratedField]
        [SerializeField]
        float _MaximumVerticalDisplacement = 10f;

        [Tooltip("Maximum amount a point on the surface will be displaced horizontally by waves from its rest position.\n\nIncrease this if gaps appear at sides of screen.")]
        [@GenerateAPI]
        [@DecoratedField]
        [SerializeField]
        float _MaximumHorizontalDisplacement = 15f;


        [@Heading("Collision Data Baking")]

#if !d_WaveHarmonic_Crest_CPUQueries
        [HideInInspector]
#endif

        [Tooltip("Enable running this FFT with baked data.\n\nThis makes the FFT periodic (repeating in time).")]
        [@Show(nameof(_Mode), nameof(LodInputMode.Global))]
        [@DecoratedField, SerializeField]
        internal bool _EnableBakedCollision = false;

#if !d_WaveHarmonic_Crest_CPUQueries
        [HideInInspector]
#endif

        [Tooltip("Frames per second of baked data.\n\nLarger values may help the collision track the surface closely at the cost of more frames and increase baked data size.")]
        [@Enable(nameof(_EnableBakedCollision))]
        [@Show(nameof(_Mode), nameof(LodInputMode.Global))]
        [@DecoratedField, SerializeField]
        internal int _TimeResolution = 4;

#if !d_WaveHarmonic_Crest_CPUQueries
        [HideInInspector]
#endif

        [Tooltip("Smallest wavelength required in collision.\n\nTo preview the effect of this, disable power sliders in spectrum for smaller values than this number. Smaller values require more resolution and increase baked data size.")]
        [@Enable(nameof(_EnableBakedCollision))]
        [@Show(nameof(_Mode), nameof(LodInputMode.Global))]
        [@DecoratedField, SerializeField]
        internal float _SmallestWavelengthRequired = 2f;

#if !d_WaveHarmonic_Crest_CPUQueries
        [HideInInspector]
#endif

        [Tooltip("FFT waves will loop with a period of this many seconds.\n\nSmaller values decrease data size but can make waves visibly repetitive.")]
        [@Enable(nameof(_EnableBakedCollision))]
        [@Show(nameof(_Mode), nameof(LodInputMode.Global))]
        [@Range(4f, 128f)]
        [SerializeField]
        internal float _BakedTimeLoopLength = 32f;

        internal float LoopPeriod =>
#if d_WaveHarmonic_Crest_CPUQueries
            _EnableBakedCollision ? _BakedTimeLoopLength :
#endif
            _TimeLoopLength;

        // WebGPU will crash above at 128.
        private protected override int MinimumResolution => 16;
        private protected override int MaximumResolution => Helpers.IsWebGPU ? 64 : int.MaxValue;

        FFTCompute _FFTCompute;

        FFTCompute.Parameters _OldFFTParameters;
        internal FFTCompute.Parameters GetFFTParameters(float gravity) => new
        (
            _ActiveSpectrum,
            Resolution,
            _TimeLoopLength,
            WindSpeedMPS,
            WindDirRadForFFT,
            WindTurbulence,
            _WindAlignment,
            gravity,
            _ApplyAdvancedSpectrumControls
        );

        private protected override void OnUpdate(WaterRenderer water)
        {
            base.OnUpdate(water);

            // We do not filter FFTs.
            _FirstCascade = 0;
            _LastCascade = k_CascadeCount - 1;

            ReportMaxDisplacement(water);

            // If geometry is being used, the water input shader will rotate the waves to align to geo
            var parameters = GetFFTParameters(water.Gravity);

            // Don't create tons of generators when values are varying. Notify so that existing generators may be adapted.
            if (parameters.GetHashCode() != _OldFFTParameters.GetHashCode())
            {
                FFTCompute.OnGenerationDataUpdated(_OldFFTParameters, parameters);
            }

#if UNITY_EDITOR
            _FFTCompute = FFTCompute.GetInstance(parameters);
#endif

            _OldFFTParameters = parameters;
        }

        internal override void Draw(Lod lod, CommandBuffer buffer, RenderTargetIdentifier target, int pass = -1, float weight = 1, int slice = -1)
        {
            if (_LastGenerateFrameCount != Time.frameCount)
            {
                // Parameters will unlikely change as our Update is called in LateUpdate with Draw
                // not too far after.
                var parameters = GetFFTParameters(lod.Water.Gravity);

                _WaveBuffers = FFTCompute.GenerateDisplacements
                (
                    buffer,
                    lod.Water.CurrentTime,
                    parameters,
                    UpdateDataEachFrame
                );

#if UNITY_EDITOR
                _FFTCompute = FFTCompute.GetInstance(parameters);
#endif

                _LastGenerateFrameCount = Time.frameCount;
            }

            base.Draw(lod, buffer, target, pass, weight, slice);
        }

        private protected override void SetRenderParameters<T>(WaterRenderer water, T wrapper)
        {
            base.SetRenderParameters(water, wrapper);

            // If using geometry, the primary wave direction is used by the input shader to
            // rotate the waves relative to the geo rotation. If not, the wind direction is
            // already used in the FFT generation.
            var waveDir = (Mode is LodInputMode.Spline or LodInputMode.Paint) ? PrimaryWaveDirection : Vector2.right;
            wrapper.SetVector(ShaderIDs.s_AxisX, waveDir);
        }

        private protected override void ReportMaxDisplacement(WaterRenderer water)
        {
            if (!Enabled) return;

            if (_OverrideCulling)
            {
                // Apply weight or will cause popping due to scale change.
                MaximumReportedHorizontalDisplacement = _MaximumHorizontalDisplacement * Weight;
                MaximumReportedVerticalDisplacement = MaximumReportedWavesDisplacement = _MaximumVerticalDisplacement * Weight;
            }
            else
            {
                var powerLinear = 0f;

                for (var i = 0; i < WaveSpectrum.k_NumberOfOctaves; i++)
                {
                    powerLinear += _ActiveSpectrum._PowerLinearScales[i];
                }

                // Empirical multiplier (3-5), went with 5 to be safe.
                // We may be missing some more multipliers from the compute shader.
                // Look there if this proves insufficient.
                var wind = Mathf.Clamp01(WindSpeedKPH / 150f);
                var rms = Mathf.Sqrt(powerLinear) * 5f;
                MaximumReportedHorizontalDisplacement = rms * _ActiveSpectrum._Chop * Weight * wind;
                MaximumReportedVerticalDisplacement = MaximumReportedWavesDisplacement = rms * Weight * wind;
            }
        }

        float WindDirRadForFFT
        {
            get
            {
                // These input types use a wave direction provided by geometry or the painted user direction
                if (Mode is LodInputMode.Spline or LodInputMode.Paint)
                {
                    return 0f;
                }

                return WaveDirectionHeadingAngle * Mathf.Deg2Rad;
            }
        }

        float GetWindTurbulence()
        {
            return _OverrideGlobalWindTurbulence || WaterRenderer.Instance == null ? _WindTurbulence : WaterRenderer.Instance.WindTurbulence;
        }

#if UNITY_EDITOR
        void OnGUI()
        {
            if (_DrawSlicesInEditor)
            {
                _FFTCompute?.OnGUI();
            }
        }
#endif
    }

    partial class ShapeFFT
    {
        static int s_InstanceCount;

        private protected override void Awake()
        {
            base.Awake();
            s_InstanceCount++;
        }

        private protected override void OnDestroy()
        {
            base.OnDestroy();

            if (--s_InstanceCount <= 0)
            {
                FFTCompute.CleanUpAll();
            }
        }
    }

    partial class ShapeFFT
    {
        private protected override int Version => Mathf.Max(base.Version, 2);

        private protected override void OnMigrate()
        {
            base.OnMigrate();

            if (_Version < 2)
            {
                _OverrideGlobalWindTurbulence = true;
            }
        }
    }

#if UNITY_EDITOR
    partial class ShapeFFT
    {
        private protected override void Reset()
        {
            base.Reset();

            if (_Mode != LodInputMode.Global)
            {
                _OverrideGlobalWindTurbulence = true;
            }
        }
    }
#endif
}
