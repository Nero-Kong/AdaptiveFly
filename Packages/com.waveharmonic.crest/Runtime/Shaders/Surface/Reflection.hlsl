// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#ifndef CREST_WATER_REFLECTION_H
#define CREST_WATER_REFLECTION_H

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Surface/Keywords.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Macros.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Surface/Utility.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Helpers.hlsl"

float _Crest_ReflectionOverscan;
float4 _Crest_ReflectionPositionNormal[2];
float4x4 _Crest_ReflectionMatrixIVP[2];
float4x4 _Crest_ReflectionMatrixV[2];
Texture2DArray _Crest_ReflectionColorTexture;
SamplerState sampler_Crest_ReflectionColorTexture;
Texture2DArray _Crest_ReflectionDepthTexture;

// Try and use already defined samplers.
#if defined(CREST_HDRP) && defined(SHADERCONFIG_CS_HLSL) && defined(UNITY_SHADER_VARIABLES_INCLUDED)
#define sampler_Crest_point_clamp s_point_clamp_sampler
#else
SamplerState sampler_Crest_point_clamp;
#endif

m_CrestNameSpace

half4 PlanarReflection
(
    const Texture2DArray i_ReflectionsTexture,
    const SamplerState i_ReflectionsSampler,
    const half i_Intensity,
    const half i_Smoothness,
    const half i_Roughness,
    const float i_SurfaceDepth,
    const half3 i_NormalWS,
    const half i_NormalStrength,
    const half3 i_ViewDirectionWS,
    const float2 i_PositionNDC,
    const bool i_Underwater
)
{
    const uint slice = i_Underwater ? 1 : 0;

    half3 planeNormal = half3(0.0, i_Underwater ? -1.0 : 1.0, 0.0);
    half3 reflected = reflect(-i_ViewDirectionWS, lerp(planeNormal, i_NormalWS, i_NormalStrength));
    reflected.y = -reflected.y;

    float4 positionCS = mul(UNITY_MATRIX_VP, half4(reflected, 0.0));
#if UNITY_UV_STARTS_AT_TOP
    positionCS.y = -positionCS.y;
#endif

    float2 positionNDC = positionCS.xy * rcp(positionCS.w) * 0.5 + 0.5;

    // Overscan.
    positionNDC.xy -= 0.5;
    positionNDC.xy *= _Crest_ReflectionOverscan;
    positionNDC.xy += 0.5;

    // Cancel out distortion if out of bounds. We could make this nicer by doing an edge fade but the improvement is
    // barely noticeable. Edge fade requires recalculating the above a second time.
    const float4 positionAndNormal = _Crest_ReflectionPositionNormal[slice];

    if (dot(positionNDC - positionAndNormal.xy, positionAndNormal.zw) < 0.0)
    {
        if (i_Underwater)
        {
            float2 ndc = i_PositionNDC;
            ndc.xy -= 0.5;
            ndc.xy *= _Crest_ReflectionOverscan;
            ndc.xy += 0.5;

            positionNDC = lerp(ndc, positionNDC, 0.25);
        }
        else
        {
            // Below horizon sample!
            // There are still some bad samples, but they are very minor.
            const half2 hypotenuse = positionAndNormal.xy - positionNDC;
            const half angle = acos(saturate(dot(positionAndNormal.zw, normalize(hypotenuse))));
            const half adjacentLength = (cos(angle) * length(hypotenuse));

            positionNDC += (positionAndNormal.zw * adjacentLength) / _Crest_ReflectionOverscan;
        }
    }

    half4 reflection;

#if d_Crest_PlanarReflectionsApplySmoothness
    if (_Crest_PlanarReflectionsApplySmoothness)
    {
        const half roughness = PerceptualSmoothnessToPerceptualRoughness(i_Smoothness);
        half level = PerceptualRoughnessToMipmapLevel(roughness, i_Roughness);
        reflection = i_ReflectionsTexture.SampleLevel(sampler_Crest_ReflectionColorTexture, float3(positionNDC, i_Underwater), level);
    }
    else
#endif
    {
        reflection = i_ReflectionsTexture.SampleLevel(sampler_Crest_point_clamp, float3(positionNDC, i_Underwater), 0.0);
    }

    // If more than four layers are used on the terrain, they will appear black if HDR
    // is enabled on the planar reflection camera. Alpha is probably a negative value.
    reflection.a = saturate(reflection.a);

    reflection.a *= i_Intensity;

    // Mitigate leaks.
    {
        // TODO: calculate linear depth from device depth directly. First attempt failed.
        // Most effective when surface is smooth due to mip-maps. Surprisingly effective
        // even when rough.
        const float rRawDepth = _Crest_ReflectionDepthTexture.SampleLevel(sampler_Crest_point_clamp, float3(positionNDC, i_Underwater), 0).r;
        const float3 rPositionWS = Utility::SafeComputeWorldSpacePosition(positionNDC, rRawDepth, _Crest_ReflectionMatrixIVP[slice]);
        const float rDepth = LinearEyeDepth(rPositionWS, _Crest_ReflectionMatrixV[slice]);

        if (rRawDepth > 0.0 && rDepth <= i_SurfaceDepth)
        {
            reflection.a = 0.0;
        }
    }

    return reflection;
}

m_CrestNameSpaceEnd

#endif
