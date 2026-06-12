// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

// BIRP fallback not really tested yet - shaders need fixing up.

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using WaveHarmonic.Crest.Internal;
using WaveHarmonic.Crest.Utility;

namespace WaveHarmonic.Crest
{
    /// <summary>
    /// Stores shadowing data to use during water shading.
    /// </summary>
    /// <remarks>
    /// Shadowing is persistent and supports sampling across many frames and jittered
    /// sampling for (very) soft shadows. Soft shadows is red, hard shadows is green.
    /// </remarks>
    [FilterEnum(nameof(_TextureFormatMode), Filtered.Mode.Exclude, (int)LodTextureFormatMode.Automatic)]
    public sealed partial class ShadowLod : PersistentLod
    {
        [@Space(10)]

        [Tooltip("Whether to vary soft shadow jitter by scattering/absorption density.")]
        [@DecoratedField, SerializeField]
        bool _DynamicSoftShadows = true;

        [Tooltip("Factor control for dynamic soft jitter.")]
        [@Show(nameof(_DynamicSoftShadows))]
        [@Range(0f, 1f, Range.Clamp.Minimum)]
        [SerializeField]
        float _SoftJitterExtinctionFactor = 0.75f;

        [Tooltip("Jitter diameter for soft shadows, controls softness of this shadowing component.")]
        [@Hide(nameof(_DynamicSoftShadows))]
        [@Range(0f, k_MaximumJitter)]
        [@GenerateAPI]
        [SerializeField]
        internal float _JitterDiameterSoft = 15f;

        [Tooltip("Current frame weight for accumulation over frames for soft shadows.\n\nRoughly means 'responsiveness' for soft shadows.")]
        [@Range(0f, 1f)]
        [@GenerateAPI]
        [SerializeField]
        internal float _CurrentFrameWeightSoft = 0.03f;

        [Tooltip("Jitter diameter for hard shadows, controls softness of this shadowing component.")]
        [@Range(0f, k_MaximumJitter)]
        [@GenerateAPI]
        [SerializeField]
        internal float _JitterDiameterHard = 0.6f;

        [Tooltip("Current frame weight for accumulation over frames for hard shadows.\n\nRoughly means 'responsiveness' for hard shadows.")]
        [@Range(0f, 1f)]
        [@GenerateAPI]
        [SerializeField]
        internal float _CurrentFrameWeightHard = 0.15f;

        [@Space(10)]

        [Tooltip("Whether to disable the null light warning, use this if you assign it dynamically and expect it to be null at points")]
        [@DecoratedField, SerializeField]
        internal bool _AllowNullLight = false;

        [Tooltip("Whether to disable the no shadows warning. Use this if you toggle the shadows on the primary light dynamically.")]
        [@DecoratedField, SerializeField]
        internal bool _AllowNoShadows = false;

        static new class ShaderIDs
        {
            public static readonly int s_DynamicSoftShadowsFactor = Shader.PropertyToID("g_Crest_DynamicSoftShadowsFactor");
            public static readonly int s_SampleColorMap = Shader.PropertyToID("_Crest_SampleColorMap");
            public static readonly int s_CenterPos = Shader.PropertyToID("_Crest_CenterPos");
            public static readonly int s_Scale = Shader.PropertyToID("_Crest_Scale");
            public static readonly int s_JitterDiameters_CurrentFrameWeights = Shader.PropertyToID("_Crest_JitterDiameters_CurrentFrameWeights");
            public static readonly int s_MainCameraProjectionMatrix = Shader.PropertyToID("_Crest_MainCameraProjectionMatrix");

            // BIRP only.
            public static readonly int s_ShadowPassExecuteLastFrame = Shader.PropertyToID("_Crest_ShadowPassExecuteLastFrame");
            public static readonly int s_ClearShadows = Shader.PropertyToID("_Crest_ClearShadows");
        }

        const string k_DrawLodSample = "Sample";

        const float k_MaximumJitter = 32f;

        internal static readonly Color s_GizmoColor = new(0f, 0f, 0f, 0.5f);
        internal static bool s_ProcessData = true;

        internal override string ID => "Shadow";
        internal override string Name => "Shadows";
        internal override Color GizmoColor => s_GizmoColor;
        private protected override Color ClearColor => Color.black;
        private protected override bool NeedToReadWriteTextureData => true;
        internal override int BufferCount => 2;
        internal override bool SkipEndOfFrame => true;

