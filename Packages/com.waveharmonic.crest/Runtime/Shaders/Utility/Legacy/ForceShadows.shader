// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

Shader "Hidden/Crest/Legacy/ForceShadows"
{
    CGINCLUDE
    #pragma vertex Vertex
    #pragma fragment Fragment

    fixed4 Vertex(fixed4 v : POSITION) : SV_POSITION
    {
        return 0;
    }

    fixed4 Fragment(fixed4 i : SV_POSITION) : SV_Target
    {
        return 0;
    }
    ENDCG

    SubShader
    {
        ZTest Always
        ZWrite Off
        ColorMask 0

        Pass
        {
            Tags
            {
                "LightMode" = "ForwardBase"
            }

            CGPROGRAM
            #pragma multi_compile_fwdbase
            ENDCG
        }

        Pass
        {
            Tags
            {
                "LightMode" = "ForwardAdd"
            }

            CGPROGRAM
            #pragma multi_compile_fwdadd_fullshadows
            ENDCG
        }
    }
}
