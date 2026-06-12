// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using WaveHarmonic.Crest.Internal;
using WaveHarmonic.Crest.Utility;

namespace WaveHarmonic.Crest
{
    using Inputs = Utility.SortedList<int, ILodInput>;

    /// <summary>
    /// Texture format preset.
    /// </summary>
    [@GenerateDoc]
    public enum LodTextureFormatMode
    {
        /// <inheritdoc cref="Generated.LodTextureFormatMode.Manual"/>
        [Tooltip("Uses the Texture Format property.")]
        Manual,

        /// <inheritdoc cref="Generated.LodTextureFormatMode.Performance"/>
        [Tooltip("Chooses a texture format for performance.")]
        Performance = 100,

        /// <inheritdoc cref="Generated.LodTextureFormatMode.Precision"/>
        [Tooltip("Chooses a texture format for precision.\n\nThis format can reduce artifacts.")]
        Precision = 200,

        /// <inheritdoc cref="Generated.LodTextureFormatMode.Automatic"/>
        [Tooltip("Chooses a texture format based on another.\n\nFor example, Dynamic Waves will match precision of Animated Waves.")]
        Automatic = 300,
    }

    /// <summary>
    /// Base class for data/behaviours created on each LOD.
    /// </summary>
    [System.Serializable]
    public abstract partial class Lod : Versioned
    {
        [Tooltip("Whether the simulation is enabled.")]
        [@GenerateAPI(Getter.Custom, Setter.Custom)]
        [@DecoratedField, SerializeField]
        internal bool _Enabled;

        [Tooltip("Whether to override the resolution.\n\nIf not enabled, then the simulation will use the resolution defined on the Water Renderer.")]
        [@Hide(typeof(AnimatedWavesLod))]
        [@GenerateAPI(Setter.Dirty)]
        [@InlineToggle, SerializeField]
        internal bool _OverrideResolution = true;

        [Tooltip("The resolution of the simulation data.\n\nSet higher for sharper results at the cost of higher memory usage.")]
        [@Show(nameof(_OverrideResolution))]
        [@ShowComputedProperty(nameof(Resolution))]
        [@Delayed]
        [@GenerateAPI(Getter.Custom, Setter.Dirty)]
        [SerializeField]
        internal int _Resolution = 256;

        [Tooltip("Chooses a texture format based on a preset value.")]
        [@Filtered]
        [@GenerateAPI(Setter.Dirty)]
        [SerializeField]
        private protected LodTextureFormatMode _TextureFormatMode = LodTextureFormatMode.Performance;

        [Tooltip("The render texture format used for this simulation data.\n\nIt will be overriden if the format is incompatible with the platform.")]
        [@ShowComputedProperty(nameof(RequestedTextureFormat))]
        [@Show(nameof(_TextureFormatMode), nameof(LodTextureFormatMode.Manual))]
        [@GenerateAPI(Setter.Dirty)]
        [@DecoratedField, SerializeField]
        internal GraphicsFormat _TextureFormat;

        [@Space(10)]

        [Tooltip("Blurs the output.\n\nEnable if blurring is desired or intolerable artifacts are present.\nThe blur is optimized to only run on inner LODs and at lower scales.")]
        [@Hide(typeof(AnimatedWavesLod))]
        [@Hide(typeof(DynamicWavesLod))]
        [@GenerateAPI(Setter.Dirty)]
        [@DecoratedField]
        [SerializeField]
        private protected bool _Blur;

        [Tooltip("Number of blur iterations.\n\nBlur iterations are optimized to only run maximum iterations on the inner LODs.")]
        [@Hide(typeof(AnimatedWavesLod))]
        [@Hide(typeof(DynamicWavesLod))]
        [@Enable(nameof(_Blur))]
        [@GenerateAPI]
        [@DecoratedField]
        [SerializeField]
        private protected int _BlurIterations = 1;

        // NOTE: This MUST match the value in Constants.hlsl, as it
        // determines the size of the texture arrays in the shaders.
        internal const int k_MaximumSlices = 15;
        internal const int k_ThreadGroupSize = 8;
        internal const int k_ThreadGroupSizeX = k_ThreadGroupSize;
        internal const int k_ThreadGroupSizeY = k_ThreadGroupSize;

