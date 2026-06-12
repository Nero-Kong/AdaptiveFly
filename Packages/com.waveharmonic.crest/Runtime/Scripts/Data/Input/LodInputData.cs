// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using System;
using UnityEngine;
using UnityEngine.Rendering;
using WaveHarmonic.Crest.Internal;

namespace WaveHarmonic.Crest
{
    [AttributeUsage(AttributeTargets.Class)]
    sealed class ForLodInput : Attribute
    {
        public readonly Type _Type;
        public readonly LodInputMode _Mode;

        public ForLodInput(Type type, LodInputMode mode)
        {
            _Type = type;
            _Mode = mode;
        }
    }

    /// <summary>
    /// Data storage for an input, pertinent to the associated input mode.
    /// </summary>
    [Serializable]
    public abstract partial class LodInputData : Versioned
    {
        [SerializeField, HideInInspector]
        internal LodInput _Input;

        private protected Rect _Rect;
        private protected Bounds _Bounds;
        private protected bool _RecalculateRect = true;
        private protected bool _RecalculateBounds = true;

        internal abstract bool IsEnabled { get; }
        internal abstract void OnEnable();
        internal abstract void OnDisable();
        internal abstract void Draw(Lod lod, Component component, CommandBuffer buffer, RenderTargetIdentifier target, int slice);
        internal abstract void RecalculateRect();
        internal abstract void RecalculateBounds();

        internal virtual bool HasHeightRange => true;

        internal Rect Rect
        {
            get
            {
                if (_RecalculateRect)
                {
                    RecalculateRect();
                    _RecalculateRect = false;
                }

                return _Rect;
            }
        }

        internal Bounds Bounds
        {
            get
            {
                if (_RecalculateBounds)
                {
                    RecalculateBounds();
                    _RecalculateBounds = false;
                }

                return _Bounds;
            }
        }

        // Warning: NotImplementedException is thrown for paint and texture types.
        internal Vector2 HeightRange
        {
            get
            {
                if (!HasHeightRange) return Vector2.zero;
                var bounds = Bounds;
                return new(bounds.min.y, bounds.max.y);
            }
        }

        private protected void RecalculateCulling()
        {
            _RecalculateRect = _RecalculateBounds = true;
        }

        internal virtual void OnUpdate()
        {
            if (_Input.transform.hasChanged)
            {
                RecalculateCulling();
            }
        }

        internal virtual void OnLateUpdate()
        {

        }

#if UNITY_EDITOR
        internal abstract void OnChange(string propertyPath, object previousValue);
        internal abstract bool InferMode(Component component, ref LodInputMode mode);
        internal virtual void Reset() { }
#endif
    }

    /// <summary>
    /// Modes that inputs can use. Not all inputs support all modes. Refer to the UI.
    /// </summary>
    [@GenerateDoc]
    public enum LodInputMode
    {
        /// <inheritdoc cref="Generated.LodInputMode.Unset"/>
        [Tooltip("Unset is the serialization default.\n\nThis will be replaced with the default mode automatically. Unset can also be used if something is invalid.")]
        Unset = 0,

        /// <inheritdoc cref="Generated.LodInputMode.Paint"/>
        [Tooltip("Hand-painted data by the user.")]
        Paint,

        /// <inheritdoc cref="Generated.LodInputMode.Spline"/>
        [Tooltip("Driven by a user created spline.")]
        Spline,

        /// <inheritdoc cref="Generated.LodInputMode.Renderer"/>
        [Tooltip("Attached 'Renderer' (mesh, particle or other) used to drive data.")]
        Renderer,

        /// <inheritdoc cref="Generated.LodInputMode.Primitive"/>
        [Tooltip("Driven by a mathematical primitive such as a cube or sphere.")]
        Primitive,

        /// <inheritdoc cref="Generated.LodInputMode.Global"/>
        [Tooltip("Covers the entire water area.")]
        Global,

        /// <inheritdoc cref="Generated.LodInputMode.Texture"/>
        [Tooltip("Data driven by a user provided texture.")]
        Texture,

        /// <inheritdoc cref="Generated.LodInputMode.Geometry"/>
        [Tooltip("Renders geometry using a default material.")]
        Geometry,
    }

    /// <summary>
    /// Blend presets for inputs.
    /// </summary>
    [@GenerateDoc]
    public enum LodInputBlend
    {
        /// <inheritdoc cref="Generated.LodInputBlend.Off"/>
        [Tooltip("No blending. Overwrites.")]
        Off,

        /// <inheritdoc cref="Generated.LodInputBlend.Additive"/>
        [Tooltip("Additive blending.")]
        Additive,

        /// <inheritdoc cref="Generated.LodInputBlend.Minimum"/>
        [Tooltip("Takes the minimum value.")]
        Minimum,

        /// <inheritdoc cref="Generated.LodInputBlend.Maximum"/>
        [Tooltip("Takes the maximum value.")]
        Maximum,

        /// <inheritdoc cref="Generated.LodInputBlend.Alpha"/>
        [Tooltip("Applies the inverse weight to the target.\n\nBasically overwrites what is already in the simulation.")]
        Alpha,

        /// <inheritdoc cref="Generated.LodInputBlend.AlphaClip"/>
        [Tooltip("Same as alpha except anything above zero will overwrite rather than blend.")]
        AlphaClip,
    }

    /// <summary>
    /// Primitive shapes.
    /// </summary>
    // Have this match UnityEngine.PrimitiveType.
    [@GenerateDoc]
    public enum LodInputPrimitive
    {
        /// <inheritdoc cref="Generated.LodInputPrimitive.Sphere"/>
        [Tooltip("Spheroid.")]
        Sphere = 0,

        /// <inheritdoc cref="Generated.LodInputPrimitive.Cube"/>
        [Tooltip("Cuboid.")]
        Cube = 3,

        /// <inheritdoc cref="Generated.LodInputPrimitive.Quad"/>
        [Tooltip("Quad.")]
        Quad = 5,
    }
}
