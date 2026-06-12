// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#ifndef d_WaveHarmonic_Crest_Surface_Emission
#define d_WaveHarmonic_Crest_Surface_Emission

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Macros.hlsl"

m_CrestNameSpace

half3 FoamBioluminescence
(
    const half i_FoamData,
    const half i_FoamMap,
    const half3 i_BioluminescenceColor,
    const half i_BioluminescenceIntensity,
    const half i_BioluminescenceGlowCoverage,
    const half i_BioluminescenceGlowIntensity,
    const bool i_BioluminescenceSparklesEnabled,
    const half i_BioluminescenceSparklesMap,
    const half i_BioluminescenceSparklesCoverage,
    const half i_BioluminescenceSparklesIntensity,
    const half i_BioluminescenceMaximumDepth,
    const half i_WaterDepth
)
{
    half3 emission = 0.0;

    const half weight = 1.0 - saturate(i_WaterDepth / i_BioluminescenceMaximumDepth);

    if (weight <= 0.0)
    {
        return emission;
    }

    emission +=
            (i_BioluminescenceColor * i_FoamMap * i_BioluminescenceIntensity) +
            (i_BioluminescenceColor * saturate(i_FoamData - (1.0 - i_BioluminescenceGlowCoverage)) * i_BioluminescenceGlowIntensity);

    if (i_BioluminescenceSparklesEnabled)
    {
        emission += (i_BioluminescenceColor * i_BioluminescenceSparklesMap * saturate(i_FoamData - (1.0 - i_BioluminescenceSparklesCoverage)) * i_BioluminescenceSparklesIntensity);
    }

    emission *= weight;

    return emission;
}

m_CrestNameSpaceEnd

#endif // d_WaveHarmonic_Crest_Surface_Emission