        internal const string k_BlurField = nameof(_Blur);
        internal const string k_TextureFormatModeField = nameof(_TextureFormatMode);

        internal static class ShaderIDs
        {
            public static readonly int s_LodIndex = Shader.PropertyToID("_Crest_LodIndex");
            public static readonly int s_LodChange = Shader.PropertyToID("_Crest_LodChange");
            public static readonly int s_TemporaryBlurLodTexture = Shader.PropertyToID("_Crest_TemporaryBlurLodTexture");
        }

        // Used for creating shader property names etc.
        internal abstract string ID { get; }
        internal virtual string Name => ID;

        /// <summary>
        /// The requested texture format used for this simulation, either by manual mode or
        /// one of the aliases. It will be overriden if the format is incompatible with the
        /// platform (<see cref="CompatibleTextureFormat"/>).
        /// </summary>
        private protected abstract GraphicsFormat RequestedTextureFormat { get; }

        /// <summary>
        /// This is the platform compatible texture format that will used.
        /// </summary>
        public GraphicsFormat CompatibleTextureFormat { get; private set; }

        private protected abstract Color ClearColor { get; }
        private protected abstract bool NeedToReadWriteTextureData { get; }
        private protected abstract Inputs Inputs { get; }
        internal abstract Color GizmoColor { get; }
        internal virtual int BufferCount => 1;
        private protected virtual Texture2DArray NullTexture => BlackTextureArray;
        private protected virtual bool RequiresClearBorder => false;

        private protected IQueryable Queryable { get; set; }

        // This is used as alternative to Texture2D.blackTexture, as using that
        // is not possible in some shaders.
        static Texture2DArray s_BlackTextureArray = null;
        static Texture2DArray BlackTextureArray
        {
            get
            {
                if (s_BlackTextureArray == null)
                {
                    s_BlackTextureArray = TextureArrayHelpers.CreateTexture2DArray(Texture2D.blackTexture, k_MaximumSlices);
                    s_BlackTextureArray.name = "_Crest_LodBlackTexture";
                }

                return s_BlackTextureArray;
            }
        }

        private protected bool Persistent => BufferCount > 1;
        internal virtual bool SkipEndOfFrame => false;

        internal RenderTexture DataTexture { get; private protected set; }

        private protected Matrix4x4[] _ViewMatrices = new Matrix4x4[k_MaximumSlices];
        private protected Cascade[] _Cascades = new Cascade[k_MaximumSlices];
        internal Cascade[] Cascades => _Cascades;
        private protected BufferedData<Vector4[]> _SamplingParameters;

        internal int Slices => _Water.LodLevels;

        // Currently use as a failure flag.
        private protected bool _Valid;

        internal WaterRenderer _Water;
        internal WaterRenderer Water => _Water;

        private protected bool _TargetsToClear;

        private protected readonly int _TextureShaderID;
        private protected readonly int _TextureSourceShaderID;
        private protected readonly int _SamplingParametersShaderID;
        private protected readonly int _SamplingParametersCascadeShaderID;
        private protected readonly int _SamplingParametersCascadeSourceShaderID;

        readonly string _TextureName;

        internal Lod()
        {
            // @Garbage
            var name = $"g_Crest_Cascade{ID}";
            _TextureShaderID = Shader.PropertyToID(name);
            _TextureSourceShaderID = Shader.PropertyToID($"{name}Source");
            _SamplingParametersShaderID = Shader.PropertyToID($"g_Crest_SamplingParameters{ID}");
            _SamplingParametersCascadeShaderID = Shader.PropertyToID($"g_Crest_SamplingParametersCascade{ID}");
            _SamplingParametersCascadeSourceShaderID = Shader.PropertyToID($"g_Crest_SamplingParametersCascade{ID}Source");

            _TextureName = $"_Crest_{ID}Lod";
        }

        private protected RenderTexture CreateLodDataTextures(string postfix = null)
        {
            postfix ??= string.Empty;

            RenderTexture result = new(Resolution, Resolution, 0, CompatibleTextureFormat)
            {
                wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0,
                useMipMap = false,
                name = _TextureName + postfix,
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = Slices,
                enableRandomWrite = NeedToReadWriteTextureData
            };
            result.Create();
            return result;
        }

