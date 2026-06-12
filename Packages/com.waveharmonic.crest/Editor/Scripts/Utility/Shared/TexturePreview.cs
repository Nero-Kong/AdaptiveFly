// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using WaveHarmonic.Crest.Internal;

namespace WaveHarmonic.Crest.Editor
{
    abstract class TexturePreview : ObjectPreview
    {
        static readonly System.Reflection.MethodInfo s_DrawPreview = typeof(ObjectPreview).GetMethod("DrawPreview", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        static readonly object[] s_DrawPreviewArguments = new object[3];
        static readonly Object[] s_DrawPreviewTargets = new Object[1];

        public static TexturePreview s_ActiveInstance;
        public bool Open { get; private set; }

        UnityEditor.Editor _Editor;
        RenderTexture _RenderTexture;
        RenderTextureDescriptor _OriginalDescriptor = new();
        Texture _Current;
        protected int _Slice;
        bool _First = true;

        protected abstract Texture OriginalTexture { get; }
        protected virtual Texture ModifiedTexture { get; }

        Texture Texture => ModifiedTexture != null ? ModifiedTexture : OriginalTexture;

        protected virtual bool Flipped => false;

        // Preview complains if not a certain set of formats.
        bool Incompatible => !(GraphicsFormatUtility.IsIEEE754Format(Texture.graphicsFormat)
            || GraphicsFormatUtility.IsNormFormat(Texture.graphicsFormat));

        public TexturePreview() { }

        public override bool HasPreviewGUI()
        {
            if (Event.current != null && Event.current.type == EventType.Layout)
            {
                Open = false;
            }

            return OriginalTexture;
        }

        public override void Cleanup()
        {
            base.Cleanup();
            Object.DestroyImmediate(_Editor);
            if (_RenderTexture != null) _RenderTexture.Release();
            Object.DestroyImmediate(_RenderTexture);
        }

        public override void OnPreviewSettings()
        {
            if (_First && Event.current.type == EventType.Repaint && !Application.isPlaying)
            {
                // Solves on enter edit mode:
                // ArgumentException: Getting control 2's position in a group with only 2 controls when doing repaint
                _First = false;
                return;
            }

            if (_Editor != null) _Editor.OnPreviewSettings();
        }

        public override void OnPreviewGUI(Rect rect, GUIStyle background)
        {
            s_ActiveInstance = this;
            Open = true;

            // This check is in original.
            if (Event.current.type == EventType.Repaint)
            {
                background.Draw(rect, false, false, false, false);
            }

            if (Texture is Cubemap)
            {
                if (_Editor == null || _Editor.target != Texture)
                {
                    Object.DestroyImmediate(_Editor);
                    _Editor = UnityEditor.Editor.CreateEditor(Texture);
                }

                _Editor.DrawPreview(rect);
                return;
            }

            var descriptor = Texture.GetDescriptor();

            if (Helpers.RenderTextureNeedsUpdating(descriptor, _OriginalDescriptor))
            {
                _OriginalDescriptor = descriptor;

                if (Incompatible)
                {
                    descriptor.graphicsFormat = GraphicsFormat.R32G32B32A32_SFloat;
                }

                Helpers.SafeCreateRenderTexture(ref _RenderTexture, descriptor);
                _RenderTexture.Create();
                Object.DestroyImmediate(_Editor);
                // Raises both, but no way to avoid it:
                // | The targets array should not be used inside OnSceneGUI or OnPreviewGUI. Use the single target property instead.
                // | The serializedObject should not be used inside OnSceneGUI or OnPreviewGUI. Use the target property directly instead.
                _Editor = UnityEditor.Editor.CreateEditor(_RenderTexture);
                // Reset for incompatible copy.
                descriptor = _OriginalDescriptor;
            }

            // Name may change without texture changing (see SWS).
            _RenderTexture.name = Texture.name + " (Preview)";

            if (Incompatible)
            {
                var temporary = RenderTexture.GetTemporary(descriptor);
                Graphics.CopyTexture(Texture, temporary);
                Helpers.Blit(temporary, _RenderTexture);
                RenderTexture.ReleaseTemporary(temporary);
            }
            else
            {
                Graphics.CopyTexture(Texture, _RenderTexture);
            }

            s_DrawPreviewTargets[0] = _Editor.target;
            s_DrawPreviewArguments[0] = _Editor;
            s_DrawPreviewArguments[1] = rect;
            s_DrawPreviewArguments[2] = s_DrawPreviewTargets;

            // Use to be _Editor.DrawPreview(rect) but spammed errors with multiple selected.
            s_DrawPreview?.Invoke(null, s_DrawPreviewArguments);
        }

#if CREST_DEBUG
        public override void OnInteractivePreviewGUI(Rect rect, GUIStyle background)
        {
            OnPreviewGUI(rect, background);

            if (Texture is Cubemap)
            {
                return;
            }

            // Show pixel value in preview.
            _Slice = Development.Utility.GetPreviewSlice(_Editor, Texture);
            var color = Development.Utility.InspectPixel(rect, OriginalTexture, Flipped, _Slice);
            var text = Development.Utility.GetInspectPixelString(color, OriginalTexture);
            EditorGUI.DropShadowLabel(new Rect(rect.x, rect.y, rect.width, 40), text);
        }
#endif

        void Allocate(Texture texture)
        {
            // LOD with buffered data like foam will recreate every frame freezing controls.
            if (_Editor != null && _Current == Texture) return;
            _Current = texture;
            Object.DestroyImmediate(_Editor);
            _Editor = UnityEditor.Editor.CreateEditor(texture);
        }
    }
}
