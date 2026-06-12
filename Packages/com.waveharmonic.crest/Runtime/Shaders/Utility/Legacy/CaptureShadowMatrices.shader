// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

Shader "Hidden/Crest/Legacy/CaptureShadowMatrices"
{
    SubShader
    {
        Blend Off
        ColorMask 0
        Cull Off
        ZTest Always
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vertex
            #pragma fragment Fragment

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            RWStructuredBuffer<float4x4> _Crest_WorldToShadow;
            float4x4 unity_WorldToShadow[4];

            float4 Vertex(uint id : SV_VertexID) : SV_POSITION
            {
                return GetFullScreenTriangleVertexPosition(id);
            }

            float4 Fragment(float4 pos : SV_POSITION) : SV_Target
            {
                _Crest_WorldToShadow[0] = unity_WorldToShadow[0];
                _Crest_WorldToShadow[1] = unity_WorldToShadow[1];
                _Crest_WorldToShadow[2] = unity_WorldToShadow[2];
                _Crest_WorldToShadow[3] = unity_WorldToShadow[3];
                return 0;
            }
            ENDHLSL
        }
    }
}
