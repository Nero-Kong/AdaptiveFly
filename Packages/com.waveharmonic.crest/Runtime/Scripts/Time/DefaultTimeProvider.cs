// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

namespace WaveHarmonic.Crest
{
    /// <summary>
    /// Default time provider - sets the water time to Unity's game time.
    /// </summary>
    sealed class DefaultTimeProvider : ITimeProvider
    {
        public float Time => UnityEngine.Time.time;
        public float Delta => UnityEngine.Time.deltaTime;
    }
}
