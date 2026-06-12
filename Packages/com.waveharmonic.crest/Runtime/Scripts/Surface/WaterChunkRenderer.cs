// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;
using WaveHarmonic.Crest.Internal;

namespace WaveHarmonic.Crest
{
    interface IReportsHeight
    {
        bool ReportHeight(WaterRenderer water, ref Rect bounds, ref float minimum, ref float maximum);
    }

    interface IReportsDisplacement
    {
        bool ReportDisplacement(WaterRenderer water, ref Rect bounds, ref float horizontal, ref float vertical);
    }

    /// <summary>
    /// Sets shader parameters for each geometry tile/chunk.
    /// </summary>
#if !CREST_DEBUG
    [AddComponentMenu("")]
#endif
    [@ExecuteDuringEditMode]
    sealed partial class WaterChunkRenderer : ManagedBehaviour<WaterRenderer>
    {
        [SerializeField]
        internal bool _DrawRenderBounds = false;

        internal const string k_UpdateMeshBoundsMarker = "Crest.WaterChunkRenderer.UpdateMeshBounds";

        static readonly Unity.Profiling.ProfilerMarker s_UpdateMeshBoundsMarker = new(k_UpdateMeshBoundsMarker);

        internal Transform _Transform;
        internal Mesh _Mesh;
        public Renderer Rend { get; private set; }
        internal MaterialPropertyBlock _MaterialPropertyBlock;
        Matrix4x4 _CurrentObjectToWorld;
        Matrix4x4 _PreviousObjectToWorld;
        internal Material _MotionVectorMaterial;
        internal int _SortingOrder;
        internal int _SiblingIndex;

        internal Rect _UnexpandedBoundsXZ = new();
        public Rect UnexpandedBoundsXZ => _UnexpandedBoundsXZ;

        internal Bounds _LocalBounds;
        internal float _LocalScale;

        // WaterBody culling.
        internal bool _Culled;

        // Frustum visibility.
        internal bool _Visible;

        internal WaterRenderer _Water;

        public bool MaterialOverridden { get; set; }

        // We need to ensure that all water data has been bound for the mask to
        // render properly - this is something that needs to happen irrespective
        // of occlusion culling because we need the mask to render as a
        // contiguous surface.
        internal bool _WaterDataHasBeenBound = true;

        internal int _LodIndex = -1;


        // There is a 1-frame delay with Initialized in edit mode due to setting
        // enableInEditMode in EditorApplication.update. This only really affect this
        // component as it is instantiate via script, and is partial driven externally.
        // So instead, call this after instantiation.
        internal void Initialize(int index, Renderer renderer, Mesh mesh)
        {
            _LodIndex = index;
            Rend = renderer;
            _Mesh = mesh;
            _Transform = transform;
        }

        private protected override void OnStart()
        {
            base.OnStart();

            UpdateMeshBounds();
        }

        internal void UpdateMeshBounds(WaterRenderer water, SurfaceRenderer surface)
        {
            _WaterDataHasBeenBound = false;

            var count = surface.TimeSliceBoundsUpdateFrameCount;

            // Time slice update to distribute the load.
            if (count <= 1 || !(_SiblingIndex % count != Time.frameCount % surface.Chunks.Count % count))
            {
                // This needs to be called on Update because the bounds depend on transform scale which can change. Also OnWillRenderObject depends on
                // the bounds being correct. This could however be called on scale change events, but would add slightly more complexity.
                UpdateMeshBounds();
            }
        }

        bool ShouldRender(bool culled)
        {
            // Is visible to camera.
            if (!_Visible)
            {
                return false;
            }

            // If including culling, is it culled.
            if (culled && _Culled)
            {
                return false;
            }

            return true;
        }

        internal void OnLateUpdate()
        {
            _PreviousObjectToWorld = _Water.Surface.PreviousObjectToWorld[_SiblingIndex];
            _CurrentObjectToWorld = _Transform.localToWorldMatrix;
            _Water.Surface.PreviousObjectToWorld[_SiblingIndex] = _CurrentObjectToWorld;
        }

        internal void RenderMotionVectors(SurfaceRenderer surface, Camera camera)
        {
            if (!ShouldRender(culled: true))
            {
                return;
            }

            // RenderMesh will copy properties immediately, thus we need them bound.
            if (!_WaterDataHasBeenBound)
            {
                Bind();
            }

            var material = MaterialOverridden ? _MotionVectorMaterial : surface._MotionVectorMaterial;

            var parameters = new RenderParams(material)
            {
                motionVectorMode = MotionVectorGenerationMode.Object,
                material = material,
                matProps = _MaterialPropertyBlock,
                worldBounds = Rend.bounds,
                layer = surface.Layer,
                renderingLayerMask = (uint)surface.Layer,
                receiveShadows = false,
                shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
                lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off,
                reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off,
                camera = camera,
            };

            Graphics.RenderMesh(parameters, _Mesh, 0, _CurrentObjectToWorld, _PreviousObjectToWorld);
        }

