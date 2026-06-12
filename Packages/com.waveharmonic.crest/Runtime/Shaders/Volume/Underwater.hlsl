// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Macros.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Volume/UnderwaterShared.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Volume/Debug.hlsl"

#if (CREST_LEGACY_UNDERWATER == 1) || defined(CREST_URP)
#define d_Crest_SampleColor 1
#else
#define d_Crest_SampleColor 0
#endif

#ifndef SUPPORTS_FOVEATED_RENDERING_NON_UNIFORM_RASTER
#define FoveatedRemapLinearToNonUniform(uv) uv
#endif

#if (CREST_LEGACY_UNDERWATER != 0) || d_Crest_CustomColorTexture
TEXTURE2D_X(_Crest_CameraColorTexture);
#endif

#if d_Crest_ComputeMask
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Surface/Data.hlsl"
#endif

m_CrestNameSpace

#if (CREST_LEGACY_UNDERWATER != 0) || d_Crest_CustomColorTexture
float3 SampleSceneColor(float2 i_UV)
{
    return LOAD_TEXTURE2D_X(_Crest_CameraColorTexture, i_UV * _ScreenSize.xy).rgb;
}
#endif

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
#if d_Crest_ComputeMask
    float3 positionWS : TEXCOORD;
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
    // Use actual geometry instead of full screen triangle.
    output.positionCS = TransformObjectToHClip(input.positionOS);
#if d_Crest_ComputeMask
    output.positionWS = TransformObjectToWorld(input.positionOS);
#endif
#else
    output.positionCS = GetFullScreenTriangleVertexPosition(input.id, UNITY_RAW_FAR_CLIP_VALUE);
#endif

    return output;
}

half4 Fragment(Varyings input)
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    uint2 positionSS = input.positionCS.xy;
    const float2 positionNDC = (positionSS + 0.5) / _ScreenSize.xy;
    float mask = -1.0;

#if !d_Crest_NoMaskColor
#if d_Crest_ComputeMask
    {
        float3 positionWS = input.positionWS;
#if (SHADEROPTIONS_CAMERA_RELATIVE_RENDERING != 0)
        positionWS.xyz += _WorldSpaceCameraPos.xyz;
#endif
        mask = positionWS.y <= SampleWaterLineHeight(positionWS.xz) ? -1 : 1;
    }
#else
    mask = LOAD_TEXTURE2D_X(_Crest_WaterMaskTexture, positionSS).x;
#endif

#if !_DEBUG_VISUALIZE_MASK
    // Preserve alpha channel.
    if (mask > CREST_MASK_BELOW_SURFACE)
    {
        discard;
    }
#endif
#endif // !d_Crest_NoMaskColor

    float rawDepth = LoadSceneDepth(positionSS);

    half3 sceneColour;

#if d_Crest_CustomColorTexture
    if (m_CrestPortalNegativeVolume)
    {
        sceneColour = LOAD_TEXTURE2D_X(_Crest_CameraColorTexture, positionSS).rgb;
    }
    else
#endif
    {
        // Use sample in case texture is downsampled.
        sceneColour = SampleSceneColor(positionNDC).rgb;
    }

#if d_Crest_NoMaskDepth
    const float rawMaskDepth = 0.0;
#else
    const float rawMaskDepth = LOAD_TEXTURE2D_X(_Crest_WaterMaskDepthTexture, positionSS).x;
#endif

#if _DEBUG_VISUALIZE_STENCIL
    return DebugRenderStencil(sceneColour);
#endif

    bool isWaterSurface; bool isUnderwater; bool hasCaustics; float sceneZ; bool outScatterScene; bool applyLighting;
    GetWaterSurfaceAndUnderwaterData(input.positionCS, positionSS, rawMaskDepth, mask, rawDepth, isWaterSurface, isUnderwater, hasCaustics, outScatterScene, applyLighting, sceneZ);

#if !_DEBUG_VISUALIZE_MASK
    // Preserve alpha channel.
    if (!isUnderwater)
    {
        discard;
    }
#endif

    float fogDistance = sceneZ;
    ApplyWaterVolumeToUnderwaterFog(input.positionCS, fogDistance);

#if _DEBUG_VISUALIZE_MASK
    return DebugRenderWaterMask(isWaterSurface, isUnderwater, mask, sceneColour);
#endif

    if (isUnderwater)
    {
        const float2 uv = FoveatedRemapLinearToNonUniform(positionNDC);
        float3 positionWS = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
        const half3 view = GetWorldSpaceNormalizeViewDir(positionWS);
#if (SHADEROPTIONS_CAMERA_RELATIVE_RENDERING != 0)
        positionWS += _WorldSpaceCameraPos;
#endif
        half3 vo =0; half3 vl = 0;
        sceneColour = ApplyUnderwaterEffect(sceneColour, rawDepth, sceneZ, fogDistance, view, positionSS, positionWS, hasCaustics, outScatterScene, applyLighting, 1.0, vo, vl);
    }

    return half4(sceneColour, 1.0);
}

half4 FragmentPlanarReflections(Varyings input)
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    const uint2 positionSS = input.positionCS.xy;
    const float2 positionNDC = (positionSS + 0.5) / _ScreenSize.xy;
    float depth = LoadSceneDepth(positionSS);

    // TODO: Do something nicer. Could zero alpha if scene depth is above threshold.
    if (depth == 0.0)
    {
        return half4(_Crest_Scattering.xyz, 1.0);
    }

#if d_Crest_SampleColor
    half3 color = SampleSceneColor(positionNDC).rgb;
#else
    half3 color = LoadSceneColor(positionSS).rgb;
#endif

    // Calculate position and account for possible NaNs discovered during testing.
    float3 positionWS = Utility::SafeComputeWorldSpacePosition(positionNDC, depth, UNITY_MATRIX_I_VP);

#if (SHADEROPTIONS_CAMERA_RELATIVE_RENDERING != 0)
    positionWS += _WorldSpaceCameraPos;
#endif

    const half3 view = GetWorldSpaceNormalizeViewDir(positionWS);
    const bool hasCaustics = depth > 0.0;

    half3 vo = 0; half3 vl = 0;
    color = ApplyUnderwaterEffect(color, depth, 0.0, 0.0, view, positionSS, positionWS, hasCaustics, true, true, 1.0, vo, vl);

    return half4(color, 1.0);
}

m_CrestNameSpaceEnd

m_CrestVertex
m_CrestFragment(half4)

half4 FragmentPlanarReflections(m_Crest::Varyings input) : SV_Target
{
    return m_Crest::FragmentPlanarReflections(input);
}
