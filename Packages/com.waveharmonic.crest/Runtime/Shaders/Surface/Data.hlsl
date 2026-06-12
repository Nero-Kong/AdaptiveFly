// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#ifndef d_WaveHarmonic_Crest_SurfaceData
#define d_WaveHarmonic_Crest_SurfaceData

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Macros.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Globals.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Surface/Utility.hlsl"

TEXTURE2D_FLOAT(_Crest_WaterLine);
float  _Crest_WaterLineTexel;
float2 _Crest_WaterLineResolution;
float2 _Crest_WaterLineSnappedPosition;
bool   _Crest_WaterLineFlatWater;

m_CrestNameSpace

float SampleWaterLineHeight(const float2 i_PositionWS)
{
    if (_Crest_WaterLineFlatWater)
    {
        return g_Crest_WaterCenter.y;
    }
    else
    {
        const float2 uv = (i_PositionWS - _Crest_WaterLineSnappedPosition) / (_Crest_WaterLineTexel * _Crest_WaterLineResolution) + 0.5;
        return _Crest_WaterLine.SampleLevel(LODData_linear_clamp_sampler, uv, 0).r + g_Crest_WaterCenter.y;
    }
}

half3 SampleWaterLineNormal(const float2 i_PositionWS, const float i_Height)
{
    if (_Crest_WaterLineFlatWater)
    {
        return half3(0, 1, 0);
    }
    else
    {
        const float2 uv = (i_PositionWS - _Crest_WaterLineSnappedPosition) / (_Crest_WaterLineTexel * _Crest_WaterLineResolution) + 0.5;
        const float3 dd = float3(1.0 / _Crest_WaterLineResolution.xy, 0.0);
        const float xOffset = _Crest_WaterLine.SampleLevel(LODData_linear_clamp_sampler, uv + dd.xz, 0).r;
        const float zOffset = _Crest_WaterLine.SampleLevel(LODData_linear_clamp_sampler, uv + dd.zy, 0).r;

        return normalize(half3
        (
            (xOffset - i_Height) / _Crest_WaterLineTexel,
            1.0,
            (zOffset - i_Height) / _Crest_WaterLineTexel
        ));
    }
}

m_CrestNameSpaceEnd

#endif
