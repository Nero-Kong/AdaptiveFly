// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#ifndef d_WaterLevelDepth
#define d_WaterLevelDepth

#define d_WaterLevelDisplacementOnly 1

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Macros.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Vertex/Surface.hlsl"

m_CrestNameSpace

half4 Fragment(Varyings varyings)
{
    return half4(0.0, 0.0, 0.0, 1.0);
}

m_CrestNameSpaceEnd

m_CrestVertex
m_CrestFragment(half4)

#endif // d_WaterLevelDepth
