// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;
using UnityEngine.Rendering;

namespace WaveHarmonic.Crest
{
    /// <summary>
    /// A persistent simulation that moves around with a displacement LOD.
    /// </summary>
    [System.Serializable]
    public abstract partial class PersistentLod : Lod
    {
        [@Space(10)]

        [Tooltip("Frequency to run the simulation, in updates per second.\n\nLower frequencies are more efficient but may lead to visible jitter or slowness.")]
        [@Range(15, 200)]
        [@GenerateAPI]
        [SerializeField]
        private protected int _SimulationFrequency = 60;

        static new class ShaderIDs
        {
            public static readonly int s_SimDeltaTime = Shader.PropertyToID("_Crest_SimDeltaTime");
            public static readonly int s_TemporaryPersistentTarget = Shader.PropertyToID("_Crest_TemporaryPersistentTarget");
        }

        private protected override bool NeedToReadWriteTextureData => true;
        internal override int BufferCount => 2;

        // Is this the first step since being enabled?
        private protected bool _NeedsPrewarmingThisStep = true;

        // This is how far the simulation time is behind Unity's time.
        private protected float _TimeToSimulate = 0f;

        // Pristine historic data. Needed if using blur or multiple viewpoints. For the
        // latter, we cannot optimize the upstream data texture away due to camera filtering.
        private protected RenderTexture _PersistentDataTexture;

        internal int LastUpdateSubstepCount { get; private set; }

        private protected virtual int Kernel => 0;
        private protected virtual bool SkipFlipBuffers => false;
        private protected abstract ComputeShader SimulationShader { get; }

        internal override void Initialize()
        {
            if (SimulationShader == null)
            {
                _Valid = false;
                return;
            }

            base.Initialize();

            _NeedsPrewarmingThisStep = true;
        }

        private protected override void Allocate()
        {
            base.Allocate();

            // Use per-camera data.
            if (!_Water.IsSingleViewpointMode)
            {
                return;
            }

            if (Blur)
            {
                _PersistentDataTexture = CreateLodDataTextures("_Source");
            }
        }

        internal override void Destroy()
        {
            base.Destroy();

            if (_PersistentDataTexture != null) _PersistentDataTexture.Release();
            Helpers.Destroy(_PersistentDataTexture);

            foreach (var data in _AdditionalCameraData.Values)
            {
                var x = data._PersistentData;
                if (x != null) x.Release();
                Helpers.Destroy(x);
            }

            _AdditionalCameraData.Clear();
        }

        internal override void BuildCommandBuffer(WaterRenderer water, CommandBuffer buffer)
        {
            buffer.BeginSample(ID);

            FlipBuffers(buffer);

            // How far are we behind.
            _TimeToSimulate += water.DeltaTime;

            // Do a set of substeps to catch up.
            var substeps = Mathf.FloorToInt(_TimeToSimulate * _SimulationFrequency);
            var delta = substeps > 0 ? (1f / _SimulationFrequency) : 0f;

            LastUpdateSubstepCount = substeps;

            // Even if no steps were needed this frame, the simulation still needs to advect to
            // compensate for camera motion / water scale changes, so do a trivial substep.
            // This could be a specialised kernel that only advects, or the simulation shader
            // could have a branch for 0 delta time.
            if (substeps == 0)
            {
                substeps = 1;
                delta = 0f;
            }

            // Use temporary if only storing one texture upstream which has the source.
            var useTemporary = _Water.IsSingleViewpointMode && !Blur;

            if (useTemporary)
            {
                // No need to clear, as the update dispatch overwrites every pixel, but finding
                // artifacts if not and there is a renderer input. Happens for foam and dynamic
                // waves. Confusing/concerning.
                buffer.GetTemporaryRT(ShaderIDs.s_TemporaryPersistentTarget, DataTexture.descriptor);
                CoreUtils.SetRenderTarget(buffer, ShaderIDs.s_TemporaryPersistentTarget, ClearFlag.Color, ClearColor);
            }

            var final = new RenderTargetIdentifier(DataTexture);
            var target = useTemporary ? new RenderTargetIdentifier(ShaderIDs.s_TemporaryPersistentTarget) : final;
            var source = useTemporary ? final : new RenderTargetIdentifier(_PersistentDataTexture);

            var wrapper = new PropertyWrapperCompute(buffer, SimulationShader, Kernel);

            for (var substep = 0; substep < substeps; substep++)
            {
                var isFirstStep = substep == 0;
                var frame = isFirstStep ? 1 : 0;

                // Record how much we caught up
                _TimeToSimulate -= delta;

                // Buffers are already flipped, but we need to ping-pong for subsequent substeps.
                if (!isFirstStep)
                {
                    // Use temporary target for ping-pong instead of flipping buffer. We do not want
                    // to buffer substeps as they will not match buffered cascade data etc. Each buffer
                    // entry must be for a single frame and substeps are "sub-frame".
                    (source, target) = (target, source);
                }
                else
                {
                    // We only want to handle teleports for the first step.
                    _NeedsPrewarmingThisStep = _NeedsPrewarmingThisStep || _Water._HasTeleportedThisFrame;
                }

                // Both simulation update and input draws need delta time.
                buffer.SetGlobalFloat(ShaderIDs.s_SimDeltaTime, delta);

                wrapper.SetTexture(Crest.ShaderIDs.s_Source, source);
                wrapper.SetTexture(Crest.ShaderIDs.s_Target, target);

                // Compute which LOD data we are sampling source data from. if a scale change has
                // happened this can be any LOD up or down the chain. This is only valid on the
                // first update step, after that the scale source/target data are in the right
                // places.
                wrapper.SetFloat(Lod.ShaderIDs.s_LodChange, isFirstStep ? _Water.ScaleDifferencePower2 : 0);

                wrapper.SetVectorArray(WaterRenderer.ShaderIDs.s_CascadeDataSource, _Water.CascadeData.Previous(frame));
                wrapper.SetVectorArray(_SamplingParametersCascadeSourceShaderID, _SamplingParameters.Previous(frame));

                SetAdditionalSimulationParameters(wrapper);

                var threads = Resolution / k_ThreadGroupSize;
                wrapper.Dispatch(threads, threads, Slices);

                // Only add forces if we did a step.
                if (delta > 0f)
                {
                    SubmitDraws(buffer, Inputs, target);
                }

                // The very first step since being enabled.
                _NeedsPrewarmingThisStep = false;
            }

            // Swap textures if needed.
            if (target != final)
            {
                buffer.CopyTexture(target, final);
            }
            // Preserve non-blurred historic data.
            else if (!useTemporary)
            {
                buffer.CopyTexture(target, source);
            }

            if (useTemporary)
            {
                buffer.ReleaseTemporaryRT(ShaderIDs.s_TemporaryPersistentTarget);
            }

            TryBlur(buffer);

            buffer.EndSample(ID);
        }

