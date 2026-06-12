// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;
using UnityEngine.Rendering;
using WaveHarmonic.Crest.Internal;

namespace WaveHarmonic.Crest
{
    partial class WaterRenderer
    {
        void ExecuteLighting(ScriptableRenderContext context, Camera camera)
        {
            var sun = PrimaryLight;
            var useFallback = sun != null && sun.isActiveAndEnabled && (RenderPipelineHelper.IsHighDefinition || sun.bakingOutput.isBaked);
            Helpers.SetGlobalBoolean(ShaderIDs.s_PrimaryLightFallback, useFallback);

            if (useFallback)
            {
                var direction = -sun.transform.forward;
                var intensity = Color.clear;

                // Adapted from Helpers.FinalColor.
                var linear = GraphicsSettings.lightsUseLinearIntensity;
                var color = linear ? sun.color.linear : sun.color;
                color *= sun.intensity;
                if (linear && sun.useColorTemperature) color *= Mathf.CorrelatedColorTemperatureToRGB(sun.colorTemperature);
                if (!linear) color = color.MaybeLinear();
                intensity = linear ? color.MaybeGamma() : color;

#if d_UnityHDRP
                if (RenderPipelineHelper.IsHighDefinition)
                {
                    ApplyAtmosphericAttenuation(camera, sun, direction, intensity);
                }
#endif

                Shader.SetGlobalVector(ShaderIDs.s_PrimaryLightDirection, direction);
                Shader.SetGlobalVector(ShaderIDs.s_PrimaryLightIntensity, intensity);
            }

            if (RenderPipelineHelper.IsLegacy)
            {
                OnBeginCameraRenderingLegacy(camera);
            }
        }
    }
}
