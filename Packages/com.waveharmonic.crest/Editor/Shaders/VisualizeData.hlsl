// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#ifndef d_WaveHarmonic_Crest_Editor_VisualizeData
#define d_WaveHarmonic_Crest_Editor_VisualizeData

#define d_RequirePositionWS 1
#define d_RequireUndisplacedXZ 1
#define d_RequireLodAlpha 1

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Settings/Visualize.Crest.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Macros.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Vertex/Surface.hlsl"

#define d_VisualizeAlbedo _Crest_DataType == VISUALIZEDATATYPES_ALBEDO
#define d_VisualizeDepth _Crest_DataType == VISUALIZEDATATYPES_DEPTH
#define d_VisualizeDisplacement _Crest_DataType == VISUALIZEDATATYPES_DISPLACEMENT
#define d_VisualizeFlow _Crest_DataType == VISUALIZEDATATYPES_FLOW
#define d_VisualizeFoam _Crest_DataType == VISUALIZEDATATYPES_FOAM
#define d_VisualizeLevel _Crest_DataType == VISUALIZEDATATYPES_LEVEL
#define d_VisualizeShadow _Crest_DataType == VISUALIZEDATATYPES_SHADOW
#define d_VisualizeShorelineDistance _Crest_DataType == VISUALIZEDATATYPES_SHORELINE_DISTANCE
#define d_VisualizeAbsorption _Crest_DataType == VISUALIZEDATATYPES_ABSORPTION
#define d_VisualizeScattering _Crest_DataType == VISUALIZEDATATYPES_SCATTERING
#define d_VisualizeDynamicWaves _Crest_DataType == VISUALIZEDATATYPES_DYNAMIC_WAVES
#define d_VisualizeClip _Crest_DataType == VISUALIZEDATATYPES_CLIP
#define d_VisualizeCascades _Crest_DataType == VISUALIZEDATATYPES_CASCADES

uint  _Crest_DataType;
bool  _Crest_Saturate;
float _Crest_Exposure;
float _Crest_Range;

m_CrestNameSpace

half4 Fragment(const Varyings i_Input)
{
    const uint slice0 = _Crest_LodIndex;
    const uint slice1 = slice0 + 1;

    const Cascade cascade0 = Cascade::Make(slice0);
    const Cascade cascade1 = Cascade::Make(slice1);

    const bool isLastLod = slice0 == (g_Crest_LodCount - 1);
    const float weight0 = (1.0 - i_Input._LodAlpha) * cascade0._Weight;
    const float weight1 = (1.0 - weight0) * cascade1._Weight;

    const float3 position = i_Input._PositionWS;
    const float2 undisplaced = i_Input._UndispacedPositionXZ;

    half3 displacement = 0.0;
    half2 ripples = 0.0;
    half level = 0.0;
    half depth = 0.0;
    half distance = 0.0;
    half4 albedo = 0.0;
    half clip = 0.0;
    half2 flow = 0.0;
    half foam = 0.0;
    half2 shadow = 0.0;
    half3 absorption = 0.0;
    half3 scattering = 0.0;

    if (weight0 > m_CrestSampleLodThreshold)
    {
        Cascade::MakeAnimatedWaves(slice0).SampleDisplacement(undisplaced, weight0, displacement);
        Cascade::MakeLevel(slice0).SampleLevel(undisplaced, weight0, level);
        Cascade::MakeDynamicWaves(slice0).SampleDynamicWaves(undisplaced, weight0, ripples);

        Cascade::MakeAlbedo(slice0).SampleAlbedo(undisplaced, weight0, albedo);
        Cascade::MakeDepth(slice0).SampleSignedDepthFromSeaLevelAndDistance(position.xz, weight0, depth, distance);
        Cascade::MakeClip(slice0).SampleClip(position.xz, weight0, clip);
        Cascade::MakeFlow(slice0).SampleFlow(undisplaced, weight0, flow);
        Cascade::MakeFoam(slice0).SampleFoam(undisplaced, weight0, foam);
        Cascade::MakeShadow(slice0).SampleShadow(position.xz, weight0, shadow);

        Cascade::MakeAbsorption(slice0).SampleAbsorption(undisplaced, weight0, absorption);
        Cascade::MakeScattering(slice0).SampleScattering(undisplaced, weight0, scattering);
    }

    if (weight1 > m_CrestSampleLodThreshold)
    {
        Cascade::MakeAnimatedWaves(slice1).SampleDisplacement(undisplaced, weight1, displacement);
        Cascade::MakeLevel(slice1).SampleLevel(undisplaced, weight1, level);
        Cascade::MakeDynamicWaves(slice1).SampleDynamicWaves(undisplaced, weight1, ripples);

        Cascade::MakeAlbedo(slice1).SampleAlbedo(undisplaced, weight1, albedo);
        Cascade::MakeDepth(slice1).SampleSignedDepthFromSeaLevelAndDistance(position.xz, weight1, depth, distance);
        Cascade::MakeClip(slice1).SampleClip(position.xz, weight1, clip);
        Cascade::MakeFlow(slice1).SampleFlow(undisplaced, weight1, flow);
        Cascade::MakeFoam(slice1).SampleFoam(undisplaced, weight1, foam);
        Cascade::MakeShadow(slice1).SampleShadow(position.xz, weight1, shadow);

        Cascade::MakeAbsorption(slice1).SampleAbsorption(undisplaced, weight1, absorption);
        Cascade::MakeScattering(slice1).SampleScattering(undisplaced, weight1, scattering);
    }

    if (isLastLod)
    {
        depth = m_FloatMaximum;
        distance = m_FloatMaximum;
    }


    half3 result = 0.0;

    if (d_VisualizeDisplacement)
    {
        result = (displacement + 1.0) * 0.5;
    }

    if (d_VisualizeDynamicWaves)
    {
        result.xy = (ripples + 1.0) * 0.5;
    }

    if (d_VisualizeLevel)
    {
        result = level;
    }


    if (d_VisualizeDepth)
    {
        result.x = depth / _Crest_Range;
    }

    if (d_VisualizeShorelineDistance)
    {
        result.x = distance / _Crest_Range;
    }


    if (d_VisualizeAbsorption)
    {
        result = absorption;
    }


    if (d_VisualizeScattering)
    {
        result = scattering;
    }

    if (d_VisualizeAlbedo)
    {
        result = albedo.rgb * albedo.a;
    }

    if (d_VisualizeClip)
    {
        result.x = clip;
    }

    if (d_VisualizeFlow)
    {
        result.xy = flow;
    }

    if (d_VisualizeFoam)
    {
        result.x = foam;
    }

    if (d_VisualizeShadow)
    {
        result.xy = shadow;
    }


    if (d_VisualizeCascades)
    {
        half3 tint[7];
        tint[0] = half3(1.0, 0.0, 0.0);
        tint[1] = half3(1.0, 1.0, 0.0);
        tint[2] = half3(1.0, 0.0, 1.0);
        tint[3] = half3(0.0, 1.0, 1.0);
        tint[4] = half3(0.0, 0.0, 1.0);
        tint[5] = half3(1.0, 0.0, 1.0);
        tint[6] = half3(0.5, 0.5, 1.0);
        result = weight0 * tint[slice0 % 7] + weight1 * tint[slice1 % 7];
    }
    else
    {
        result *= exp2(_Crest_Exposure);

        if (_Crest_Saturate)
        {
            result = saturate(result);
        }
    }

    return half4(result, 1.0);
}

m_CrestNameSpaceEnd

m_CrestVertex
m_CrestFragment(half4)

#endif // d_WaveHarmonic_Crest_Editor_VisualizeData
