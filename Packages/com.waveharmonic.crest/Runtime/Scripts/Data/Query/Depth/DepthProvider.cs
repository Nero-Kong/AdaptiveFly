// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;

// Linter does not support mixing inheritdoc plus defining own parameters.
#pragma warning disable CS1573 // Parameter has no matching param tag in the XML comment (but other parameters do)

namespace WaveHarmonic.Crest
{
    /// <summary>
    /// Interface for an object that returns water depth and distance to water edge.
    /// </summary>
    public interface IDepthProvider : IQueryProvider
    {
        internal static NoneProvider None { get; } = new();

        internal static IDepthProvider Create(WaterRenderer water)
        {
            return water.IsMultipleViewpointMode ? new DepthQueryPerCamera(water) : new DepthQuery(water);
        }

        internal sealed class NoneProvider : IDepthProvider
        {
            public int Query(int _0, float _1, Vector3[] _2, Vector3[] result, Vector3? _3 = null)
            {
                if (result != null) System.Array.Clear(result, 0, result.Length);
                return 0;
            }
        }

        /// <summary>
        /// Query water depth data at a set of points.
        /// </summary>
        /// <param name="results">Water depth and distance to shoreline (XY respectively). Both are signed.</param>
        /// <inheritdoc cref="IQueryProvider.Query(int, float, Vector3[], int, Vector3?)" />
        int Query(int hash, float minimumLength, Vector3[] points, Vector3[] results, Vector3? center = null);
    }
}
