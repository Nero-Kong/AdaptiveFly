// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;
using WaveHarmonic.Crest.Internal;

namespace WaveHarmonic.Crest
{
    /// <summary>
    /// Samples water surface shape - displacement, height, normal, velocity.
    /// </summary>
    sealed class CollisionQuery : QueryBase, ICollisionProvider
    {
        public CollisionQuery(WaterRenderer water) : base(water.AnimatedWavesLod) { }
        protected override int Kernel => 0;

        public int Query(int ownerHash, float minSpatialLength, Vector3[] queryPoints, Vector3[] resultDisplacements, Vector3[] resultNormals, Vector3[] resultVelocities, CollisionLayer layer = CollisionLayer.Everything, Vector3? center = null)
        {
            var result = (int)QueryStatus.OK;

            if (!UpdateQueryPoints(ownerHash, minSpatialLength, queryPoints, resultNormals != null ? queryPoints : null))
            {
                result |= (int)QueryStatus.PostFailed;
            }

            if (!RetrieveResults(ownerHash, resultDisplacements, null, resultNormals))
            {
                result |= (int)QueryStatus.RetrieveFailed;
            }

            if (resultVelocities != null)
            {
                result |= CalculateVelocities(ownerHash, resultVelocities);
            }

            return result;
        }

        public int Query(int ownerHash, float minimumSpatialLength, Vector3[] queryPoints, float[] resultHeights, Vector3[] resultNormals, Vector3[] resultVelocities, CollisionLayer layer = CollisionLayer.Everything, Vector3? center = null)
        {
            var result = (int)QueryStatus.OK;

            if (!UpdateQueryPoints(ownerHash, minimumSpatialLength, queryPoints, resultNormals != null ? queryPoints : null))
            {
                result |= (int)QueryStatus.PostFailed;
            }

            if (!RetrieveResults(ownerHash, null, resultHeights, resultNormals))
            {
                result |= (int)QueryStatus.RetrieveFailed;
            }

            if (resultVelocities != null)
            {
                result |= CalculateVelocities(ownerHash, resultVelocities);
            }

            return result;
        }
    }

    sealed class CollisionQueryPerCamera : QueryPerCamera<CollisionQueryWithPasses>, ICollisionProvider
    {
        public CollisionQueryPerCamera() : base(WaterRenderer.Instance) { }
        public CollisionQueryPerCamera(WaterRenderer water) : base(water) { }

        public int Query(int hash, float minimumLength, Vector3[] points, float[] heights, Vector3[] normals, Vector3[] velocities, CollisionLayer layer = CollisionLayer.Everything, Vector3? center = null)
        {
            if (_Water.IsSeparateViewpointCameraLoop)
            {
                return _Providers[_Water.CurrentCamera].Query(hash, minimumLength, points, heights, normals, velocities, layer, center);
            }

            var lastStatus = -1;
            var lastDistance = Mathf.Infinity;

            var newCenter = FindCenter(points, center);

            foreach (var provider in _Providers)
            {
                var camera = provider.Key;

                if (!_Water.ShouldExecuteQueries(camera))
                {
                    continue;
                }

                var distance = (newCenter - camera.transform.position.XZ()).sqrMagnitude;

                if (lastStatus == (int)QueryBase.QueryStatus.OK && lastDistance < distance)
                {
                    continue;
                }

                var status = provider.Value.Query(hash, minimumLength, points, heights, normals, velocities, layer, center);

                if (lastStatus < 0 || status == (int)QueryBase.QueryStatus.OK)
                {
                    lastStatus = status;
                    lastDistance = distance;
                }
            }

            return lastStatus;
        }

        public int Query(int hash, float minimumLength, Vector3[] points, Vector3[] displacements, Vector3[] normals, Vector3[] velocities, CollisionLayer layer = CollisionLayer.Everything, Vector3? center = null)
        {
            if (_Water.IsSeparateViewpointCameraLoop)
            {
                return _Providers[_Water.CurrentCamera].Query(hash, minimumLength, points, displacements, normals, velocities, layer, center);
            }

            var lastStatus = -1;
            var lastDistance = Mathf.Infinity;

            var newCenter = FindCenter(points, center);

            foreach (var provider in _Providers)
            {
                var camera = provider.Key;

                if (!_Water.ShouldExecuteQueries(camera))
                {
                    continue;
                }

                var distance = (newCenter - camera.transform.position.XZ()).sqrMagnitude;

                if (lastStatus == (int)QueryBase.QueryStatus.OK && lastDistance < distance)
                {
                    continue;
                }

                var status = provider.Value.Query(hash, minimumLength, points, displacements, normals, velocities, layer, center);

                if (lastStatus < 0 || status == (int)QueryBase.QueryStatus.OK)
                {
                    lastStatus = status;
                    lastDistance = distance;
                }
            }

            return lastStatus;
        }

        public void SendReadBack(WaterRenderer water, CollisionLayers layers)
        {
            _Providers[water.CurrentCamera].SendReadBack(water, layers);
        }

        public void UpdateQueries(WaterRenderer water, CollisionLayer layer)
        {
            _Providers[water.CurrentCamera].UpdateQueries(water, layer);
        }
    }

