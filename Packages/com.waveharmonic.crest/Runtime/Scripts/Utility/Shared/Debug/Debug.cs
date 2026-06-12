// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;

namespace WaveHarmonic.Crest.Utility
{
    static class DebugUtility
    {
        public delegate void DrawLine(Vector3 position, Vector3 up, Color color, float duration);

        public static void DrawCross(DrawLine draw, Vector3 position, float r, Color color, float duration = 0f)
        {
            draw(position - Vector3.up * r, position + Vector3.up * r, color, duration);
            draw(position - Vector3.right * r, position + Vector3.right * r, color, duration);
            draw(position - Vector3.forward * r, position + Vector3.forward * r, color, duration);
        }

        public static void DrawCross(DrawLine draw, Vector3 position, Vector3 up, float r, Color color, float duration = 0f)
        {
            up.Normalize();
            var right = Vector3.Normalize(Vector3.Cross(up, Vector3.forward));
            var forward = Vector3.Cross(up, right);
            draw(position - up * r, position + up * r, color, duration);
            draw(position - right * r, position + right * r, color, duration);
            draw(position - forward * r, position + forward * r, color, duration);
        }
    }
}
