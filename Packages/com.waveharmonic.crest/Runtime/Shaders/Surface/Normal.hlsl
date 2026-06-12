// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#ifndef CREST_WATER_NORMAL_H
#define CREST_WATER_NORMAL_H

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Settings.Crest.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Constants.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Macros.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Texture.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Flow.hlsl"

#if (CREST_SHIFTING_ORIGIN != 0)
#include "Packages/com.waveharmonic.crest.shifting-origin/Runtime/Shaders/ShiftingOrigin.hlsl"
#endif

#if _CREST_CUSTOM_MESH
float4 _Crest_NormalMapParameters[MAX_LOD_COUNT];
#define _Crest_ChunkNormalMapParameters _Crest_NormalMapParameters[i_CascadeData._IndexI]
#else
// These are per cascade, set per chunk instance.
float  _Crest_ChunkFarNormalsWeight;
float2 _Crest_ChunkNormalScrollSpeed;
#define _Crest_ChunkNormalMapParameters float3(_Crest_ChunkNormalScrollSpeed, _Crest_ChunkFarNormalsWeight)
#endif

m_CrestNameSpace

// Limit how close to horizontal reflection ray can get, useful to avoid unsightly below-horizon reflections.
half3 ApplyMinimumReflectionDirectionY
(
    const half i_MinimumReflectionDirectionY,
    const half3 i_ViewDirectionWS,
    const half3 i_NormalWS
)
{
    half3 normal = i_NormalWS;

    float3 refl = reflect(-i_ViewDirectionWS, normal);
    if (refl.y < i_MinimumReflectionDirectionY)
    {
        // Find the normal that keeps the reflection direction above the horizon. Compute
        // the reflection dir that does work, normalize it, and then normal is half vector
        // between this good reflection direction and view direction.
        float3 FL = refl;
        FL.y = i_MinimumReflectionDirectionY;
        FL = normalize(FL);
        normal = normalize(FL + i_ViewDirectionWS);
    }

    return normal;
}

half2 SampleNormalMaps
(
    const TiledTexture i_NormalMap,
    const half i_Strength,
    const float2 i_UndisplacedXZ,
    const float i_LodAlpha,
    const Cascade i_CascadeData
)
{
    float2 worldXZUndisplaced = i_UndisplacedXZ;

#if (CREST_SHIFTING_ORIGIN != 0)
    // Apply tiled floating origin offset. Always needed.
    worldXZUndisplaced -= ShiftingOriginOffset(i_NormalMap, i_CascadeData);
#endif

    const float3 parameters = _Crest_ChunkNormalMapParameters.xyz;
    const float2 speed = parameters.xy;
    const float farWeight = parameters.z;

    const float2 v0 = float2(0.94, 0.34), v1 = float2(-0.85, -0.53);
    float scale = i_NormalMap._scale * i_CascadeData._Scale * 0.1;
    const float time = i_NormalMap._speed * g_Crest_Time;
    const float spdmulL = speed.x * time;
    half2 norm =
        UnpackNormal(i_NormalMap.Sample((worldXZUndisplaced + v0 * spdmulL) / scale)).xy +
        UnpackNormal(i_NormalMap.Sample((worldXZUndisplaced + v1 * spdmulL) / scale)).xy;

    // blend in next higher scale of normals to obtain continuity
    const half nblend = i_LodAlpha * farWeight;
    if (nblend > 0.001)
    {
        // next lod level
        scale *= 2.0;
        const float spdmulH = speed.y * time;
        norm = lerp(norm,
            UnpackNormal(i_NormalMap.Sample((worldXZUndisplaced + v0 * spdmulH) / scale)).xy +
            UnpackNormal(i_NormalMap.Sample((worldXZUndisplaced + v1 * spdmulH) / scale)).xy,
            nblend);
    }

    // approximate combine of normals. would be better if normals applied in local frame.
    return norm;
}

half2 SampleNormalMaps
(
    const Flow i_Flow,
    const TiledTexture i_NormalMap,
    const half i_Strength,
    const float2 i_UndisplacedXZ,
    const float i_LodAlpha,
    const Cascade i_CascadeData
)
{
    return SampleNormalMaps
    (
        i_NormalMap,
        i_Strength,
        i_UndisplacedXZ - i_Flow._Flow * (i_Flow._Offset0 - i_Flow._Period * 0.5),
        i_LodAlpha,
        i_CascadeData
    ) * i_Flow._Weight0 + SampleNormalMaps
    (
        i_NormalMap,
        i_Strength,
        i_UndisplacedXZ - i_Flow._Flow * (i_Flow._Offset1 - i_Flow._Period * 0.5),
        i_LodAlpha,
        i_CascadeData
    ) * i_Flow._Weight1;
}

half NormalMapTurbulence
(
    const half3 i_NormalWS,
    const half2 i_NormalMap,
    const half i_NormalMapStrength,
    const half i_Coverage,
    const half i_Strength,
    const half3 i_ViewDirectionWS,
    const half i_Determinant,
    const half i_WaterLevel,
    const float i_PixelZ,
    const half3 i_PrimaryLightDirection
)
{
    half strength = i_NormalMapStrength;

    if (saturate(i_Coverage - i_Determinant) > 0)
    {
        // Add boosted normal map.
        half3 normal = i_NormalWS;
        normal.xz += i_NormalMap * i_Strength;
        normal = normalize(normal);

        // Increase normal map strength only if "sparkle".
        if (dot(normal, normalize(i_ViewDirectionWS + i_PrimaryLightDirection)) >= 0.99)
        {
            // Height (100m) & distance (2m) cull. Looks odd up close and degrades up high.
            const half cull = max(saturate(abs(_WorldSpaceCameraPos.y - i_WaterLevel) * 0.01), 1.0 - saturate(i_PixelZ * 0.5));
            strength = lerp(i_Strength, strength, cull);
        }
    }

    return strength;
}

void WaterNormal
(
    const half i_Strength,
    const bool i_Underwater,
    inout half3 io_NormalWS
)
{
    // Finalise normal
    io_NormalWS = normalize(io_NormalWS);

    if (i_Strength < 1.0)
    {
        io_NormalWS.xz *= i_Strength;
        io_NormalWS.y = lerp(1.0, io_NormalWS.y, i_Strength);
    }

    if (i_Underwater)
    {
        // Flip when underwater.
        io_NormalWS.xyz *= -1.0;
    }
}



m_CrestNameSpaceEnd

#endif
