// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using WaveHarmonic.Crest.Internal;

namespace WaveHarmonic.Crest
{
    /// <summary>
    /// Base class for Shape components.
    /// </summary>
    [@ExecuteDuringEditMode(ExecuteDuringEditMode.Include.None)]
    [@HelpURL("Manual/Waves.html#wave-conditions")]
    [@FilterEnum(nameof(_Blend), Filtered.Mode.Include, (int)LodInputBlend.Off, (int)LodInputBlend.Additive, (int)LodInputBlend.Alpha, (int)LodInputBlend.AlphaClip)]
    public abstract partial class ShapeWaves : LodInput
    {
        [@Heading("Waves")]

        [Tooltip("The spectrum that defines the water surface shape.")]
        [@Embedded(defaultPropertyName: nameof(_ActiveSpectrum))]
        [@GenerateAPI]
        [SerializeField]
        internal WaveSpectrum _Spectrum;

        [Tooltip("Whether to evaluate the spectrum every frame.\n\nWhen false, the wave spectrum is evaluated once on startup in editor play mode and standalone builds, rather than every frame. This is less flexible, but it reduces the performance cost significantly.")]
        [@GenerateAPI]
        [FormerlySerializedAs("_SpectrumFixedAtRuntime")]
        [@DecoratedField]
        [SerializeField]
        private protected bool _EvaluateSpectrumAtRunTimeEveryFrame;

        [@Space(10)]

        [Tooltip("How much these waves respect the shallow water attenuation.\n\nAttenuation is defined on the Animated Waves. Set to zero to ignore attenuation.")]
        [@Range(0f, 1f)]
        [@GenerateAPI]
        [SerializeField]
        float _RespectShallowWaterAttenuation = 1f;

        [Tooltip("Whether global waves is applied above or below sea level.\n\nWaves are faded out to avoid hard transitionds. They are fully faded by 1m from sea level.")]
        [@Enable(nameof(_Mode), nameof(LodInputMode.Global))]
        [@GenerateAPI]
        [@DecoratedField]
        [SerializeField]
        bool _SeaLevelOnly = true;

        [@Space(10)]

        [Tooltip("Whether to use the wind direction on this component rather than the global wind direction.\n\nGlobal wind direction comes from the Water Renderer component.")]
        [@GenerateAPI]
        [@InlineToggle, SerializeField]
        bool _OverrideGlobalWindDirection;

        [@Label("Wind Direction")]
        [Tooltip("Primary wave direction heading (degrees).\n\nThis is the angle from x axis in degrees that the waves are oriented towards. If a spline is being used to place the waves, this angle is relative to the spline.")]
        [@Disable(nameof(_Mode), nameof(LodInputMode.Paint))]
        [@Show(nameof(_OverrideGlobalWindDirection))]
        [@ShowComputedProperty(nameof(WaveDirectionHeadingAngle))]
        [@Range(-180, 180)]
        [@GenerateAPI(Getter.Custom)]
        [SerializeField]
        private protected float _WaveDirectionHeadingAngle = 0f;

        [Tooltip("Whether to use the wind speed on this component rather than the global wind speed.\n\nGlobal wind speed comes from the Water Renderer component.")]
        [@GenerateAPI]
        [@InlineToggle, SerializeField]
        bool _OverrideGlobalWindSpeed;

        [Tooltip("Wind speed in km/h. Controls wave conditions.")]
        [@ShowComputedProperty(nameof(WindSpeedKPH))]
        [@Show(nameof(_OverrideGlobalWindSpeed))]
        [@Range(0, 150f, scale: 2f)]
        [@GenerateAPI]
        [SerializeField]
        float _WindSpeed = 20f;


        [@Heading("Generation Settings")]

        [Tooltip("Resolution to use for wave generation buffers.\n\nLow resolutions are more efficient but can result in noticeable patterns in the shape.")]
        [@Stepped(16, 512, step: 2, power: true)]
        [@GenerateAPI(Getter.Custom)]
        [SerializeField]
        private protected int _Resolution = 128;


