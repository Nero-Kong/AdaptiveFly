// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#ifndef d_WaveHarmonic_Crest_Meniscus
#define d_WaveHarmonic_Crest_Meniscus

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Settings.Crest.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Macros.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Constants.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Cascade.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Depth.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Globals.hlsl"

TEXTURE2D_X(_Crest_WaterMaskTexture);

m_CrestNameSpace

struct Attributes
{
    uint id : SV_VertexID;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings Vertex(Attributes input)
{
    Varyings output;
    ZERO_INITIALIZE(Varyings, output);
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    output.positionCS = GetFullScreenTriangleVertexPosition(input.id, UNITY_RAW_FAR_CLIP_VALUE);
    return output;
}

half4 Fragment(Varyings input)
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    const uint2 positionSS = input.positionCS.xy;
    const float mask = LOAD_TEXTURE2D_X(_Crest_WaterMaskTexture, positionSS).x;
    const float2 offset = -((float2)mask) * g_Crest_HorizonNormal;
    float weight = 1.0;

    // Sample three pixels along the normal. If the sample is different than the
    // current mask, apply meniscus. Offset must be added to positionSS as floats.
    [unroll]
    for (int i = 1; i <= 3; i++)
    {
        const float2 uv = positionSS + offset * (float)i;
        const float newMask = LOAD_TEXTURE2D_X(_Crest_WaterMaskTexture, uv).r;
        weight *= newMask != mask && newMask != 0.0 ? 0.9 : 1.0;
    }

    return weight;
}

m_CrestNameSpaceEnd

m_CrestVertex
m_CrestFragment(half4)

#endif // d_WaveHarmonic_Crest_Meniscus