        private protected override GraphicsFormat RequestedTextureFormat => _TextureFormatMode switch
        {
            LodTextureFormatMode.Performance => GraphicsFormat.R8G8_UNorm,
            LodTextureFormatMode.Precision => GraphicsFormat.R16G16_UNorm,
            LodTextureFormatMode.Manual => _TextureFormat,
            _ => throw new System.NotImplementedException(),
        };

        Light _Light;

        // SRP version needs access to this externally, hence internal get.
        internal CommandBuffer CopyShadowMapBuffer { get; private set; }
        PropertyWrapperMaterial[] _RenderMaterial;

        enum Error
        {
            None,
            NoLight,
            NoShadows,
            IncorrectLightType,
        }

        Error _Error;

        private protected override int Kernel => (int)RenderPipelineHelper.RenderPipeline;
        private protected override bool SkipFlipBuffers => true;
        private protected override ComputeShader SimulationShader => WaterResources.Instance.Compute._UpdateShadow;

        bool _IsSimulationBuffer;


        internal override void Initialize()
        {
            if (WaterResources.Instance.Shaders._UpdateShadow == null)
            {
                _Valid = false;
                return;
            }

            var isShadowsDisabled = false;

            if (RenderPipelineHelper.IsLegacy)
            {
                if (QualitySettings.shadows == UnityEngine.ShadowQuality.Disable)
                {
                    isShadowsDisabled = true;
                }
            }
            else if (RenderPipelineHelper.IsUniversal)
            {
#if d_UnityURP
                var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

                if (asset.mainLightRenderingMode == LightRenderingMode.Disabled)
                {
                    Debug.LogWarning("Crest: Main Light must be enabled to enable water shadowing.", _Water);
                    _Valid = false;
                    return;
                }

                isShadowsDisabled = !asset.supportsMainLightShadows;
#endif
            }

            if (isShadowsDisabled)
            {
                Debug.LogWarning("Crest: Shadows must be enabled in the quality settings to enable water shadowing.", _Water);
                _Valid = false;
                return;
            }

            base.Initialize();
        }

        internal override void SetGlobals(bool enable)
        {
            base.SetGlobals(enable);
            Shader.SetGlobalFloat(ShaderIDs.s_DynamicSoftShadowsFactor, 1f);

            if (RenderPipelineHelper.IsLegacy)
            {
                Helpers.SetGlobalBoolean(ShaderIDs.s_ShadowPassExecuteLastFrame, true);
            }
        }

        internal override void Enable()
        {
            base.Enable();

            CleanUpShadowCommandBuffers();

            if (RenderPipelineHelper.IsHighDefinition)
            {
#if d_UnityHDRP
                SampleShadowsHDRP.Enable(_Water);
#endif
            }
            else if (RenderPipelineHelper.IsUniversal)
            {
#if d_UnityURP
                SampleShadowsURP.Enable(_Water);
#endif
            }
        }

        internal override void Disable()
        {
            base.Disable();

            CleanUpShadowCommandBuffers();
            Shader.SetGlobalFloat(ShaderIDs.s_DynamicSoftShadowsFactor, 1f);

#if d_UnityHDRP
            SampleShadowsHDRP.Disable();
#endif
        }

        internal override void Destroy()
        {
            base.Destroy();

            for (var index = 0; index < _RenderMaterial.Length; index++)
            {
                Helpers.Destroy(_RenderMaterial[index].Material);
            }
        }

        private protected override void Allocate()
        {
            base.Allocate();

            {
                _RenderMaterial = new PropertyWrapperMaterial[Slices];
                var shader = WaterResources.Instance.Shaders._UpdateShadow;
                for (var i = 0; i < _RenderMaterial.Length; i++)
                {
                    _RenderMaterial[i] = new(shader);
                    _RenderMaterial[i].SetInteger(Lod.ShaderIDs.s_LodIndex, i);
                }
            }

            // Enable sample shadows custom pass.
            if (RenderPipelineHelper.IsHighDefinition)
            {
#if d_UnityHDRP
                SampleShadowsHDRP.Enable(_Water);
#endif
            }
            else if (RenderPipelineHelper.IsUniversal)
            {
#if d_UnityURP
                SampleShadowsURP.Enable(_Water);
#endif
            }
        }