        [@Heading("Level of Detail")]

        [Tooltip("Whether the maximum possible vertical displacement is used for the Drop Detail Height Based On Waves calculation.\n\nThis setting is ignored for global waves, as they always contribute. For local waves, only enable for large areas that are treated like global waves (eg a storm).")]
        [@Disable(nameof(_Mode), nameof(LodInputMode.Global))]
        [@GenerateAPI]
        [@DecoratedField]
        [SerializeField]
        bool _IncludeInDropDetailHeightBasedOnWaves;


        // Debug

        [Tooltip("In Editor, shows the wave generation buffers on screen.")]
        [@DecoratedField(order = k_DebugGroupOrder * Constants.k_FieldGroupOrder), SerializeField]
        internal bool _DrawSlicesInEditor = false;


        private protected static new class ShaderIDs
        {
            public static readonly int s_TransitionalWavelengthThreshold = Shader.PropertyToID("_Crest_TransitionalWavelengthThreshold");
            public static readonly int s_WaveResolutionMultiplier = Shader.PropertyToID("_Crest_WaveResolutionMultiplier");
            public static readonly int s_WaveBufferParameters = Shader.PropertyToID("_Crest_WaveBufferParameters");
            public static readonly int s_AlphaSource = Shader.PropertyToID("_Crest_AlphaSource");
            public static readonly int s_WaveBuffer = Shader.PropertyToID("_Crest_WaveBuffer");
            public static readonly int s_WaveBufferSliceIndex = Shader.PropertyToID("_Crest_WaveBufferSliceIndex");
            public static readonly int s_AverageWavelength = Shader.PropertyToID("_Crest_AverageWavelength");
            public static readonly int s_RespectShallowWaterAttenuation = Shader.PropertyToID("_Crest_RespectShallowWaterAttenuation");
            public static readonly int s_MaximumAttenuationDepth = Shader.PropertyToID("_Crest_MaximumAttenuationDepth");
            public static readonly int s_AxisX = Shader.PropertyToID("_Crest_AxisX");
            public static readonly int s_SeaLevelOnly = Shader.PropertyToID("_Crest_SeaLevelOnly");
            public static readonly int s_WaveBufferAttenuation = Shader.PropertyToID("_Crest_WaveBufferAttenuation");
        }

        private protected virtual WaveSpectrum DefaultSpectrum => WindSpectrum;

        static WaveSpectrum s_WindSpectrum;
        private protected static WaveSpectrum WindSpectrum
        {
            get
            {
                if (s_WindSpectrum == null)
                {
                    s_WindSpectrum = ScriptableObject.CreateInstance<WaveSpectrum>();
                    s_WindSpectrum.name = "Wind Waves (instance)";
                    s_WindSpectrum.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
                }

                return s_WindSpectrum;
            }
        }

        private protected abstract int MinimumResolution { get; }
        private protected abstract int MaximumResolution { get; }

        static ComputeShader s_TransferWavesComputeShader;
        static LocalKeyword s_KeywordTexture;
        static LocalKeyword s_KeywordTextureBlend;
        readonly Vector4[] _WaveBufferParameters = new Vector4[Lod.k_MaximumSlices];

        internal static int s_RenderPassOverride = -1;

        private protected WaveSpectrum _ActiveSpectrum = null;
        private protected Vector2 PrimaryWaveDirection => new(Mathf.Cos(Mathf.PI * WaveDirectionHeadingAngle / 180f), Mathf.Sin(Mathf.PI * WaveDirectionHeadingAngle / 180f));

        /// <summary>
        /// The wind speed in kilometers per hour (KPH).
        /// </summary>
        /// <remarks>
        /// Wind speed can come from this component or the <see cref="WaterRenderer"/>.
        /// </remarks>
        public float WindSpeedKPH => _OverrideGlobalWindSpeed || WaterRenderer.Instance == null ? _WindSpeed : WaterRenderer.Instance.WindSpeed;

