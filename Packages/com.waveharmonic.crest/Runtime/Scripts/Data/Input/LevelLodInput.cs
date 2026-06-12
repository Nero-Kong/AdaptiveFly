// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;

namespace WaveHarmonic.Crest
{
    /// <summary>
    /// Registers a custom input to the <see cref="LevelLod"/>.
    /// </summary>
    /// <remarks>
    /// Attach this to objects that you want to influence the water height.
    /// </remarks>
    [@HelpURL("Manual/WaterBodies.html#water-bodies")]
    [@FilterEnum(nameof(_Blend), Filtered.Mode.Include, (int)LodInputBlend.Off, (int)LodInputBlend.Additive, (int)LodInputBlend.Minimum, (int)LodInputBlend.Maximum)]
    public sealed partial class LevelLodInput : LodInput
    {
        [@Heading("Water Chunk Culling")]

        [Tooltip("Whether to use the manual \"Height Range\" for water chunk culling.\n\nMandatory for non mesh inputs like \"Texture\".")]
        [@GenerateAPI]
        [@InlineToggle]
        [@SerializeField]
        bool _OverrideHeight;

        [Tooltip("The minimum and maximum height value to report for water chunk culling.")]
        [@Enable(nameof(_OverrideHeight))]
        [@Range(-100, 100, Range.Clamp.None)]
        [@GenerateAPI]
        [SerializeField]
        Vector2 _HeightRange = new(-100, 100);

        LevelLodInput()
        {
            _FollowHorizontalWaveMotion = true;
        }

        // Water level is packed into alpha using the displaced position.
        private protected override bool FollowHorizontalMotion => true;
        internal override LodInputMode DefaultMode => LodInputMode.Geometry;

        internal override void InferBlend()
        {
            base.InferBlend();

            _Blend = LodInputBlend.Off;

            if (_Mode is LodInputMode.Paint or LodInputMode.Texture)
            {
                _Blend = LodInputBlend.Additive;
            }
        }

        private protected override void Initialize()
        {
            base.Initialize();
            _Reporter ??= new(this);
            _HeightReporter = _Reporter;
        }

        private protected override void OnDisable()
        {
            base.OnDisable();
            _HeightReporter = null;
        }

        bool ReportHeight(WaterRenderer water, ref Rect bounds, ref float minimum, ref float maximum)
        {
            if (!Enabled)
            {
                return false;
            }

            // These modes do not provide a height yet.
            if (!Data.HasHeightRange && !_OverrideHeight)
            {
                return false;
            }

            var rect = Data.Rect;

            if (bounds.Overlaps(rect, false))
            {
                var range = _OverrideHeight ? _HeightRange : Data.HeightRange;
                range *= Weight;

                // Make relative to sea level.
                range.x -= water.SeaLevel;
                range.y -= water.SeaLevel;

                var current = new Vector2(minimum, maximum);

                range = _Blend switch
                {
                    LodInputBlend.Additive => range + current,
                    LodInputBlend.Minimum => Vector2.Min(range, current),
                    LodInputBlend.Maximum => Vector2.Max(range, current),
                    _ => range,
                };

                minimum = Mathf.Min(minimum, range.x);
                maximum = Mathf.Max(maximum, range.y);

                return true;
            }

            return false;
        }
    }

    partial class LevelLodInput
    {
        Reporter _Reporter;

        sealed class Reporter : IReportsHeight
        {
            readonly LevelLodInput _Input;
            public Reporter(LevelLodInput input) => _Input = input;
            public bool ReportHeight(WaterRenderer water, ref Rect bounds, ref float minimum, ref float maximum) =>
                _Input.ReportHeight(water, ref bounds, ref minimum, ref maximum);
        }
    }

    partial class LevelLodInput
    {
        private protected override int Version => Mathf.Max(base.Version, 1);

        private protected override void OnMigrate()
        {
            base.OnMigrate();

            // Version 1
            // - Implemented blend mode but default value was serialized as Additive.
            if (_Version < 1)
            {
                if (_Mode is LodInputMode.Spline or LodInputMode.Renderer) _Blend = LodInputBlend.Off;
            }
        }
    }
}
