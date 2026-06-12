// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;
using UnityEngine.Rendering;

namespace WaveHarmonic.Crest
{
    sealed class MaskRendererBIRP : MaskRenderer
    {
        CommandBuffer _Commands;

        public MaskRendererBIRP(WaterRenderer water) : base(water) { }

        public override void Enable()
        {
            base.Enable();
            Allocate();
        }


        public override void OnBeginCameraRendering(Camera camera)
        {
            if (!ShouldExecute(camera))
            {
                return;
            }


            _Commands ??= new()
            {
                name = UnderwaterRenderer.k_DrawMask,
            };

            _Water.UpdateMatrices(camera);

            var descriptor = Rendering.BIRP.GetCameraTargetDescriptor(camera);
            descriptor.useDynamicScale = camera.allowDynamicResolution;

            Allocate();

            ReAllocate(descriptor);
            Execute(camera, _Commands);

            // Handles both forward and deferred.
            camera.AddCommandBuffer(CameraEvent.BeforeGBuffer, _Commands);
            camera.AddCommandBuffer(CameraEvent.BeforeDepthTexture, _Commands);
        }

        public override void OnEndCameraRendering(Camera camera)
        {
            if (_Commands == null)
            {
                return;
            }

            camera.RemoveCommandBuffer(CameraEvent.BeforeGBuffer, _Commands);
            camera.RemoveCommandBuffer(CameraEvent.BeforeDepthTexture, _Commands);

            _Commands.Clear();
        }
    }
}
