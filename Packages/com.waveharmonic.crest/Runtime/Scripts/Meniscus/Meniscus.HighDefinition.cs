// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#if d_UnityHDRP

using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace WaveHarmonic.Crest
{
    partial class Meniscus
    {
        internal sealed class MeniscusRendererHDRP : MeniscusRenderer
        {
            const string k_Name = "Meniscus";
            MeniscusCustomPass _CustomPass;
            GameObject _GameObject;

            public MeniscusRendererHDRP(WaterRenderer water, Meniscus meniscus) : base(water, meniscus)
            {

            }

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
                    ref _CustomPass,
                    k_Draw,
                    CustomPassInjectionPoint.BeforePostProcess,
                    priority: -1
                );

                _CustomPass._Renderer = this;
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

            sealed class MeniscusCustomPass : CustomPass
            {
                internal MeniscusRenderer _Renderer;

                protected override void Execute(CustomPassContext context)
                {
                    var camera = context.hdCamera.camera;

                    if (!_Renderer.ShouldExecute(camera))
                    {
                        return;
                    }

                    _Renderer.Execute(camera, new CommandWrapper(context.cmd));
                }
            }
        }
    }
}

#endif