        /// <summary>
        /// The wind speed in meters per second (MPS).
        /// </summary>
        /// <remarks>
        /// Wind speed can come from this component or the <see cref="WaterRenderer"/>.
        /// </remarks>
        public float WindSpeedMPS => WindSpeedKPH / 3.6f;

        private protected ShapeWaves()
        {
            _FollowHorizontalWaveMotion = true;
        }

        private protected override void Attach()
        {
            base.Attach();
            _Reporter ??= new(this);
            _DisplacementReporter = _Reporter;
            _WaveDisplacementReporter = _Reporter;
        }

        private protected override void Detach()
        {
            base.Detach();
            _DisplacementReporter = null;
            _WaveDisplacementReporter = null;
        }

        internal enum WindSpeedSource
        {
            None, // Wind Speed is at maximum.
            ShapeWaves,
            WaterRenderer,
        }

        internal WindSpeedSource GetWindSpeedSource()
        {
            if (WindSpeedKPH >= WaterRenderer.k_MaximumWindSpeedKPH)
            {
                return WindSpeedSource.None;
            }

            return OverrideGlobalWindSpeed ? WindSpeedSource.ShapeWaves : WindSpeedSource.WaterRenderer;
        }

        internal override void Draw(Lod simulation, CommandBuffer buffer, RenderTargetIdentifier target, int pass = -1, float weight = 1f, int slice = -1)
        {
            if (weight * Weight <= 0f)
            {
                return;
            }

            // Iterating over slices which means this is non compute so pass to graphics draw.
            if (!IsCompute)
            {
                GraphicsDraw(simulation, buffer, target, pass, weight, slice);
                return;
            }

            if (_FirstCascade < 0 || _LastCascade < 0)
            {
                return;
            }

            var lodCount = simulation.Slices;
            var lodResolution = simulation.Resolution;
            var shape = (AnimatedWavesLod)simulation;

            var wrapper = new PropertyWrapperCompute(buffer, s_TransferWavesComputeShader, 0);

            // Write to per-octave _WaveBuffers (ie pre-combined). Not the same as _AnimatedWaves.
            wrapper.SetTexture(Crest.ShaderIDs.s_Target, target);
            // Input weight. Weight for each octave calculated in compute.
            wrapper.SetFloat(LodInput.ShaderIDs.s_Weight, Weight);

            wrapper.SetInteger(Crest.ShaderIDs.s_Resolution, Resolution);

            var water = shape._Water;

            for (var lodIdx = lodCount - 1; lodIdx >= lodCount - slice; lodIdx--)
            {
                _WaveBufferParameters[lodIdx] = new(-1, -2, 0, 1);

                var found = false;
                var filter = new AnimatedWavesLod.WavelengthFilter(water, lodIdx, lodResolution);

                for (var i = _FirstCascade; i <= _LastCascade; i++)
                {
                    _Wavelength = MinWavelength(i) / shape.WaveResolutionMultiplier;

                    // Do the weight from scratch because this is the real filter.
                    var w = AnimatedWavesLod.FilterByWavelength(filter, _Wavelength) * Weight;

                    if (w <= 0f)
                    {
                        continue;
                    }

                    if (!found)
                    {
                        _WaveBufferParameters[lodIdx].x = i;
                        found = true;
                    }

                    _WaveBufferParameters[lodIdx].y = i;
                }
            }

            // Set transitional weights.
            if (!shape.PreserveWaveQuality)
            {
                _WaveBufferParameters[lodCount - 2].w = 1f - water.ViewerAltitudeLevelAlpha;
            }

            _WaveBufferParameters[lodCount - 1].w = water.ViewerAltitudeLevelAlpha;

            SetRenderParameters(water, wrapper);

            wrapper.SetFloat(ShaderIDs.s_WaveResolutionMultiplier, shape.WaveResolutionMultiplier);
            wrapper.SetFloat(ShaderIDs.s_TransitionalWavelengthThreshold, water.MaximumWavelength(water.LodLevels - 1, simulation.Resolution) * 0.5f);
            wrapper.SetVectorArray(ShaderIDs.s_WaveBufferParameters, _WaveBufferParameters);

            var isTexture = Mode is LodInputMode.Paint or LodInputMode.Texture;
            var isAlphaBlend = Blend is LodInputBlend.Off or LodInputBlend.Alpha or LodInputBlend.AlphaClip;

            wrapper.SetKeyword(s_KeywordTexture, isTexture && !isAlphaBlend);
            wrapper.SetKeyword(s_KeywordTextureBlend, isTexture && isAlphaBlend);

            wrapper.SetFloatArray(ShaderIDs.s_WaveBufferAttenuation, _ActiveSpectrum._Attenuation);

            if (isTexture)
            {
                wrapper.SetInteger(Crest.ShaderIDs.s_Blend, (int)_Blend);
            }

            if (Mode == LodInputMode.Global)
            {
                wrapper.SetBoolean(ShaderIDs.s_SeaLevelOnly, _SeaLevelOnly);

                var threads = shape.Resolution / Lod.k_ThreadGroupSize;
                wrapper.Dispatch(threads, threads, slice);
            }
            else
            {
                base.Draw(simulation, buffer, target, pass, weight, slice);
            }
        }

