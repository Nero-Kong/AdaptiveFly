// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;
using UnityEngine.Rendering;

namespace WaveHarmonic.Crest
{
    partial class Meniscus
    {
        internal sealed class MeniscusRendererBIRP : MeniscusRenderer
        {
            CommandBuffer _Commands;

            // NOTE: This will not work for recursive rendering.
            bool _CommandsRegistered;

            public MeniscusRendererBIRP(WaterRenderer water, Meniscus meniscus) : base(water, meniscus)
            {

            }

            public override void OnBeginCameraRendering(Camera camera)
            {
                if (!ShouldExecute(camera))
                {
                    return;
                }

                _Commands ??= new()
                {
                    name = k_Draw,
                };

                _Commands.Clear();

                Execute(camera, new CommandWrapper(_Commands));

                camera.AddCommandBuffer(CameraEvent.AfterForwardAlpha, _Commands);

                _CommandsRegistered = true;
            }

            public override void OnEndCameraRendering(Camera camera)
            {
                if (!_CommandsRegistered)
                {
                    return;
                }

                camera.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, _Commands);

                _CommandsRegistered = false;
            }
        }
    }
}
