// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;

namespace WaveHarmonic.Crest
{
    partial class WaterRenderer
    {
        private protected override int Version => Mathf.Max(base.Version, 2);

#pragma warning disable CS0618 // Type or member is obsolete
        private protected override void OnMigrate()
        {
            base.OnMigrate();

            if (_Version < 1)
            {
                Surface._Layer = _Layer;
                Surface._Material = _Material;
                Surface._VolumeMaterial = _VolumeMaterial;
                Surface._ChunkTemplate = _ChunkTemplate;
                Surface._CastShadows = _CastShadows;
                Surface._WaterBodyCulling = _WaterBodyCulling;
                Surface._TimeSliceBoundsUpdateFrameCount = _TimeSliceBoundsUpdateFrameCount;
                Surface._AllowRenderQueueSorting = _AllowRenderQueueSorting;
                Surface._SurfaceSelfIntersectionFixMode = _SurfaceSelfIntersectionFixMode;

                _DepthLod._IncludeTerrainHeight = false;
            }

            if (_Version < 2)
            {
                AnimatedWavesLod.QuerySource = (LodQuerySource)Mathf.Max(0, (int)AnimatedWavesLod.CollisionSource - 1);
            }
        }
#pragma warning restore CS0618 // Type or member is obsolete
    }
}