        void GraphicsDraw(Lod simulation, CommandBuffer buffer, RenderTargetIdentifier target, int pass, float weight, int slice)
        {
            var lod = simulation as AnimatedWavesLod;
            var lodResolution = simulation.Resolution;

            var wrapper = new PropertyWrapperBuffer(buffer);
            SetRenderParameters(simulation._Water, wrapper);

            var isFirst = true;

            for (var i = _FirstCascade; i <= _LastCascade; i++)
            {
                _Wavelength = MinWavelength(i) / lod.WaveResolutionMultiplier;

                // Do the weight from scratch because this is the real filter.
                weight = AnimatedWavesLod.FilterByWavelength(simulation._Water, slice, _Wavelength, lodResolution) * Weight;
                if (weight <= 0f) continue;

                var average = _Wavelength * 1.5f * lod.WaveResolutionMultiplier;
                // We only have one renderer so we need to use global.
                buffer.SetGlobalFloat(ShaderIDs.s_AverageWavelength, average);
                buffer.SetGlobalInt(ShaderIDs.s_WaveBufferSliceIndex, i);

                // Only apply blend mode once per component / LOD. Multiple passes can happen to gather all
                // wavelengths and is incorrect to apply blend mode to those subsequent passes (ie component
                // would be blending against itself).
                if (!isFirst)
                {
                    s_RenderPassOverride = 1;
                }

                isFirst = false;

                base.Draw(simulation, buffer, target, pass, weight, slice);
            }

            // Wavelength must be zero or waves will be filtered beforehand and not be written to every LOD.
            _Wavelength = 0;
            s_RenderPassOverride = -1;
        }

        internal override float Filter(WaterRenderer water, int slice)
        {
            return 1f;
        }

        private protected const int k_CascadeCount = 16;

        // First cascade of wave buffer that has waves and will be rendered.
        private protected int _FirstCascade = -1;
        // Last cascade of wave buffer that has waves and will be rendered.
        // Default to lower than first default to break loops.
        private protected int _LastCascade = -2;

        // Used to populate data on first frame.
        private protected bool _FirstUpdate = true;

        // Wave generation done in Draw. Keeps track to limit to once per frame.
        private protected int _LastGenerateFrameCount = -1;

        internal override bool Enabled => _FirstCascade > -1 && (WaterRenderer.Instance == null || WaterRenderer.Instance.Gravity != 0f) && Mode switch
        {
            LodInputMode.Global => enabled && s_TransferWavesComputeShader != null,
            _ => base.Enabled,
        };

        internal override LodInputMode DefaultMode => LodInputMode.Global;
        internal override int Pass => (int)DisplacementPass.LodDependent;

        private protected override bool FollowHorizontalMotion => true;

        float _Wavelength;

        private protected RenderTexture _WaveBuffers;
        internal RenderTexture WaveBuffer => _WaveBuffers;

        internal Rect _Rect;

