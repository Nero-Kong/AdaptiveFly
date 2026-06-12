// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;

namespace WaveHarmonic.Crest
{
    /// <summary>
    /// Registers a custom input to the <see cref="AnimatedWavesLod"/>.
    /// </summary>
    /// <remarks>
    /// Attach this to objects that you want to render into the displacment textures to
    /// affect the water shape.
    /// </remarks>
    [@HelpURL("Manual/Waves.html#animated-waves-inputs")]
    public sealed partial class AnimatedWavesLodInput : LodInput
    {
        [@Space(10)]

        [Tooltip("When to render the input into the displacement data.")]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        DisplacementPass _DisplacementPass = DisplacementPass.LodIndependent;

        [Tooltip("Whether to filter this input by wavelength.\n\nIf disabled, it will render to all LODs.")]
        [@Enable(nameof(_DisplacementPass), nameof(DisplacementPass.LodDependent))]
        [@GenerateAPI]
        [DecoratedField, SerializeField]
        bool _FilterByWavelength;

        [Tooltip("Which octave to render into.\n\nFor example, set this to 2 to render into the 2m-4m octave. These refer to the same octaves as the wave spectrum editor.")]
        [@Enable(nameof(_DisplacementPass), nameof(DisplacementPass.LodDependent))]
        [@Enable(nameof(_FilterByWavelength))]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        float _OctaveWavelength = 512f;


        [Header("Culling")]

        [Tooltip("Inform the system how much this input will displace the water surface vertically.\n\nThis is used to set bounding box heights for the water chunks.")]
        [@GenerateAPI]
        [SerializeField]
        float _MaximumDisplacementVertical = 0f;

        [Tooltip("Inform the system how much this input will displace the water surface horizontally.\n\nThis is used to set bounding box widths for the water chunks.")]
        [@GenerateAPI]
        [SerializeField]
        float _MaximumDisplacementHorizontal = 0f;

        [Tooltip("Use the bounding box of an attached renderer component to determine the maximum vertical displacement.")]
        [@Enable(nameof(_Mode), nameof(LodInputMode.Renderer))]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        bool _ReportRendererBounds = false;


        internal override LodInputMode DefaultMode => LodInputMode.Renderer;
        internal override int Pass => (int)_DisplacementPass;

        internal AnimatedWavesLodInput() : base()
        {
            _FollowHorizontalWaveMotion = true;
        }

        private protected override void Initialize()
        {
            base.Initialize();
            _Reporter ??= new(this);
            _DisplacementReporter = _Reporter;
        }

        private protected override void OnDisable()
        {
            base.OnDisable();
            _DisplacementReporter = null;
        }

        internal override float Filter(WaterRenderer water, int slice)
        {
            return AnimatedWavesLod.FilterByWavelength(water, slice, _FilterByWavelength ? _OctaveWavelength : 0f, water.AnimatedWavesLod.Resolution);
        }

        bool ReportDisplacement(WaterRenderer water, ref Rect bounds, ref float horizontal, ref float vertical)
        {
            if (!Enabled)
            {
                return false;
            }

            var maxDispVert = _MaximumDisplacementVertical;

            // let water system know how far from the sea level this shape may displace the surface
            // TODO: we need separate min/max vertical displacement to be optimal.
            if (_ReportRendererBounds)
            {
                var range = Data.HeightRange;
                var minY = range.x;
                var maxY = range.y;
                var seaLevel = water.SeaLevel;
                maxDispVert = Mathf.Max(maxDispVert, Mathf.Abs(seaLevel - minY), Mathf.Abs(seaLevel - maxY));
            }

            var rect = Data.Rect;

            if (bounds.Overlaps(rect, false))
            {
                horizontal += _MaximumDisplacementHorizontal;
                vertical += maxDispVert;
                return true;
            }

            return false;
        }

        float ReportWaveDisplacement(WaterRenderer water, float displacement)
        {
            return displacement;
        }
    }

    partial class AnimatedWavesLodInput
    {
        Reporter _Reporter;

        sealed class Reporter : IReportsDisplacement, IReportWaveDisplacement
        {
            readonly AnimatedWavesLodInput _Input;
            public Reporter(AnimatedWavesLodInput input) => _Input = input;
            public bool ReportDisplacement(WaterRenderer water, ref Rect bounds, ref float horizontal, ref float vertical) => _Input.ReportDisplacement(water, ref bounds, ref horizontal, ref vertical);
            public float ReportWaveDisplacement(WaterRenderer water, float displacement) => _Input.ReportWaveDisplacement(water, displacement);
        }
    }

    partial class AnimatedWavesLodInput
    {
        private protected override int Version => Mathf.Max(base.Version, 1);

        [System.Obsolete("Please use DisplacementPass instead.")]
        [Tooltip("When to render the input into the displacement data.\n\nIf enabled, it renders into all LODs of the simulation after the combine step rather than before with filtering. Furthermore, it will also affect dynamic waves.")]
        [@GenerateAPI(Setter.Custom)]
        [@DecoratedField, SerializeField]
        [HideInInspector]
        bool _RenderPostCombine = true;

        [System.Obsolete]
        void SetRenderPostCombine(bool previous, bool current, bool force = false)
        {
            if (previous == current && !force) return;
            _DisplacementPass = current ? DisplacementPass.LodIndependent : DisplacementPass.LodDependent;
        }

#pragma warning disable CS0612 // Type or member is obsolete
#pragma warning disable CS0618 // Type or member is obsolete
        private protected override void OnMigrate()
        {
            base.OnMigrate();

            if (_Version < 1)
            {
                SetRenderPostCombine(_RenderPostCombine, _RenderPostCombine, force: true);
            }
        }
#pragma warning restore CS0612 // Type or member is obsolete
#pragma warning restore CS0618 // Type or member is obsolete
    }
}
