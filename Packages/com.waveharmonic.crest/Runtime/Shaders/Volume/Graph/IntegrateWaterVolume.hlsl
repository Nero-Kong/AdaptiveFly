// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

// NOTE: It is important that everything has a Crest prefix to avoid possible conflicts.
// NOTE: No keywords so no mask color/depth variants not available.

#ifndef d_WaveHarmonic_Crest_ApplyWaterVolumeFog
#define d_WaveHarmonic_Crest_ApplyWaterVolumeFog

#ifndef SHADERGRAPH_PREVIEW

// TODO: enable dithering?
#define k_DisableCaustics
#define k_DisableDithering

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Macros.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Settings.Crest.hlsl"

#if (CREST_PORTALS != 0)
#define d_Crest_Portal 1
#endif

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Surface/Shim.hlsl"

// Uses SHADERPASS which is broken for everyone else.
#undef d_IsAdditionalLight

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Depth.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Volume/UnderwaterShared.hlsl"

#if !d_Crest_WaterSurface
#define CREST_INTEGRATE_COLOR_AFTER_FOG(color)
#define CREST_INTEGRATE_COLOR_FINAL(color)
#endif

m_CrestNameSpace

static bool s_IsUnderWater;
static half3 s_VolumeOpacity;
static half3 s_VolumeLighting;

float3 ApplyFog(float3 color)
{
#if (CREST_DISCARD_ATMOSPHERIC_SCATTERING == 0)
    if (!s_IsUnderWater)
    {
        return color;
    }
    else
#endif
    {
        return lerp(color, s_VolumeLighting, s_VolumeOpacity * (s_IsUnderWater ? 1.0 : 0.0));
    }
}

float3 NoFog(float3 color)
{
    return color;
}

void SetUpFog(const float2 i_PositionNDC, const float3 i_PositionWS, const float i_DepthRaw, const half i_Multiplier)
{
#if !CREST_BIRP
// Uses SHADERPASS which is broken for everyone else.
#if CREST_SHADOWPASS
    return;
#endif
#endif

    const float2 positionSS = i_PositionNDC.xy * _ScreenSize.xy;

    const half mask = LOAD_TEXTURE2D_X(_Crest_WaterMaskTexture, positionSS).r;

    // Skip if not underwater. We could also "&& rawSurfaceDepth < i_DepthRaw" to
    // exclude objects behind the front-faces from receiving atmospheric fog, but we
    // are using transparent blending which leaves a bright outline due to the edges
    // receiving insufficient fog. Excluding these objects from atmospheric fog gives
    // little benefit.
    if (mask >= CREST_MASK_NO_FOG && mask < CREST_MASK_ABOVE_SURFACE_KEPT)
    {
        return;
    }

#if (CREST_DISCARD_ATMOSPHERIC_SCATTERING != 0)
#if !d_Transparent
    // FIXME: Find alternative solution for new mask.
    const float rawSurfaceDepth = LOAD_DEPTH_TEXTURE_X(_Crest_WaterMaskDepthTexture, positionSS).r;

    // Skip discarding fog if opaque object is behind back-faces.
    if (mask < CREST_MASK_NO_FOG && mask > CREST_MASK_BELOW_SURFACE_KEPT && i_DepthRaw < rawSurfaceDepth)
    {
        return;
    }
#endif
#endif

    // Get the largest distance.
    float rawFogDistance = i_DepthRaw;
    float fogDistanceOffset = _ProjectionParams.y;
    float fogDistance = 0.0;

#if (CREST_PORTALS != 0)
    if (!Portal::EvaluateFog(i_PositionNDC, mask, rawFogDistance, fogDistanceOffset))
    {
        return;
    }
    else
#endif
    {
        fogDistance = Utility::CrestLinearEyeDepth(rawFogDistance) - fogDistanceOffset;
    }

    s_IsUnderWater = true;

    ApplyUnderwaterEffect
    (
        0,              // Color
        0,              // TIR only
        0,              // Caustics only
        fogDistance,
        GetWorldSpaceNormalizeViewDir(i_PositionWS),
        positionSS,
        i_PositionWS,
        false,          // No caustics
        true,           // TODO: implement
        true,           // TODO: implement
        i_Multiplier,
        s_VolumeOpacity,
        s_VolumeLighting
    );
}

m_CrestNameSpaceEnd

#if d_Transparent
#define ApplyFog(x) ApplyFog(x)
#else
#define ApplyFog(x) NoFog(x)
#endif