        private protected Vector2 _MaximumDisplacement;
        private protected float MaximumReportedHorizontalDisplacement { get; set; }
        private protected float MaximumReportedVerticalDisplacement { get; set; }
        private protected float MaximumReportedWavesDisplacement { get; set; }


        private protected bool UpdateDataEachFrame
        {
            get
            {
                var updateDataEachFrame = _EvaluateSpectrumAtRunTimeEveryFrame;
#if UNITY_EDITOR
                if (!Application.isPlaying) updateDataEachFrame = true;
#endif
                return updateDataEachFrame;
            }
        }

        /// <summary>
        /// Min wavelength for a cascade in the wave buffer. Does not depend on viewpoint.
        /// </summary>
        private protected float MinWavelength(int cascadeIdx)
        {
            var diameter = 0.5f * (1 << cascadeIdx);
            // Matches constant WAVE_SAMPLE_FACTOR in FFTSpectrum.compute
            return diameter / 8f;
        }

        private protected abstract void ReportMaxDisplacement(WaterRenderer water);

        private protected override void OnUpdate(WaterRenderer water)
        {
            base.OnUpdate(water);

            _ActiveSpectrum = _Spectrum != null ? _Spectrum : DefaultSpectrum;

            _FirstUpdate = false;
        }

        private protected virtual void SetRenderParameters<T>(WaterRenderer water, T wrapper) where T : IPropertyWrapper
        {
            wrapper.SetTexture(ShaderIDs.s_WaveBuffer, _WaveBuffers);
            wrapper.SetFloat(ShaderIDs.s_RespectShallowWaterAttenuation, _RespectShallowWaterAttenuation);
            wrapper.SetFloat(ShaderIDs.s_MaximumAttenuationDepth, water._AnimatedWavesLod.ShallowsMaximumDepth);
        }

        private protected override void Initialize()
        {
            base.Initialize();

            WaterResources.Instance.AfterEnabled -= InitializeResources;
            WaterResources.Instance.AfterEnabled += InitializeResources;
            InitializeResources();

            _FirstUpdate = true;

            // Initialise with spectrum
            if (_Spectrum != null)
            {
                _ActiveSpectrum = _Spectrum;
            }

            if (_ActiveSpectrum == null)
            {
                _ActiveSpectrum = DefaultSpectrum;
            }
        }

        private protected override void OnDisable()
        {
            base.OnDisable();

            WaterResources.Instance.AfterEnabled -= InitializeResources;
        }

        void InitializeResources()
        {
            s_TransferWavesComputeShader = WaterResources.Instance.Compute._ShapeWavesTransfer;
            s_KeywordTexture = WaterResources.Instance.Keywords.AnimatedWavesTransferWavesTexture;
            s_KeywordTextureBlend = WaterResources.Instance.Keywords.AnimatedWavesTransferWavesTextureBlend;
        }

        bool ReportDisplacement(WaterRenderer water, ref Rect bounds, ref float horizontal, ref float vertical)
        {
            if (!Enabled)
            {
                return false;
            }

            if (Mode == LodInputMode.Global)
            {
                // Global is always additive.
                horizontal += MaximumReportedHorizontalDisplacement;
                vertical += MaximumReportedVerticalDisplacement;
                return true;
            }

            _Rect = Data.Rect;

            if (bounds.Overlaps(_Rect, false))
            {
                var nh = horizontal;
                var nv = vertical;
                switch (Blend)
                {
                    case LodInputBlend.Off:
                        nh = MaximumReportedHorizontalDisplacement;
                        nv = MaximumReportedVerticalDisplacement;
                        break;
                    case LodInputBlend.Additive:
                        nh += MaximumReportedHorizontalDisplacement;
                        nv += MaximumReportedVerticalDisplacement;
                        break;
                    case LodInputBlend.Alpha:
                    case LodInputBlend.AlphaClip:
                        nh = Mathf.Max(nh, MaximumReportedHorizontalDisplacement);
                        nv = Mathf.Max(nh, MaximumReportedVerticalDisplacement);
                        break;
                }

                horizontal = Mathf.Max(horizontal, nh);
                vertical = Mathf.Max(vertical, nv);

                return true;
            }

            return false;
        }