        /// <summary>
        /// Set any simulation specific shader parameters.
        /// </summary>
        private protected virtual void SetAdditionalSimulationParameters(PropertyWrapperCompute properties)
        {

        }

        private protected override void ReAllocate()
        {
            base.ReAllocate();

            if (!Enabled)
            {
                return;
            }

            var descriptor = DataTexture.descriptor;

            if (_Water.IsMultipleViewpointMode)
            {
                foreach (var (key, data) in _AdditionalCameraData)
                {
                    var texture = data._PersistentData;
                    texture.Release();
                    texture.descriptor = descriptor;
                    texture.Create();
                }

                return;
            }

            if (_PersistentDataTexture != null)
            {
                _PersistentDataTexture.Release();
                if (Blur)
                {
                    _PersistentDataTexture.descriptor = descriptor;
                    _PersistentDataTexture.Create();
                }
                else
                {
                    Helpers.Destroy(_PersistentDataTexture);
                }
            }
            else if (Blur)
            {
                _PersistentDataTexture = CreateLodDataTextures("_Source");
            }
        }
    }

    partial class PersistentLod
    {
        sealed class AdditionalCameraData
        {
            public RenderTexture _PersistentData;
            public float _TimeToSimulate;
        }

        readonly System.Collections.Generic.Dictionary<Camera, AdditionalCameraData> _AdditionalCameraData = new();

        internal override void LoadCameraData(Camera camera)
        {
            base.LoadCameraData(camera);

            AdditionalCameraData data;

            if (!_AdditionalCameraData.ContainsKey(camera))
            {
                data = new()
                {
                    _PersistentData = CreateLodDataTextures("_Source"),
                    _TimeToSimulate = _TimeToSimulate,
                };

                _AdditionalCameraData.Add(camera, data);
            }
            else
            {
                data = _AdditionalCameraData[camera];
            }

            _PersistentDataTexture = data._PersistentData;
            _TimeToSimulate = data._TimeToSimulate;
        }

        internal override void StoreCameraData(Camera camera)
        {
            base.StoreCameraData(camera);

            if (_AdditionalCameraData.ContainsKey(camera))
            {
                _AdditionalCameraData[camera]._TimeToSimulate = _TimeToSimulate;
            }
        }

        internal override void RemoveCameraData(Camera camera)
        {
            base.RemoveCameraData(camera);

            if (_AdditionalCameraData.ContainsKey(camera))
            {
                var rt = _AdditionalCameraData[camera]._PersistentData;
                if (rt != null) rt.Release();
                Helpers.Destroy(rt);
                _AdditionalCameraData.Remove(camera);
            }
        }
    }
}
