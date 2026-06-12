// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

// Guard against missing uniforms.
#ifdef SHADERPASS

#define m_Properties(iomod) \
    const float2 i_UndisplacedXZ, \
    float i_LodAlpha, \
    const half i_WaterLevelOffset, \
    const half2 i_Flow, \
    const half3 i_ViewDirectionWS, \
    const bool i_Facing, \
    const half3 i_SceneColor, \
    const float i_SceneDepthRaw, \
    const float4 i_ScreenPosition, \
    const float4 i_ScreenPositionRaw, \
    const float3 i_PositionWS, \
    const float3 i_PositionVS, \
    const half3 i_NormalWS, \
    const float2 i_StaticLightMapUV, \
    iomod half3 o_Albedo, \
    iomod half3 o_NormalWS, \
    iomod half3 o_Specular, \
    iomod half3 o_Emission, \
    iomod half o_Smoothness, \
    iomod half o_Occlusion, \
    iomod half o_Alpha

#define m_Parameters \
    i_UndisplacedXZ, \
    i_LodAlpha, \
    i_WaterLevelOffset, \
    i_Flow, \
    i_ViewDirectionWS, \
    i_Facing, \
    i_SceneColor, \
    i_SceneDepthRaw, \
    i_ScreenPosition, \
    i_ScreenPositionRaw, \
    i_PositionWS, \
    i_PositionVS, \
    i_NormalWS, \
    i_StaticLightMapUV, \
    o_Albedo, \
    o_NormalWS, \
    o_Specular, \
    o_Emission, \
    o_Smoothness, \
    o_Occlusion, \
    o_Alpha

// Guard against Shader Graph preview.
#ifndef SHADERGRAPH_PREVIEW

#define d_Crest_WaterSurface 1

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Surface/Shim.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Surface/Keywords.hlsl"

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Macros.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Globals.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/InputsDriven.hlsl"

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Cascade.hlsl"

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Depth.hlsl"

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Texture.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Flow.hlsl"

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Lighting.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Shadows.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Surface/Facing.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Surface/Normal.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Surface/Reflection.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Surface/Refraction.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Surface/Caustics.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Surface/VolumeLighting.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Surface/Emission.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Surface/Fresnel.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Surface/Foam.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Surface/Alpha.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Surface/Fog.hlsl"

#if (CREST_PORTALS != 0)
#include "Packages/com.waveharmonic.crest.portals/Runtime/Shaders/Library/Portals.hlsl"
#endif

bool _Crest_DrawBoundaryXZ;
float4 _Crest_BoundaryXZ;

m_CrestNameSpace

static const TiledTexture _Crest_NormalMapTiledTexture =
    TiledTexture::Make(_Crest_NormalMapTexture, sampler_Crest_NormalMapTexture, _Crest_NormalMapTexture_TexelSize, _Crest_NormalMapScale, _Crest_NormalMapScrollSpeed);

static const TiledTexture _Crest_FoamTiledTexture =
    TiledTexture::Make(_Crest_FoamTexture, sampler_Crest_FoamTexture, _Crest_FoamTexture_TexelSize, _Crest_FoamScale, _Crest_FoamScrollSpeed);

static const TiledTexture _Crest_CausticsTiledTexture =
    TiledTexture::Make(_Crest_CausticsTexture, sampler_Crest_CausticsTexture, _Crest_CausticsTexture_TexelSize, _Crest_CausticsTextureScale, _Crest_CausticsScrollSpeed);
static const TiledTexture _Crest_CausticsDistortionTiledTexture =
    TiledTexture::Make(_Crest_CausticsDistortionTexture, sampler_Crest_CausticsDistortionTexture, _Crest_CausticsDistortionTexture_TexelSize, _Crest_CausticsDistortionScale, 1.0);