        private protected void FlipBuffers(CommandBuffer commands)
        {
            if (_ReAllocateTexture)
            {
                ReAllocate();
            }

#if UNITY_EDITOR
            // Fixes flickering in frame debugger when navigating draw calls.
            if (!UnityEditor.EditorApplication.isPaused || Time.deltaTime > 0)
#endif
            {
                _SamplingParameters.Flip();
            }

            UpdateSamplingParameters(commands);
        }

        private protected void Clear(RenderTexture target)
        {
            Helpers.ClearRenderTexture(target, ClearColor, depth: false);
        }

        private protected virtual bool AlwaysClear => false;

        // Only works with input-only data (ie no simulation steps).
        internal virtual void BuildCommandBuffer(WaterRenderer water, CommandBuffer buffer)
        {
            FlipBuffers(buffer);

            buffer.BeginSample(ID);

            if (_TargetsToClear || AlwaysClear)
            {
                // IMPORTANT:
                // We need both clears otherwise problems on PS5.
                // With only native clear, flow would clear to non black (ie constant flow).
                // With only custom clear, absorption/scattering inputs were not clearing.
                CoreUtils.SetRenderTarget(buffer, DataTexture, ClearFlag.Color, ClearColor);

                // Custom clear because clear not working.
                if (Helpers.RequiresCustomClear && WaterResources.Instance.Compute._Clear != null)
                {
                    var compute = WaterResources.Instance._ComputeLibrary._ClearCompute;
                    var wrapper = new PropertyWrapperCompute(buffer, compute._Shader, compute._KernelClearTarget);
                    compute.SetVariantForFormat(wrapper, CompatibleTextureFormat);
                    wrapper.SetTexture(Crest.ShaderIDs.s_Target, DataTexture);
                    wrapper.SetVector(Crest.ShaderIDs.s_ClearMask, Color.white);
                    wrapper.SetVector(Crest.ShaderIDs.s_ClearColor, ClearColor);
                    wrapper.Dispatch(Resolution / k_ThreadGroupSizeX, Resolution / k_ThreadGroupSizeY, Slices);
                }

                _TargetsToClear = false;
            }

            if (Inputs.Count > 0)
            {
                SubmitDraws(buffer, Inputs, DataTexture);

                // Ensure all targets clear when there are no inputs.
                _TargetsToClear = true;
            }

            TryBlur(buffer);

            if (RequiresClearBorder)
            {
                ClearBorder(buffer);
            }

            Queryable?.UpdateQueries(_Water);

            buffer.EndSample(ID);
        }

        private protected bool SubmitDraws(CommandBuffer buffer, Inputs draws, RenderTargetIdentifier target, int pass = -1, bool filter = false)
        {
            var drawn = false;

            foreach (var draw in draws)
            {
                var input = draw.Value;
                if (!input.Enabled)
                {
                    continue;
                }

                if (pass != -1)
                {
                    var p = input.Pass;
                    if (p != -1 && p != pass) continue;
                }

                var rect = input.Rect;

                if (input.IsCompute)
                {
                    var smallest = 0;
                    if (rect != Rect.zero)
                    {
                        smallest = -1;
                        for (var slice = Slices - 1; slice >= 0; slice--)
                        {
                            if (rect != Rect.zero && !rect.Overlaps(Cascades[slice].TexelRect)) break;
                            smallest = slice;
                        }

                        if (smallest < 0) continue;
                    }

                    // Pass the slice count to only draw to valid slices.
                    input.Draw(this, buffer, target, pass, slice: Slices - smallest);
                    drawn = true;
                    continue;
                }

                for (var slice = Slices - 1; slice >= 0; slice--)
                {
                    if (rect != Rect.zero && !rect.Overlaps(Cascades[slice].TexelRect)) break;

                    var weight = filter ? input.Filter(_Water, slice) : 1f;
                    if (weight <= 0f) continue;

                    // Parameters override RTI values:
                    // https://docs.unity3d.com/ScriptReference/Rendering.CommandBuffer.SetRenderTarget.html
                    CoreUtils.SetRenderTarget(buffer, target, depthSlice: slice);
                    buffer.SetGlobalInteger(ShaderIDs.s_LodIndex, slice);

                    // This will work for CG but not for HDRP hlsl files.
                    buffer.SetViewProjectionMatrices(_ViewMatrices[slice], _Water.GetProjectionMatrix(slice));

                    input.Draw(this, buffer, target, pass, weight, slice);
                    drawn = true;
                }
            }

            return drawn;
        }

