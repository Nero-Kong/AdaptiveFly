Shader "Hidden/Crest/Debug/Visualize Data"
{
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

        Tags
        {
            "RenderPipeline"="HDRenderPipeline"
            "LightMode"="Forward"
            "RenderType"="Transparent"
            "Queue"="Transparent"
        }

        Pass
        {
            Name "Visualize"

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "VisualizeData.hlsl"
            ENDHLSL
        }
    }

    SubShader
    {
        PackageRequirements
        {
            "com.unity.render-pipelines.universal"
        }

        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "LightMode"="UniversalForward"
            // Required as I could not set ZWrite for some reason leading to overwritten.
            "RenderType"="Transparent"
            "Queue"="Transparent"
        }

        Pass
        {
            Name "Visualize"

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "VisualizeData.hlsl"
            ENDHLSL
        }
    }

    SubShader
    {
        Tags
        {
            "LightMode"="ForwardBase"
            "RenderType"="Transparent"
            "Queue"="Transparent"
        }

        Pass
        {
            Name "Visualize"

            HLSLPROGRAM
            #include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Legacy/Core.hlsl"
            #include "VisualizeData.hlsl"
            ENDHLSL
        }
    }
}
