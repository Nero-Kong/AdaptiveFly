// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#ifndef CREST_WATER_REFRACTION_H
#define CREST_WATER_REFRACTION_H

#if !d_Crest_SimpleTransparency

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Settings.Crest.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Macros.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Depth.hlsl"

#if (CREST_PORTALS != 0)
#include "Packages/com.waveharmonic.crest.portals/Runtime/Shaders/Library/Portals.hlsl"
#endif

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Surface/Utility.hlsl"

#ifndef SUPPORTS_FOVEATED_RENDERING_NON_UNIFORM_RASTER
#define FoveatedRemapLinearToNonUniform(uv) uv
#endif

#if (UNITY_VERSION < 60000000) || !defined(CREST_URP)
float4 _CameraDepthTexture_TexelSize;
#endif

m_CrestNameSpace

float2 GetRefractionCoordinates(const half3 i_View, const half3 i_Normal, const float3 i_Position, const half i_IOR, const half i_Strength)
{
    float3 position = i_Position;

#if (SHADEROPTIONS_CAMERA_RELATIVE_RENDERING != 0)
    position -= _WorldSpaceCameraPos;
#endif

    const half3 ray = refract(-i_View, i_Normal, i_IOR) * i_Strength;
    float2 uv = ComputeNormalizedDeviceCoordinates(position + ray, UNITY_MATRIX_VP);

#if CREST_HDRP
    // Prevent artifacts at edge. Maybe because depth is an atlas for HDRP.
    uv = clamp(uv, _CameraDepthTexture_TexelSize.xy, 1.0 - _CameraDepthTexture_TexelSize.xy);
#endif

    return FoveatedRemapLinearToNonUniform(uv);
}

// We take the unrefracted scene colour as input because having a Scene Colour node in the graph
// appears to be necessary to ensure the scene colours are bound?
void RefractedScene
(
    const half i_RefractionStrength,
    const half i_AirIOR,
    const half i_WaterIOR,
    const half3 i_NormalWS,
    const float3 i_PositionWS,
    const float2 i_PositionNDC,
    const float4 i_ScreenPositionRaw,
    const float i_PixelZ,
    const half3 i_View,
    const float i_SceneZ,
    const float i_SceneZRaw,
    const float i_Scale,
    const float i_LodAlpha,
    const bool i_Underwater,
    const half i_TotalInternalReflectionIntensity,
    out half3 o_SceneColor,
    out float o_SceneDistance,
    out float3 o_ScenePositionWS,
    out float2 o_PositionSS,
    out bool o_Caustics
)
{
    o_Caustics = true;

    half strength = i_RefractionStrength;

    const half _AirToWaterRatio = i_AirIOR / i_WaterIOR;
    const half _WaterToAirRatio = i_WaterIOR / i_AirIOR;

    // If no TIR, then use same IOR.
    const bool isA2WR = !i_Underwater || i_TotalInternalReflectionIntensity < 1.0;

    const half eta = isA2WR ? _AirToWaterRatio : _WaterToAirRatio;

    half3 normal = i_NormalWS;

    // Exchanges accuracy for less artifacts.
    if (isA2WR)
    {
        half multiplier = 0.0;

        if (i_Underwater)
        {
            multiplier = 1.0;
            // Max fade when water is 5m deep.
            multiplier = saturate(g_Crest_WaterDepthAtViewer * 0.2);
            // Max fade by displacement.
            multiplier *= saturate(g_Crest_MaximumVerticalDisplacement - 1.0);
            // Fade towards screen edge where off screen samples happen. + n is fade start.
            multiplier *= saturate((dot(i_PositionNDC - 0.5, -g_Crest_HorizonNormal) + 0.5) * 2.0);
        }

        normal.y *= multiplier;
    }

    // Since we lose detail at a distance, boosting refraction helps visually.
    strength *= lerp(i_Scale, i_Scale * 2.0, i_LodAlpha) * 0.25;

    // Restrict to a reasonable maximum.
    strength = min(strength, i_RefractionStrength * 4.0);

    float2 uv = GetRefractionCoordinates(i_View, normal, i_PositionWS, eta, strength);

    o_PositionSS = min(uv * _ScreenSize.xy, _ScreenSize.xy - 1.0);

#if CREST_BIRP
    float deviceDepth = LoadSceneDepth(o_PositionSS);
#else
    float deviceDepth = SHADERGRAPH_SAMPLE_SCENE_DEPTH(uv);
#endif

#if (CREST_PORTALS != 0)
#if _ALPHATEST_ON
    Portal::EvaluateRefraction(uv, i_SceneZRaw, i_Underwater, deviceDepth, o_Caustics);
#endif
#endif

    float linearDepth = Utility::CrestLinearEyeDepth(deviceDepth);
    float depthDifference = linearDepth - i_PixelZ;

    normal *= saturate(depthDifference);

    uv = GetRefractionCoordinates(i_View, normal, i_PositionWS, eta, strength);

    o_PositionSS = min(uv * _ScreenSize.xy, _ScreenSize.xy - 1.0);

#if CREST_BIRP
    deviceDepth = LoadSceneDepth(o_PositionSS);
#else
    deviceDepth = SHADERGRAPH_SAMPLE_SCENE_DEPTH(uv);
#endif

    linearDepth = Utility::CrestLinearEyeDepth(deviceDepth);
    // It seems that when MSAA is enabled this can sometimes be negative.
    depthDifference = max(linearDepth - i_PixelZ, 0.0);

#if CREST_BIRP
    // Sampling artifacts which manifest as a fine outline around refractions. Always
    // affects BIRP unless we use Load. Does not affect URP unless downsampling or MSAA
    // is used, but Load exposes us to RT scaling. Best to use Sample with HDRP too.
    o_SceneColor = LoadSceneColor(o_PositionSS).rgb;
#else
    // Sampling artifacts if downsampling or MSAA used. Load does not help. And we get
    // outlines around all objects irrespective of refraction.
    o_SceneColor = SHADERGRAPH_SAMPLE_SCENE_COLOR(uv).rgb;
#endif

    o_SceneDistance = depthDifference;
    o_ScenePositionWS = ComputeWorldSpacePosition(uv, deviceDepth, UNITY_MATRIX_I_VP);
#if (SHADEROPTIONS_CAMERA_RELATIVE_RENDERING != 0)
    o_ScenePositionWS += _WorldSpaceCameraPos;
#endif
}

m_CrestNameSpaceEnd

#endif
#endif