        /// <summary>
        /// Set a new origin. This is equivalent to subtracting the new origin position from any world position state.
        /// </summary>
        internal void SetOrigin(Vector3 newOrigin)
        {
            _SamplingParameters.RunLambda(data =>
            {
                for (var index = 0; index < _Water.LodLevels; index++)
                {
                    // We really only care about the previous states, as the current/next frame will be
                    // re-calculated. This realigns the snapped position with the now shifted camera.
                    data[index].x -= newOrigin.x;
                    data[index].y -= newOrigin.z;
                }
            });
        }

        void ClearBorder(CommandBuffer buffer)
        {
            var size = Resolution / 8;

            var compute = WaterResources.Instance._ComputeLibrary._ClearCompute;

            var wrapper = new PropertyWrapperCompute(buffer, compute._Shader, compute._KernelClearTargetBoundaryX);
            // Only need to be done once.
            compute.SetVariantForFormat(wrapper, DataTexture.graphicsFormat);
            wrapper.SetTexture(Crest.ShaderIDs.s_Target, DataTexture);
            wrapper.SetVector(Crest.ShaderIDs.s_ClearColor, ClearColor);
            wrapper.SetInteger(Crest.ShaderIDs.s_Resolution, Resolution);
            wrapper.SetInteger(Crest.ShaderIDs.s_TargetSlice, Slices - 1);
            wrapper.Dispatch(size, 1, 1);

            wrapper = new(buffer, compute._Shader, compute._KernelClearTargetBoundaryY);
            wrapper.SetTexture(Crest.ShaderIDs.s_Target, DataTexture);
            wrapper.SetVector(Crest.ShaderIDs.s_ClearColor, ClearColor);
            wrapper.SetInteger(Crest.ShaderIDs.s_Resolution, Resolution);
            wrapper.SetInteger(Crest.ShaderIDs.s_TargetSlice, Slices - 1);
            wrapper.Dispatch(1, size, 1);
        }

        void UpdateSamplingParameters(CommandBuffer commands, bool initialize = false)
        {
            var position = _Water.Position;
            var resolution = _Enabled ? Resolution : TextureArrayHelpers.k_SmallTextureSize;

            var parameters = _SamplingParameters.Current;
            var levels = Slices;

            for (var slice = 0; slice < levels; slice++)
            {
                // Find snap period.
                var texel = 2f * 2f * _Water.CascadeData.Current[slice].x / resolution;
                // Snap so that shape texels are stationary.
                var snapped = position - new Vector3(Mathf.Repeat(position.x, texel), 0, Mathf.Repeat(position.z, texel));

                var cascade = new Cascade(snapped.XZ(), texel, resolution);
                _Cascades[slice] = cascade;
                parameters[slice] = cascade.Packed;
                if (initialize && BufferCount > 1) _SamplingParameters.Previous(1)[slice] = cascade.Packed;

                _ViewMatrices[slice] = WaterRenderer.CalculateViewMatrixFromSnappedPositionRHS(snapped);
            }

            if (!initialize)
            {
                commands.SetGlobalVector(_SamplingParametersShaderID, new(levels, resolution, 1f / resolution, 0));
                commands.SetGlobalVectorArray(_SamplingParametersCascadeShaderID, parameters);

                if (BufferCount > 1)
                {
                    commands.SetGlobalVectorArray(_SamplingParametersCascadeSourceShaderID, _SamplingParameters.Previous(1));
                }

                return;
            }

            Shader.SetGlobalVector(_SamplingParametersShaderID, new(levels, resolution, 1f / resolution, 0));
            Shader.SetGlobalVectorArray(_SamplingParametersCascadeShaderID, parameters);

            if (BufferCount > 1)
            {
                Shader.SetGlobalVectorArray(_SamplingParametersCascadeSourceShaderID, _SamplingParameters.Previous(1));
            }
        }

