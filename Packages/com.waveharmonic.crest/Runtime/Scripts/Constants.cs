// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

// 9160b2d47f1cb3d7559ed4fafa79e52b9448ef2a6d863948fe3c4eeddcd958da

namespace WaveHarmonic.Crest
{
    static class Constants
    {
#if CREST_OCEAN
        const string k_Prefix = "Crest 5 ";
#else
        const string k_Prefix = "Crest ";
#endif
        const string k_MenuScripts = "Crest/";
        public const string k_MenuPrefixScripts = k_MenuScripts + k_Prefix;
        public const string k_MenuPrefixInternal = k_MenuScripts + "Internal/";
        public const string k_MenuPrefixDebug = k_MenuScripts + "Debug/" + k_Prefix;
        public const string k_MenuPrefixInputs = k_MenuScripts + "Inputs/" + k_Prefix;
        public const string k_MenuPrefixTime = k_MenuScripts + "Time/" + k_Prefix;
        public const string k_MenuPrefixSpline = k_MenuScripts + "Spline/" + k_Prefix;
        public const string k_MenuPrefixPhysics = k_MenuScripts + "Physics/" + k_Prefix;
        public const string k_MenuPrefixSample = k_MenuScripts + "Sample/" + k_Prefix;

#if UNITY_EDITOR
        public const int k_FieldGroupOrder = Editor.Inspector.k_FieldGroupOrder;
        public const int k_FieldHeadingOrder = Editor.Inspector.k_FieldHeadingOrder;
#else
        public const int k_FieldGroupOrder = 0;
        public const int k_FieldHeadingOrder = 0;
#endif

        // Unity only supports textures up to a size of 16384, even if maxTextureSize returns a larger size.
        // https://docs.unity3d.com/ScriptReference/SystemInfo-maxTextureSize.html
        public const int k_MaximumTextureResolution = 16384;

        public static class Symbols
        {
            public const string k_Refraction =
#if d_Crest_SimpleTransparency
                "CREST_NOOP";
#else
                "UNITY_2022_3_OR_NEWER";
#endif
        }

    }
}