        float ReportWaveDisplacement(WaterRenderer water, float displacement)
        {
            if (Mode == LodInputMode.Global)
            {
                return displacement + MaximumReportedWavesDisplacement;
            }

            if (!_IncludeInDropDetailHeightBasedOnWaves)
            {
                return displacement;
            }

            // TODO: use bounds to transition slowly to avoid pops.
            if (_Rect.Contains(water.Position.XZ()))
            {
                displacement = Blend switch
                {
                    LodInputBlend.Off => MaximumReportedWavesDisplacement,
                    LodInputBlend.Additive => displacement + MaximumReportedWavesDisplacement,
                    LodInputBlend.Alpha or LodInputBlend.AlphaClip => Mathf.Max(displacement, MaximumReportedWavesDisplacement),
                    _ => MaximumReportedWavesDisplacement,
                };
            }

            return displacement;
        }

        float GetWaveDirectionHeadingAngle()
        {
            return _OverrideGlobalWindDirection || WaterRenderer.Instance == null ? _WaveDirectionHeadingAngle : WaterRenderer.Instance.WindDirection;
        }

        internal int GetResolution()
        {
            return Mathf.Clamp(_Resolution, MinimumResolution, MaximumResolution);
        }
    }

    partial class ShapeWaves
    {
        Reporter _Reporter;

        sealed class Reporter : IReportsDisplacement, IReportWaveDisplacement
        {
            readonly ShapeWaves _Input;
            public Reporter(ShapeWaves input) => _Input = input;
            public bool ReportDisplacement(WaterRenderer water, ref Rect bounds, ref float horizontal, ref float vertical) => _Input.ReportDisplacement(water, ref bounds, ref horizontal, ref vertical);
            public float ReportWaveDisplacement(WaterRenderer water, float displacement) => _Input.ReportWaveDisplacement(water, displacement);
        }
    }

    partial class ShapeWaves
    {
        static int s_InstanceCount = 0;

        private protected override void Awake()
        {
            base.Awake();
            s_InstanceCount++;
        }

        private protected virtual void OnDestroy()
        {
            if (--s_InstanceCount <= 0)
            {
                if (s_WindSpectrum != null)
                {
                    Helpers.Destroy(s_WindSpectrum);
                    s_WindSpectrum = null;
                }
            }
        }
    }

    partial class ShapeWaves
    {
        private protected override int Version => Mathf.Max(base.Version, 3);

        [HideInInspector, SerializeField]
        AlphaSource _AlphaSource;
        enum AlphaSource { AlwaysOne, FromZero, FromZeroNormalized }

        private protected override void OnMigrate()
        {
            base.OnMigrate();

            // Version 1
            // - Merge Alpha Source into Blend.
            // - Rename and invert Spectrum Fixed at Run-Time
            if (_Version < 1)
            {
                if (_Blend == LodInputBlend.Alpha)
                {
                    _Blend = _AlphaSource switch
                    {
                        AlphaSource.AlwaysOne => LodInputBlend.Off,
                        AlphaSource.FromZero => LodInputBlend.Alpha,
                        AlphaSource.FromZeroNormalized => LodInputBlend.AlphaClip,
                        _ => _Blend, // Linter complained (linter has one off error).
                    };
                }

                _EvaluateSpectrumAtRunTimeEveryFrame = !_EvaluateSpectrumAtRunTimeEveryFrame;
            }

            // Version 2
            // - Global wind direction
            if (_Version < 2)
            {
                _OverrideGlobalWindDirection = true;
            }

            if (_Version < 3)
            {
                _SeaLevelOnly = false;
            }
        }
    }

#if UNITY_EDITOR
    partial class ShapeWaves
    {
        private protected override void Reset()
        {
            base.Reset();

            if (_Mode != LodInputMode.Global)
            {
                _OverrideGlobalWindSpeed = true;
                _OverrideGlobalWindDirection = true;
            }
        }
    }
#endif
}