        /// <summary>
        /// Returns index of lod that completely covers the sample area. If no such lod
        /// available, returns -1.
        /// </summary>
        internal int SuggestIndex(Rect sampleArea)
        {
            for (var slice = 0; slice < Slices; slice++)
            {
                var cascade = _Cascades[slice];

                // Shape texture needs to completely contain sample area.
                var rect = cascade.TexelRect;

                // Shrink rect by 1 texel border - this is to make finite differences fit as well.
                var texel = cascade._Texel;
                rect.x += texel; rect.y += texel;
                rect.width -= 2f * texel; rect.height -= 2f * texel;

                if (!rect.Contains(sampleArea.min) || !rect.Contains(sampleArea.max))
                {
                    continue;
                }

                return slice;
            }

            return -1;
        }

        /// <summary>
        /// Returns index of lod that completely covers the sample area, and contains
        /// wavelengths that repeat no more than twice across the smaller spatial length. If
        /// no such lod available, returns -1. This means high frequency wavelengths are
        /// filtered out, and the lod index can be used for each sample in the sample area.
        /// </summary>
        internal int SuggestIndexForWaves(Rect sampleArea)
        {
            return SuggestIndexForWaves(sampleArea, Mathf.Min(sampleArea.width, sampleArea.height));
        }

        internal int SuggestIndexForWaves(Rect sampleArea, float minimumSpatialLength)
        {
            var count = Slices;

            for (var index = 0; index < count; index++)
            {
                var cascade = _Cascades[index];

                // Shape texture needs to completely contain sample area.
                var rect = cascade.TexelRect;

                // Shrink rect by 1 texel border - this is to make finite differences fit as well.
                var texel = cascade._Texel;
                rect.x += texel; rect.y += texel;
                rect.width -= 2f * texel; rect.height -= 2f * texel;

                if (!rect.Contains(sampleArea.min) || !rect.Contains(sampleArea.max))
                {
                    continue;
                }

                // The smallest wavelengths should repeat no more than twice across the smaller
                // spatial length. Unless we're in the last LOD - then this is the best we can do.
                var minimumWavelength = _Water.MaximumWavelength(index, Resolution) / 2f;
                if (minimumWavelength < minimumSpatialLength / 2f && index < count - 1)
                {
                    continue;
                }

                return index;
            }

            return -1;
        }

        // Blurs the output if enabled.
        private protected void TryBlur(CommandBuffer commands)
        {
            if (!_Blur || _Water.Scale >= 32)
            {
                return;
            }

            var rt = DataTexture;

            var compute = WaterResources.Instance._ComputeLibrary._BlurCompute;

            var horizontal = new PropertyWrapperCompute(commands, compute._Shader, compute._KernelHorizontal);
            var vertical = new PropertyWrapperCompute(commands, compute._Shader, compute._KernelVertical);

            var temporary = ShaderIDs.s_TemporaryBlurLodTexture;

            commands.GetTemporaryRT(temporary, rt.descriptor);

            // Applies to both.
            compute.SetVariantForFormat(horizontal, rt.graphicsFormat);
            horizontal.SetInteger(Crest.ShaderIDs.s_Resolution, rt.width);

            horizontal.SetTexture(Crest.ShaderIDs.s_Source, rt);
            horizontal.SetTexture(Crest.ShaderIDs.s_Target, temporary);
            vertical.SetTexture(Crest.ShaderIDs.s_Source, temporary);
            vertical.SetTexture(Crest.ShaderIDs.s_Target, rt);

            var x = rt.width / 8;
            var y = rt.height / 8;
            // Skip outer LODs.
            var z = Mathf.Min(rt.volumeDepth, 4);
            for (var i = 0; i < _BlurIterations; i++)
            {
                // Limit number of iterations for outer LODs.
                horizontal.Dispatch(x, y, Mathf.Max(z - i, 1));
                vertical.Dispatch(x, y, Mathf.Max(z - i, 1));
            }

            commands.ReleaseTemporaryRT(temporary);
        }

        /// <summary>
        /// Bind data needed to load or compute from this simulation.
        /// </summary>
        internal virtual void Bind<T>(T target) where T : IPropertyWrapper
        {

        }

