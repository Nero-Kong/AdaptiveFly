// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#ifndef d_WaveHarmonic_Crest_VolumeDebug
#define d_WaveHarmonic_Crest_VolumeDebug

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Macros.hlsl"

m_CrestNameSpace

float4 DebugRenderWaterMask(const bool isWaterSurface, const bool isUnderwater, const float mask, const float3 sceneColour)
{
    // Red:     surface front face when above water
    // Green:   surface back face when below water
    // Cyan:    background when above water
    // Magenta: background when below water
    if (isWaterSurface)
    {
        return float4(sceneColour * float3(mask >= CREST_MASK_ABOVE_SURFACE, mask <= CREST_MASK_BELOW_SURFACE, 0.0), 1.0);
    }
    else
    {
        return float4(sceneColour * float3(isUnderwater * 0.5, (1.0 - isUnderwater) * 0.5, 1.0), 1.0);
    }
}

float4 DebugRenderStencil(float3 sceneColour)
{
    float3 stencil = 1.0;
#if d_Crest_Portal
#if d_Crest_FogAfter
    stencil = float3(1.0, 0.0, 0.0);
#elif d_Crest_FogBefore
    stencil = float3(0.0, 1.0, 0.0);
#else
    stencil = float3(0.0, 0.0, 1.0);
#endif
#endif
    return float4(sceneColour * stencil, 1.0);
}

m_CrestNameSpaceEnd

#endif // d_WaveHarmonic_Crest_VolumeDebug
