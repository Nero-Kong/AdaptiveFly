// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;

namespace WaveHarmonic.Crest
{
    partial class SurfaceRenderer
    {
        internal void Reset()
        {
            _Material = AssetDatabase.LoadAssetAtPath<Material>("Packages/com.waveharmonic.crest/Runtime/Materials/Water.mat");
            _ChunkTemplate = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.waveharmonic.crest/Runtime/Prefabs/Chunk.prefab");
        }

        [@OnChange]
        void OnChange(string path, object previous)
        {
            switch (path)
            {
                case nameof(_Enabled):
                    SetEnabled((bool)previous, _Enabled);
                    break;
                case nameof(_Layer):
                    SetLayer((int)previous, _Layer);
                    break;
                case nameof(_MeshType):
                    _Water.Rebuild();
                    break;
                case nameof(_ChunkTemplate):
                    // We have to rebuild, as we instantiate entire GO. If we restricted it to just a
                    // MeshRenderer, then we could just replace those.
                    Rebuild();
                    break;
                case nameof(_CastShadows):
                    SetCastShadows((bool)previous, _CastShadows);
                    break;
                case nameof(_AllowRenderQueueSorting):
                    SetAllowRenderQueueSorting((bool)previous, _AllowRenderQueueSorting);
                    break;
                case nameof(_Debug) + "." + nameof(DebugFields._DisableSkirt):
                case nameof(_Debug) + "." + nameof(DebugFields._UniformTiles):
                    Rebuild();
                    break;
                case nameof(_Debug) + "." + nameof(DebugFields._DrawRendererBounds):
                    foreach (var chunk in Chunks) chunk._DrawRenderBounds = _Debug._DrawRendererBounds;
                    break;
            }
        }
    }
}

#endif