        internal virtual void Initialize()
        {
            // All simulations require a GPU so do not proceed any further.
            if (_Water.IsRunningWithoutGraphics)
            {
                _Valid = false;
                return;
            }

            // Validate textures.
            {
                // Find a compatible texture format.
                CompatibleTextureFormat = Helpers.GetCompatibleTextureFormat(RequestedTextureFormat, Helpers.s_DataGraphicsFormatUsage, Name, NeedToReadWriteTextureData);

                if (CompatibleTextureFormat == GraphicsFormat.None)
                {
                    Debug.Log($"Crest: Disabling {Name} simulation due to no valid available texture format.");
                    _Valid = false;
                    return;
                }

                Debug.Assert(Slices <= k_MaximumSlices);
            }

            _Valid = true;

            Allocate();
        }

        internal virtual void SetGlobals(bool enable)
        {
            if (_Water.IsRunningWithoutGraphics) return;
            // Bind/unbind data texture for all shaders.
            Shader.SetGlobalTexture(_TextureShaderID, enable && Enabled ? DataTexture : NullTexture);

            if (_SamplingParameters == null || _SamplingParameters.Size != BufferCount)
            {
                _SamplingParameters = new(BufferCount, () => new Vector4[k_MaximumSlices]);
            }

            // For safety. Disable to see if we are sampling outside of LOD chain.
            _SamplingParameters.RunLambda(x => System.Array.Fill(x, new(0, 0, 1, 0)));

            UpdateSamplingParameters(null, initialize: true);
        }

        internal virtual void Enable()
        {
            // Blank
        }

        internal virtual void Disable()
        {
            // Always clean up provider (CPU may be running).
            Queryable?.CleanUp();
        }

        internal virtual void Destroy()
        {
            // Release resources and destroy object to avoid reference leak.
            if (DataTexture != null) DataTexture.Release();
            Helpers.Destroy(DataTexture);

            _AdditionalCameraData.Clear();
        }

        internal virtual void AfterExecute()
        {

        }

        private protected virtual void Allocate()
        {
            // Use per-camera data.
            DataTexture = CreateLodDataTextures();
            Clear(DataTexture);

            // Bind globally once here on init, which will bind to all shaders.
            Shader.SetGlobalTexture(_TextureShaderID, DataTexture);

            _ReAllocateTexture = false;
        }

        bool GetEnabled() => _Enabled && _Valid;

        // NOTE: This could be called by the user due to API.
        void SetEnabled(bool previous, bool current)
        {
            if (previous == current) return;
            if (_Water == null || !_Water.isActiveAndEnabled) return;

            if (current)
            {
                Initialize();
                Enable();
            }
            else
            {
                Disable();
                Destroy();
            }

            SetGlobals(current);
        }

        int GetResolution() => _OverrideResolution || Water == null ? _Resolution : Water.LodResolution;

        private protected virtual void ReAllocate()
        {
            if (!Enabled) return;
            CompatibleTextureFormat = Helpers.GetCompatibleTextureFormat(RequestedTextureFormat, Helpers.s_DataGraphicsFormatUsage, Name, NeedToReadWriteTextureData);
            var descriptor = DataTexture.descriptor;
            descriptor.height = descriptor.width = Resolution;
            descriptor.graphicsFormat = CompatibleTextureFormat;
            descriptor.enableRandomWrite = NeedToReadWriteTextureData;
            DataTexture.Release();
            DataTexture.descriptor = descriptor;
            DataTexture.Create();

            _ReAllocateTexture = false;

            UpdateSamplingParameters(null, initialize: true);
        }

#if UNITY_EDITOR
        [@OnChange]
        private protected virtual void OnChange(string propertyPath, object previousValue)
        {
            switch (propertyPath)
            {
                case nameof(_Enabled):
                    SetEnabled((bool)previousValue, _Enabled);
                    break;
                case nameof(_Blur):
                case nameof(_Resolution):
                case nameof(_OverrideResolution):
                case nameof(_TextureFormat):
                case nameof(_TextureFormatMode):
                    ReAllocate();
                    break;
            }
        }
#endif
    }

    partial class Lod
    {
        readonly Dictionary<Camera, BufferedData<Vector4[]>> _AdditionalCameraData = new();

        internal virtual void LoadCameraData(Camera camera)
        {
            Queryable?.Initialize(_Water);

            // For non-persistent sims, we do not need to store per camera data.
            if (!Persistent)
            {
                return;
            }

            if (!_AdditionalCameraData.ContainsKey(camera))
            {
                _SamplingParameters = new(BufferCount, () => new Vector4[k_MaximumSlices]);
                _AdditionalCameraData.Add(camera, _SamplingParameters);
            }
            else
            {
                _SamplingParameters = _AdditionalCameraData[camera];
            }
        }

