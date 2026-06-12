// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

// Constants for shader graph. For example, we can force shader features when we have yet to make a keyword for it in
// shader graph.

// This file must be included before all other includes. And it must be done for every node. This is due to #ifndef
// limiting includes from being evaluated once, and we cannot specify the order because shader graph does this.

#ifndef CREST_SHADERGRAPH_CONSTANTS_H
#define CREST_SHADERGRAPH_CONSTANTS_H

// "pow(f,e) will not work for negative f"
#pragma warning (disable : 3571)

#ifdef UNIVERSAL_PIPELINE_CORE_INCLUDED
    #define CREST_URP 1

#if _SURFACE_TYPE_TRANSPARENT
    #define d_Transparent 1
#endif

#elif BUILTIN_TARGET_API
    #define CREST_BIRP 1

    #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Legacy/Core.hlsl"

#if _BUILTIN_SURFACE_TYPE_TRANSPARENT
    #define d_Transparent 1
#endif
#else
    // HDRP does not appear to have a reliable keyword to target.
    #define CREST_HDRP 1

#if _SURFACE_TYPE_TRANSPARENT
    #define d_Transparent 1
#endif

    #if (SHADERPASS == SHADERPASS_SHADOWS)
        #define CREST_SHADOWPASS 1
    #endif
#endif

#if defined(CREST_BIRP) || defined(CREST_URP)
#if (SHADERPASS == SHADERPASS_SHADOWCASTER)
#define CREST_SHADOWPASS 1
#endif
#endif

#if defined(CREST_HDRP) && (SHADERPASS == SHADERPASS_FORWARD)
#define CREST_HDRP_FORWARD_PASS 1
#endif

#if defined(CREST_BIRP) && (SHADERPASS == SHADERPASS_FORWARD_ADD)
#ifndef DIRECTIONAL_COOKIE
#define d_SkipRefraction 1
#define d_IsAdditionalLight 1
#endif
#endif

#if !defined(CREST_HDRP) && (SHADERPASS == SHADERPASS_MOTION_VECTORS)
#define _TRANSPARENT_WRITES_MOTION_VEC
#endif

#endif // CREST_SHADERGRAPH_CONSTANTS_H
