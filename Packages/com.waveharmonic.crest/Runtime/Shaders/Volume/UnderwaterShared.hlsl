// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#ifndef CREST_UNDERWATER_EFFECT_SHARED_INCLUDED
#define CREST_UNDERWATER_EFFECT_SHARED_INCLUDED

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Settings.Crest.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Surface/Keywords.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Macros.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Constants.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/InputsDriven.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Globals.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Helpers.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Depth.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Helpers.hlsl"

#undef d_Crest_CausticsForceDistortion
#define d_Crest_CausticsForceDistortion 1

#if d_Crest_Portal
#include "Packages/com.waveharmonic.crest.portals/Runtime/Shaders/Library/Portals.hlsl"
#endif

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Cascade.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Texture.hlsl"

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Lighting.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Shadows.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Surface/VolumeLighting.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Surface/Caustics.hlsl"

// These are set via call to CopyPropertiesFromMaterial() and must have the same
// names as the surface material parameters.
CBUFFER_START(CrestPerMaterial)

//
// Surface Shared
//

#ifndef d_Crest_WaterSurface

half4 _Crest_Absorption;
half4 _Crest_Scattering;
half _Crest_Anisotropy;

half _Crest_DirectTerm;
half _Crest_AmbientTerm;
half _Crest_ShadowsAffectsAmbientFactor;

bool _Crest_CausticsEnabled;
float _Crest_CausticsTextureScale;
float _Crest_CausticsScrollSpeed;
float _Crest_CausticsTextureAverage;
float _Crest_CausticsStrength;
float _Crest_CausticsFocalDepth;
float _Crest_CausticsDepthOfField;
float _Crest_CausticsDistortionStrength;
float _Crest_CausticsDistortionScale;
half _Crest_CausticsMotionBlur;
float4 _Crest_CausticsTexture_TexelSize;
float4 _Crest_CausticsDistortionTexture_TexelSize;

#endif // !d_Crest_WaterSurface

//
// Volume Only
//

// Out-scattering. Driven by the Water Renderer and Underwater Environmental Lighting.
float _Crest_VolumeExtinctionLength;
float _Crest_UnderwaterEnvironmentalLightingWeight;

// Also applied to transparent objects.
half _Crest_ExtinctionMultiplier;
half _Crest_SunBoost;
float _Crest_OutScatteringFactor;
float _Crest_OutScatteringExtinctionFactor;
half3 _Crest_AmbientLighting;
uint _Crest_DataSliceOffset;

half _Crest_DitheringIntensity;
CBUFFER_END

TEXTURE2D_X(_Crest_WaterMaskDepthTexture);

#ifndef d_Crest_WaterSurface
TEXTURE2D_X(_Crest_WaterMaskTexture);

TEXTURE2D(_Crest_CausticsTexture);
SAMPLER(sampler_Crest_CausticsTexture);
TEXTURE2D(_Crest_CausticsDistortionTexture);
SAMPLER(sampler_Crest_CausticsDistortionTexture);

// NOTE: Cannot put this in namespace due to compiler bug. Fixed when using DXC.
static const m_Crest::TiledTexture _Crest_CausticsTiledTexture =
    m_Crest::TiledTexture::Make(_Crest_CausticsTexture, sampler_Crest_CausticsTexture, _Crest_CausticsTexture_TexelSize, _Crest_CausticsTextureScale, _Crest_CausticsScrollSpeed);
static const m_Crest::TiledTexture _Crest_CausticsDistortionTiledTexture =
    m_Crest::TiledTexture::Make(_Crest_CausticsDistortionTexture, sampler_Crest_CausticsDistortionTexture, _Crest_CausticsDistortionTexture_TexelSize, _Crest_CausticsDistortionScale, 1.0);
#endif // !d_Crest_WaterSurface

m_CrestNameSpace

#if d_Crest_OutScattering