        internal virtual void StoreCameraData(Camera camera)
        {

        }

        internal virtual void RemoveCameraData(Camera camera)
        {
            if (_AdditionalCameraData.ContainsKey(camera))
            {
                _AdditionalCameraData.Remove(camera);
            }
        }
    }

    // API
    partial class Lod
    {
        bool _ReAllocateTexture;

        void SetDirty<I>(I previous, I current) where I : System.IEquatable<I>
        {
            if (Equals(previous, current)) return;
            _ReAllocateTexture = true;
        }

        void SetDirty(System.Enum previous, System.Enum current)
        {
            if (previous == current) return;
            _ReAllocateTexture = true;
        }
    }

    interface IQueryableLod<out T> where T : IQueryProvider
    {
        string Name { get; }
        bool Enabled { get; }
        WaterRenderer Water { get; }
        int MaximumQueryCount { get; }
        float Texel { get; }
        LodQuerySource QuerySource { get; }
    }

    /// <summary>
    /// The source of collisions (ie water shape).
    /// </summary>
    [@GenerateDoc]
    public enum LodQuerySource
    {
        /// <inheritdoc cref="Generated.LodQuerySource.None"/>
        [Tooltip("No query source.")]
        None,

        /// <inheritdoc cref="Generated.LodQuerySource.GPU"/>
        [Tooltip("Uses AsyncGPUReadback to retrieve data from GPU to CPU.\n\nThis is the most optimal approach.")]
        GPU,

        /// <inheritdoc cref="Generated.LodQuerySource.CPU"/>
        [Tooltip("Computes data entirely on the CPU.")]
        CPU,
    }

    /// <summary>
    /// Base type for simulations with a provider.
    /// </summary>
    /// <typeparam name="T">The query provider.</typeparam>
    [System.Serializable]
    public abstract partial class Lod<T> : Lod, IQueryableLod<T> where T : IQueryProvider
    {
        [@Space(10)]

        [Tooltip("Where to obtain water data on CPU for physics / gameplay.")]
        [@GenerateAPI(Setter.Internal)]
        [@Filtered]
        [SerializeField]
        private protected LodQuerySource _QuerySource = LodQuerySource.GPU;

        [Tooltip("Maximum number of queries that can be performed when using GPU queries.")]
        [@Show(nameof(_QuerySource), nameof(LodQuerySource.GPU))]
        [@GenerateAPI(Setter.None)]
        [@DecoratedField]
        [SerializeField]
        private protected int _MaximumQueryCount = QueryBase.k_DefaultMaximumQueryCount;

        /// <summary>
        /// Provides data from the GPU to CPU.
        /// </summary>
        public T Provider { get; set; }

        WaterRenderer IQueryableLod<T>.Water => Water;
        string IQueryableLod<T>.Name => Name;
        float IQueryableLod<T>.Texel => _Cascades[0]._Texel;

        private protected abstract T CreateProvider(bool onEnable);

        internal override void SetGlobals(bool onEnable)
        {
            base.SetGlobals(onEnable);
            // We should always have a provider (null provider if disabled).
            InitializeProvider(onEnable);
        }

        private protected void InitializeProvider(bool onEnable)
        {
            Provider = CreateProvider(onEnable);
            // None providers are not IQueryable.
            Queryable = Provider as IQueryable;
        }

        internal override void AfterExecute()
        {
            Queryable?.SendReadBack(_Water);
        }
    }

#if UNITY_EDITOR
    partial class Lod<T>
    {
        private protected void ResetQueryChange()
        {
            if (_Water == null || !_Water.isActiveAndEnabled || !_Enabled) return;
            Queryable?.CleanUp();
            InitializeProvider(true);
        }

        [@OnChange]
        private protected override void OnChange(string path, object previous)
        {
            base.OnChange(path, previous);

            switch (path)
            {
                case nameof(_QuerySource):
                case nameof(_MaximumQueryCount):
                    ResetQueryChange();
                    break;
            }
        }
    }
#endif
}
