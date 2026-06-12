// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

namespace WaveHarmonic.Crest
{
    /// <summary>
    /// Registers a custom input to the <see cref="ShadowLod"/>.
    /// </summary>
    /// <remarks>
    /// Attach this objects that you want use to override shadows.
    /// </remarks>
    [@HelpURL("Manual/WaterAppearance.html#shadows-lod")]
    public sealed partial class ShadowLodInput : LodInput
    {
        internal override LodInputMode DefaultMode => LodInputMode.Renderer;
    }
}
