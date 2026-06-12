// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;
using UnityEngine.Events;
using WaveHarmonic.Crest.Internal;

namespace WaveHarmonic.Crest
{
    /// <summary>
    /// What transform to use for queries.
    /// </summary>
    [@GenerateDoc]
    public enum QuerySource
    {
        /// <inheritdoc cref="Generated.QuerySource.Transform"/>
        [Tooltip("This game object's transform.")]
        Transform,

        /// <inheritdoc cref="Generated.QuerySource.Viewer"/>
        [Tooltip("The viewer's transform.\n\nThe viewer is the main camera the system uses.")]
        Viewer
    }

    /// <summary>
    /// Emits events (UnityEvents) based on the sampled water data.
    /// </summary>
    [AddComponentMenu(Constants.k_MenuPrefixScripts + "Query Events")]
    [@HelpURL("Manual/Events.html#query-events")]
    public sealed partial class QueryEvents : ManagedBehaviour<WaterRenderer>
    {
        [Tooltip("What transform should the queries be based on.\n\n\"Viewer\" will reuse queries already performed by the Water Renderer")]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        QuerySource _Source;

        [Tooltip("The viewer as the source of the queries.\n\nOnly needs to be set if using multiple viewpoints on the Water Renderer.")]
        [@Show(nameof(_Source), nameof(QuerySource.Viewer))]
        [@GenerateAPI]
        [@DecoratedField]
        [SerializeField]
        Camera _Viewer;

        [Tooltip(ICollisionProvider.k_LayerTooltip)]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        internal CollisionLayer _Layer;


        [Header("Distance From Water Surface")]

        [Tooltip("The minimum wavelength for queries.\n\nThe higher the value, the more smaller waves will be ignored when sampling the water surface.")]
        [@Enable(nameof(_Source), nameof(QuerySource.Transform))]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        float _MinimumWavelength = 1f;

        [@Label("Signed")]
        [Tooltip("Whether to keep the sign of the value (ie positive/negative).\n\nA positive value means the query point is above the surface, while a negative means it below the surface.")]
        [@Disable(nameof(_DistanceFromSurfaceUseCurve))]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        bool _DistanceFromSurfaceSigned;

        [@Label("Maximum Distance")]
        [Tooltip("The maximum distance.\n\nAlways use a real distance in real units (ie not normalized).")]
        [@GenerateAPI]
        [UnityEngine.Serialization.FormerlySerializedAs("_MaximumDistance")]
        [@DecoratedField, SerializeField]
        float _DistanceFromSurfaceMaximum = 100f;

        [@Label("Use Curve")]
        [Tooltip("Whether to apply a curve to the distance.\n\nNormalizes and inverts the distance to be between zero and one, then applies a curve.")]
        [@GenerateAPI]
        [UnityEngine.Serialization.FormerlySerializedAs("_NormaliseDistance")]
        [@DecoratedField, SerializeField]
        bool _DistanceFromSurfaceUseCurve = true;

        [@Label("Curve")]
        [Tooltip("Apply a curve to the distance.\n\nValues towards \"one\" means closer to the water surface.")]
        [@Enable(nameof(_DistanceFromSurfaceUseCurve))]
        [@GenerateAPI]
        [UnityEngine.Serialization.FormerlySerializedAs("_DistanceCurve")]
        [@DecoratedField, SerializeField]
        AnimationCurve _DistanceFromSurfaceCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);


        [Header("Distance From Water Edge")]

        [@Label("Signed")]
        [Tooltip("Whether to keep the sign of the value (ie positive/negative).\n\nA positive value means the query point is over water, while a negative means it is over land.")]
        [@Disable(nameof(_DistanceFromEdgeUseCurve))]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        bool _DistanceFromEdgeSigned;

        [@Label("Maximum Distance")]
        [Tooltip("The maximum distance.\n\nAlways use a real distance in real units (ie not normalized).")]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        float _DistanceFromEdgeMaximum = 100f;

        [@Label("Use Curve")]
        [Tooltip("Apply a curve to the distance.\n\nNormalizes and inverts the distance to be between zero and one, then applies a curve.")]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        bool _DistanceFromEdgeUseCurve = true;

