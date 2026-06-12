// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#ifndef CREST_WATER_VERT_HELPERS_H
#define CREST_WATER_VERT_HELPERS_H

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Macros.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Globals.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Cascade.hlsl"

// These are per cascade, set per chunk instance.
CBUFFER_START(CrestChunkGeometryData)
float _Crest_ChunkMeshScaleAlpha;
float _Crest_ChunkGeometryGridWidth;
CBUFFER_END

m_CrestNameSpace

// i_meshScaleAlpha is passed in as it is provided per tile and is set only for LOD0
float ComputeLodAlpha(float3 i_worldPos, float i_meshScaleAlpha, in const Cascade i_cascadeData0)
{
    // taxicab distance from water center drives LOD transitions
    float2 offsetFromCenter = abs(float2(i_worldPos.x - g_Crest_WaterCenter.x, i_worldPos.z - g_Crest_WaterCenter.z));
    float taxicab_norm = max(offsetFromCenter.x, offsetFromCenter.y);

    // interpolation factor to next lod (lower density / higher sampling period)
    // TODO - pass this in, and then make a node to provide it automatically
    float lodAlpha = taxicab_norm / i_cascadeData0._Scale - 1.0;

    // LOD alpha is remapped to ensure patches weld together properly. Patches can vary significantly in shape (with
    // strips added and removed), and this variance depends on the base vertex density of the mesh, as this defines the
    // strip width.
    lodAlpha = max((lodAlpha - g_Crest_LodAlphaBlackPointFade) / g_Crest_LodAlphaBlackPointWhitePointFade, 0.);

    // blend out lod0 when viewpoint gains altitude
    lodAlpha = min(lodAlpha + i_meshScaleAlpha, 1.);

#if _DEBUGDISABLESMOOTHLOD_ON
    lodAlpha = 0.;
#endif

    return lodAlpha;
}

void SnapAndTransitionVertLayout(in const float4x4 i_objectMatrix, in const float i_meshScaleAlpha, in const Cascade i_cascadeData0, in const float i_geometryGridSize, inout float3 io_worldPos, out float o_lodAlpha)
{
    const float GRID_SIZE_2 = 2.0 * i_geometryGridSize, GRID_SIZE_4 = 4.0 * i_geometryGridSize;

    // snap the verts to the grid
    // The snap size should be twice the original size to keep the shape of the eight triangles (otherwise the edge layout changes).
    float2 objectPosXZWS = i_objectMatrix._m03_m23;

    // Relative world space - add camera pos to get back out to world. Would be nice if we could operate in RWS..
#if (SHADEROPTIONS_CAMERA_RELATIVE_RENDERING != 0)
    objectPosXZWS += _WorldSpaceCameraPos.xz;
#endif

    const float2 gridOffset = frac(objectPosXZWS / GRID_SIZE_2) * GRID_SIZE_2;
    io_worldPos.xz -= gridOffset; // caution - sign of frac might change in non-hlsl shaders

    // compute lod transition alpha
    o_lodAlpha = ComputeLodAlpha(io_worldPos, i_meshScaleAlpha, i_cascadeData0);

    // now smoothly transition vert layouts between lod levels - move interior verts inwards towards center
    float2 m = frac(io_worldPos.xz / GRID_SIZE_4); // this always returns positive
    float2 offset = m - 0.5;
    // Check if vert is within one square from the center point which the verts move towards. the verts that need moving
    // inwards should have a radius of 0.25, whereas the outer ring of verts will have radius 0.5. Pick half way between
    // to give max leeway for numerical robustness.
    const float minRadius = 0.375;
    if (abs(offset.x) < minRadius) io_worldPos.x += offset.x * o_lodAlpha * GRID_SIZE_4;
    if (abs(offset.y) < minRadius) io_worldPos.z += offset.y * o_lodAlpha * GRID_SIZE_4;

#if SHADER_API_VULKAN
#if CREST_HDRP
#if _TRANSPARENT_WRITES_MOTION_VEC
    // Fixes artifacts where parts of the surface appear to be clipped. It appears to
    // be a precision issue (LOD resolution not power of 2), but only when the MV code
    // path is active - even though it writes to a separate target.
    if (any(isinf(gridOffset)))
    {
        o_lodAlpha = 0.0;
    }
#endif
#endif
#endif
}

void SnapAndTransitionVertLayout(in const float i_meshScaleAlpha, in const Cascade i_cascadeData0, in const float i_geometryGridSize, inout float3 io_worldPos, out float o_lodAlpha)
{
    SnapAndTransitionVertLayout(UNITY_MATRIX_M, i_meshScaleAlpha, i_cascadeData0, i_geometryGridSize, io_worldPos, o_lodAlpha);
}

// Fix precision errors at edges.
void PatchVerticeLayout(const float2 i_ObjectPosition, const float2 i_CameraPosition, inout float2 io_PositionWS)
{
    // Scale up by small "epsilon" to solve numerical issues. Expand slightly about tile center.
    // :WaterGridPrecisionErrors
    const float2 center = i_ObjectPosition;
    const float2 camera = abs(i_CameraPosition);
    // Scale "epsilon" by distance from zero. Gaps look bad from above surface, and
    // overlaps look bad from below surface. We want to close gaps without introducing
    // overlaps. A fixed "epsilon" will either not solve gaps at large distances or
    // introduce too many overlaps at small distances. Even with scaling, there are
    // still unsolvable overlaps underwater (especially at large distances). 100,000
    // (0.00001) is the maximum position before Unity warns the user of precision issues.
    io_PositionWS = lerp(center, io_PositionWS, lerp(1.0, 1.01, max(camera.x, camera.y) * 0.00001));
}

m_CrestNameSpaceEnd

#endif // CREST_WATER_VERT_HELPERS_H