        /// <summary>
        /// Validates the primary light.
        /// </summary>
        /// <returns>
        /// Whether the light is valid. An invalid light should be treated as a developer error and not recoverable.
        /// </returns>
        bool ValidateLight()
        {
            if (_Light == null)
            {
                if (!_AllowNullLight)
                {
                    if (_Error != Error.NoLight)
                    {
                        Debug.LogWarning($"Crest: Primary light must be specified on {nameof(WaterRenderer)} script to enable shadows.", _Water);
                        _Error = Error.NoLight;
                    }
                    return false;
                }

                return true;
            }

            if (_Light.shadows == LightShadows.None)
            {
                if (!_AllowNoShadows)
                {
                    if (_Error != Error.NoShadows)
                    {
                        Debug.LogWarning("Crest: Shadows must be enabled on primary light to enable water shadowing (types Hard and Soft are equivalent for the water system).", _Light);
                        _Error = Error.NoShadows;
                    }
                    return false;
                }
            }

            if (_Light.type != LightType.Directional)
            {
                if (_Error != Error.IncorrectLightType)
                {
                    Debug.LogWarning("Crest: Primary light must be of type Directional.", _Light);
                    _Error = Error.IncorrectLightType;
                }
                return false;
            }

            _Error = Error.None;
            return true;
        }

