// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using WaveHarmonic.Crest.Internal;

namespace WaveHarmonic.Crest
{
    abstract class BakedWaveData : CustomScriptableObject
    {
        public abstract ICollisionProvider CreateCollisionProvider();
        public abstract float WindSpeed { get; }
    }

    //
    // Collision
    //

    /// <summary>
    /// The source of collisions (ie water shape).
    /// </summary>
    [System.Obsolete("Please use QuerySource and LodQuerySource.")]
    [@GenerateDoc]
    public enum CollisionSource
    {
        /// <inheritdoc cref="Generated.CollisionSource.None"/>
        [Tooltip("No collision source. Flat water.")]
        None = 0,

        // GerstnerWavesCPU = 1,

        /// <inheritdoc cref="Generated.CollisionSource.GPU"/>
        [Tooltip("Uses AsyncGPUReadback to retrieve data from GPU to CPU.\n\nThis is the most optimal approach.")]
        GPU = 2,

        /// <inheritdoc cref="Generated.CollisionSource.CPU"/>
        [Tooltip("Computes data entirely on the CPU.")]
        CPU = 3,
    }

    /// <summary>
    /// The pass to render displacement into.
    /// </summary>
    [@GenerateDoc]
    public enum DisplacementPass
    {
        /// <inheritdoc cref="Generated.DisplacementPass.LodDependent"/>
        [Tooltip("Displacement that is dependent on an LOD (eg waves).\n\nUses filtering to determine which LOD to write to.")]
        LodDependent,

        /// <inheritdoc cref="Generated.DisplacementPass.LodIndependent"/>
        [Tooltip("Renders to all LODs.")]
        LodIndependent,

        /// <inheritdoc cref="Generated.DisplacementPass.LodIndependentLast"/>
        [Tooltip("Renders to all LODs, but as a separate pass.\n\nTypically used to render visual displacement which does not affect collisions.")]
        [InspectorName("Lod Independent (Last)")]
        LodIndependentLast,
    }

    /// <summary>
    /// Flags to enable extra collsion layers.
    /// </summary>
    [System.Flags]
    [@GenerateDoc]
    public enum CollisionLayers
    {
        // NOTE: numbers must be in order for defaults to work (everything first).

        /// <inheritdoc cref="Generated.CollisionLayers.Nothing"/>
        [Tooltip("No extra layers (ie single layer).")]
        Nothing = 0,

        /// <inheritdoc cref="Generated.CollisionLayers.DynamicWaves"/>
        [Tooltip("Separate layer for dynamic waves.\n\nDynamic waves are normally combined together for efficiency. By enabling this layer, dynamic waves are combined and added in a separate pass.")]
        DynamicWaves = 1 << 1,

        /// <inheritdoc cref="Generated.CollisionLayers.Displacement"/>
        [Tooltip("Extra displacement layer for visual displacement.")]
        Displacement = 1 << 2,

        /// <inheritdoc cref="Generated.CollisionLayers.Everything"/>
        [Tooltip("All layers.")]
        Everything = ~0,
    }

    /// <summary>
    /// The wave sampling method to determine quality and performance.
    /// </summary>
    [@GenerateDoc]
    public enum WaveSampling
    {
        /// <inheritdoc cref="Generated.WaveSampling.Automatic"/>
        [Tooltip("Automatically chooses the other options as needed (512+ resolution needs precision).")]
        Automatic,

        /// <inheritdoc cref="Generated.WaveSampling.Performance"/>
        [Tooltip("Reduces samples by copying waves from higher LODs to lower LODs.\n\nBest for resolutions lower than 512.")]
        Performance,

        /// <inheritdoc cref="Generated.WaveSampling.Precision"/>
        [Tooltip("Samples directly from the wave buffers to preserve wave quality.\n\nNeeded for higher resolutions (512+). Higher LOD counts can also benefit with this enabled.")]
        Precision,
    }

