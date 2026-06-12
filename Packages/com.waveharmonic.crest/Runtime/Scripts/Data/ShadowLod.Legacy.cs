// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;
using UnityEngine.Rendering;

namespace WaveHarmonic.Crest
{
    partial class ShadowLod
    {
        internal bool ShouldRender(Camera camera)
        {
            if (!_Enabled)
            {
                return false;
            }

            // Even though volume also uses shadows, it only makes sense with a surface.
            if (!_Water._ActiveModules.HasFlag(WaterRenderer.ActiveModules.Surface))
            {
                return false;
            }

            // Only sample shadows for the main camera.
            if (_Water.IsSingleViewpointMode && _Water.Viewer != camera)
            {
                return false;
            }

            return true;
        }

        internal void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            // TODO: refactor this similar to MaskRenderer.
#if d_UnityURP
            if (RenderPipelineHelper.IsUniversal)
            {
                SampleShadowsURP.EnqueuePass(context, camera);
                return;
            }
#endif

            if (!RenderPipelineHelper.IsLegacy)
            {
                return;
            }

            CopyShadowMapBuffer?.Clear();

#if UNITY_EDITOR
            // Do not execute when editor is not active to conserve power and prevent possible leaks.
            if (!UnityEditorInternal.InternalEditorUtility.isApplicationActive)
            {
                return;
            }
#endif

            if (CopyShadowMapBuffer != null)
            {
                if (_Light != null)
                {
                    // Calling this in OnPreRender was too late to be executed in the same frame.
                    _Light.RemoveCommandBuffer(LightEvent.BeforeScreenspaceMask, CopyShadowMapBuffer);
                    _Light.AddCommandBuffer(LightEvent.BeforeScreenspaceMask, CopyShadowMapBuffer);
                }

                // Disable for XR SPI otherwise input will not have correct world position.
                Rendering.BIRP.DisableXR(CopyShadowMapBuffer, camera);

                BuildCommandBuffer(_Water, CopyShadowMapBuffer);

                // Restore XR SPI as we cannot rely on remaining pipeline to do it for us.
                Rendering.BIRP.EnableXR(CopyShadowMapBuffer, camera);
            }
        }

        internal void OnEndCameraRendering(Camera camera)
        {
            // Method is only called for either single viewpoint mode or separate viewpoints.
            if (_Water.IsMultipleViewpointMode)
            {
                StoreCameraData(camera);
            }

            if (!RenderPipelineHelper.IsLegacy)
            {
                return;
            }

#if UNITY_EDITOR
            // Do not execute when editor is not active to conserve power and prevent possible leaks.
            if (!UnityEditorInternal.InternalEditorUtility.isApplicationActive)
            {
                CopyShadowMapBuffer?.Clear();
                return;
            }
#endif

            // CBs added to a light are executed for every camera, but the LOD data is only
            // supports a single camera. Removing the CB after the camera renders restricts the
            // CB to one camera. Careful of recursive rendering for planar reflections, as it
            // executes a camera within this camera's frame.
            if (_Light != null && CopyShadowMapBuffer != null)
            {
                _Light.RemoveCommandBuffer(LightEvent.BeforeScreenspaceMask, CopyShadowMapBuffer);
            }
        }
    }
}
