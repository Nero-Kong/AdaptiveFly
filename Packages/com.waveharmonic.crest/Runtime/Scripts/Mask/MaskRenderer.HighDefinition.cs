// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#if d_UnityHDRP

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace WaveHarmonic.Crest
{
    sealed class MaskRendererHDRP : MaskRenderer
    {
        const string k_Name = "Water Mask";
        MaskCustomPass _MaskCustomPass;
        GameObject _GameObject;

        public MaskRendererHDRP(WaterRenderer water) : base(water) { }

        public override void Enable()
        {
            base.Enable();

            _GameObject = CustomPassHelpers.CreateOrUpdate
            (
                parent: _Water.Container.transform,
                k_Name,
                hide: !_Water._Debug._ShowHiddenObjects
            );

            CustomPassHelpers.CreateOrUpdate
            (
                _GameObject,
                ref _MaskCustomPass,
                UnderwaterRenderer.k_DrawMask,
                CustomPassInjectionPoint.BeforeRendering
            );

            _MaskCustomPass._MaskPass = this;
        }

        public override void Disable()
        {
            base.Disable();

            if (_GameObject != null)
            {
                // Will also trigger Cleanup below.
                _GameObject.SetActive(false);
            }
        }

        public override void OnBeginCameraRendering(Camera camera)
        {

        }

        public override void OnEndCameraRendering(Camera camera)
        {

        }

        sealed class MaskCustomPass : CustomPass
        {
            internal MaskRenderer _MaskPass;

            // Called when the custom pass object is enabled making it somewhat useless.
            protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
            {
                _MaskPass.Allocate();
            }

            protected override void Cleanup()
            {
                _MaskPass.Release();
            }

            protected override void Execute(CustomPassContext context)
            {
                var camera = context.hdCamera.camera;

                if (!_MaskPass.ShouldExecute(camera))
                {
                    return;
                }

                // HDRP does not need ReAllocate. But it is easier to also call Allocate here.
                // Allocating RTHandles outside the render loop raises an error. Seriously, do not
                // attempt to optmize this away.
                _MaskPass.Allocate();
                _MaskPass.Execute(camera, context.cmd);
            }
        }
    }
}

#endif // d_UnityHDRP