    /// <summary>
    /// Captures waves/shape that is drawn kinematically - there is no frame-to-frame
    /// state.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>
    ///   A combine pass is done which combines downwards from low detail LODs into
    ///   the high detail LODs.
    /// </item>
    /// <item>
    ///   The LOD data is passed to the water material when the surface is drawn.
    /// <item>
    /// </item>
    ///   <see cref="DynamicWavesLod"/> adds its results into this data. They piggy back
    ///   off the combine pass and subsequent assignment to the water material.
    /// </item>
    /// </list>
    /// The RGB channels are the XYZ displacement from a rest plane at water level to
    /// the corresponding displaced position on the surface.
    /// </remarks>
    [@FilterEnum(nameof(_TextureFormatMode), Filtered.Mode.Exclude, (int)LodTextureFormatMode.Automatic)]
    public sealed partial class AnimatedWavesLod : Lod<ICollisionProvider>
    {
        [Tooltip("Collision layers to enable.\n\nSome layers will have overhead with CPU, GPU and memory.")]
        [@Enable(nameof(_QuerySource), nameof(LodQuerySource.GPU))]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        internal CollisionLayers _CollisionLayers = CollisionLayers.Everything;

        [@Enable(nameof(_QuerySource), nameof(LodQuerySource.CPU))]
        [@DecoratedField, SerializeField]
        internal BakedWaveData _BakedWaveData;

        [@Space(10)]

        [Tooltip("The wave sampling method to determine quality and performance.")]
        [@GenerateAPI]
        [@DecoratedField]
        [SerializeField]
        internal WaveSampling _WaveSampling;

        [Tooltip("Shifts wavelengths to maintain quality for higher resolutions.\n\nSet this to 2 to improve wave quality. In some cases like flowing rivers, this can make a substantial difference to visual stability. We recommend doubling the Resolution on the WaterRenderer component to preserve detail after making this change.")]
        [@Enable(nameof(_WaveSampling), nameof(WaveSampling.Performance))]
        [@Range(1f, 4f)]
        [@GenerateAPI(Getter.Custom)]
        [SerializeField]
        float _WaveResolutionMultiplier = 1f;

        [@Space(10)]

        [Tooltip("How much waves are dampened in shallow water.")]
        [@Range(0f, 1f)]
        [@GenerateAPI]
        [SerializeField]
        float _AttenuationInShallows = 0.95f;

        [Tooltip("Any water deeper than this will receive full wave strength.\n\nThe lower the value, the less effective the depth cache will be at attenuating very large waves. Set to the maximum value (1,000) to disable.")]
        [@Range(1f, 1000f)]
        [@GenerateAPI]
        [SerializeField]
        float _ShallowsMaximumDepth = 1000f;


        const string k_DrawCombine = "Combine";


        internal static new partial class ShaderIDs
        {
            public static readonly int s_WaveBuffer = Shader.PropertyToID("_Crest_WaveBuffer");
            public static readonly int s_DynamicWavesTarget = Shader.PropertyToID("_Crest_DynamicWavesTarget");
            public static readonly int s_AttenuationInShallows = Shader.PropertyToID("_Crest_AttenuationInShallows");
        }

        internal static readonly Color s_GizmoColor = new(0f, 1f, 0f, 0.5f);

        /// <summary>
        /// Turn shape combine pass on/off. Debug only - stripped in builds.
        /// </summary>
        internal static bool s_Combine = true;

        WaterResources.ShapeCombineCompute _CombineShader;
        RenderTexture _PersistentDataTexture;

        internal override string ID => "AnimatedWaves";
        internal override string Name => "Animated Waves";
        internal override Color GizmoColor => s_GizmoColor;
        private protected override bool NeedToReadWriteTextureData => true;
        private protected override Color ClearColor => Color.black;
        internal override int BufferCount => _Water.WriteMotionVectors ? 2 : 1;

        // NOTE: Tried RGB111110Float but errors becomes visible. One option would be to use a UNORM setup.
        private protected override GraphicsFormat RequestedTextureFormat => _TextureFormatMode switch
        {
            LodTextureFormatMode.Performance => GraphicsFormat.R16G16B16A16_SFloat,
            LodTextureFormatMode.Precision => GraphicsFormat.R32G32B32A32_SFloat,
            LodTextureFormatMode.Manual => _TextureFormat,
            _ => throw new System.NotImplementedException(),
        };

        internal bool PreserveWaveQuality => WaveSampling switch
        {
            WaveSampling.Automatic => Resolution >= 512,
            WaveSampling.Performance => false,
            WaveSampling.Precision => true,
            _ => throw new System.NotImplementedException(),
        };

