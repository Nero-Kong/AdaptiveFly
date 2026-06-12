// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using WaveHarmonic.Crest.Utility;

namespace WaveHarmonic.Crest
{
    /// <summary>
    /// Simulates horizontal motion of water.
    /// </summary>
    [FilterEnum(nameof(_QuerySource), Filtered.Mode.Exclude, (int)LodQuerySource.CPU)]
    [FilterEnum(nameof(_TextureFormatMode), Filtered.Mode.Exclude, (int)LodTextureFormatMode.Automatic)]
    public sealed partial class FlowLod : Lod<IFlowProvider>
    {
        const string k_FlowKeyword = "CREST_FLOW_ON_INTERNAL";

        static new class ShaderIDs
        {
            public static readonly int s_Flow = Shader.PropertyToID("g_Crest_Flow");
        }

        internal static readonly Color s_GizmoColor = new(0f, 0f, 1f, 0.5f);

        internal override string ID => "Flow";
        internal override Color GizmoColor => s_GizmoColor;
        private protected override Color ClearColor => Color.black;
        private protected override bool NeedToReadWriteTextureData => true;

        private protected override GraphicsFormat RequestedTextureFormat => _TextureFormatMode switch
        {
            LodTextureFormatMode.Performance => GraphicsFormat.R16G16_SFloat,
            LodTextureFormatMode.Precision => GraphicsFormat.R32G32_SFloat,
            LodTextureFormatMode.Manual => _TextureFormat,
            _ => throw new System.NotImplementedException(),
        };

        internal FlowLod()
        {
            _Resolution = 128;
            _TextureFormat = GraphicsFormat.R16G16_SFloat;
            _MaximumQueryCount = 1024;
        }

        internal override void Enable()
        {
            base.Enable();

            Shader.EnableKeyword(k_FlowKeyword);
        }

        internal override void Disable()
        {
            base.Disable();

            Shader.DisableKeyword(k_FlowKeyword);
        }

        internal override void BuildCommandBuffer(WaterRenderer water, CommandBuffer buffer)
        {
            var time = water.CurrentTime;
            var period = 1f;
            var half = period * 0.5f;
            var offset0 = Helpers.Fmod(time, period);
            var weight0 = offset0 / half;
            if (weight0 > 1f) weight0 = 2f - weight0;
            var offset1 = Helpers.Fmod(time + half, period);
            var weight1 = 1f - weight0;

            Shader.SetGlobalVector(ShaderIDs.s_Flow, new(offset0, weight0, offset1, weight1));

            base.BuildCommandBuffer(water, buffer);
        }

        private protected override IFlowProvider CreateProvider(bool onEnable)
        {
            Queryable?.CleanUp();
            // Flow is GPU only, and can only be queried using the compute path.
            return onEnable && Enabled && QuerySource == LodQuerySource.GPU
                ? IFlowProvider.Create(_Water)
                : IFlowProvider.None;
        }

        internal override void SetGlobals(bool onEnable)
        {
            base.SetGlobals(onEnable);

            // Zero offset. Use the first sample.
            Shader.SetGlobalVector(ShaderIDs.s_Flow, new(0, 1, 0, 0));
        }

        internal static readonly SortedList<int, ILodInput> s_Inputs = new(Helpers.DuplicateComparison);
        private protected override SortedList<int, ILodInput> Inputs => s_Inputs;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void OnLoad()
        {
            s_Inputs.Clear();
        }
    }
}