        /// <summary>
        /// Stores the primary light.
        /// </summary>
        /// <returns>
        /// Whether there is a light that casts shadows.
        /// </returns>
        bool SetUpLight()
        {
            if (_Light == null)
            {
                _Light = _Water.PrimaryLight;

                if (_Light == null)
                {
                    return false;
                }
            }

            if (_Light.shadows == LightShadows.None)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// May happen if scenes change etc
        /// </summary>
        void ClearBufferIfLightChanged()
        {
            if (_Light != _Water.PrimaryLight)
            {
                Clear(DataTexture);
                Clear(_PersistentDataTexture);
                CleanUpShadowCommandBuffers();
                _Light = null;
            }
        }

        void CleanUpShadowCommandBuffers()
        {
            if (!RenderPipelineHelper.IsLegacy)
            {
                return;
            }

            CopyShadowMapBuffer?.Release();
            CopyShadowMapBuffer = null;
        }

        void Update(CommandBuffer buffer)
        {
            // If disabled then we hit a failure state. Try and recover in edit mode by proceeding.
            if (!_Valid && Application.isPlaying)
            {
                return;
            }

            ClearBufferIfLightChanged();

            var hasShadowCastingLight = SetUpLight();
            // If in play mode, and this becomes false, then we hit a failed state and will not recover.
            _Valid = ValidateLight();

            if (!s_ProcessData || !_Valid || !hasShadowCastingLight)
            {
                if (CopyShadowMapBuffer != null)
                {
                    // If we have a command buffer, then there is likely shadow data so we need to clear it.
                    Clear(DataTexture);
                    Clear(_PersistentDataTexture);
                    CleanUpShadowCommandBuffers();
                }

                return;
            }

            CopyShadowMapBuffer ??= new() { name = WaterRenderer.k_DrawLodData };
            CopyShadowMapBuffer.Clear();

            // clear the shadow collection. it will be overwritten with shadow values IF the shadows render,
            // which only happens if there are (nontransparent) shadow receivers around. this is only reliable
            // in play mode, so don't do it in edit mode.
#if UNITY_EDITOR
            if (Application.isPlaying)
#endif
            {
                buffer.BeginSample(ID);
                CoreUtils.SetRenderTarget(buffer, DataTexture, ClearFlag.Color, ClearColor);
                buffer.EndSample(ID);
            }
        }

        internal override void BuildCommandBuffer(WaterRenderer water, CommandBuffer buffer)
        {
            var isSimulationBuffer = buffer == _Water.SimulationBuffer;

            _IsSimulationBuffer = isSimulationBuffer;

            if (isSimulationBuffer)
            {
                var skip = true;
                if (RenderPipelineHelper.IsLegacy)
                {
                    // If no shadow casters are present, BIRP will not execute the command buffer
                    // leaving outdated shadows in data. We set a flag to determine if the command
                    // buffer was executed. There will a 1-frame delay.
                    skip = Helpers.GetGlobalBoolean(ShaderIDs.s_ShadowPassExecuteLastFrame);
                    Helpers.SetGlobalBoolean(ShaderIDs.s_ShadowPassExecuteLastFrame, false);
                }

                Update(buffer);

                // Only do a partial update when called by WaterRenderer as we want to execute
                // with the camera's command buffer (in frame).
                if (skip)
                {
                    return;
                }
            }

            // Cache the camera for further down.
            var camera = water.Viewer;

            if (camera == null)
            {
                return;
            }

            base.BuildCommandBuffer(water, buffer);

            if (RenderPipelineHelper.IsLegacy && !isSimulationBuffer)
            {
                buffer.SetGlobalBoolean(ShaderIDs.s_ShadowPassExecuteLastFrame, true);
            }
        }

        private protected override void SetAdditionalSimulationParameters(PropertyWrapperCompute properties)
        {
            base.SetAdditionalSimulationParameters(properties);

            var jitter = new Vector4
            (
                _JitterDiameterSoft,
                _JitterDiameterHard,
                _CurrentFrameWeightSoft,
                _CurrentFrameWeightHard
            );

            var waterMaterial = _Water.Surface.Material;
            var hasColor = waterMaterial != null && waterMaterial.HasVector(WaterRenderer.ShaderIDs.s_Absorption) && waterMaterial.HasProperty(WaterRenderer.ShaderIDs.s_Scattering);
            var absorption = hasColor ? waterMaterial.GetVector(WaterRenderer.ShaderIDs.s_Absorption).XYZ() : Vector3.zero;
            var scattering = hasColor ? ((Vector4)waterMaterial.GetColor(WaterRenderer.ShaderIDs.s_Scattering).MaybeLinear()).XYZ() : Vector3.zero;
            var sampleAbsorption = _Water.AbsorptionLod.Enabled;
            var sampleScattering = _Water.ScatteringLod.Enabled;
            var sampleColor = sampleAbsorption || sampleScattering;

            if (_DynamicSoftShadows && hasColor && !sampleColor)
            {
                // This approximates varying of soft shadowing by volume scattering/absorption density.
                var extinction = absorption + scattering;
                var factor = Mathf.Clamp01(Mathf.Min(Mathf.Min(extinction.x, extinction.y), extinction.z) * _SoftJitterExtinctionFactor);
                jitter.x = (1f - factor) * k_MaximumJitter;
            }

            Shader.SetGlobalFloat(ShaderIDs.s_DynamicSoftShadowsFactor, _DynamicSoftShadows ? _SoftJitterExtinctionFactor : 1f);

            var camera = _Water.Viewer;

            properties.SetVector(ShaderIDs.s_JitterDiameters_CurrentFrameWeights, jitter);
            properties.SetMatrix(ShaderIDs.s_MainCameraProjectionMatrix, GL.GetGPUProjectionMatrix(camera.projectionMatrix, renderIntoTexture: true) * camera.worldToCameraMatrix);

            // Dynamic Soft Shadows.
            properties.SetBoolean(ShaderIDs.s_SampleColorMap, _DynamicSoftShadows && sampleColor);

            if (_DynamicSoftShadows && sampleColor)
            {
                properties.SetVector(WaterRenderer.ShaderIDs.s_Absorption, absorption);
                properties.SetVector(WaterRenderer.ShaderIDs.s_Scattering, scattering);
            }

            if (RenderPipelineHelper.IsLegacy)
            {
                // If we are executing the simulation buffer, then we are clearing.
                properties.SetBoolean(ShaderIDs.s_ClearShadows, _IsSimulationBuffer);

                properties.SetKeyword(new LocalKeyword(SimulationShader, "SHADOWS_SINGLE_CASCADE"), QualitySettings.shadowCascades == 1);
                properties.SetKeyword(new LocalKeyword(SimulationShader, "SHADOWS_SPLIT_SPHERES"), QualitySettings.shadowProjection == ShadowProjection.StableFit);
            }
        }

        internal ShadowLod()
        {
            _Enabled = true;
            _TextureFormat = GraphicsFormat.R8G8_UNorm;
            _Blur = true;
        }

        internal static SortedList<int, ILodInput> s_Inputs = new(Helpers.DuplicateComparison);
        private protected override SortedList<int, ILodInput> Inputs => s_Inputs;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void OnLoad()
        {
            s_Inputs.Clear();
        }
    }
}
