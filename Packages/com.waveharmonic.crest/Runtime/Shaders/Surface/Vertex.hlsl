// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

// Guard against missing uniforms.
#ifdef SHADERPASS

#define m_Properties(iomod) \
    const float3 i_PositionWS, \
    const half3 i_Normal, \
    const float3 i_ObjectPosition, \
    const float3 i_CameraPosition, \
    const float i_Time, \
    iomod float3 o_PositionWS, \
    iomod half3 o_Normal, \
    iomod float2 o_UndisplacedXZ, \
    iomod float o_LodAlpha, \
    iomod half o_WaterLevelOffset, \
    iomod half2 o_Flow

// Guard against Shader Graph preview.
#ifndef SHADERGRAPH_PREVIEW

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Surface/Shim.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Surface/Keywords.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Macros.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Globals.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/InputsDriven.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Helpers.hlsl"

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Cascade.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Surface/Geometry.hlsl"

#if d_Crest_MotionVectors
#define m_Slice ComputeSlice(_Crest_LodIndex, isMotionVectors ? g_Crest_LodChange : 0, g_Crest_LodCount)
#define m_Make(slice) Make(slice, isMotionVectors)
#else
#define m_Slice _Crest_LodIndex
#define m_Make(slice) Make(slice)
#endif

m_CrestNameSpace

#if !d_Crest_CustomMesh
void Vertex(m_Properties(inout))
{
    // Chunk mesh has no normals.
    o_Normal = half3(0.0, 1.0, 0.0);

    // This will get called twice.
    // With current and previous time respectively.
    const bool isMotionVectors = i_Time < _Time.y;

    const float slice0 = m_Slice;
    const float slice1 = slice0 + 1;
    const Cascade cascade0 = Cascade::m_Make(slice0);
    const Cascade cascade1 = Cascade::m_Make(slice1);

    // Vertex snapping and LOD transition.
    SnapAndTransitionVertLayout
    (
        _Crest_ChunkMeshScaleAlpha,
        Cascade::Make(_Crest_LodIndex),
        _Crest_ChunkGeometryGridWidth,
        o_PositionWS,
        o_LodAlpha
    );

    PatchVerticeLayout(i_ObjectPosition.xz, i_CameraPosition.xz, o_PositionWS.xz);

    o_UndisplacedXZ = o_PositionWS.xz;

    // Calculate sample weights. params.z allows shape to be faded out (used on last lod to support pop-less scale transitions).
    const float weight0 = (1.0 - o_LodAlpha) * cascade0._Weight;
    const float weight1 = (1.0 - weight0) * cascade1._Weight;

    half2 derivatives = 0.0;

    // Data that needs to be sampled at the undisplaced position.
    if (weight0 > m_CrestSampleLodThreshold)
    {
#if d_Crest_MotionVectors
        if (isMotionVectors)
        {
            Cascade::MakeAnimatedWavesSource(slice0).SampleDisplacement(o_UndisplacedXZ, weight0, o_PositionWS);
        }
        else
#endif
        {
            Cascade::MakeAnimatedWaves(slice0).SampleDisplacement(o_UndisplacedXZ, weight0, o_PositionWS, derivatives, o_WaterLevelOffset);
        }
    }

    if (weight1 > m_CrestSampleLodThreshold)
    {
#if d_Crest_MotionVectors
        if (isMotionVectors)
        {
            Cascade::MakeAnimatedWavesSource(slice1).SampleDisplacement(o_UndisplacedXZ, weight1, o_PositionWS);
        }
        else
#endif
        {
            Cascade::MakeAnimatedWaves(slice1).SampleDisplacement(o_UndisplacedXZ, weight1, o_PositionWS, derivatives, o_WaterLevelOffset);
        }
    }

    // Account for water level changes which change angle of water surface, impacting normal.
    // true = normalize.
    o_Normal.xz += -derivatives;
    o_Normal = TransformWorldToObjectNormal(o_Normal, true);

#if d_Crest_FlowLod
    // Data that needs to be sampled at the displaced position.
    if (weight0 > m_CrestSampleLodThreshold)
    {
        Cascade::MakeFlow(slice0).SampleFlow(o_UndisplacedXZ, weight0, o_Flow);
    }

    if (weight1 > m_CrestSampleLodThreshold)
    {
        Cascade::MakeFlow(slice1).SampleFlow(o_UndisplacedXZ, weight1, o_Flow);
    }
#endif // d_Crest_FlowLod

#if d_Crest_MotionVectors
    if (isMotionVectors)
    {
        o_PositionWS.xz -= g_Crest_WaterCenter.xz;
        o_PositionWS.xz *= g_Crest_WaterScaleChange;
        o_PositionWS.xz += g_Crest_WaterCenter.xz;
        o_PositionWS.xz += g_Crest_WaterCenterDelta;
    }
#endif
}
#endif

m_CrestNameSpaceEnd

#endif // SHADERGRAPH_PREVIEW

void Vertex_float(m_Properties(out))
{
    o_Normal = i_Normal;
    o_PositionWS = i_PositionWS;
    o_UndisplacedXZ = i_PositionWS.xz;
    o_LodAlpha = 0.0;
    o_WaterLevelOffset = 0.0;
    o_Flow = 0.0;

#if !d_Crest_CustomMesh
#if !SHADERGRAPH_PREVIEW
    m_Crest::Vertex
    (
        i_PositionWS,
        i_Normal,
        i_ObjectPosition,
        i_CameraPosition,
        i_Time,
        o_PositionWS,
        o_Normal,
        o_UndisplacedXZ,
        o_LodAlpha,
        o_WaterLevelOffset,
        o_Flow
    );
#endif // SHADERGRAPH_PREVIEW
#endif // d_Crest_CustomMesh
}

#undef m_Properties

#endif // SHADERPASS
