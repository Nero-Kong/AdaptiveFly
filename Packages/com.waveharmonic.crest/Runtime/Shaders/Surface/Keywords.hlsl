// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#ifndef d_WaveHarmonic_Crest_SurfaceKeywords
#define d_WaveHarmonic_Crest_SurfaceKeywords

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Keywords.hlsl"

#define d_Crest_AlphaTest defined(_ALPHATEST_ON)
#define d_Crest_MotionVectors defined(_TRANSPARENT_WRITES_MOTION_VEC)

#define d_Crest_CustomMesh defined(_CREST_CUSTOM_MESH)

#define d_Crest_AdditionalLights CREST_ADDITIONAL_LIGHTS
#define d_Crest_CausticsForceDistortion CREST_CAUSTICS_FORCE_DISTORTION
#define d_Crest_FoamBioluminescence CREST_FOAM_BIOLUMINESCENCE
#define d_Crest_FoamMultiScale CREST_FOAM_SAMPLING_MULTI_SCALE
#define d_Crest_FoamStochastic CREST_FOAM_SAMPLING_STOCHASTIC
#define d_Crest_NormalMap CREST_NORMAL_MAPS
#define d_Crest_SimpleTransparency CREST_SIMPLE_TRANSPARENCY
#define d_Crest_PlanarReflections CREST_PLANAR_REFLECTIONS
#define d_Crest_PlanarReflectionsApplySmoothness CREST_PLANAR_REFLECTIONS_APPLY_SMOOTHNESS

#endif