#if CREST_BIRP
// Color is RGBA.
#ifdef UNITY_PASS_FORWARDADD
#define m_Unity_FogColor fixed4(0, 0, 0, 0)
#else
#define m_Unity_FogColor unity_FogColor
#endif // UNITY_PASS_FORWARDADD

#undef UNITY_APPLY_FOG
#if (CREST_DISCARD_ATMOSPHERIC_SCATTERING != 0)
#define UNITY_APPLY_FOG(coord, color) \
if (m_Crest::s_IsUnderWater) \
{ \
    color.rgb = m_Crest::ApplyFog(color.rgb); \
} \
else \
{ \
    UNITY_APPLY_FOG_COLOR(coord, color, m_Unity_FogColor); \
    CREST_INTEGRATE_COLOR_AFTER_FOG(color.rgb) \
} \
CREST_INTEGRATE_COLOR_FINAL(color.rgb)
#else
#define UNITY_APPLY_FOG(coord, color) \
UNITY_APPLY_FOG_COLOR(coord, color, m_Unity_FogColor); \
color.rgb = m_Crest::ApplyFog(color.rgb); \
if (!m_Crest::s_IsUnderWater) \
{ \
    CREST_INTEGRATE_COLOR_AFTER_FOG(color.rgb) \
} \
CREST_INTEGRATE_COLOR_FINAL(color.rgb)
#endif // CREST_DISCARD_ATMOSPHERIC_SCATTERING
#endif // CREST_BIRP

#if CREST_HDRP
// Color is RGBA.
#if (CREST_DISCARD_ATMOSPHERIC_SCATTERING != 0)
#define EvaluateAtmosphericScattering(i, V, color) color; \
if (m_Crest::s_IsUnderWater) \
{ \
    color.rgb = m_Crest::ApplyFog(color.rgb); \
} \
else \
{ \
    color = EvaluateAtmosphericScattering(i, V, color); \
    CREST_INTEGRATE_COLOR_AFTER_FOG(color.rgb) \
} \
CREST_INTEGRATE_COLOR_FINAL(color.rgb)
#else
#define EvaluateAtmosphericScattering(i, V, color) color; \
color = EvaluateAtmosphericScattering(i, V, color); \
color.rgb = m_Crest::ApplyFog(color.rgb); \
if (!m_Crest::s_IsUnderWater) \
{ \
    CREST_INTEGRATE_COLOR_AFTER_FOG(color.rgb); \
} \
CREST_INTEGRATE_COLOR_FINAL(color.rgb)
#endif
#endif

#if CREST_URP
// Color is RGB.
#if (CREST_DISCARD_ATMOSPHERIC_SCATTERING != 0)
#define MixFog(color, coord) color; \
if (m_Crest::s_IsUnderWater) \
{ \
    color = m_Crest::ApplyFog(color); \
} \
else \
{ \
    color = MixFog(color, coord); \
    CREST_INTEGRATE_COLOR_AFTER_FOG(color) \
} \
CREST_INTEGRATE_COLOR_FINAL(color)
#else
#define MixFog(color, coord) color; \
color = MixFog(color, coord); \
color = m_Crest::ApplyFog(color); \
if (!m_Crest::s_IsUnderWater) \
{ \
    CREST_INTEGRATE_COLOR_AFTER_FOG(color); \
} \
CREST_INTEGRATE_COLOR_FINAL(color)
#endif
#endif

#endif // SHADERGRAPH_PREVIEW

void CrestNodeIntegrateWaterVolume_half
(
    const float2 i_PositionNDC,
    const float3 i_PositionWS,
    const float i_DepthRaw,
    const half i_Multiplier,
    const half4 i_Color,
    const half3 i_Emission,
    out half4 o_Color,
    out half3 o_Emission
)
{
    o_Color = i_Color;
    o_Emission = i_Emission;

#ifndef SHADERGRAPH_PREVIEW
    m_Crest::SetUpFog(i_PositionNDC, i_PositionWS, i_DepthRaw, i_Multiplier);
#endif
}

void CrestNodeIntegrateWaterVolume_float
(
    const float2 i_PositionNDC,
    const float3 i_PositionWS,
    const float i_DepthRaw,
    const half i_Multiplier,
    const float4 i_Color,
    const float3 i_Emission,
    out float4 o_Color,
    out float3 o_Emission
)
{
    o_Color = i_Color;
    o_Emission = i_Emission;

    if (i_Multiplier == 0)
    {
        return;
    }

#ifndef SHADERGRAPH_PREVIEW
    m_Crest::SetUpFog(i_PositionNDC, i_PositionWS, i_DepthRaw, i_Multiplier);
#endif
}

#endif // d_WaveHarmonic_Crest_ApplyWaterVolumeFog
