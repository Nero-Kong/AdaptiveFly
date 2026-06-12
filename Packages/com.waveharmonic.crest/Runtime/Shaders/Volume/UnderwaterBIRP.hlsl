// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#define BUILTIN_TARGET_API 1

#define CREST_BIRP 1
#define CREST_SHADERGRAPH_CONSTANTS_H

#pragma multi_compile_fragment _ DIRECTIONAL_COOKIE

#include "Packages/com.unity.shadergraph/Editor/Generation/Targets/BuiltIn/Editor/ShaderGraph/Includes/ShaderPass.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Legacy/Defines.hlsl"

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Legacy/Core.hlsl"

#include "Packages/com.unity.shadergraph/Editor/Generation/Targets/BuiltIn/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.shadergraph/Editor/Generation/Targets/BuiltIn/Editor/ShaderGraph/Includes/LegacySurfaceVertex.hlsl"
#include "Packages/com.unity.shadergraph/Editor/Generation/Targets/BuiltIn/ShaderLibrary/ShaderGraphFunctions.hlsl"

#include "Packages/com.unity.shadergraph/Editor/Generation/Targets/BuiltIn/ShaderLibrary/DeclareOpaqueTexture.hlsl"
#include "Packages/com.unity.shadergraph/Editor/Generation/Targets/BuiltIn/ShaderLibrary/DeclareDepthTexture.hlsl"
