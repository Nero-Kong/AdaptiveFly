// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#ifndef d_WaveHarmonic_Crest_Meniscus
#define d_WaveHarmonic_Crest_Meniscus

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Settings.Crest.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Macros.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Constants.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Globals.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Cascade.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Helpers.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Depth.hlsl"

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Surface/Shim.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Keywords.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Lighting.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Surface/VolumeLighting.hlsl"

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Surface/Data.hlsl"

#if d_Masked
TEXTURE2D_X(_Crest_WaterMaskTexture);
#endif

#if d_Crest_Lighting
// Surface/Volume parameters.
half4 _Crest_Absorption;
half4 _Crest_Scattering;
half  _Crest_Anisotropy;
half  _Crest_DirectTerm;
half  _Crest_AmbientTerm;
half  _Crest_ShadowsAffectsAmbientFactor;

// Volume parameters.
half  _Crest_SunBoost;
half3 _Crest_AmbientLighting;
uint  _Crest_DataSliceOffset;
#endif

half _Crest_Radius;
half _Crest_RefractionStrength;

bool _Crest_PortalInverted;

m_CrestNameSpace

struct Attributes
{
#if d_Crest_Geometry
    float3 positionOS : POSITION;
#else
    uint id : SV_VertexID;
#endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
#if d_Crest_Geometry
    float3 positionWS : TEXCOORD;
#else
    float2 uv : TEXCOORD;
#endif
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings Vertex(Attributes input)
{
    Varyings output;
    ZERO_INITIALIZE(Varyings, output);
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

#if d_Crest_Geometry
    output.positionCS = TransformObjectToHClip(input.positionOS);
    output.positionWS = TransformObjectToWorld(input.positionOS);
#else
    output.positionCS = GetFullScreenTriangleVertexPosition(input.id);
    output.uv = GetFullScreenTriangleTexCoord(input.id);
#endif

    return output;
}

half4 Fragment(Varyings input)
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

#if d_Masked
    // Prevent rendering inside of new portal modules.
    if ((LOAD_TEXTURE2D_X(_Crest_WaterMaskTexture, input.positionCS.xy).r == k_Crest_MaskInsidePortal) == _Crest_PortalInverted)
    {
        discard;
    }
#endif

    float3 positionWS;
    float3 directionWS;
    float2 uv;

#if d_Crest_Geometry
    {
        positionWS = input.positionWS;
#if (SHADEROPTIONS_CAMERA_RELATIVE_RENDERING != 0)
        positionWS.xyz += _WorldSpaceCameraPos.xyz;
#endif
        directionWS = GetWorldSpaceNormalizeViewDir(positionWS);
        uv = input.positionCS.xy / _ScreenSize.xy;
    }
#else
    {
        positionWS = ComputeWorldSpacePosition(input.uv, UNITY_NEAR_CLIP_VALUE, UNITY_MATRIX_I_VP);

#if (SHADEROPTIONS_CAMERA_RELATIVE_RENDERING != 0)
        positionWS.xyz += _WorldSpaceCameraPos.xyz;
#endif

        directionWS = GetWorldSpaceNormalizeViewDir(positionWS);

        uv = input.uv;
    }
#endif

    const float height = SampleWaterLineHeight(positionWS.xz).r;

    // Double as we half it if below.
    float radius = _Crest_Radius * 2.0;

#if d_Crest_Refraction
    // Double the radius as aggressive falloff makes it much smaller.
    radius *= 2.0;
#endif

    float signedDistance = positionWS.y - height;

    float3 viewDirWS = normalize(_WorldSpaceCameraPos - positionWS);

    const float distance = abs(signedDistance);

    if (signedDistance < 0)
    {
        radius = max(0.002, radius * 0.25);
    }

    if (distance > radius)
    {
        discard;
    }

    half3 color = 0.0;

#if d_Crest_Lighting
    {
        half3 absorption = _Crest_Absorption.xyz;
        half3 scattering = _Crest_Scattering.xyz;

        // Keep the same as the volume.
        const uint sliceIndex = clamp(_Crest_DataSliceOffset, 0, g_Crest_LodCount - 2);

#if d_Crest_AbsorptionLod
        if (g_Crest_SampleAbsorptionSimulation)
        {
            absorption = Cascade::MakeAbsorption(sliceIndex).Sample(_WorldSpaceCameraPos.xz).xyz;
        }
#endif

#if d_Crest_ScatteringLod
        if (g_Crest_SampleScatteringSimulation)
        {
            scattering = Cascade::MakeScattering(sliceIndex).Sample(_WorldSpaceCameraPos.xz).xyz;
        }
#endif

        float3 lightDirection; float3 lightColor;
        PrimaryLight(positionWS, lightColor, lightDirection);

        const half3 extinction = VolumeExtinction(absorption, scattering);

        half opacity = 1.0;
#if !d_Crest_Refraction
        // Meniscus can look too dark in shallow water.
        {
            const float depth = Cascade::MakeDepth(sliceIndex).SampleSignedDepthFromSeaLevel(_WorldSpaceCameraPos.xz);
            opacity = VolumeOpacity(extinction, depth * 0.25);
        }
#endif

        half shadow = 1.0;

#if d_Crest_ShadowLod
        {
            // Soft in red, hard in green. But hard not computed in HDRP.
            shadow = 1.0 - Cascade::MakeShadow(sliceIndex).SampleShadow(_WorldSpaceCameraPos.xz).x;
        }
#endif

        half3 lighting = VolumeLighting
        (
            extinction,
            scattering,
            _Crest_Anisotropy,
            shadow,
            lerp(half3(0, 1, 0), directionWS, opacity),
            AmbientLight(_Crest_AmbientLighting),
            lerp(half3(0, -1, 0), lightDirection, opacity),
            lightColor,
            half3(0.0, 0.0, 0.0), // Additional lights
            1.0, // Additional light blend
            _Crest_AmbientTerm,
            _Crest_DirectTerm,
            1.0, // Additional light term
            _Crest_SunBoost,
            _Crest_ShadowsAffectsAmbientFactor
        );

#if CREST_HDRP
        lighting *= GetCurrentExposureMultiplier();
#endif

        color = lighting;
    }
#endif

    const float falloff = 1.0 - smoothstep(0.0, radius, distance);

#if d_Crest_Refraction
    {
        const half3 normal = SampleWaterLineNormal(positionWS.xz, height);
        float2 dir = Utility::WorldNormalToScreenDirection(positionWS, normal, UNITY_MATRIX_VP, 0.01);

        const float aspect = _ScreenParams.x / _ScreenParams.y;
        dir.x /= aspect;

        const float2 uvRefracted = uv - dir * falloff * _Crest_RefractionStrength;

        half3 scene = SampleSceneColor(uvRefracted);

        if (signedDistance >= 0)
        {
            // Blend back in with original. Cannot seem to do this with alpha without losing
            // some lighting.
            scene = lerp
            (
                scene,
                SampleSceneColor(uv),
                saturate((distance / radius) * 5.0)
            );
        }

        color = lerp(color, scene, 0.5);
    }
#endif

    return float4(color, falloff);
}

m_CrestNameSpaceEnd

m_CrestVertex
m_CrestFragment(half4)

#endif // d_WaveHarmonic_Crest_Meniscus
