// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#ifndef d_WaveHarmonic_Crest_Mask
#define d_WaveHarmonic_Crest_Mask

#if d_LodInput
#define d_RequirePositionWS 1
#endif

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Settings.Crest.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Macros.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Constants.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Globals.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Helpers.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Vertex/Surface.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Depth.hlsl"

#if (CREST_PORTALS != 0)
#include "Packages/com.waveharmonic.crest.portals/Runtime/Shaders/Library/Portals.hlsl"
#endif

m_CrestNameSpace

half4 Fragment(const Varyings i_Input, const bool i_FrontFace)
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i_Input);

#if d_LodInput
    return half4(i_Input._PositionWS.y - g_Crest_WaterCenter.y, 0, 0, 1);
#endif

    half result = 0.0;

#if (CREST_PORTALS != 0)
#if !d_Tunnel
    if (m_CrestPortal)
    {
        Portal::EvaluateMask(i_Input._PositionCS);
    }
#endif
#endif

    if (IsUnderWater(i_FrontFace, g_Crest_ForceUnderwater))
    {
        result = CREST_MASK_BELOW_SURFACE;
    }
    else
    {
        result = CREST_MASK_ABOVE_SURFACE;
    }

#if (CREST_PORTALS != 0)
#if d_Crest_NegativeVolumePass
    result = Portal::FixMaskForNegativeVolume(result, i_Input._PositionCS.xy);
#endif

#if d_Tunnel
    const float2 positionSS = i_Input._PositionCS.xy;
    const float ffz = LOAD_DEPTH_TEXTURE_X(_Crest_PortalFogBeforeTexture, positionSS);
    const float bfz = LOAD_DEPTH_TEXTURE_X(_Crest_PortalFogAfterTexture, positionSS);
    if (ffz <= 0.0 && bfz > 0.0)
    {
        result = CREST_MASK_ABOVE_SURFACE;
    }
#endif
#endif

    return (half4)result;
}

m_CrestNameSpaceEnd

m_CrestVertex
m_CrestFragmentWithFrontFace(half4)

#endif // d_WaveHarmonic_Crest_Mask
