// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using System;
using UnityEngine;

namespace WaveHarmonic.Crest
{
    partial class WaterRenderer
    {
        const string k_SurfaceRendererObsoleteMessage = "This property can now be found on WaterRenderer.Surface";

        //
        // Fields
        //

        [Obsolete(k_SurfaceRendererObsoleteMessage)]
        [HideInInspector]
        [Tooltip("The water chunk renderers will have this layer.")]
        [@GenerateAPI(Getter.Custom, Setter.Custom)]
        [SerializeField]
        int _Layer = 4; // Water

        [Obsolete(k_SurfaceRendererObsoleteMessage)]
        [HideInInspector]
        [Tooltip("Material to use for the water surface.")]
        [@GenerateAPI(Getter.Custom, Setter.Custom)]
        [SerializeField]
        internal Material _Material = null;

        [Obsolete(k_SurfaceRendererObsoleteMessage)]
        [HideInInspector]
        [Tooltip("Underwater will copy from this material if set.\n\nUseful for overriding properties for the underwater effect. To see what properties can be overriden, see the disabled properties on the underwater material. This does not affect the surface.")]
        [@GenerateAPI(Getter.Custom, Setter.Custom)]
        [SerializeField]
        internal Material _VolumeMaterial = null;

        [Obsolete(k_SurfaceRendererObsoleteMessage)]
        [HideInInspector]
        [Tooltip("Template for water chunks as a prefab.\n\nThe only requirements are that the prefab must contain a MeshRenderer at the root and not a MeshFilter or WaterChunkRenderer. MR values will be overwritten where necessary and the prefabs are linked in edit mode.")]
        [SerializeField]
        internal GameObject _ChunkTemplate;

        [Obsolete(k_SurfaceRendererObsoleteMessage)]
        [HideInInspector]
        [Tooltip("Have the water surface cast shadows for albedo (both foam and custom).")]
        [@GenerateAPI(Getter.Custom, Setter.Custom)]
        [SerializeField]
        internal bool _CastShadows;

        [Obsolete(k_SurfaceRendererObsoleteMessage)]
        [HideInInspector]
        [Tooltip("Whether 'Water Body' components will cull the water tiles.\n\nDisable if you want to use the 'Material Override' feature and still have an ocean.")]
        [@GenerateAPI(Getter.Custom, Setter.Custom)]
        [SerializeField]
        bool _WaterBodyCulling = true;

        [Obsolete(k_SurfaceRendererObsoleteMessage)]
        [HideInInspector]
        [Tooltip("How many frames to distribute the chunk bounds calculation.\n\nThe chunk bounds are calculated per frame to ensure culling is correct when using inputs that affect displacement. Some performance can be saved by distributing the load over several frames. The higher the frames, the longer it will take - lowest being instant.")]
        [@Range(1, 30, Range.Clamp.Minimum)]
        [@GenerateAPI(Getter.Custom, Setter.Custom)]
        [SerializeField]
        int _TimeSliceBoundsUpdateFrameCount = 1;

        [Obsolete(k_SurfaceRendererObsoleteMessage)]
        [HideInInspector]
        [Tooltip("How to handle self-intersections of the water surface.\n\nThey can be caused by choppy waves which can cause a flipped underwater effect. When not using the portals/volumes, this fix is only applied when within 2 metres of the water surface. Automatic will disable the fix if portals/volumes are used which is the recommend setting.")]
        [SerializeField]
        internal SurfaceRenderer.SurfaceSelfIntersectionFixMode _SurfaceSelfIntersectionFixMode = SurfaceRenderer.SurfaceSelfIntersectionFixMode.Automatic;

        [Obsolete(k_SurfaceRendererObsoleteMessage)]
        [HideInInspector]
        [Tooltip("Whether to allow sorting using the render queue.\n\nIf you need to change the minor part of the render queue (eg +100), then enable this option. As a side effect, it will also disable the front-to-back rendering optimization for Crest. This option does not affect changing the major part of the render queue (eg AlphaTest, Transparent), as that is always allowed.\n\nRender queue sorting is required for some third-party integrations.")]
        [@GenerateAPI(Getter.Custom, Setter.Custom)]
        [SerializeField]
        bool _AllowRenderQueueSorting;


        //
        // API
        //

        [Obsolete]
        int GetLayer()
        {
            return Surface.Layer;
        }

        [Obsolete]
        void SetLayer(int previous, int current)
        {
            Surface.Layer = current;
        }

        [Obsolete]
        Material GetMaterial()
        {
            return Surface.Material;
        }

        [Obsolete]
        void SetMaterial(Material previous, Material current)
        {
            Surface.Material = current;
        }

        [Obsolete]
        Material GetVolumeMaterial()
        {
            return Surface.VolumeMaterial;
        }

        [Obsolete]
        void SetVolumeMaterial(Material previous, Material current)
        {
            Surface.VolumeMaterial = current;
        }

        [Obsolete]
        bool GetCastShadows()
        {
            return Surface.CastShadows;
        }

        [Obsolete]
        void SetCastShadows(bool previous, bool current)
        {
            Surface.CastShadows = current;
        }

        [Obsolete]
        bool GetWaterBodyCulling()
        {
            return Surface.WaterBodyCulling;
        }

        [Obsolete]
        void SetWaterBodyCulling(bool previous, bool current)
        {
            Surface.WaterBodyCulling = current;
        }

        [Obsolete]
        int GetTimeSliceBoundsUpdateFrameCount()
        {
            return Surface.TimeSliceBoundsUpdateFrameCount;
        }

        [Obsolete]
        void SetTimeSliceBoundsUpdateFrameCount(int previous, int current)
        {
            Surface.TimeSliceBoundsUpdateFrameCount = current;
        }

        [Obsolete]
        bool GetAllowRenderQueueSorting()
        {
            return Surface.AllowRenderQueueSorting;
        }

        [Obsolete]
        void SetAllowRenderQueueSorting(bool previous, bool current)
        {
            Surface.AllowRenderQueueSorting = current;
        }
    }
}
