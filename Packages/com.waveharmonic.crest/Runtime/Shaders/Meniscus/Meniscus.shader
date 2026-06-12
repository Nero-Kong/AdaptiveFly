// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

Shader "Crest/Meniscus"
{
    Properties
    {
        _Crest_Radius("Radius", Range(0.001, 0.1)) = 0.01

        [Space(10)]

        [Toggle(d_Crest_Refraction)]
        _Crest_RefractionEnabled("Refraction", Integer) = 1
        _Crest_RefractionStrength("Refraction Strength", Range(0, 1)) = 0.2

        [Space(10)]

        [Toggle(d_Crest_Lighting)]
        _Crest_LightingEnabled("Lighting", Integer) = 1
    }

    HLSLINCLUDE
    #pragma vertex Vertex
    #pragma fragment Fragment

    // #pragma enable_d3d11_debug_symbols
    ENDHLSL

    SubShader
    {
        PackageRequirements
        {
            "com.unity.render-pipelines.high-definition"
        }

        Tags { "RenderPipeline"="HDRenderPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask RGB
        Cull Off
        ZTest Always
        ZWrite Off

        Pass
        {
            Name "Meniscus"

            HLSLPROGRAM
            #pragma shader_feature_local_fragment _ d_Crest_Refraction
            #pragma shader_feature_local_fragment _ d_Crest_Lighting

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/RP/HDRP/Common.hlsl"

            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Meniscus/Meniscus.hlsl"
            ENDHLSL
        }

        Pass
        {
            PackageRequirements
            {
                "com.waveharmonic.crest.portals"
            }

            Name "Meniscus (Masked)"

            HLSLPROGRAM
            #pragma shader_feature_local_fragment _ d_Crest_Refraction
            #pragma shader_feature_local_fragment _ d_Crest_Lighting

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/RP/HDRP/Common.hlsl"

            #define d_Masked 1

            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Meniscus/Meniscus.hlsl"
            ENDHLSL
        }

        Pass
        {
            PackageRequirements
            {
                "com.waveharmonic.crest.portals"
            }

            Name "Meniscus (Front Face)"

            Cull Back
            ZTest LEqual

            HLSLPROGRAM
            #pragma shader_feature_local_fragment _ d_Crest_Refraction
            #pragma shader_feature_local_fragment _ d_Crest_Lighting

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/RP/HDRP/Common.hlsl"

            #define d_Crest_Geometry 1

            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Meniscus/Meniscus.hlsl"
            ENDHLSL
        }

        Pass
        {
            PackageRequirements
            {
                "com.waveharmonic.crest.portals"
            }

            Name "Meniscus (Back Face)"

            Cull Front
            ZTest LEqual

            HLSLPROGRAM
            #pragma shader_feature_local_fragment _ d_Crest_Refraction
            #pragma shader_feature_local_fragment _ d_Crest_Lighting

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/RP/HDRP/Common.hlsl"

            #define d_Crest_Geometry 1

            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Meniscus/Meniscus.hlsl"
            ENDHLSL
        }


        //
        // Obsolete. Depends on the raster mask.
        //

        Pass
        {
            Name "Meniscus"

            Blend DstColor Zero

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Meniscus/Meniscus.Obsolete.hlsl"
            ENDHLSL
        }

        Pass
        {
            PackageRequirements
            {
                "com.waveharmonic.crest.portals"
            }

            Name "Meniscus (Portal)"

            Blend DstColor Zero

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            // Full-screen only applicable portals with back-faces.
            #define d_Crest_HasBackFace 1

            #include "Packages/com.waveharmonic.crest.portals/Runtime/Shaders/Meniscus.hlsl"
            ENDHLSL
        }

        Pass
        {
            PackageRequirements
            {
                "com.waveharmonic.crest.portals"
            }

            Name "Meniscus (Front Face)"

            Blend DstColor Zero
            Cull Back
            ZTest LEqual

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            #define d_Crest_Geometry 1
            #define d_Crest_FrontFace 1

            #include "Packages/com.waveharmonic.crest.portals/Runtime/Shaders/Meniscus.hlsl"
            ENDHLSL
        }

        Pass
        {
            PackageRequirements
            {
                "com.waveharmonic.crest.portals"
            }

            Name "Meniscus (Back Face)"

            Blend DstColor Zero
            Cull Front
            ZTest LEqual

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            #define d_Crest_Geometry 1
            #define d_Crest_BackFace 1

            #include "Packages/com.waveharmonic.crest.portals/Runtime/Shaders/Meniscus.hlsl"
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

        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask RGB
        Cull Off
        ZTest Always
        ZWrite Off

        Pass
        {
            Name "Meniscus"

            HLSLPROGRAM
            #pragma shader_feature_local_fragment _ d_Crest_Refraction
            #pragma shader_feature_local_fragment _ d_Crest_Lighting

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Meniscus/Meniscus.hlsl"
            ENDHLSL
        }

        Pass
        {
            PackageRequirements
            {
                "com.waveharmonic.crest.portals"
            }

            Name "Meniscus (Masked)"

            HLSLPROGRAM
            #pragma shader_feature_local_fragment _ d_Crest_Refraction
            #pragma shader_feature_local_fragment _ d_Crest_Lighting

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            #define d_Masked 1

            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Meniscus/Meniscus.hlsl"
            ENDHLSL
        }

        Pass
        {
            PackageRequirements
            {
                "com.waveharmonic.crest.portals"
            }

            Name "Meniscus (Front Face)"

            Cull Back
            ZTest LEqual

            HLSLPROGRAM
            #pragma shader_feature_local_fragment _ d_Crest_Refraction
            #pragma shader_feature_local_fragment _ d_Crest_Lighting

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            #define d_Crest_Geometry 1

            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Meniscus/Meniscus.hlsl"
            ENDHLSL
        }

        Pass
        {
            PackageRequirements
            {
                "com.waveharmonic.crest.portals"
            }

            Name "Meniscus (Back Face)"

            Cull Front
            ZTest LEqual

            HLSLPROGRAM
            #pragma shader_feature_local_fragment _ d_Crest_Refraction
            #pragma shader_feature_local_fragment _ d_Crest_Lighting

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            #define d_Crest_Geometry 1

            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Meniscus/Meniscus.hlsl"
            ENDHLSL
        }


        //
        // Obsolete. Depends on the raster mask.
        //

        Pass
        {
            Name "Meniscus"

            Blend DstColor Zero

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Meniscus/Meniscus.Obsolete.hlsl"
            ENDHLSL
        }

        Pass
        {
            PackageRequirements
            {
                "com.waveharmonic.crest.portals"
            }

            Name "Meniscus (Portal)"

            Blend DstColor Zero

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Full-screen only applicable portals with back-faces.
            #define d_Crest_HasBackFace 1

            #include "Packages/com.waveharmonic.crest.portals/Runtime/Shaders/Meniscus.hlsl"
            ENDHLSL
        }

        Pass
        {
            PackageRequirements
            {
                "com.waveharmonic.crest.portals"
            }

            Name "Meniscus (Front Face)"

            Blend DstColor Zero
            Cull Back
            ZTest LEqual

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define d_Crest_Geometry 1
            #define d_Crest_FrontFace 1

            #include "Packages/com.waveharmonic.crest.portals/Runtime/Shaders/Meniscus.hlsl"
            ENDHLSL
        }

        Pass
        {
            PackageRequirements
            {
                "com.waveharmonic.crest.portals"
            }

            Name "Meniscus (Back Face)"

            Blend DstColor Zero
            Cull Front
            ZTest LEqual

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define d_Crest_Geometry 1
            #define d_Crest_BackFace 1

            #include "Packages/com.waveharmonic.crest.portals/Runtime/Shaders/Meniscus.hlsl"
            ENDHLSL
        }
    }

    SubShader
    {
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask RGB
        Cull Off
        ZTest Always
        ZWrite Off

        Pass
        {
            Name "Meniscus"

            HLSLPROGRAM
            #pragma shader_feature_local_fragment _ d_Crest_Refraction
            #pragma shader_feature_local_fragment _ d_Crest_Lighting

            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Legacy/Core.hlsl"
            #include "Packages/com.unity.shadergraph/Editor/Generation/Targets/BuiltIn/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Meniscus/Meniscus.hlsl"
            ENDHLSL
        }

        Pass
        {
            PackageRequirements
            {
                "com.waveharmonic.crest.portals"
            }

            Name "Meniscus (Masked)"

            HLSLPROGRAM
            #pragma shader_feature_local_fragment _ d_Crest_Refraction
            #pragma shader_feature_local_fragment _ d_Crest_Lighting

            #define d_Masked 1

            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Legacy/Core.hlsl"
            #include "Packages/com.unity.shadergraph/Editor/Generation/Targets/BuiltIn/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Meniscus/Meniscus.hlsl"
            ENDHLSL
        }

        Pass
        {
            PackageRequirements
            {
                "com.waveharmonic.crest.portals"
            }

            Name "Meniscus (Front)"

            Cull Back
            ZTest LEqual

            HLSLPROGRAM
            #pragma shader_feature_local_fragment _ d_Crest_Refraction
            #pragma shader_feature_local_fragment _ d_Crest_Lighting
            #define d_Crest_Geometry 1

            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Legacy/Core.hlsl"
            #include "Packages/com.unity.shadergraph/Editor/Generation/Targets/BuiltIn/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Meniscus/Meniscus.hlsl"
            ENDHLSL
        }

        Pass
        {
            PackageRequirements
            {
                "com.waveharmonic.crest.portals"
            }

            Name "Meniscus (Back)"

            Cull Front
            ZTest LEqual

            HLSLPROGRAM
            #pragma shader_feature_local_fragment _ d_Crest_Refraction
            #pragma shader_feature_local_fragment _ d_Crest_Lighting
            #define d_Crest_Geometry 1

            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Legacy/Core.hlsl"
            #include "Packages/com.unity.shadergraph/Editor/Generation/Targets/BuiltIn/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Meniscus/Meniscus.hlsl"
            ENDHLSL
        }


        //
        // Obsolete. Depends on the raster mask.
        //

        Pass
        {
            Name "Meniscus"

            Blend DstColor Zero

            HLSLPROGRAM
            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Legacy/Core.hlsl"
            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Meniscus/Meniscus.Obsolete.hlsl"
            ENDHLSL
        }

        Pass
        {
            PackageRequirements
            {
                "com.waveharmonic.crest.portals"
            }

            Name "Meniscus (Portal)"

            Blend DstColor Zero

            HLSLPROGRAM
            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Legacy/Core.hlsl"

            // Full-screen only applicable portals with back-faces.
            #define d_Crest_HasBackFace 1

            #include "Packages/com.waveharmonic.crest.portals/Runtime/Shaders/Meniscus.hlsl"
            ENDHLSL
        }

        Pass
        {
            PackageRequirements
            {
                "com.waveharmonic.crest.portals"
            }

            Name "Meniscus (Front Face)"

            Blend DstColor Zero
            Cull Back
            ZTest LEqual

            HLSLPROGRAM
            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Legacy/Core.hlsl"

            #define d_Crest_Geometry 1
            #define d_Crest_FrontFace 1

            #include "Packages/com.waveharmonic.crest.portals/Runtime/Shaders/Meniscus.hlsl"
            ENDHLSL
        }

        Pass
        {
            PackageRequirements
            {
                "com.waveharmonic.crest.portals"
            }

            Name "Meniscus (Back Face)"

            Blend DstColor Zero
            Cull Front
            ZTest LEqual

            HLSLPROGRAM
            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Legacy/Core.hlsl"

            #define d_Crest_Geometry 1
            #define d_Crest_BackFace 1

            #include "Packages/com.waveharmonic.crest.portals/Runtime/Shaders/Meniscus.hlsl"
            ENDHLSL
        }
    }

    CustomEditor "WaveHarmonic.Crest.Editor.CustomShaderGUI"
}