        internal AnimatedWavesLod()
        {
            _Enabled = true;
            _OverrideResolution = false;
            _TextureFormat = GraphicsFormat.R16G16B16A16_SFloat;
        }

        internal override void Initialize()
        {
            _CombineShader = WaterResources.Instance._ComputeLibrary._ShapeCombineCompute;
            if (_CombineShader._Shader == null)
            {
                _Valid = false;
                return;
            }

            base.Initialize();

            if (Persistent && !_Water.IsMultipleViewpointMode)
            {
                _PersistentDataTexture = CreateLodDataTextures();
            }
        }

        internal override void Destroy()
        {
            base.Destroy();

            if (_PersistentDataTexture != null) _PersistentDataTexture.Release();
            Helpers.Destroy(_PersistentDataTexture);

            foreach (var data in _AdditionalCameraData.Values)
            {
                if (data != null) data.Release();
                Helpers.Destroy(data);
            }

            _AdditionalCameraData.Clear();
        }

        internal override void SetGlobals(bool enable)
        {
            base.SetGlobals(enable);

            if (_Water.IsRunningWithoutGraphics)
            {
                return;
            }

            if (Persistent)
            {
                Shader.SetGlobalTexture(_TextureSourceShaderID, enable && Enabled ? _PersistentDataTexture : NullTexture);
            }
        }

