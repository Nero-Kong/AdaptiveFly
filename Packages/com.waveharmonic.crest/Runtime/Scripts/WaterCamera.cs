// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;
using WaveHarmonic.Crest.Internal;

namespace WaveHarmonic.Crest
{
    /// <summary>
    /// Marks a camera to render water regardless of exclusions.
    /// </summary>
    /// <remarks>
    /// This is only necessary when using <see cref="WaterCameraExclusion"/>.
    /// </remarks>
    [AddComponentMenu(Constants.k_MenuPrefixScripts + "Water Camera")]
    public sealed class WaterCamera : ManagedBehaviour<WaterRenderer>
    {

    }
}