        void UpdateMeshBounds()
        {
            s_UpdateMeshBoundsMarker.Begin(this);

            var bounds = _LocalBounds;

            bounds = ComputeBounds(_Transform, bounds);

            _UnexpandedBoundsXZ = new(0, 0, bounds.size.x, bounds.size.z)
            {
                center = bounds.center.XZ(),
            };

            bounds = ExpandBoundsForDisplacements(_Transform, bounds);

            Rend.bounds = bounds;

            s_UpdateMeshBoundsMarker.End();
        }

        internal void Bind()
        {
            _MaterialPropertyBlock = _Water.Surface.PerCascadeMPB[_LodIndex];
            new PropertyWrapperMPB(_MaterialPropertyBlock).SetSHCoefficients(_Transform.position);
            Rend.SetPropertyBlock(_MaterialPropertyBlock);

            _WaterDataHasBeenBound = true;
        }

        void OnDestroy()
        {
            Helpers.Destroy(_Mesh);
            _Mesh = null;
        }

        // Called when visible to a camera
        void OnWillRenderObject()
        {
            if (Rend == null)
            {
                return;
            }

#if CREST_DEBUG
            if (_Water._Debug._VisualizeData)
            {
                Rend.sharedMaterial = _Water.Surface.VisualizeDataMaterial;
                MaterialOverridden = true;
            }
#endif

            if (!MaterialOverridden && Rend.sharedMaterial != _Water.Surface.Material)
            {
                Rend.sharedMaterial = _Water.Surface.Material;
                _MotionVectorMaterial = _Water.Surface._MotionVectorMaterial;
            }

            if (!_WaterDataHasBeenBound)
            {
                Bind();
            }
        }

        public Bounds ComputeBounds(Transform transform, Bounds bounds)
        {
            var extents = bounds.extents;
            var center = bounds.center;

            // Apply transform. Rotation already done.
            var scale = _LocalScale * _Water.Scale;
            extents.x *= scale;
            extents.z *= scale;
            center.x *= scale;
            center.z *= scale;
            center += transform.position;

            bounds.center = center;
            bounds.extents = extents;

            return bounds;
        }


        // this is called every frame because the bounds are given in world space and depend on the transform scale, which
        // can change depending on view altitude
        public Bounds ExpandBoundsForDisplacements(Transform transform, Bounds bounds)
        {
            var extents = bounds.extents;
            var center = bounds.center;

            var rect = _UnexpandedBoundsXZ;

            // Extend the kinematic bounds slightly to give room for dynamic waves.
            if (_Water._DynamicWavesLod.Enabled)
            {
                var settings = _Water.DynamicWavesLod.Settings;
                extents.x += settings._HorizontalDisplace;
                extents.y += settings._VerticalDisplacementCullingContributions;
                extents.z += settings._HorizontalDisplace;
            }

            // Extend bounds by local waves.
            {
                var horizontal = 0f;
                var vertical = 0f;

                foreach (var (key, input) in AnimatedWavesLod.s_Inputs)
                {
                    input.DisplacementReporter?.ReportDisplacement(_Water, ref rect, ref horizontal, ref vertical);
                }

                extents.x += horizontal;
                extents.y += vertical;
                extents.z += horizontal;
            }

            // Expand and offset bounds by height.
            {
                var minimum = 0f;
                var maximum = 0f;

                foreach (var (key, input) in LevelLod.s_Inputs)
                {
                    input.HeightReporter?.ReportHeight(_Water, ref rect, ref minimum, ref maximum);
                }

                extents.y += Mathf.Abs((minimum - maximum) * 0.5f);

                var offset = Mathf.Lerp(minimum, maximum, 0.5f);
                center.y += offset;
            }

            // Get XZ bounds. Doing this manually bypasses updating render bounds call.
            bounds.center = center;
            bounds.extents = extents;

            return bounds;
        }
    }

    static class BoundsHelper
    {
        internal static void DebugDraw(this Bounds b)
        {
            var xmin = b.min.x;
            var ymin = b.min.y;
            var zmin = b.min.z;
            var xmax = b.max.x;
            var ymax = b.max.y;
            var zmax = b.max.z;

            Debug.DrawLine(new(xmin, ymin, zmin), new(xmin, ymin, zmax));
            Debug.DrawLine(new(xmin, ymin, zmin), new(xmax, ymin, zmin));
            Debug.DrawLine(new(xmax, ymin, zmax), new(xmin, ymin, zmax));
            Debug.DrawLine(new(xmax, ymin, zmax), new(xmax, ymin, zmin));

            Debug.DrawLine(new(xmin, ymax, zmin), new(xmin, ymax, zmax));
            Debug.DrawLine(new(xmin, ymax, zmin), new(xmax, ymax, zmin));
            Debug.DrawLine(new(xmax, ymax, zmax), new(xmin, ymax, zmax));
            Debug.DrawLine(new(xmax, ymax, zmax), new(xmax, ymax, zmin));

            Debug.DrawLine(new(xmax, ymax, zmax), new(xmax, ymin, zmax));
            Debug.DrawLine(new(xmin, ymin, zmin), new(xmin, ymax, zmin));
            Debug.DrawLine(new(xmax, ymin, zmin), new(xmax, ymax, zmin));
            Debug.DrawLine(new(xmin, ymax, zmax), new(xmin, ymin, zmax));
        }
    }
}