        internal override void BuildCommandBuffer(WaterRenderer water, CommandBuffer buffer)
        {
            buffer.BeginSample(ID);

            FlipBuffers(buffer);

            // Flip textures
            if (Persistent)
            {
                (_PersistentDataTexture, DataTexture) = (DataTexture, _PersistentDataTexture);

                // Update current and previous. Latter for MVs and/or VFX.
                buffer.SetGlobalTexture(_TextureSourceShaderID, _PersistentDataTexture);
                buffer.SetGlobalTexture(_TextureShaderID, DataTexture);
            }

            Shader.SetGlobalFloat(ShaderIDs.s_AttenuationInShallows, _AttenuationInShallows);

            // Get temporary buffer to store waves. They will be copied in the combine pass.
            buffer.GetTemporaryRT(ShaderIDs.s_WaveBuffer, DataTexture.descriptor);
            CoreUtils.SetRenderTarget(buffer, ShaderIDs.s_WaveBuffer, ClearFlag.Color, ClearColor);

            // Custom clear because clear not working.
            if (Helpers.RequiresCustomClear && WaterResources.Instance.Compute._Clear != null)
            {
                var compute = WaterResources.Instance._ComputeLibrary._ClearCompute;
                var wrapper = new PropertyWrapperCompute(buffer, compute._Shader, compute._KernelClearTarget);
                compute.SetVariantForFormat(wrapper, DataTexture.graphicsFormat);
                wrapper.SetTexture(Crest.ShaderIDs.s_Target, ShaderIDs.s_WaveBuffer);
                wrapper.SetVector(Crest.ShaderIDs.s_ClearMask, Color.white);
                wrapper.SetVector(Crest.ShaderIDs.s_ClearColor, ClearColor);
                wrapper.Dispatch(Resolution / k_ThreadGroupSizeX, Resolution / k_ThreadGroupSizeY, Slices);
            }

            // LOD dependent data.
            // Write to per-octave _WaveBuffers. Not the same as _AnimatedWaves.
            // Draw any data with lod preference.
            SubmitDraws(buffer, s_Inputs, ShaderIDs.s_WaveBuffer, (int)DisplacementPass.LodDependent, filter: true);

            var lastSlice = Slices - 1;
            var threadSize = Resolution / k_ThreadGroupSize;

            // Combine the LODs - copy results from biggest LOD down to LOD 0
            {
                var wrapper = new PropertyWrapperCompute
                (
                    buffer,
                    _CombineShader._Shader,
                    PreserveWaveQuality
                        ? _CombineShader._CopyAnimatedWavesKernel
                        : _CombineShader._CombineAnimatedWavesKernel
                );

                if (_Water._DynamicWavesLod.Enabled)
                {
                    _Water._DynamicWavesLod.Bind(wrapper);
                }

                // Start with last LOD which does not combine.
                wrapper.SetKeyword(_CombineShader._CombineKeyword, false);
                wrapper.SetKeyword(_CombineShader._DynamicWavesKeyword, _Water._DynamicWavesLod.Enabled && !PreserveWaveQuality && !_CollisionLayers.HasFlag(CollisionLayers.DynamicWaves));

                // The per-octave wave buffers we read from.
                wrapper.SetTexture(ShaderIDs.s_WaveBuffer, ShaderIDs.s_WaveBuffer);

                // Set the animated waves texture where we read/write to combine the results.
                wrapper.SetTexture(Crest.ShaderIDs.s_Target, DataTexture);

                if (PreserveWaveQuality)
                {
                    wrapper.Dispatch(threadSize, threadSize, Slices);
                }
                else
                {
                    buffer.BeginSample(k_DrawCombine);

                    // Combine waves.
                    for (var slice = lastSlice; slice >= 0; slice--)
                    {
                        wrapper.SetInteger(Lod.ShaderIDs.s_LodIndex, slice);
                        wrapper.Dispatch(threadSize, threadSize, 1);

                        if (slice == lastSlice)
                        {
                            // From here on, use combine.
                            wrapper.SetKeyword(_CombineShader._CombineKeyword, s_Combine);
                        }
                    }

                    buffer.EndSample(k_DrawCombine);
                }
            }

            buffer.ReleaseTemporaryRT(ShaderIDs.s_WaveBuffer);

            // LOD independent data.
            // Draw any data that did not express a preference for one lod or another.
            var drawn = SubmitDraws(buffer, s_Inputs, DataTexture, (int)DisplacementPass.LodIndependent);

            // Alpha channel is cleared in combine step, but if any inputs draw in post-combine
            // step then alpha may have data.
            if (drawn && WaterResources.Instance.Compute._Clear != null)
            {
                var compute = WaterResources.Instance._ComputeLibrary._ClearCompute;
                var wrapper = new PropertyWrapperCompute(buffer, compute._Shader, compute._KernelClearTarget);
                compute.SetVariantForFormat(wrapper, DataTexture.graphicsFormat);
                wrapper.SetTexture(Crest.ShaderIDs.s_Target, DataTexture);
                wrapper.SetVector(Crest.ShaderIDs.s_ClearMask, Color.black);
                wrapper.SetVector(Crest.ShaderIDs.s_ClearColor, Color.clear);
                wrapper.Dispatch(Resolution / k_ThreadGroupSizeX, Resolution / k_ThreadGroupSizeY, Slices);
            }

            // Pack height data into alpha channel.
            // We do not add height to displacement directly for better precision and layering.
            var heightShader = WaterResources.Instance.Compute._PackLevel;
            if (_Water._LevelLod.Enabled && heightShader != null)
            {
                buffer.SetComputeTextureParam(heightShader, 0, Crest.ShaderIDs.s_Target, DataTexture);
                buffer.DispatchCompute
                (
                    heightShader,
                    0,
                    Resolution / k_ThreadGroupSizeX,
                    Resolution / k_ThreadGroupSizeY,
                    Slices
                );
            }

            // Query collisions including only Animated Waves.
            // Requires copying the water level.
            // Guard not required, as Query already does this check before returning the
            // correct provider, thus nothing would be reqistered nor dispatched. But seems
            // right to do so anyhow.
            if (_CollisionLayers != CollisionLayers.Nothing)
            {
                Provider.UpdateQueries(_Water, CollisionLayer.AfterAnimatedWaves);
            }

            // Transfer Dynamic Waves to Animated Waves.
            if ((_CollisionLayers.HasFlag(CollisionLayers.DynamicWaves) || PreserveWaveQuality) && _Water._DynamicWavesLod.Enabled)
            {
                buffer.BeginSample(k_DrawCombine);
                // Clearing not required as we overwrite enter texture.
                buffer.GetTemporaryRT(ShaderIDs.s_DynamicWavesTarget, DataTexture.descriptor);

                var wrapper = new PropertyWrapperCompute(buffer, _CombineShader._Shader, _CombineShader._CombineDynamicWavesKernel);

                // Flow keyword is already set, and Dynamic Waves already bound. If binding Dynamic
                // Waves becomes kernel specific (eg binding textures), then we need to rebind.

                // Start with last LOD which does not combine.
                wrapper.SetKeyword(_CombineShader._CombineKeyword, false);

                wrapper.SetTexture(Crest.ShaderIDs.s_Target, ShaderIDs.s_DynamicWavesTarget);

                _Water._DynamicWavesLod.Bind(wrapper);

                // Compute displacement from Dynamic Waves.
                for (var slice = lastSlice; slice >= 0; slice--)
                {
                    wrapper.SetInteger(Lod.ShaderIDs.s_LodIndex, slice);
                    wrapper.Dispatch(threadSize, threadSize, 1);

                    if (slice == lastSlice)
                    {
                        // From here on, use combine.
                        wrapper.SetKeyword(_CombineShader._CombineKeyword, s_Combine);
                    }
                }

                // Copy Dynamic Waves displacement into Animated Waves.
                if (WaterResources.Instance.Compute._Blit != null)
                {
                    var compute = WaterResources.Instance._ComputeLibrary._BlitCompute;
                    wrapper = new(buffer, compute._Shader, 0);
                    compute.SetVariantForFormat(wrapper, DataTexture.graphicsFormat);
                    wrapper.SetTexture(Crest.ShaderIDs.s_Source, ShaderIDs.s_DynamicWavesTarget);
                    wrapper.SetTexture(Crest.ShaderIDs.s_Target, DataTexture);
                    wrapper.Dispatch(threadSize, threadSize, Slices);
                }

                buffer.ReleaseTemporaryRT(ShaderIDs.s_DynamicWavesTarget);
                buffer.EndSample(k_DrawCombine);

                // Query collisions including Dynamic Waves.
                // Does not require copying the water level as they are added with zero alpha.
                if (_CollisionLayers.HasFlag(CollisionLayers.DynamicWaves))
                {
                    Provider.UpdateQueries(_Water, CollisionLayer.AfterDynamicWaves);
                }
            }

            if (_CollisionLayers.HasFlag(CollisionLayers.Displacement))
            {
                // LOD independent data.
                // Draw any data that did not express a preference for one lod or another.
                drawn = SubmitDraws(buffer, s_Inputs, DataTexture, (int)DisplacementPass.LodIndependentLast);
            }

            if (_CollisionLayers == CollisionLayers.Nothing || _CollisionLayers.HasFlag(CollisionLayers.Displacement))
            {
                Queryable?.UpdateQueries(_Water);
            }

            buffer.EndSample(ID);
        }