        [@Label("Curve")]
        [Tooltip("Apply a curve to the distance.\n\nValues towards \"one\" means closer to the water's edge.")]
        [@Enable(nameof(_DistanceFromEdgeUseCurve))]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        AnimationCurve _DistanceFromEdgeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);


        [Header("Events")]

        [Tooltip("Triggers when game object goes below water surface.\n\nTriggers once per state change.")]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        UnityEvent _OnBelowWater = new();

        [Tooltip("Triggers when game object goes above water surface.\n\nTriggers once per state change.")]
        [@GenerateAPI]
        [SerializeField]
        UnityEvent _OnAboveWater = new();

        [Tooltip("Sends the distance from the water surface.")]
        [@GenerateAPI]
        [UnityEngine.Serialization.FormerlySerializedAs("_DistanceFromWater")]
        [SerializeField]
        internal UnityEvent<float> _DistanceFromSurface = new();

        [Tooltip("Sends the distance from the water's edge.")]
        [@GenerateAPI]
        [SerializeField]
        internal UnityEvent<float> _DistanceFromEdge = new();

        bool HasOnBelowWater => OnBelowWater != null || !_OnBelowWater.IsEmpty();
        bool HasOnAboveWater => OnAboveWater != null || !_OnAboveWater.IsEmpty();
        bool HasDistanceFromSurface => DistanceFromSurface != null || !_DistanceFromSurface.IsEmpty();
        bool HasDistanceFromEdge => DistanceFromEdge != null || !_DistanceFromEdge.IsEmpty();

        // Store state
        bool _IsAboveSurface = false;
        bool _IsFirstUpdate = true;
        readonly SampleCollisionHelper _SampleHeightHelper = new();
        readonly SampleDepthHelper _SampleDepthHelper = new();

        private protected override System.Action<WaterRenderer> OnUpdateMethod => OnUpdate;
        void OnUpdate(WaterRenderer water)
        {
            // Transform requires queries which need to be in Update.
            if (_Source != QuerySource.Transform) return;
            SendDistanceFromSurface(water);
            SendDistanceFromEdge(water);
        }

        private protected override System.Action<WaterRenderer> OnLateUpdateMethod => OnLateUpdate;
        void OnLateUpdate(WaterRenderer water)
        {
            // Viewer is set between Update and LateUpdate.
            if (_Source != QuerySource.Viewer) return;
            SendDistanceFromSurface(water);
            SendDistanceFromEdge(water);
        }

        void SendDistanceFromSurface(WaterRenderer water)
        {
            if (!HasDistanceFromSurface && !HasOnAboveWater && !HasOnBelowWater)
            {
                return;
            }

            var distance = water.ViewerHeightAboveWater;

            if (water.MultipleViewpoints && (_Viewer == null || !water.GetViewerHeightAboveWater(_Viewer, out distance)))
            {
                return;
            }

            if (_Source == QuerySource.Transform)
            {
                if (!_SampleHeightHelper.SampleHeight(transform.position, out var height, minimumLength: 2f * _MinimumWavelength, _Layer)) return;
                distance = transform.position.y - height;
            }

            var isAboveSurface = distance > 0;

            // Has the below/above water surface state changed?
            if (_IsAboveSurface != isAboveSurface || _IsFirstUpdate)
            {
                _IsAboveSurface = isAboveSurface;
                _IsFirstUpdate = false;

                if (_IsAboveSurface)
                {
                    _OnAboveWater?.Invoke();
                    OnAboveWater?.Invoke();
                }
                else
                {
                    _OnBelowWater?.Invoke();
                    OnBelowWater?.Invoke();
                }
            }

            // Save some processing when not being used.
            if (HasDistanceFromSurface)
            {
                distance = Mathf.Clamp(distance, -_DistanceFromSurfaceMaximum, _DistanceFromSurfaceMaximum);

                // Throw away sign when using a curve. Cannot think of a use case for negative
                // normalized numbers.
                if (!_DistanceFromSurfaceSigned || _DistanceFromSurfaceUseCurve)
                {
                    distance = Mathf.Abs(distance);
                }

                if (_DistanceFromSurfaceUseCurve)
                {
                    // Normalize for the curve. Invert so towards one is closer to target.
                    distance = _DistanceFromSurfaceCurve.Evaluate(1f - distance / _DistanceFromSurfaceMaximum);
                }

                _DistanceFromSurface?.Invoke(distance);
                DistanceFromSurface?.Invoke(distance);
            }
        }

        void SendDistanceFromEdge(WaterRenderer water)
        {
            // No events to process.
            if (!HasDistanceFromEdge)
            {
                return;
            }

            var distance = water.ViewerDistanceToShoreline;

            if (water.MultipleViewpoints && (_Viewer == null || !water.GetViewerDistanceToShoreline(_Viewer, out distance)))
            {
                return;
            }

            if (_Source == QuerySource.Transform)
            {
                if (!_SampleDepthHelper.SampleDistanceToWaterEdge(transform.position, out distance))
                {
                    return;
                }
            }

            distance = Mathf.Clamp(distance, -_DistanceFromEdgeMaximum, _DistanceFromEdgeMaximum);

            // Throw away sign when using a curve. Cannot think of a use case for negative
            // normalized numbers.
            if (!_DistanceFromEdgeSigned || _DistanceFromEdgeUseCurve)
            {
                distance = Mathf.Abs(distance);
            }

            if (_DistanceFromEdgeUseCurve)
            {
                // Normalize for the curve. Invert so towards one is closer to target.
                distance = _DistanceFromEdgeCurve.Evaluate(1f - distance / _DistanceFromEdgeMaximum);
            }

            _DistanceFromEdge?.Invoke(distance);
            DistanceFromEdge?.Invoke(distance);
        }
    }
}