    sealed class CollisionQueryWithPasses : ICollisionProvider, IQueryable
    {
        readonly CollisionQuery _AnimatedWaves;
        readonly CollisionQuery _DynamicWaves;
        readonly CollisionQuery _Displacement;
        readonly WaterRenderer _Water;

        public int ResultGuidCount => _AnimatedWaves.ResultGuidCount + _DynamicWaves.ResultGuidCount + _Displacement.ResultGuidCount;
        public int RequestCount => _AnimatedWaves.RequestCount + _DynamicWaves.RequestCount + _Displacement.RequestCount;
        public int QueryCount => _AnimatedWaves.QueryCount + _DynamicWaves.QueryCount + _Displacement.QueryCount;

        public CollisionQueryWithPasses()
        {
            _Water = WaterRenderer.Instance;
            _AnimatedWaves = new(_Water);
            _DynamicWaves = new(_Water);
            _Displacement = new(_Water);
        }

        public CollisionQueryWithPasses(WaterRenderer water)
        {
            _Water = water;
            _AnimatedWaves = new(water);
            _DynamicWaves = new(water);
            _Displacement = new(water);
        }

        // Gets the provider for the given layer, falling back to previous layer when needed.
        CollisionQuery GetProvider(CollisionLayer layer)
        {
            var layers = _Water.AnimatedWavesLod._CollisionLayers;

            // Displacement is the fallback if there are no layers (ie single layer).
            if (layers == CollisionLayers.Nothing)
            {
                return _Displacement;
            }

            var everything = layer == CollisionLayer.Everything;

            // Displacement is the final layer, if present.
            if (everything && layers.HasFlag(CollisionLayers.Displacement))
            {
                return _Displacement;
            }

            // Chosen/fallback to Dynamic Waves.
            if ((everything || layer >= CollisionLayer.AfterDynamicWaves) &&
                layers.HasFlag(CollisionLayers.DynamicWaves) && _Water.DynamicWavesLod.Enabled)
            {
                return _DynamicWaves;
            }

            // If not single layer, this is always present.
            return _AnimatedWaves;
        }

        public int Query(int hash, float minimumLength, Vector3[] points, float[] heights, Vector3[] normals, Vector3[] velocities, CollisionLayer layer = CollisionLayer.Everything, Vector3? center = null)
        {
            return GetProvider(layer).Query(hash, minimumLength, points, heights, normals, velocities);
        }

        public int Query(int hash, float minimumLength, Vector3[] points, Vector3[] displacements, Vector3[] normals, Vector3[] velocities, CollisionLayer layer = CollisionLayer.Everything, Vector3? center = null)
        {
            return GetProvider(layer).Query(hash, minimumLength, points, displacements, normals, velocities);
        }

        public void UpdateQueries(WaterRenderer water, CollisionLayer layer)
        {
            switch (layer)
            {
                case CollisionLayer.Everything: _Displacement.UpdateQueries(water); break;
                case CollisionLayer.AfterAnimatedWaves: _AnimatedWaves.UpdateQueries(water); break;
                case CollisionLayer.AfterDynamicWaves: _DynamicWaves.UpdateQueries(water); break;
            }
        }

        public void UpdateQueries(WaterRenderer water)
        {
            _Displacement.UpdateQueries(water);
        }

        public void SendReadBack(WaterRenderer water, CollisionLayers layers)
        {
            // Will only submit readback if there are queries.
            _AnimatedWaves.SendReadBack(water);
            _DynamicWaves.SendReadBack(water);
            _Displacement.SendReadBack(water);
        }

        public void SendReadBack(WaterRenderer water)
        {
            _Displacement.SendReadBack(water);
        }

        public void CleanUp()
        {
            _AnimatedWaves.CleanUp();
            _DynamicWaves.CleanUp();
            _Displacement.CleanUp();
        }

        public void Initialize(WaterRenderer water)
        {

        }
    }

    // These are required because of collision layer.
    static partial class Extensions
    {
        public static void UpdateQueries(this ICollisionProvider self, WaterRenderer water, CollisionLayer layer)
        {
            if (self is CollisionQueryPerCamera a)
            {
                a.UpdateQueries(water, layer);
            }
            else if (self is CollisionQueryWithPasses b)
            {
                b.UpdateQueries(water, layer);
            }
            else if (self is ICollisionProvider.NoneProvider c)
            {

            }
            else
            {
                Debug.LogError("Crest: no valid query provider. Report this to developers!");
            }
        }
        public static void UpdateQueries(this ICollisionProvider self, WaterRenderer water) => (self as IQueryable)?.UpdateQueries(water);
        public static void SendReadBack(this ICollisionProvider self, WaterRenderer water, CollisionLayers layer)
        {
            if (self is CollisionQueryPerCamera a)
            {
                a.SendReadBack(water, layer);
            }
            else if (self is CollisionQueryWithPasses b)
            {
                b.SendReadBack(water, layer);
            }
            else if (self is ICollisionProvider.NoneProvider c)
            {

            }
            else
            {
                Debug.LogError("Crest: no valid query provider. Report this to developers!");
            }
        }
        public static void CleanUp(this ICollisionProvider self) => (self as IQueryable)?.CleanUp();
    }
}