void Fragment
(
    m_Properties(inout),
    const uint i_LodIndex0,
    const uint i_LodIndex1,
    const float2 i_PositionSS,
    const bool i_Underwater,
    const float i_SceneZRaw,
    const float i_NegativeFog
)
{
    float2 scenePositionSS = i_PositionSS;

    // Editor only. There is no defined editor symbol.
    if (_Crest_DrawBoundaryXZ)
    {
        const float2 p = abs(i_PositionWS.xz - _Crest_BoundaryXZ.xy);
        const float2 s = _Crest_BoundaryXZ.zw * 0.5;
        if ((p.x > s.x && p.x < s.x + 1.0 && p.y < s.y + 1.0) || (p.y > s.y && p.y < s.y + 1.0 && p.x < s.x + 1.0))
        {
            o_Emission = half3(1.0, 0.0, 1.0);
#if CREST_HDRP
            o_Emission /= GetCurrentExposureMultiplier();
#endif
        }
    }

    const uint slice0 = i_LodIndex0;
    const uint slice1 = i_LodIndex1;

    const Cascade cascade0 = Cascade::Make(slice0);
    const Cascade cascade1 = Cascade::Make(slice1);

    const float sceneZ = Utility::CrestLinearEyeDepth(i_SceneZRaw);
    const float pixelZ = -i_PositionVS.z;

    const bool isLastLod = slice0 == (g_Crest_LodCount - 1);
    const float weight0 = (1.0 - i_LodAlpha) * cascade0._Weight;
    const float weight1 = (1.0 - weight0) * cascade1._Weight;

    // Data that fades towards the edge.
    half foam = 0.0; half _determinant = 0.0; half4 albedo = 0.0; half2 shadow = 0.0; half waterDepth = i_WaterLevelOffset; half shorelineDistance = 0.0; half2 flowData = 0.0;
    if (weight0 > m_CrestSampleLodThreshold)
    {
        if (_Crest_ApplyDisplacementNormals)
        {
            Cascade::MakeAnimatedWaves(slice0).SampleDisplacementNormal(i_UndisplacedXZ, weight0, o_NormalWS.xz, _determinant);
        }

#if d_Crest_CustomMesh && d_Crest_FlowLod
        Cascade::MakeFlow(slice0).SampleFlow(i_UndisplacedXZ, weight0, flowData);
#endif

        if (_Crest_FoamEnabled)
        {
            Cascade::MakeFoam(slice0).SampleFoam(i_UndisplacedXZ, weight0, foam);
        }

#if d_Crest_AlbedoLod
        if (_Crest_AlbedoEnabled)
        {
            Cascade::MakeAlbedo(slice0).SampleAlbedo(i_UndisplacedXZ, weight0, albedo);
        }
#endif

#if d_Crest_ShadowLod
        if (_Crest_ShadowsEnabled)
        {
            Cascade::MakeShadow(slice0).SampleShadow(i_PositionWS.xz, weight0, shadow);
        }
#endif

#if d_Crest_SimpleTransparency || d_Crest_FoamBioluminescence
#if !d_Crest_SimpleTransparency
        if (_Crest_FoamEnabled && _Crest_FoamBioluminescenceEnabled)
#endif
        {
            Cascade::MakeDepth(slice0).SampleSignedDepthFromSeaLevelAndDistance(i_PositionWS.xz, weight0, waterDepth, shorelineDistance);
        }
#endif
    }

    if (weight1 > m_CrestSampleLodThreshold)
    {
        if (_Crest_ApplyDisplacementNormals)
        {
            Cascade::MakeAnimatedWaves(slice1).SampleDisplacementNormal(i_UndisplacedXZ, weight1, o_NormalWS.xz, _determinant);
        }

#if d_Crest_CustomMesh && d_Crest_FlowLod
        Cascade::MakeFlow(slice1).SampleFlow(i_UndisplacedXZ, weight1, flowData);
#endif

        if (_Crest_FoamEnabled)
        {
            Cascade::MakeFoam(slice1).SampleFoam(i_UndisplacedXZ, weight1, foam);
        }

#if d_Crest_AlbedoLod
        if (_Crest_AlbedoEnabled)
        {
            Cascade::MakeAlbedo(slice1).SampleAlbedo(i_UndisplacedXZ, weight1, albedo);
        }
#endif

#if d_Crest_ShadowLod
        if (_Crest_ShadowsEnabled)
        {
            Cascade::MakeShadow(slice1).SampleShadow(i_PositionWS.xz, weight1, shadow);
        }
#endif

#if d_Crest_SimpleTransparency || d_Crest_FoamBioluminescence
#if !d_Crest_SimpleTransparency
        if (_Crest_FoamEnabled && _Crest_FoamBioluminescenceEnabled)
#endif
        {
            Cascade::MakeDepth(slice1).SampleSignedDepthFromSeaLevelAndDistance(i_PositionWS.xz, weight1, waterDepth, shorelineDistance);
        }
#endif
    }

    // Invert so shadows are black as we normally multiply this by lighting.
    shadow = 1.0 - shadow;

    // Data that displays to the edge.
    // The default simulation value has been written to the border of the last slice.
    half3 absorption = _Crest_Absorption.xyz; half3 scattering = _Crest_Scattering.xyz;

#if d_Crest_AbsorptionLod || d_Crest_ScatteringLod
    {
        const float weight0 = (1.0 - (isLastLod ? 0.0 : i_LodAlpha)) * cascade0._Weight;
        const float weight1 = (1.0 - weight0) * cascade1._Weight;

#if d_Crest_ScatteringLod
        if (g_Crest_SampleScatteringSimulation)
        {
            scattering = 0.0;
        }
#endif

#if d_Crest_AbsorptionLod
        if (g_Crest_SampleAbsorptionSimulation)
        {
            absorption = 0.0;
        }
#endif

        if (weight0 > m_CrestSampleLodThreshold)
        {
#if d_Crest_ScatteringLod
            if (g_Crest_SampleScatteringSimulation)
            {
                Cascade::MakeScattering(slice0).SampleScattering(i_UndisplacedXZ, weight0, scattering);
            }
#endif

#if d_Crest_AbsorptionLod
            if (g_Crest_SampleAbsorptionSimulation)
            {
                Cascade::MakeAbsorption(slice0).SampleAbsorption(i_UndisplacedXZ, weight0, absorption);
            }
#endif
        }

        if (weight1 > m_CrestSampleLodThreshold)
        {
#if d_Crest_ScatteringLod
            if (g_Crest_SampleScatteringSimulation)
            {
                Cascade::MakeScattering(slice1).SampleScattering(i_UndisplacedXZ, weight1, scattering);
            }
#endif

#if d_Crest_AbsorptionLod
            if (g_Crest_SampleAbsorptionSimulation)
            {
                Cascade::MakeAbsorption(slice1).SampleAbsorption(i_UndisplacedXZ, weight1, absorption);
            }
#endif
        }
    }
#endif

#if d_Crest_FlowLod
#if !d_Crest_CustomMesh
    flowData = i_Flow;
#endif
    const Flow flow = Flow::Make(flowData, g_Crest_Flow);
#endif

    // Determinant needs to be one when no waves.
    if (isLastLod)
    {
        _determinant += 1.0 - weight0;
        waterDepth = 10000.0;
    }

#if d_Transparent
        // Feather at intersection. Cannot be used for shadows since depth is not available.
        const float feather =
#if d_Crest_SimpleTransparency
            saturate(waterDepth / 0.2);
#else
            saturate((sceneZ - pixelZ) / 0.2);
#endif
#endif

    const half3 extinction = VolumeExtinction(absorption, scattering);

    float3 lightIntensity = 0.0;
    half3 lightDirection = 0.0;

    PrimaryLight
    (
        i_PositionWS,
        lightIntensity,
        lightDirection
    );

    // Normal.
    {
#if d_Crest_NormalMap
        if (_Crest_NormalMapEnabled)
        {
            const half2 normalMap = SampleNormalMaps
            (
#if d_Crest_FlowLod
                flow,
#endif
                _Crest_NormalMapTiledTexture,
                _Crest_NormalMapStrength,
                i_UndisplacedXZ,
                i_LodAlpha,
                cascade0
            );

            half normalMapStrength = _Crest_NormalMapStrength;

            if (_Crest_NormalMapTurbulenceEnabled)
            {
                normalMapStrength = NormalMapTurbulence
                (
                    o_NormalWS,
                    normalMap,
                    normalMapStrength,
                    _Crest_NormalMapTurbulenceCoverage,
                    _Crest_NormalMapTurbulenceStrength,
                    i_ViewDirectionWS,
                    _determinant,
                    g_Crest_WaterCenter.y + i_WaterLevelOffset,
                    pixelZ,
                    lightDirection
                );
            }

            o_NormalWS.xz += normalMap * normalMapStrength;
        }
#endif // d_Crest_NormalMap

        // Handles normalization.
        WaterNormal
        (
            _Crest_NormalsStrengthOverall,
            i_Underwater,
            o_NormalWS
        );
    }

    const half3 ambientLight = AmbientLight();
    half3 additionalLight = 0.0;
#if d_Crest_AdditionalLights
    additionalLight = AdditionalLighting(i_PositionWS, i_ScreenPositionRaw, i_StaticLightMapUV, i_PositionSS, o_NormalWS);
#endif

#if CREST_BIRP
#ifndef USING_DIRECTIONAL_LIGHT
    // BIRP additional lights are the main light.
    lightIntensity *= max(k_Crest_AdditionalLightLerp, dot(o_NormalWS, lightDirection));
#endif
#endif

    // Default for opaque render type.
    float sceneDistance = 1000.0;
    float3 scenePositionWS = i_PositionWS;

#if d_Crest_SimpleTransparency
    sceneDistance = waterDepth;
    // Increase ray for grazing angles.
    sceneDistance += (1.0 - dot(i_ViewDirectionWS, o_NormalWS)) * waterDepth;
    scenePositionWS.y = i_PositionWS.y - waterDepth;
    // Cannot sample scene so go with average light.
    o_Emission = i_Underwater ? 0.0 : (0.5 * (lightIntensity + additionalLight + ambientLight) * INV_PI);
#if CREST_HDRP
    o_Emission /= GetCurrentExposureMultiplier();
#endif
#endif

    bool caustics = !i_Underwater;

#if !d_Crest_SimpleTransparency
#if d_Transparent
#ifndef d_SkipRefraction
    RefractedScene
    (
        _Crest_RefractionStrength,
        1.000293, // air
        _Crest_RefractiveIndexOfWater,
        o_NormalWS,
        i_PositionWS,
        i_ScreenPosition.xy,
        i_ScreenPositionRaw,
        pixelZ,
        i_ViewDirectionWS,
        sceneZ,
        i_SceneZRaw,
        cascade0._Scale,
        i_LodAlpha,
        i_Underwater,
        _Crest_TotalInternalReflectionIntensity,
        o_Emission,
        sceneDistance,
        scenePositionWS,
        scenePositionSS,
        caustics
    );

#if CREST_BIRP
#if SHADERPASS == SHADERPASS_FORWARD_BASE
    if (g_Crest_PrimaryLightHasCookie)
    {
        // If light has a cookie, it is zero for the ForwardBase pass. We need to split
        // emission over ForwardBase and ForwardAdd. Ambient is done in the former, while
        // direct (eg caustics) is done in ForwardAdd.
        o_Emission = 0;
    }
#endif
#endif
#endif // d_SkipRefraction
#endif // d_Transparent
#endif // d_Crest_SimpleTransparency

#if d_Crest_OutScattering
    // Out-scattering.
    if (!i_Underwater)
    {
        // Account for extinction of light as it travels down through volume.
        o_Emission *= exp(-extinction * max(0.0, i_PositionWS.y - scenePositionWS.y));
    }
#endif

#if d_Transparent
#ifndef d_SkipRefraction
    // Caustics
    if (_Crest_CausticsEnabled && !i_Underwater && caustics)
    {
        half lightOcclusion = 1.0;
#if !d_Crest_SimpleTransparency
        lightOcclusion = PrimaryLightShadows(scenePositionWS, scenePositionSS);
#endif

#if d_Crest_SimpleTransparency
        if (_Crest_RefractionStrength > 0.0)
        {
            // Gives a parallax like effect.
            const half3 ray = refract(-i_ViewDirectionWS, o_NormalWS, 1.0 / _Crest_RefractiveIndexOfWater) * _Crest_RefractionStrength;
            scenePositionWS += ray * waterDepth * 2.0;
        }
#endif

        half blur = 0.0;
#if d_Crest_FlowLod
        blur = _Crest_CausticsMotionBlur;
#endif

        o_Emission *= Caustics
        (
#if d_Crest_FlowLod
            flow,
#endif
            scenePositionWS,
            i_PositionWS.y,
            lightIntensity,
            lightDirection,
            lightOcclusion,
            sceneDistance,
            _Crest_CausticsTiledTexture,
            _Crest_CausticsTextureAverage,
            _Crest_CausticsStrength,
            _Crest_CausticsFocalDepth,
            _Crest_CausticsDepthOfField,
            _Crest_CausticsDistortionTiledTexture,
            _Crest_CausticsDistortionStrength,
            blur,
            _Crest_CausticsForceDistortion
        );
    }
#endif // d_SkipRefraction
#endif // d_Transparent

    half3 sss = 0.0;

    if (_Crest_SSSEnabled)
    {
        sss = PinchSSS
        (
            _determinant,
            _Crest_SSSPinchMinimum,
            _Crest_SSSPinchMaximum,
            _Crest_SSSPinchFalloff,
            _Crest_SSSIntensity,
            lightDirection,
            _Crest_SSSDirectionalFalloff,
            i_ViewDirectionWS
        );
    }

    // Volume Lighting
    const half3 volumeOpacity = VolumeOpacity(extinction, sceneDistance);
    const half3 volumeLight = VolumeLighting
    (
        extinction,
        scattering,
        _Crest_Anisotropy,
        shadow.x,
        i_ViewDirectionWS,
        ambientLight,
        lightDirection,
        lightIntensity,
        additionalLight,
        _Crest_AdditionalLightsBlend,
        _Crest_AmbientTerm,
        _Crest_DirectTerm,
        _Crest_DirectTermAdditional,
        sss,
        _Crest_ShadowsAffectsAmbientFactor
    );

    // Fresnel
    float reflected = 0.0;
    float transmitted = 0.0;
    {
        ApplyFresnel
        (
            i_ViewDirectionWS,
            o_NormalWS,
            i_Underwater,
            1.0, // air
            _Crest_RefractiveIndexOfWater,
            _Crest_TotalInternalReflectionIntensity,
            _Crest_Fresnel,
            transmitted,
            reflected
        );

        if (i_Underwater)
        {
            o_Emission *= transmitted;
            o_Emission += volumeLight * reflected;
        }
        else
        {
            o_Emission *= 1.0 - volumeOpacity;
            o_Emission += volumeLight * volumeOpacity;

            if (_Crest_ApplyFresnelToVolumeLighting)
            {
                o_Emission *= transmitted;
            }
        }
    }

#if _SPECULAR_SETUP
    // Specular
    {
        o_Specular = _Crest_Specular * reflected * shadow.y;
    }
#endif

    // Smoothness
    {
        // Vary smoothness by distance.
        o_Smoothness = lerp(_Crest_Smoothness, _Crest_SmoothnessFar, pow(saturate(pixelZ / _Crest_SmoothnessFarDistance), _Crest_SmoothnessFalloff));
    }

    // Occlusion
    {
        o_Occlusion = i_Underwater ? _Crest_OcclusionUnderwater : _Crest_Occlusion;
    }

    // Planar Reflections
#if d_Crest_PlanarReflections
    if (_Crest_PlanarReflectionsEnabled)
    {
        half4 reflection = PlanarReflection
        (
            _Crest_ReflectionColorTexture,
            sampler_Crest_ReflectionColorTexture,
            _Crest_PlanarReflectionsIntensity,
            o_Smoothness,
            _Crest_PlanarReflectionsRoughness,
            pixelZ,
            o_NormalWS,
            _Crest_PlanarReflectionsDistortion,
            i_ViewDirectionWS,
            i_ScreenPosition.xy,
            i_Underwater
        );

        half alpha = reflection.a;
        o_Emission = lerp(o_Emission, reflection.rgb, alpha * reflected * o_Occlusion);
        // Override reflections with planar reflections.
        // Results are darker than Unity's.
        o_Occlusion *= 1.0 - alpha;
    }
#endif // d_Crest_PlanarReflections

    // Foam
    if (_Crest_FoamEnabled)
    {
        half2 albedo = MultiScaleFoamAlbedo
        (
#if d_Crest_FlowLod
            flow,
#endif
            _Crest_FoamTiledTexture,
            _Crest_FoamFeather,
            foam,
            cascade0,
            cascade1,
            i_LodAlpha,
            i_UndisplacedXZ,
            _Crest_FoamBioluminescenceEnabled && _Crest_FoamBioluminescenceSparklesEnabled
        );

        half2 normal = MultiScaleFoamNormal
        (
#if d_Crest_FlowLod
            flow,
#endif
            _Crest_FoamTiledTexture,
            _Crest_FoamFeather,
            foam,
            cascade0,
            cascade1,
            i_LodAlpha,
            i_UndisplacedXZ,
            _Crest_FoamNormalStrength,
            albedo.x,
            pixelZ
        );

        half3 intensity = _Crest_FoamIntensityAlbedo;

        ApplyFoamToSurface
        (
            albedo.x,
            normal,
            intensity,
            _Crest_Occlusion,
            _Crest_FoamSmoothness,
            _Crest_Specular,
            i_Underwater,
            o_Albedo,
            o_NormalWS,
            o_Emission,
            o_Occlusion,
            o_Smoothness,
            o_Specular
        );

        // We will use this for shadow casting.
        const half foamData = foam;
        foam = albedo.r;

#if d_Crest_FoamBioluminescence
        if (_Crest_FoamBioluminescenceEnabled)
        {
            half3 emission = FoamBioluminescence
            (
                foamData,
                albedo.r,
                _Crest_FoamBioluminescenceColor.rgb,
                _Crest_FoamBioluminescenceIntensity,
                _Crest_FoamBioluminescenceGlowCoverage,
                _Crest_FoamBioluminescenceGlowIntensity,
                _Crest_FoamBioluminescenceSparklesEnabled,
                albedo.y,
                _Crest_FoamBioluminescenceSparklesCoverage,
                _Crest_FoamBioluminescenceSparklesIntensity,
                _Crest_FoamBioluminescenceMaximumDepth,
                waterDepth
            );

            emission *= _Crest_FoamBioluminescenceSeaLevelOnly ? saturate(1.0 - abs(i_WaterLevelOffset)) : 1.0;

#if d_Transparent
            // Apply feathering to avoid hardening the edge.
            emission *= feather * feather * feather;
#endif

            o_Emission += emission;
        }
#endif // d_Crest_FoamBioluminescence
    }

    // Albedo
#if d_Crest_AlbedoLod
    if (_Crest_AlbedoEnabled)
    {
        const float foamMask = _Crest_AlbedoIgnoreFoam ? (1.0 - saturate(foam)) : 1.0;
        o_Albedo = lerp(o_Albedo, albedo.rgb, albedo.a * foamMask);
        o_Emission *= 1.0 - albedo.a * foamMask;
    }
#endif

#if d_Crest_SimpleTransparency
    o_Alpha = i_Underwater
        ? 1.0 - transmitted
        : max(max(length(volumeOpacity), _Crest_TransparencyMinimumAlpha), max(foam, albedo.a));
#endif

    // Alpha
    {
#ifndef CREST_SHADOWPASS
#if d_Transparent
        // Feather at intersection. Cannot be used for shadows since depth is not available.
        o_Alpha = min(o_Alpha, feather);
#endif
#endif

        // This keyword works for all RPs despite BIRP having prefixes in serialised data.
#if d_Crest_AlphaTest
#if CREST_SHADOWPASS
        o_Alpha = min(o_Alpha, max(foam, albedo.a) - _Crest_ShadowCasterThreshold);
#endif
#endif // d_Crest_AlphaTest

        // Specular in HDRP is still affected outside the 0-1 alpha range.
        o_Alpha = min(o_Alpha, 1.0);
    }

    if (!i_Underwater && _Crest_MinimumReflectionDirectionY > -1.0)
    {
        o_NormalWS = ApplyMinimumReflectionDirectionY(_Crest_MinimumReflectionDirectionY, i_ViewDirectionWS, o_NormalWS);
    }

    SetUpFog
    (
        i_Underwater,
        i_PositionWS,
        1.0, // N/A: multiplier for fog nodes
        pixelZ - i_NegativeFog,
        i_ViewDirectionWS,
        i_PositionSS
    );
}

