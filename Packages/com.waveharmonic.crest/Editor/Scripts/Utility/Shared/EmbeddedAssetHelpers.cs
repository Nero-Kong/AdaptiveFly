// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

// This file is subject to the Unity Companion License:
// https://github.com/Unity-Technologies/com.unity.cinemachine/blob/593fa283bee378322337e5d9f5a7b91331a45799/LICENSE.md

// Lovingly adapted from Cinemachine:
// https://github.com/Unity-Technologies/com.unity.cinemachine/blob/593fa283bee378322337e5d9f5a7b91331a45799/Editor/Utility/EmbeddedAssetHelpers.cs

using System.Reflection;
using UnityEditor;
using UnityEditor.VersionControl;
using UnityEngine;

namespace WaveHarmonic.Crest.Editor
{
    /// <summary>
    /// Interface for editors that receive an argument
    /// </summary>
    interface IEmbeddableEditor
    {
        void SetHostComponent(object host);
        void SetTypeOfHostComponent(System.Type hostType);
    }

    /// <summary>
    /// Helper for drawing embedded asset editors
    /// </summary>
    sealed class EmbeddedAssetEditor
    {
        /// <summary>
        /// Create in OnEnable()
        /// </summary>
        public EmbeddedAssetEditor()
        {
            _CreateButtonGUIContent = new("Create Asset", "Create a new shared settings asset");
        }

        /// <summary>
        /// Called after the asset editor is created, in case it needs
        /// to be customized
        /// </summary>
        public OnCreateEditorDelegate _OnCreateEditor;
        public delegate void OnCreateEditorDelegate(UnityEditor.Editor editor);

        /// <summary>
        /// Called when the asset being edited was changed by the user.
        /// </summary>
        public OnChangedDelegate _OnChanged;
        public delegate void OnChangedDelegate(System.Type type, Object obj);

        /// <summary>
        /// Free the resources in OnDisable()
        /// </summary>
        public void OnDisable()
        {
            DestroyEditor();
            Helpers.Destroy(_CreatedInstance);
        }

        /// <summary>
        /// Customize this after creation if you want
        /// </summary>
        public GUIContent _CreateButtonGUIContent;

        internal UnityEditor.Editor _Editor = null;

        System.Type _Type;

        Object _DefaultTarget;
        Object _CreatedInstance;
        FieldInfo _DefaultTargetField;

        const int k_IndentOffset = 3;

        public void DrawEditorCombo(Embedded embedded, GUIContent label, PropertyDrawer drawer, SerializedProperty property, string extension)
        {
            _Type = drawer.fieldInfo.FieldType;

            DrawEditorCombo
            (
                embedded,
                label,
                $"Create {property.displayName} Asset",
                $"{property.displayName.Replace(' ', '_')}",
                extension,
                string.Empty,
                false,
                property
            );
        }

