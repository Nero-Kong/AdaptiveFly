// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#ifndef d_WaveHarmonic_Crest_SurfaceFacing
#define d_WaveHarmonic_Crest_SurfaceFacing

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Constants.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Macros.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Settings.Crest.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Helpers.hlsl"

TEXTURE2D_X(_Crest_WaterMaskTexture);

m_CrestNameSpace

bool IsUnderWater(const bool i_FrontFace, const int i_ForceUnderwater, const uint2 i_PositionSS)
{
    bool underwater = false;

    // Use mask.
    if (i_ForceUnderwater == 0)
    {
        underwater = LOAD_TEXTURE2D_X(_Crest_WaterMaskTexture, i_PositionSS).r <= CREST_MASK_BELOW_SURFACE;
    }
    else
    {
        underwater = IsUnderWater(i_FrontFace, i_ForceUnderwater);
    }

    return underwater;
}

m_CrestNameSpaceEnd

#endif // d_WaveHarmonic_Crest_SurfaceFacing
