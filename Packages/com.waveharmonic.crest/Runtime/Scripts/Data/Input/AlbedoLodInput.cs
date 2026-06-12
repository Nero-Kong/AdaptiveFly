// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

namespace WaveHarmonic.Crest
{
    /// <summary>
    /// Registers a custom input to the <see cref="AlbedoLod"/>.
    /// </summary>
    /// <remarks>
    /// Attach this to objects that you want to influence the surface color.
    /// </remarks>
    [@HelpURL("Manual/WaterAppearance.html#albedo-inputs")]
    public sealed partial class AlbedoLodInput : LodInput
    {
        internal override LodInputMode DefaultMode => LodInputMode.Renderer;
    }
}