        /// <summary>
        /// Call this from OnInspectorGUI.  Will draw the asset reference field, and
        /// the embedded editor, or a Create Asset button, if no asset is set.
        /// </summary>
        public void DrawEditorCombo
        (
            Embedded embedded,
            GUIContent label,
            string title,
            string defaultName,
            string extension,
            string message,
            bool indent,
            SerializedProperty property
        )
        {
            UpdateEditor(property, embedded);

            EditorGUI.BeginChangeCheck();
            var rect = AssetField(label, property, title, defaultName, extension, message);

            if (EditorGUI.EndChangeCheck())
            {
                property.serializedObject.ApplyModifiedProperties();
                UpdateEditor(property, embedded);
            }

            // Display embedded editor.
            if (_Editor != null)
            {
                var foldoutRect = new Rect(rect.x - k_IndentOffset, rect.y, rect.width + k_IndentOffset, EditorGUIUtility.singleLineHeight);
                property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, GUIContent.none, true);

                var canEditAsset = AssetDatabase.IsOpenForEdit(_Editor.target, StatusQueryOptions.UseCachedIfPossible);

                // We take the current GUI state into account to support attribute stacking.
                var guiEnabled = GUI.enabled;
                GUI.enabled = guiEnabled && canEditAsset;

                if (property.isExpanded)
                {
                    var level = EditorGUI.indentLevel;
                    EditorGUI.indentLevel = 0;
                    EditorGUILayout.BeginHorizontal();
                    // NOTE: Tweaked for current usage but probably will not work everywhere.
                    if (level > 0) GUILayout.Space(8 * (level + 2));
                    EditorGUILayout.BeginVertical(GUI.skin.box);

                    if ((_Editor.target.hideFlags & HideFlags.NotEditable) == 0)
                    {
                        EditorGUILayout.HelpBox("This is a shared asset. Changes made here will apply to all users of this asset.", MessageType.Info);
                    }
                    EditorGUI.BeginChangeCheck();
                    _Editor.OnInspectorGUI();
                    if (EditorGUI.EndChangeCheck() && (_OnChanged != null))
                        _OnChanged(_Type, property.objectReferenceValue);
                    EditorGUILayout.EndVertical();
                    EditorGUILayout.EndHorizontal();

                    if (embedded.BottomMargin > 0)
                    {
                        EditorGUILayout.Space(embedded.BottomMargin);
                    }
                }

                // Enable GUI so the checkout button works.
                GUI.enabled = true;

                if (_Editor.target != null)
                {
                    if (!canEditAsset && GUILayout.Button("Check out"))
                    {
                        var task = Provider.Checkout(AssetDatabase.GetAssetPath(_Editor.target), CheckoutMode.Asset);
                        task.Wait();
                    }
                }

                // Restore stacked GUI enabled state.
                GUI.enabled = guiEnabled;
            }
        }

        Rect AssetField
        (
            GUIContent label,
            SerializedProperty property,
            string title,
            string defaultName,
            string extension,
            string message
        )
        {
            return EditorHelpers.AssetField
            (
                _Type,
                label,
                property,
                EditorGUILayout.GetControlRect(true),
                title,
                defaultName,
                extension,
                message,
                x => ScriptableObject.CreateInstance(_Type)
            );
        }

        public void DestroyEditor()
        {
            if (_Editor != null)
            {
                Object.DestroyImmediate(_Editor);
                _Editor = null;
            }
        }

        public void UpdateEditor(SerializedProperty property, Embedded embedded)
        {
            var target = property.objectReferenceValue;
            var hasDefaultField = !string.IsNullOrEmpty(embedded.DefaultPropertyName);

            if (target == null)
            {
                if (!hasDefaultField)
                {
                    if (_DefaultTarget == null)
                    {
                        _CreatedInstance = ScriptableObject.CreateInstance(_Type);
                        _DefaultTarget = _CreatedInstance;
                        _DefaultTarget.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
                    }
                }
                else
                {
                    if (_DefaultTargetField == null)
                    {
                        _DefaultTargetField = property.serializedObject.targetObject.GetType().GetField(embedded.DefaultPropertyName, Helpers.s_AnyMethod);
                    }

                    // Always call, as it is dynamic.
                    _DefaultTarget = (Object)_DefaultTargetField.GetValue(property.serializedObject.targetObject);
                }
            }

            if (target == null)
            {
                target = _DefaultTarget;
            }

            // Destroy the editor if target has changed.
            if (_Editor != null && _Editor.target != target)
            {
                DestroyEditor();
            }

            if (_Editor != null)
            {
                return;
            }

            // NOTE: This is triggered twice on asset switch for some reason.
            // Create editor if need one.
            if (target != null)
            {
                _Editor = UnityEditor.Editor.CreateEditor(target);

                // Pass through argument for editors that receive it
                if (property.serializedObject.targetObject != null && _Editor is IEmbeddableEditor editor)
                {
                    editor.SetHostComponent(property.serializedObject.targetObject);
                    editor.SetTypeOfHostComponent(property.serializedObject.targetObject.GetType());
                }

                _OnCreateEditor?.Invoke(_Editor);
            }
        }
    }
}