// Get the out-scattering term.
half3 EvaluateOutScattering
(
    const half3 i_Extinction,
    const float3 i_PositionWS,
    const half3 i_ViewWS,
    const half i_Multiplier,
    const float i_RawDepth,
    const float i_WaterLevel
)
{
    float3 positionWS = i_PositionWS;

#if !CREST_REFLECTION
    // Project point onto sphere at the extinction length.
    const float3 toSphere = -i_ViewWS * _Crest_VolumeExtinctionLength * i_Multiplier * _Crest_OutScatteringExtinctionFactor;
    const float3 toScene = i_PositionWS - _WorldSpaceCameraPos.xyz;
    positionWS = _WorldSpaceCameraPos.xyz + toSphere;

    // Get closest position.
    positionWS = dot(toScene, toScene) < dot(toSphere, toSphere) ? i_PositionWS : positionWS;
#endif

    // Account for average extinction of light as it travels down through volume. Assume flat water as anything
    // else would be expensive.
    float waterDepth = max(0.0, (i_WaterLevel - positionWS.y));

#if CREST_REFLECTION
    waterDepth *= 2.0;
    if (i_RawDepth == 0.0) waterDepth = _Crest_VolumeExtinctionLength * i_Multiplier;
#else
    // Full strength seems too extreme. Third strength seems reasonable.
    waterDepth *= _Crest_OutScatteringFactor;
#endif

    const float3 outScatteringTerm = exp(-i_Extinction * waterDepth);

    // Transition between the Underwater Environmental Lighting (if present) and this. This will give us the
    // benefit of both approaches.
    return lerp(outScatteringTerm, 1.0, _Crest_UnderwaterEnvironmentalLightingWeight);
}
#endif // d_Crest_OutScattering

void GetWaterSurfaceAndUnderwaterData
(
    const float4 positionCS,
    const int2 positionSS,
    const float rawMaskDepth,
    const float mask,
    inout float rawDepth,
    inout bool isWaterSurface,
    inout bool isUnderwater,
    inout bool hasCaustics,
    inout bool io_OutScatterScene,
    inout bool io_ApplyLighting,
    inout float sceneZ
)
{
    const float rawSceneDepth = rawDepth;
    hasCaustics = rawDepth != 0.0;
    isWaterSurface = false;
    isUnderwater = mask <= CREST_MASK_BELOW_SURFACE;
    io_OutScatterScene = true;
    io_ApplyLighting = true;

#if defined(d_Crest_PortalWithBackFace) || defined(d_Crest_FogBefore)
    // Has back-face or is back-face.
    Portal::EvaluateVolume(positionCS, positionSS, rawMaskDepth, rawSceneDepth, rawDepth, hasCaustics, isUnderwater, io_OutScatterScene, io_ApplyLighting);
#endif

    // Merge water depth with scene depth.
    if (rawDepth < rawMaskDepth)
    {
        isWaterSurface = true;
        hasCaustics = false;
        rawDepth = rawMaskDepth;
    }

    sceneZ = Utility::CrestLinearEyeDepth(rawDepth);
}

void ApplyWaterVolumeToUnderwaterFog(float4 positionCS, inout float fogDistance)
{
    // TODO: could we use min here with near plane? less optimized
#if d_Crest_FogAfter
    fogDistance -= Utility::CrestLinearEyeDepth(positionCS.z);
#else
    // Subtract near plane.
    fogDistance -= _ProjectionParams.y;
#endif
}

