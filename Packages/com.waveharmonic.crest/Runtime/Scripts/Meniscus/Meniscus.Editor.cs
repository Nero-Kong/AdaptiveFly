// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;

namespace WaveHarmonic.Crest
{
    partial class Meniscus
    {
        internal const string k_MaterialPath = "Packages/com.waveharmonic.crest/Runtime/Materials/Meniscus.mat";

        internal void Reset()
        {
            _Material = AssetDatabase.LoadAssetAtPath<Material>(k_MaterialPath);
        }

        [@OnChange]
        void OnChange(string path, object previous)
        {
            switch (path)
            {
                case nameof(_Enabled): SetEnabled((bool)previous, _Enabled); break;
                case nameof(_Material): SetMaterial((Material)previous, _Material); break;
            }
        }
    }
}

#endif
