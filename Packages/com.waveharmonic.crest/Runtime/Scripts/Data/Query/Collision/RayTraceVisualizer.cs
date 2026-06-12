// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;
using WaveHarmonic.Crest.Internal;
using WaveHarmonic.Crest.Utility;

namespace WaveHarmonic.Crest
{
    /// <summary>
    /// Debug draw a line trace from this gameobjects position, in this gameobjects forward direction.
    /// </summary>
    [@ExecuteDuringEditMode]
    [AddComponentMenu(Constants.k_MenuPrefixDebug + "Ray Cast Visualizer")]
    sealed class RayTraceVisualizer : ManagedBehaviour<WaterRenderer>
    {
        readonly RayCastHelper _RayCast = new(50f, 2f);

        private protected override System.Action<WaterRenderer> OnUpdateMethod => OnUpdate;
        void OnUpdate(WaterRenderer water)
        {
            if (water.AnimatedWavesLod.Provider == null)
            {
                return;
            }

            // Even if only a single ray trace is desired, this still must be called every frame until it returns true
            if (_RayCast.RayCast(transform.position, transform.forward, out var dist))
            {
                var endPos = transform.position + transform.forward * dist;
                Debug.DrawLine(transform.position, endPos, Color.green);
                DebugUtility.DrawCross(Debug.DrawLine, endPos, 2f, Color.green, 0f);
            }
            else
            {
                Debug.DrawLine(transform.position, transform.position + transform.forward * 50f, Color.red);
            }
        }
    }
}
