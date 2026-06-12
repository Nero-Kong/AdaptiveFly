// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#ifndef CREST_WATER_FOAM_H
#define CREST_WATER_FOAM_H

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Settings.Crest.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Macros.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Globals.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Texture.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Flow.hlsl"

#if (CREST_SHIFTING_ORIGIN != 0)
#include "Packages/com.waveharmonic.crest.shifting-origin/Runtime/Shaders/ShiftingOrigin.hlsl"
#endif

#if d_Crest_FoamStochastic
#define m_SampleFoam SampleStochastic
#else
#define m_SampleFoam Sample
#endif

m_CrestNameSpace

half2 WhiteFoamTexture
(
    const TiledTexture i_Texture,
    const half i_Foam,
    const half i_Feather,
    const float2 i_WorldXZ0,
    const float2 i_WorldXZ1,
    const float2 i_TexelOffset,
    const half i_LodAlpha,
    const Cascade i_CascadeData0,
    const bool i_Sample2ndChannel = false
)
{
    const float2 uvOffset = i_TexelOffset + g_Crest_Time * i_Texture._speed / 32.0;
    // Scale with lods to get multiscale detail. 10 is magic number that gets the
    // material 'scale' slider into an intuitive range.
    float scale = i_Texture._scale * 0.5;

#if d_Crest_FoamMultiScale
    // 0.5 * 0.2 == / 10. We only need the full amount for multi-scale to account for
    // the default lower scale of 4. * 0.5 alone above will not match multi-scaled, but
    // overall better matches visually.
    scale *= i_CascadeData0._Scale * 0.2;
#endif

    half2 f0 = i_Texture.m_SampleFoam((i_WorldXZ0 + uvOffset) / scale).rg;

#if d_Crest_FoamMultiScale
    const half2 f1 = i_Texture.m_SampleFoam((i_WorldXZ1 + uvOffset) / (2.0 * scale)).rg;
#endif

    // Black point fade.
    const half result = saturate(1.0 - i_Foam);

#if d_Crest_FoamBioluminescence
    if (i_Sample2ndChannel)
    {
#if d_Crest_FoamMultiScale
        f0 = lerp(f0, f1, i_LodAlpha);
#endif

        return smoothstep(result, result + i_Feather, f0);
    }
    else
#endif
    {
#if d_Crest_FoamMultiScale
        f0.r = lerp(f0.r, f1.r, i_LodAlpha);
#endif

        return half2(smoothstep(result, result + i_Feather, f0.r), 0.0);
    }
}

half2 MultiScaleFoamAlbedo
(
    const TiledTexture i_Texture,
    const half i_Feather,
    const half i_FoamData,
    const Cascade i_CascadeData0,
    const Cascade i_CascadeData1,
    const half i_LodAlpha,
    const float2 i_UndisplacedXZ,
    const bool i_Sample2ndChannel
)
{
    float2 worldXZ0 = i_UndisplacedXZ;
    float2 worldXZ1 = i_UndisplacedXZ;

#if (CREST_SHIFTING_ORIGIN != 0)
    // Apply tiled floating origin offset. Only needed if:
    //  - _FoamScale is a non integer value
    //  - _FoamScale is over 48
    worldXZ0 -= ShiftingOriginOffset(i_Texture, i_CascadeData0);
    worldXZ1 -= ShiftingOriginOffset(i_Texture, i_CascadeData1);
#endif // CREST_SHIFTING_ORIGIN

    return WhiteFoamTexture(i_Texture, i_FoamData, i_Feather, worldXZ0, worldXZ1, (float2)0.0, i_LodAlpha, i_CascadeData0, i_Sample2ndChannel);
}

half2 MultiScaleFoamNormal
(
    const TiledTexture i_Texture,
    const half i_Feather,
    const half i_FoamData,
    const Cascade i_CascadeData0,
    const Cascade i_CascadeData1,
    const half i_LodAlpha,
    const float2 i_UndisplacedXZ,
    const half i_NormalStrength,
    const half i_FoamAlbedo,
    const float i_PixelZ
)
{
    float2 worldXZ0 = i_UndisplacedXZ;
    float2 worldXZ1 = i_UndisplacedXZ;

#if (CREST_SHIFTING_ORIGIN != 0)
    // Apply tiled floating origin offset. Only needed if:
    //  - _FoamScale is a non integer value
    //  - _FoamScale is over 48
    worldXZ0 -= ShiftingOriginOffset(i_Texture, i_CascadeData0);
    worldXZ1 -= ShiftingOriginOffset(i_Texture, i_CascadeData1);
#endif // CREST_SHIFTING_ORIGIN

    // 0.25 is magic number found through tweaking.
    const float2 dd = float2(0.25 * i_PixelZ * i_Texture._texel, 0.0);
    const half whiteFoam_x = WhiteFoamTexture(i_Texture, i_FoamData, i_Feather, worldXZ0, worldXZ1, dd.xy, i_LodAlpha, i_CascadeData0).r;
    const half whiteFoam_z = WhiteFoamTexture(i_Texture, i_FoamData, i_Feather, worldXZ0, worldXZ1, dd.yx, i_LodAlpha, i_CascadeData0).r;

    // Compute a foam normal - manually push in derivatives. If I used blend
    // smooths all the normals towards straight up when there is no foam.
    // Gets material slider into friendly range.
    const float magicStrengthFactor = 0.01;
    return magicStrengthFactor * i_NormalStrength * half2(whiteFoam_x - i_FoamAlbedo, whiteFoam_z - i_FoamAlbedo) / dd.x;
}

