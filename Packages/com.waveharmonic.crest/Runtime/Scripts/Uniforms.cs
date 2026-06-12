// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;

namespace WaveHarmonic.Crest
{
    static class ShaderIDs
    {
        public static readonly int s_Blend = Shader.PropertyToID("_Crest_Blend");
        public static readonly int s_Texture = Shader.PropertyToID("_Crest_Texture");
        public static readonly int s_Source = Shader.PropertyToID("_Crest_Source");
        public static readonly int s_Target = Shader.PropertyToID("_Crest_Target");
        public static readonly int s_TargetSlice = Shader.PropertyToID("_Crest_TargetSlice");
        public static readonly int s_Resolution = Shader.PropertyToID("_Crest_Resolution");
        public static readonly int s_ClearMask = Shader.PropertyToID("_Crest_ClearMask");
        public static readonly int s_ClearColor = Shader.PropertyToID("_Crest_ClearColor");
        public static readonly int s_Matrix = Shader.PropertyToID("_Crest_Matrix");
        public static readonly int s_Position = Shader.PropertyToID("_Crest_Position");
        public static readonly int s_Diameter = Shader.PropertyToID("_Crest_Diameter");
        public static readonly int s_TextureSize = Shader.PropertyToID("_Crest_TextureSize");
        public static readonly int s_TexturePosition = Shader.PropertyToID("_Crest_TexturePosition");
        public static readonly int s_TextureRotation = Shader.PropertyToID("_Crest_TextureRotation");
        public static readonly int s_TextureResolution = Shader.PropertyToID("_Crest_TextureResolution");
        public static readonly int s_Multiplier = Shader.PropertyToID("_Crest_Multiplier");
        public static readonly int s_FeatherWidth = Shader.PropertyToID("_Crest_FeatherWidth");
        public static readonly int s_NegativeValues = Shader.PropertyToID("_Crest_NegativeValues");
        public static readonly int s_BoundaryXZ = Shader.PropertyToID("_Crest_BoundaryXZ");
        public static readonly int s_DrawBoundaryXZ = Shader.PropertyToID("_Crest_DrawBoundaryXZ");

        public static class Unity
        {
            public static readonly int s_CameraDepthTexture = Shader.PropertyToID("_CameraDepthTexture");
            public static readonly int s_CameraOpaqueTexture = Shader.PropertyToID("_CameraOpaqueTexture");
            public static readonly int s_MatrixPreviousM = Shader.PropertyToID("unity_MatrixPreviousM");
            public static readonly int s_SpecCube0 = Shader.PropertyToID("unity_SpecCube0");
            public static readonly int s_Time = Shader.PropertyToID("_Time");
            public static readonly int s_CameraToWorld = Shader.PropertyToID("_CameraToWorld");


            // Shader Graph
            public static readonly int s_Surface = Shader.PropertyToID("_Surface");
            public static readonly int s_SrcBlend = Shader.PropertyToID("_SrcBlend");
            public static readonly int s_DstBlend = Shader.PropertyToID("_DstBlend");


            // Built-In Renderer
            public static readonly int s_LightColor0 = Shader.PropertyToID("_LightColor0");
            public static readonly int s_ShadowMapTexture = Shader.PropertyToID("_ShadowMapTexture");
            public static readonly int s_WorldSpaceLightPos0 = Shader.PropertyToID("_WorldSpaceLightPos0");

            // High Definition Renderer
            public static readonly int s_ShaderVariablesGlobal = Shader.PropertyToID("ShaderVariablesGlobal");

            // Universal Renderer
            public static readonly int s_GlossyEnvironmentCubeMap = Shader.PropertyToID("_GlossyEnvironmentCubeMap");
        }
    }
}