half3 ApplyUnderwaterEffect
(
    half3 sceneColour,
    const float rawDepth,
    const float sceneZ,
    const float fogDistance,
    const half3 view,
    const uint2 i_positionSS,
    const float3 i_positionWS,
    const bool hasCaustics,
    const bool i_OutScatterScene,
    const bool i_ApplyLighting,
    const half i_multiplier,
    out half3 volumeOpacity,
    out half3 volumeLight
)
{
    const bool isUnderwater = true;

    float3 lightDirection; float3 lightColor;
    PrimaryLight(i_positionWS, lightColor, lightDirection);

    // Uniform effect calculated from camera position.
    volumeLight = 0.0;
    volumeOpacity = 1.0;
    {
        half3 absorption = _Crest_Absorption.xyz;
        half3 scattering = _Crest_Scattering.xyz;

        // We sample shadows at the camera position. Pass a user defined slice offset for smoothing out detail.
        // Offset slice so that we dont get high freq detail. But never use last lod as this has crossfading.
        uint sliceIndex = clamp(_Crest_DataSliceOffset, 0, g_Crest_LodCount - 2);

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

        absorption *= _Crest_ExtinctionMultiplier;
        scattering *= _Crest_ExtinctionMultiplier;

        half shadow = 1.0;
#if d_Crest_ShadowLod
        {
            // Camera should be at center of LOD system so no need for blending (alpha, weights, etc). This might not be
            // the case if there is large horizontal displacement, but the _Crest_DataSliceOffset should help by setting a
            // large enough slice as minimum.
            half2 shadowSoftHard = Cascade::MakeShadow(sliceIndex).SampleShadow(_WorldSpaceCameraPos.xz);
            // Soft in red, hard in green. But hard not computed in HDRP.
            shadow = 1.0 - shadowSoftHard.x;
        }
#endif

        half3 ambientLighting = AmbientLight(_Crest_AmbientLighting);

        const half3 extinction = VolumeExtinction(absorption, scattering);

#if d_Crest_OutScattering
        // Out-Scattering Term.
        {
            const float4 displacement = Cascade::MakeAnimatedWaves(sliceIndex).Sample(_WorldSpaceCameraPos.xz);
            const float waterLevel = g_Crest_WaterCenter.y + displacement.y + displacement.w;

            const half3 outScatteringTerm = EvaluateOutScattering
            (
                extinction,
                i_positionWS,
                view,
                i_multiplier,
                rawDepth,
                waterLevel
            );

            // Darken scene and light.
            sceneColour *= i_OutScatterScene ? outScatteringTerm : 1.0;
#if !CREST_REFLECTION
            lightColor *= outScatteringTerm;
            ambientLighting *= outScatteringTerm;
#endif
        }
#endif // d_Crest_OutScattering

        volumeOpacity = VolumeOpacity(extinction, fogDistance);
        volumeLight = VolumeLighting
        (
            extinction,
            scattering,
            _Crest_Anisotropy,
            shadow,
            view,
            ambientLighting,
            lightDirection,
            lightColor,
            half3(0.0, 0.0, 0.0),
            1.0, // Additional lights blend
            _Crest_AmbientTerm,
            _Crest_DirectTerm,
            1.0, // Additional lights term
            _Crest_SunBoost,
            _Crest_ShadowsAffectsAmbientFactor
        );
    }

#ifndef k_DisableCaustics
    if (_Crest_CausticsEnabled && hasCaustics)
    {
        half lightOcclusion = PrimaryLightShadows(i_positionWS, i_positionSS);
        half blur = 0.0;

        const uint slice0 = PositionToSliceIndex(i_positionWS.xz, 0, g_Crest_WaterScale);

#if d_Crest_FlowLod
        half2 flowData = Cascade::MakeFlow(slice0).SampleFlow(i_positionWS.xz);
        const Flow flow = Flow::Make(flowData, g_Crest_Flow);
        blur = _Crest_CausticsMotionBlur;
#endif

        const float4 displacement = Cascade::MakeAnimatedWaves(slice0).Sample(i_positionWS.xz);
        const float surfaceHeight = displacement.y + g_Crest_WaterCenter.y + displacement.w;

        sceneColour *= Caustics
        (
#if d_Crest_FlowLod
            flow,
#endif
            i_positionWS,
            surfaceHeight,
            lightColor,
            lightDirection,
            lightOcclusion,
            sceneZ,
            _Crest_CausticsTiledTexture,
            _Crest_CausticsTextureAverage,
            _Crest_CausticsStrength,
            _Crest_CausticsFocalDepth,
            _Crest_CausticsDepthOfField,
            _Crest_CausticsDistortionTiledTexture,
            _Crest_CausticsDistortionStrength,
            blur,
            isUnderwater
        );
    }
#endif

#if CREST_HDRP
    volumeLight *= GetCurrentExposureMultiplier();
#endif

#ifndef k_DisableDithering
#if d_Dithering
    // Increasing intensity can be required for HDRP.
    volumeLight += Utility::ScreenSpaceDither(i_positionSS) * _Crest_DitheringIntensity;
#endif
#endif

    if (i_ApplyLighting)
    {
        sceneColour = lerp(sceneColour, volumeLight, volumeOpacity);
    }

    return sceneColour;
}

m_CrestNameSpaceEnd

#endif // CREST_UNDERWATER_EFFECT_SHARED_INCLUDED
