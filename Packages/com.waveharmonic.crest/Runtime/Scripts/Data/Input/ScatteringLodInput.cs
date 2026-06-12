// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

namespace WaveHarmonic.Crest
{
    /// <summary>
    /// Registers a custom input to the <see cref="ScatteringLod"/>.
    /// </summary>
    /// <remarks>
    /// Attach this to objects that you want to influence the scattering color.
    /// </remarks>
    [@HelpURL("Manual/WaterAppearance.html#volume-color-inputs")]
    public sealed partial class ScatteringLodInput : LodInput
    {
#if d_CrestPaint
        internal override LodInputMode DefaultMode => LodInputMode.Paint;
#else
        internal override LodInputMode DefaultMode => LodInputMode.Renderer;
#endif

        internal override void InferBlend()
        {
            base.InferBlend();
            _Blend = LodInputBlend.Alpha;
        }

        // Looks fine moving around.
        private protected override bool FollowHorizontalMotion => true;
    }
}
