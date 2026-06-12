// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;
using WaveHarmonic.Crest.Internal;

namespace WaveHarmonic.Crest
{
    /// <summary>
    /// Renders the meniscus (waterline).
    /// </summary>
    [System.Serializable]
    public sealed partial class Meniscus : Versioned
    {
        [@Space(10)]

        [Tooltip("Whether the meniscus is enabled.")]
        [@GenerateAPI(Getter.Custom, Setter.Custom)]
        [@DecoratedField]
        [SerializeField]
        internal bool _Enabled = true;

        [Tooltip("Any camera with this layer in its culling mask will render the meniscus.")]
        [@Layer]
        [@GenerateAPI]
        [SerializeField]
        int _Layer = 4; // Water

        [Tooltip("The meniscus material.")]
        [@AttachMaterialEditor(order: 2)]
        [@MaterialField("Crest/Meniscus", name: "Meniscus", title: "Create Meniscus Material")]
        [@GenerateAPI(Setter.Custom)]
        [SerializeField]
        internal Material _Material;


        [@Heading("Advanced")]

        [Tooltip("Rules to exclude cameras from rendering the meniscus.\n\nThese are exclusion rules, so for all cameras, select Nothing. These rules are applied on top of the Layer rules.")]
        [@DecoratedField]
        [@GenerateAPI]
        [SerializeField]
        WaterCameraExclusion _CameraExclusions = WaterCameraExclusion.Hidden | WaterCameraExclusion.Reflection;

        WaterRenderer _Water;

        internal MeniscusRenderer Renderer { get; private set; }

        internal bool RequiresOpaqueTexture => Enabled && Material != null && Material.IsKeywordEnabled("d_Crest_Refraction");

        /// <summary>
        /// Disables rendering without de-allocating.
        /// </summary>
        public bool ForceRenderingOff { get; set; }

        internal void Enable()
        {
            Initialize(_Water);
            Renderer?.Enable();
        }

        internal void Disable()
        {
            Renderer?.Disable();
        }

        internal void Destroy()
        {
            Renderer?.Destroy();
            Renderer = null;
        }

        internal void OnActiveRenderPipelineTypeChanged()
        {
            Destroy();
            Initialize(_Water);
        }

        internal void Initialize(WaterRenderer water)
        {
            _Water = water;

            if (!Enabled)
            {
                return;
            }

#pragma warning disable format
#if d_UnityHDRP
            if (RenderPipelineHelper.IsHighDefinition)
            {
                Renderer ??= new MeniscusRendererHDRP(water, this);
            }
            else
#endif

#if d_UnityURP
            if (RenderPipelineHelper.IsUniversal)
            {
                Renderer ??= new MeniscusRendererURP(water, this);
            }
            else
#endif

            // Legacy
            {
                Renderer ??= new MeniscusRendererBIRP(water, this);
            }
#pragma warning restore format
        }

        internal bool ShouldRender(Camera camera)
        {
            if (!Enabled)
            {
                return false;
            }

            return Renderer.ShouldExecute(camera);
        }
    }

    // Getters/Setters
    partial class Meniscus
    {
        bool GetEnabled()
        {
            return _Enabled && _Material != null;
        }

        void SetEnabled(bool previous, bool current)
        {
            if (previous == current) return;
            if (_Water == null || !_Water.isActiveAndEnabled) return;
            if (_Enabled) Enable(); else Disable();
        }

        void SetMaterial(Material previous, Material current)
        {
            if (previous == current) return;
            if (_Water == null || !_Water.isActiveAndEnabled) return;
            if (previous == null) Enable(); else if (current == null) Disable();
        }
    }

    partial class Meniscus
    {
        internal abstract partial class MeniscusRenderer
        {
            private protected const string k_Draw = "Crest.DrawWater/Meniscus";

            private protected readonly WaterRenderer _Water;
            internal readonly Meniscus _Meniscus;

            public abstract void OnBeginCameraRendering(Camera camera);
            public abstract void OnEndCameraRendering(Camera camera);

            public MeniscusRenderer(WaterRenderer water, Meniscus meniscus)
            {
                _Water = water;
                _Meniscus = meniscus;
            }

            public virtual void Enable()
            {

            }

            public virtual void Disable()
            {

            }

            public virtual void Destroy()
            {

            }

            internal bool ShouldExecute(Camera camera)
            {
#if UNITY_EDITOR
                if (GL.wireframe)
                {
                    return false;
                }
#endif

                if (_Meniscus.ForceRenderingOff)
                {
                    return false;
                }

                if (!WaterRenderer.ShouldRender(camera, _Meniscus.Layer, _Meniscus._CameraExclusions))
                {
                    return false;
                }

                // Meniscus depends on both the surface and volume.
                if (!_Water._ActiveModules.HasFlag(WaterRenderer.ActiveModules.SurfaceAndVolume))
                {
                    return false;
                }

#if d_CrestPortals
                if (_Water._ActiveModules.HasFlag(WaterRenderer.ActiveModules.Portal))
                {
                    // Near surface check not compatible with portals.
                    return true;
                }
#endif

                _Water.UpdatePerCameraHeight(camera);

                // Only execute if near the surface.
                if (_Water._ViewerHeightAboveWaterPerCamera is > 2f or < -8f)
                {
                    return false;
                }

                return true;
            }

            internal void Execute<T>(Camera camera, T commands) where T : ICommandWrapper
            {
                var isFullScreenRequired = true;
                var isMasked = false;
                var passOffset = 1;

#if d_CrestPortals
                passOffset = (int)Portals.PortalRenderer.MeniscusPass.Length;

                if (_Water._ActiveModules.HasFlag(WaterRenderer.ActiveModules.Portal))
                {
                    isMasked = isFullScreenRequired = _Water._Portals.RenderMeniscus(commands, _Meniscus.Material);
                }
#endif

                if (isFullScreenRequired)
                {
                    var pass = isMasked ? 1 : 0;
                    var mpb = _Water.Surface._SurfaceDataMPB;

                    if (_Water._Underwater.UseLegacyMask)
                    {
                        pass += passOffset;
                        mpb = null;
                    }

                    commands.DrawFullScreenTriangle(_Meniscus.Material, pass, mpb);
                }
            }
        }
    }
}