#if d_Crest_CustomMesh
// Handles the last slice logic differently to match the mesh.
// Clip surface for quad will not match chunks mesh at last LOD as collateral.
#define PositionToSliceIndices MeshPositionToSliceIndices
#endif

// IMPORTANT!
// Do not branch on o_Alpha, as it is not robust. Rendering will break on stereo
// rendering in the form of losing additional lighting and/or broken reflection
// probe sampling. This is an issue with the shader compiler it seems.
void Fragment(m_Properties(inout))
{
    const float2 positionSS = i_ScreenPosition.xy * _ScreenSize.xy;

    bool underwater = IsUnderWater(i_Facing, g_Crest_ForceUnderwater, positionSS);
    float sceneRawZ = i_SceneDepthRaw;
    float negativeFog = _ProjectionParams.y;

#if d_Crest_AlphaTest
#if !d_Crest_SimpleTransparency
#ifndef CREST_SHADOWPASS
#if (CREST_PORTALS != 0)
    if (m_CrestPortal)
    {
        const float pixelRawZ = i_ScreenPositionRaw.z / i_ScreenPositionRaw.w;
        o_Alpha = Portal::EvaluateSurface(i_ScreenPosition.xy, pixelRawZ, i_PositionWS, underwater, sceneRawZ, negativeFog) ? -1.0 : 1.0;
#ifndef SHADER_API_WEBGPU
        clip(o_Alpha);
#endif
    }
#endif
#endif
#endif
#endif

    uint slice0;
    uint slice1;
    float alpha;

#if d_Crest_AlphaTest || d_Crest_CustomMesh
    PositionToSliceIndices(i_PositionWS.xz, 0, g_Crest_LodCount - 1, g_Crest_WaterScale, slice0, slice1, alpha);
#endif

#if d_Crest_AlphaTest
    {
        const Cascade cascade0 = Cascade::Make(slice0);
        const Cascade cascade1 = Cascade::Make(slice1);
        const float weight0 = (1.0 - alpha) * cascade0._Weight;
        const float weight1 = (1.0 - weight0) * cascade1._Weight;

        float clipSurface = 0.0;
        if (weight0 > m_CrestSampleLodThreshold)
        {
            Cascade::MakeClip(slice0).SampleClip(i_PositionWS.xz, weight0, clipSurface);
        }
        if (weight1 > m_CrestSampleLodThreshold)
        {
            Cascade::MakeClip(slice1).SampleClip(i_PositionWS.xz, weight1, clipSurface);
        }

        // Add 0.5 bias for LOD blending and texel resolution correction. This will help to
        // tighten and smooth clipped edges.
        o_Alpha -= clipSurface > 0.5 ? 2.0 : 0.0;

#ifndef SHADER_API_WEBGPU
        clip(o_Alpha);
#endif
    }
#endif

    {
#if !d_Crest_CustomMesh
        slice0 = _Crest_LodIndex;
        slice1 = _Crest_LodIndex + 1;
        alpha = i_LodAlpha;
#endif

        i_LodAlpha = alpha;

        Fragment
        (
            m_Parameters,
            slice0,
            slice1,
            positionSS,
            underwater,
            sceneRawZ,
            negativeFog
        );
    }
}

#ifdef PosToSliceIndices
#undef PosToSliceIndices
#endif

m_CrestNameSpaceEnd

#endif // SHADERGRAPH_PREVIEW

void Fragment_float(m_Properties(out))
{
    o_Albedo = 0.0;
    o_NormalWS = i_NormalWS;
    o_Specular = 0.0;
    o_Emission = 0.0;
    o_Smoothness = 0.9;
    o_Occlusion = 1.0;
    o_Alpha = 1.0;

#if !SHADERGRAPH_PREVIEW
    m_Crest::Fragment(m_Parameters);
#endif
}

#undef m_Properties

#endif // SHADERPASS