half2 MultiScaleFoamAlbedo
(
    const Flow i_Flow,
    const TiledTexture i_Texture,
    const half i_Feather,
    const half i_Foam,
    const Cascade i_CascadeData0,
    const Cascade i_CascadeData1,
    const half i_LodAlpha,
    const float2 i_UndisplacedXZ,
    const bool i_Sample2ndChannel
)
{
    return MultiScaleFoamAlbedo
    (
        i_Texture,
        i_Feather,
        i_Foam,
        i_CascadeData0,
        i_CascadeData1,
        i_LodAlpha,
        i_UndisplacedXZ - i_Flow._Flow * i_Flow._Offset0,
        i_Sample2ndChannel
    ) * i_Flow._Weight0 + MultiScaleFoamAlbedo
    (
        i_Texture,
        i_Feather,
        i_Foam,
        i_CascadeData0,
        i_CascadeData1,
        i_LodAlpha,
        i_UndisplacedXZ - i_Flow._Flow * i_Flow._Offset1,
        i_Sample2ndChannel
    ) * i_Flow._Weight1;
}

half2 MultiScaleFoamNormal
(
    const Flow i_Flow,
    const TiledTexture i_Texture,
    const half i_Feather,
    const half i_FoamData,
    const Cascade i_CascadeData0,
    const Cascade i_CascadeData1,
    const half i_LodAlpha,
    const float2 i_UndisplacedXZ,
    const half i_NormalStrength,
    const half i_FoamAlbedo,
    const float i_PixelZ
)
{
    return MultiScaleFoamNormal
    (
        i_Texture,
        i_Feather,
        i_FoamData,
        i_CascadeData0,
        i_CascadeData1,
        i_LodAlpha,
        i_UndisplacedXZ - i_Flow._Flow * i_Flow._Offset0,
        i_NormalStrength,
        i_FoamAlbedo,
        i_PixelZ
    ) * i_Flow._Weight0 + MultiScaleFoamNormal
    (
        i_Texture,
        i_Feather,
        i_FoamData,
        i_CascadeData0,
        i_CascadeData1,
        i_LodAlpha,
        i_UndisplacedXZ - i_Flow._Flow * i_Flow._Offset1,
        i_NormalStrength,
        i_FoamAlbedo,
        i_PixelZ
    ) * i_Flow._Weight1;
}

void ApplyFoamToSurface
(
    half i_Foam,
    const half2 i_Normal,
    const half3 i_Albedo,
    const half i_Occlusion,
    const half i_Smoothness,
    const half i_Specular,
    const bool i_Underwater,
    inout half3 io_Albedo,
    inout half3 io_NormalWS,
    inout half3 io_Emission,
    inout half io_Occlusion,
    inout float io_Smoothness,
    inout half3 io_Specular
)
{
    // Apply foam to surface.
    io_Albedo = lerp(io_Albedo, i_Albedo, i_Foam);
    io_Emission *= 1.0 - i_Foam;
    io_Occlusion = lerp(io_Occlusion, i_Occlusion, i_Foam);
    io_Smoothness = lerp(io_Smoothness, i_Smoothness, i_Foam);
    io_NormalWS.xz -= i_Normal;
    io_NormalWS = normalize(io_NormalWS);

    // Foam Transmission
    if (i_Underwater)
    {
        // Foam will be black when not facing the sun. This is a hacky way to have foam lit
        // as if it had transmission.
        // There is still ugliness around the edges. There will either be black or
        // incorrect reflections depending on the magic value.
        io_NormalWS = i_Foam > 0.0 ? half3(0.0, 1.0, 0.0) : io_NormalWS;
#if _SPECULAR_SETUP
        io_Specular = lerp(io_Specular, i_Specular, i_Foam);
#endif
    }
}

m_CrestNameSpaceEnd

#endif