        internal override void AfterExecute()
        {
            Provider.SendReadBack(_Water, _CollisionLayers);
        }

        /// <summary>
        /// Provides water shape to CPU.
        /// </summary>
        private protected override ICollisionProvider CreateProvider(bool onEnable)
        {
            ICollisionProvider provider = ICollisionProvider.None;

            Queryable?.CleanUp();

            // Respect user choice and early exit. If not in OnEnable, then none provider.
            // Do not check Enabled, as it includes _Valid which checks for GPU.
            if (!_Enabled || !onEnable)
            {
                return provider;
            }

            var source = QuerySource;

            if (_Water.Surface.IsQuadMesh)
            {
                source = LodQuerySource.None;
            }

            switch (source)
            {
                case LodQuerySource.GPU:
                {
                    if (_Valid)
                    {
                        provider = ICollisionProvider.Create(_Water);
                    }

                    if (_Water.IsRunningWithoutGraphics)
                    {
                        Debug.LogError("Crest: GPU queries requires a GPU. Please consider CPU queries if running from a server without a GPU.");
                    }

                    break;
                }
                case LodQuerySource.CPU:
                {
                    if (_BakedWaveData != null)
                    {
                        provider = _BakedWaveData.CreateCollisionProvider();
                    }

                    break;
                }
            }

            // This should not be hit, but can be if compute shaders aren't loaded correctly.
            // They will print out appropriate errors. Don't just return null and have null
            // reference exceptions spamming the logs.
            provider ??= ICollisionProvider.None;

            return provider;
        }

        //
        // DrawFilter
        //

