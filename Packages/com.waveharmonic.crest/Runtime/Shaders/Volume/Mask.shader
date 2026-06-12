// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

Shader "Hidden/Crest/Underwater/Water Surface Mask"
{
    SubShader
    {
        PackageRequirements
        {
            "com.unity.render-pipelines.high-definition"
        }

        Tags { "RenderPipeline"="HDRenderPipeline" }

        Pass
        {
            Name "Water Surface Mask"
            // We always disable culling when rendering water mask, as we only
            // use it for underwater rendering features.
            Cull Off

            Stencil
            {
                Ref [_Crest_StencilReference]
                Comp [_Crest_StencilComparison]
            }

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            // for VFACE
            #pragma target 3.0

            #pragma multi_compile_local_fragment __ d_Tunnel

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Volume/Mask.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Water Surface Mask (Negative Volume)"
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            // for VFACE
            #pragma target 3.0

            #define m_Return discard

            #define d_Crest_NegativeVolumePass 1

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Volume/Mask.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Water Surface Data"
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            #pragma target 3.0

            #define d_LodInput 1

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Volume/Mask.hlsl"
            ENDHLSL
        }
    }

    SubShader
    {
        PackageRequirements
        {
            "com.unity.render-pipelines.universal"
        }

        Tags { "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "Water Surface Mask"
            // We always disable culling when rendering water mask, as we only
            // use it for underwater rendering features.
            Cull Off

            Stencil
            {
                Ref [_Crest_StencilReference]
                Comp [_Crest_StencilComparison]
            }

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            // for VFACE
            #pragma target 3.0

            #pragma multi_compile_local_fragment __ d_Tunnel

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Volume/Mask.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Water Surface Mask (Negative Volume)"
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            // for VFACE
            #pragma target 3.0

            #define m_Return discard

            #define d_Crest_NegativeVolumePass 1

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Volume/Mask.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Water Surface Data"
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            #pragma target 3.0

            #define d_LodInput 1

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Volume/Mask.hlsl"
            ENDHLSL
        }
    }

    SubShader
    {
        Pass
        {
            Name "Water Surface Mask"
            // We always disable culling when rendering water mask, as we only
            // use it for underwater rendering features.
            Cull Off

            Stencil
            {
                Ref [_Crest_StencilReference]
                Comp [_Crest_StencilComparison]
            }

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            // for VFACE
            #pragma target 3.0

            #pragma multi_compile_local_fragment __ d_Tunnel

            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Legacy/Core.hlsl"

            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Volume/Mask.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Water Surface Mask (Negative Volume)"
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            // for VFACE
            #pragma target 3.0

            #define m_Return discard

            #define d_Crest_NegativeVolumePass 1

            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Legacy/Core.hlsl"

            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Volume/Mask.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Water Surface Data"
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            #pragma target 3.0

            #define d_LodInput 1

            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Legacy/Core.hlsl"

            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Volume/Mask.hlsl"
            ENDHLSL
        }
    }
}
