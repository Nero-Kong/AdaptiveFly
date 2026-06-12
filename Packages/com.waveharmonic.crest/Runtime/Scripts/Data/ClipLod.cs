// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using WaveHarmonic.Crest.Utility;

namespace WaveHarmonic.Crest
{
    /// <summary>
    /// The default state for clipping.
    /// </summary>
    [@GenerateDoc]
    public enum DefaultClippingState
    {
        /// <inheritdoc cref="Generated.DefaultClippingState.NothingClipped"/>
        [Tooltip("By default, nothing is clipped. Use clip inputs to remove water.")]
        NothingClipped,

        /// <inheritdoc cref="Generated.DefaultClippingState.EverythingClipped"/>
        [Tooltip("By default, everything is clipped. Use clip inputs to add water.")]
        EverythingClipped,
    }

    /// <summary>
    /// Drives water surface clipping (carving holes).
    /// </summary>
    /// <remarks>
    /// 0-1 values, surface clipped when > 0.5.
    /// </remarks>
    [FilterEnum(nameof(_TextureFormatMode), Filtered.Mode.Exclude, (int)LodTextureFormatMode.Automatic)]
    public sealed partial class ClipLod : Lod
    {
        [Tooltip("The default clipping behaviour.\n\nWhether to clip nothing by default (and clip inputs remove patches of surface), or to clip everything by default (and clip inputs add patches of surface).")]
        [@GenerateAPI(Setter.Custom)]
        [@DecoratedField, SerializeField]
        internal DefaultClippingState _DefaultClippingState = DefaultClippingState.NothingClipped;

        internal static readonly Color s_GizmoColor = new(0f, 1f, 1f, 0.5f);

        internal override string ID => "Clip";
        internal override string Name => "Clip Surface";
        internal override Color GizmoColor => s_GizmoColor;
        private protected override Color ClearColor => _DefaultClippingState == DefaultClippingState.EverythingClipped ? Color.white : Color.black;
        private protected override bool NeedToReadWriteTextureData => true;
        private protected override bool RequiresClearBorder => true;
        internal override bool SkipEndOfFrame => true;

        private protected override GraphicsFormat RequestedTextureFormat => _TextureFormatMode switch
        {
            // The clip values only really need 8bits (unless using signed distance).
            LodTextureFormatMode.Performance => GraphicsFormat.R8_UNorm,
            LodTextureFormatMode.Precision => GraphicsFormat.R16_UNorm,
            LodTextureFormatMode.Manual => _TextureFormat,
            _ => throw new System.NotImplementedException(),
        };

        internal ClipLod()
        {
            _TextureFormat = GraphicsFormat.R8_UNorm;
        }

        internal static readonly SortedList<int, ILodInput> s_Inputs = new(Helpers.DuplicateComparison);
        private protected override SortedList<int, ILodInput> Inputs => s_Inputs;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void OnLoad()
        {
            s_Inputs.Clear();
        }

        void SetDefaultClippingState(DefaultClippingState previous, DefaultClippingState current)
        {
            if (previous == current) return;
            if (_Water == null || !_Water.isActiveAndEnabled || !Enabled) return;

            // Change default clipping state.
            _TargetsToClear = true;
        }

#if UNITY_EDITOR
        [@OnChange]
        private protected override void OnChange(string path, object previous)
        {
            base.OnChange(path, previous);

            switch (path)
            {
                case nameof(_DefaultClippingState):
                    SetDefaultClippingState((DefaultClippingState)previous, _DefaultClippingState);
                    break;
            }
        }
#endif
    }
}