        internal readonly struct WavelengthFilter
        {
            public readonly float _Minimum;
            public readonly float _Maximum;
            public readonly float _TransitionThreshold;
            public readonly float _ViewerAltitudeLevelAlpha;
            public readonly int _Slice;
            public readonly int _Slices;
            public readonly bool _HighQualityCombine;

            public WavelengthFilter(WaterRenderer water, int slice, int resolution)
            {
                _Slice = slice;
                _Slices = water.LodLevels;
                _Maximum = water.MaximumWavelength(slice, resolution);
                _Minimum = _Maximum * 0.5f;
                _TransitionThreshold = water.MaximumWavelength(_Slices - 1, resolution) * 0.5f;
                _ViewerAltitudeLevelAlpha = water.ViewerAltitudeLevelAlpha;
                _HighQualityCombine = water.AnimatedWavesLod.PreserveWaveQuality;
            }
        }

        internal static float FilterByWavelength(WavelengthFilter filter, float wavelength)
        {
            // No wavelength preference - don't draw per-lod
            if (wavelength == 0f)
            {
                return 0f;
            }

            // Too small for this lod
            if (wavelength < filter._Minimum)
            {
                return 0f;
            }

            // If approaching end of lod chain, start smoothly transitioning any large wavelengths across last two lods
            if (wavelength >= filter._TransitionThreshold)
            {
                if (filter._Slice == filter._Slices - 2 && !filter._HighQualityCombine)
                {
                    return 1f - filter._ViewerAltitudeLevelAlpha;
                }

                if (filter._Slice == filter._Slices - 1)
                {
                    return filter._ViewerAltitudeLevelAlpha;
                }
            }
            else if (wavelength < filter._Maximum)
            {
                // Fits in this lod
                return 1f;
            }

            return filter._HighQualityCombine ? 1f : 0f;
        }

        internal static float FilterByWavelength(WaterRenderer water, int slice, float wavelength, int resolution)
        {
            return FilterByWavelength(new(water, slice, resolution), wavelength);
        }


        //
        // Inputs
        //

        internal static readonly Utility.SortedList<int, ILodInput> s_Inputs = new(Helpers.DuplicateComparison);
        private protected override Utility.SortedList<int, ILodInput> Inputs => s_Inputs;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void OnLoad()
        {
            s_Inputs.Clear();
        }
    }

    // Multiple Viewpoints
    partial class AnimatedWavesLod
    {
        readonly Dictionary<Camera, RenderTexture> _AdditionalCameraData = new();

        internal override void LoadCameraData(Camera camera)
        {
            base.LoadCameraData(camera);

            if (!Persistent)
            {
                return;
            }

            if (!_AdditionalCameraData.ContainsKey(camera))
            {
                _PersistentDataTexture = CreateLodDataTextures();
                Clear(_PersistentDataTexture);
                _AdditionalCameraData.Add(camera, _PersistentDataTexture);
            }
            else
            {
                _PersistentDataTexture = _AdditionalCameraData[camera];
            }
        }

        internal override void StoreCameraData(Camera camera)
        {
            base.StoreCameraData(camera);

            if (!Persistent)
            {
                return;
            }

            _AdditionalCameraData[camera] = _PersistentDataTexture;
        }

        internal override void RemoveCameraData(Camera camera)
        {
            base.RemoveCameraData(camera);

            if (_AdditionalCameraData.ContainsKey(camera))
            {
                var rt = _AdditionalCameraData[camera];
                if (rt != null) rt.Release();
                Helpers.Destroy(rt);
                _AdditionalCameraData.Remove(camera);
            }
        }
    }

    // API
    partial class AnimatedWavesLod
    {
        float GetWaveResolutionMultiplier()
        {
            return PreserveWaveQuality ? 1f : _WaveResolutionMultiplier;
        }
    }

    partial class AnimatedWavesLod
    {
        [@HideInInspector]
        [@System.Obsolete("Please use QuerySource instead.")]
        [Tooltip("Where to obtain water shape on CPU for physics / gameplay.")]
        [@GenerateAPI(Setter.Internal)]
        [@DecoratedField, SerializeField]
        internal CollisionSource _CollisionSource = CollisionSource.GPU;
    }

#if UNITY_EDITOR
    // Editor
    partial class AnimatedWavesLod
    {
        [@OnChange]
        private protected override void OnChange(string propertyPath, object previousValue)
        {
            base.OnChange(propertyPath, previousValue);

            switch (propertyPath)
            {
                case nameof(_CollisionLayers):
                    ResetQueryChange();
                    break;
            }
        }
    }
#endif
}
