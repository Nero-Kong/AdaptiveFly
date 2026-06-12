// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

// Adapted from:
// https://github.com/keijiro/LightProbeUtility/blob/85c93577338e10a52dd53f263056de08d883337a/Assets/LightProbeUtility.cs

// With fixes from:
// https://github.com/keijiro/LightProbeUtility/pull/2

using UnityEngine;

namespace WaveHarmonic.Crest
{
    static class LightProbeUtility
    {
        static readonly int[] s_SHA =
        {
            Shader.PropertyToID("unity_SHAr"),
            Shader.PropertyToID("unity_SHAg"),
            Shader.PropertyToID("unity_SHAb")
        };

        static readonly int[] s_SHB =
        {
            Shader.PropertyToID("unity_SHBr"),
            Shader.PropertyToID("unity_SHBg"),
            Shader.PropertyToID("unity_SHBb")
        };

        static readonly int s_SHC = Shader.PropertyToID("unity_SHC");

        public static void SetSHCoefficients<T>(this T properties, Vector3 position) where T : IPropertyWrapper
        {
            LightProbes.GetInterpolatedProbe(position, null, out var sh);

            // Constant + Linear.
            for (var i = 0; i < 3; i++)
            {
                properties.SetVector(s_SHA[i], new(sh[i, 3], sh[i, 1], sh[i, 2], sh[i, 0] - sh[i, 6]));
            }

            // Quadratic polynomials.
            for (var i = 0; i < 3; i++)
            {
                properties.SetVector(s_SHB[i], new(sh[i, 4], sh[i, 5], sh[i, 6] * 3, sh[i, 7]));
            }

            // Final quadratic polynomial.
            properties.SetVector(s_SHC, new(sh[0, 8], sh[1, 8], sh[2, 8], 1));
        }
    }
}
