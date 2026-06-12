// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace WaveHarmonic.Crest.Editor
{
    /// <summary>
    /// Provides general helper functions for the editor.
    /// </summary>
    static partial class EditorHelpers
    {
        internal static ComputeShader s_VisualizeNegativeValuesShader;
        internal static ComputeShader VisualizeNegativeValuesShader
        {
            get
            {
                if (s_VisualizeNegativeValuesShader == null)
                {
                    s_VisualizeNegativeValuesShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.waveharmonic.crest/Editor/Shaders/VisualizeNegativeValues.compute");
                }

                return s_VisualizeNegativeValuesShader;
            }
        }

        public static LayerMask LayerMaskField(string label, LayerMask layerMask)
        {
            // Adapted from: http://answers.unity.com/answers/1387522/view.html
            var temporary = EditorGUILayout.MaskField(
                label,
                UnityEditorInternal.InternalEditorUtility.LayerMaskToConcatenatedLayersMask(layerMask),
                UnityEditorInternal.InternalEditorUtility.layers);
            return UnityEditorInternal.InternalEditorUtility.ConcatenatedLayersMaskToLayerMask(temporary);
        }

        /// <summary>Attempts to get the scene view this camera is rendering.</summary>
        /// <returns>The scene view or null if not found.</returns>
        public static SceneView GetSceneViewFromSceneCamera(Camera camera)
        {
            foreach (SceneView sceneView in SceneView.sceneViews)
            {
                if (sceneView.camera == camera)
                {
                    return sceneView;
                }
            }

            return null;
        }

        /// <summary>Get time passed to animated materials.</summary>
        public static float GetShaderTime()
        {
            // When "Always Refresh" is disabled, Unity passes zero. Also uses realtimeSinceStartup:
            // https://github.com/Unity-Technologies/Graphics/blob/5743e39cdf0795cf7cbeb7ba8ffbbcc7ca200709/Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariablesGlobal.cs#L116
            return !Application.isPlaying && SceneView.lastActiveSceneView != null &&
                !SceneView.lastActiveSceneView.sceneViewState.alwaysRefresh ? 0f : Time.realtimeSinceStartup;
        }

        public static GameObject GetGameObject(SerializedObject serializedObject)
        {
            // We will either get the component or the GameObject it is attached to.
            return serializedObject.targetObject is GameObject
                ? serializedObject.targetObject as GameObject
                : (serializedObject.targetObject as Component).gameObject;
        }

        public static Material CreateSerializedMaterial(string shaderPath, string message)
        {
            var shader = Shader.Find(shaderPath);
            Debug.Assert(shader != null, "Crest: Cannot create required material because shader is null");

            var material = new Material(shader);

            // Record the material and any subsequent changes.
            Undo.RegisterCreatedObjectUndo(material, message);
            Undo.RegisterCompleteObjectUndo(material, message);

            return material;
        }

        public static Material CreateSerializedMaterial(string shaderPath)
        {
            return CreateSerializedMaterial(shaderPath, Undo.GetCurrentGroupName());
        }

        public static Object GetDefaultReference(this SerializedObject self, string property)
        {
            var path = AssetDatabase.GetAssetPath(MonoScript.FromMonoBehaviour(self.targetObject as MonoBehaviour));
            var importer = AssetImporter.GetAtPath(path) as MonoImporter;
            return importer.GetDefaultReference(property);
        }

        public static object GetDefiningBoxedObject(this SerializedProperty property)
        {
            object target = property.serializedObject.targetObject;

            if (property.depth > 0)
            {
                // Get the property path so we can find it from the serialized object.
                var path = string.Join(".", property.propertyPath.Split(".", System.StringSplitOptions.None)[0..^1]);
                var other = property.serializedObject.FindProperty(path);
                // Boxed value can handle both managed and generic with caveats:
                // https://docs.unity3d.com/ScriptReference/SerializedProperty-boxedValue.html
                // Not sure if it will be a new or same instance as in the scene.
                target = other.boxedValue;
            }

            return target;
        }

        internal delegate Object CreateInstance(SerializedProperty property);

        internal static Rect AssetField
        (
            System.Type type,
            GUIContent label,
            SerializedProperty property,
            Rect rect,
            string title,
            string defaultName,
            string extension,
            string message,
            CreateInstance create
        )
        {
            var hSpace = 5;
            var buttonWidth = 45;
            var buttonCount = 2;

            rect.width -= buttonWidth * buttonCount + hSpace;
            EditorGUI.PropertyField(rect, property, label);

            var r = new Rect(rect);

            r.x += r.width + hSpace;
            r.width = buttonWidth;
            if (GUI.Button(r, "New", EditorStyles.miniButtonLeft))
            {
                var path = EditorUtility.SaveFilePanelInProject(title, defaultName, extension, message);
                if (!string.IsNullOrEmpty(path))
                {
                    var asset = create(property);
                    if (asset != null)
                    {
                        if (extension == "prefab")
                        {
                            PrefabUtility.SaveAsPrefabAsset(asset as GameObject, path);
                        }
                        else
                        {
                            AssetDatabase.CreateAsset(asset, path);
                        }

                        property.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Object>(path);
                        property.serializedObject.ApplyModifiedProperties();
                    }
                    else
                    {
                        Debug.LogError($"Crest: Could not create file");
                    }
                }
            }

            // Only allow cloning if extensions match. Guards against cloning Shader Graph if
            // using its embedded material.
            var cloneable = property.objectReferenceValue != null;
            cloneable = cloneable && Path.GetExtension(AssetDatabase.GetAssetPath(property.objectReferenceValue)) == $".{extension}";

            EditorGUI.BeginDisabledGroup(!cloneable);
            r.x += r.width;
            if (GUI.Button(r, "Clone", EditorStyles.miniButtonRight))
            {
                var oldPath = AssetDatabase.GetAssetPath(property.objectReferenceValue);
                var newPath = oldPath;
                if (!newPath.StartsWithNoAlloc("Assets")) newPath = Path.Join("Assets", Path.GetFileName(newPath));
                newPath = AssetDatabase.GenerateUniqueAssetPath(newPath);
                AssetDatabase.CopyAsset(oldPath, newPath);
                property.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Object>(newPath);
            }
            EditorGUI.EndDisabledGroup();

            return rect;
        }

        internal static void RichTextHelpBox(string message, MessageType type)
        {
            var styleRichText = GUI.skin.GetStyle("HelpBox").richText;
            GUI.skin.GetStyle("HelpBox").richText = true;

            EditorGUILayout.HelpBox(message, type);

            // Revert skin since it persists.
            GUI.skin.GetStyle("HelpBox").richText = styleRichText;
        }

        // Prettify nameof.
        internal static string Pretty(this string text)
        {
            // Regular expression to split on transitions from lower to upper case and keep acronyms together
            return Regex.Replace(text, @"([a-z])([A-Z])|([A-Z])([A-Z][a-z])", "$1$3 $2$4").Replace("_", "");
        }

        internal static string Italic(this string text)
        {
            return $"<i>{text}</i>";
        }

        public static void MarkCurrentStageAsDirty()
        {
            var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();

            if (stage != null)
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(stage.scene);
            }
            else
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            }
        }
    }

    static partial class Extensions
    {
        internal static string GetSubShaderTag([DisallowNull] this Shader shader, ShaderSnippetData snippet, ShaderTagId id)
        {
            var data = ShaderUtil.GetShaderData(shader);
            if (data == null) return null;

            var index = (int)snippet.pass.SubshaderIndex;
            if (index < 0 || index >= shader.subshaderCount) return null;

            var subShader = data.GetSerializedSubshader(index);
            if (subShader == null) return null;

            var tag = subShader.FindTagValue(id);
            if (string.IsNullOrEmpty(tag.name)) return null;

            return tag.name;
        }
    }

    static partial class EditorHelpers
    {
        const int k_ButtonDropDownWidth = 15;

        static readonly GUIContent s_ButtonDropDownIcon = new(EditorGUIUtility.FindTexture("icon dropdown@2x"));
        static readonly PropertyInfo s_TopLevel = typeof(GUILayoutUtility).GetProperty("topLevel", BindingFlags.NonPublic | BindingFlags.Static);
        static readonly MethodInfo s_GetLast = typeof(GUILayoutUtility).Assembly.GetType("UnityEngine.GUILayoutGroup").GetMethod("GetLast", BindingFlags.Public | BindingFlags.Instance);

        // Only way to identify the caller is its rect.
        static Rect s_ButtonChooser;
        static int s_ButtonChoice = -2;

        // Normal button or split button with dropdown.
        public static bool Button
        (
            GUIContent label,
            out int choice,
            string[] labels,
            bool disableMain = false,
            bool disableDropDown = false,
            bool centerLabel = false,
            bool expandWidth = true,
            int minimumWidth = 0
        )
        {
            choice = -2;
            var chosen = false;

            var hasDropDown = labels?.Length > 0;
            var skin = GUI.skin.button;

            using (new EditorGUI.DisabledGroupScope(disableMain))
            {
                var style = new GUIStyle(hasDropDown ? EditorStyles.miniButtonLeft : EditorStyles.miniButton)
                {
                    padding = skin.padding,
                    stretchHeight = skin.stretchHeight,
                    fixedHeight = skin.fixedHeight
                };

                var width = style.CalcSize(label).x + style.padding.left +
                    style.padding.right + style.border.left + style.border.right;
                width = Mathf.Max(width, minimumWidth);
                // TODO: Add option to disable this (consistent width).
                if (!hasDropDown && minimumWidth > 0) width += k_ButtonDropDownWidth;

                // TODO: eyeballed based on Fix button but likely specific to it.
                if (centerLabel && hasDropDown)
                {
                    style.padding.left += k_ButtonDropDownWidth / 2;
                    width += k_ButtonDropDownWidth / 3;
                }

                if (GUILayout.Button(label, style, expandWidth ? GUILayout.ExpandWidth(true) : GUILayout.Width(width)))
                {
                    choice = -1;
                    chosen = true;
                }
            }

            if (hasDropDown)
            {
                using (new EditorGUI.DisabledGroupScope(disableDropDown))
                {
                    // TODO: color interior border same as exterior (lighten).
                    var style = new GUIStyle(EditorStyles.miniButtonRight)
                    {
                        padding = new(1, 1, 3, 3),
                        stretchHeight = skin.stretchHeight,
                        fixedHeight = skin.fixedHeight
                    };

                    var rect = (Rect)s_GetLast.Invoke(s_TopLevel.GetValue(null), null);
                    rect.width += k_ButtonDropDownWidth;

                    if (s_ButtonChoice > -1 && s_ButtonChooser == rect)
                    {
                        choice = s_ButtonChoice;
                        chosen = true;
                        s_ButtonChoice = -2;
                        s_ButtonChooser = Rect.zero;
                    }

                    if (GUILayout.Button(s_ButtonDropDownIcon, style, GUILayout.Width(k_ButtonDropDownWidth), GUILayout.ExpandHeight(true)))
                    {
                        var menu = new GenericMenu();

                        for (var i = 0; i < labels.Length; i++)
                        {
                            menu.AddItem(new(labels[i]), false, x => { s_ButtonChoice = (int)x; s_ButtonChooser = rect; }, i);
                        }

                        menu.DropDown(rect);
                    }
                }
            }

            return chosen;
        }
    }

    static partial class EditorHelpers
    {
        // Adapted from (public API may support this in future):
        // com.unity.splines@2.7.2/Editor/Components/SplineContainerEditor.cs
        static GUIStyle s_HelpLabelStyle;
        static GUIStyle HelpLabelStyle => s_HelpLabelStyle ??= new(EditorStyles.label)
        {
            wordWrap = EditorStyles.helpBox.wordWrap,
            fontSize = EditorStyles.helpBox.fontSize,
            padding = new(-2, 0, 0, 0),
            richText = true,
        };

        static readonly MethodInfo s_GetHelpIcon = typeof(EditorGUIUtility).GetMethod("GetHelpIcon", BindingFlags.Static | BindingFlags.NonPublic);

        internal static int? HelpBox
        (
            GUIContent message,
            MessageType type,
            GUIContent button = null,
            string[] buttons = null,
            bool buttonCenterLabel = false,
            int buttonMinimumWidth = 0
        )
        {
            return HelpBox
            (
                message,
                new GUIContent((Texture2D)s_GetHelpIcon.Invoke(null, new object[] { type })),
                button,
                buttons,
                buttonCenterLabel,
                buttonMinimumWidth
            );
        }

        internal static int? HelpBox
        (
            GUIContent message,
            GUIContent icon,
            GUIContent button = null,
            string[] buttons = null,
            bool buttonCenterLabel = false,
            int buttonMinimumWidth = 0
        )
        {
            int? result = null;

            // Box
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            // Icon
            EditorGUIUtility.SetIconSize(new(32f, 32f));
            EditorGUILayout.LabelField(icon, GUILayout.Width(34), GUILayout.MinHeight(34), GUILayout.ExpandHeight(true));
            EditorGUIUtility.SetIconSize(Vector2.zero);

            // Text
            EditorGUILayout.LabelField(message, HelpLabelStyle, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            // Button
            if (button != null)
            {
                GUILayout.FlexibleSpace();

                EditorGUILayout.BeginVertical();
                GUILayout.FlexibleSpace();

                EditorGUILayout.BeginHorizontal();

                if (Button(button, out var choice, buttons, centerLabel: buttonCenterLabel, minimumWidth: buttonMinimumWidth, expandWidth: false))
                {
                    result = choice;
                }

                EditorGUILayout.EndHorizontal();

                GUILayout.FlexibleSpace();
                EditorGUILayout.EndVertical();

            }

            EditorGUILayout.EndHorizontal();

            return result;
        }
    }

    namespace Internal
    {
        static class Extensions
        {
            // Recursively find the field owner (instance).
            public static bool FindOwner(this FieldInfo field, ref object target)
            {
                if (field.DeclaringType.IsAssignableFrom(target.GetType()))
                {
                    return true;
                }

                return field.FindOwnerInFields(ref target);
            }

            public static bool FindOwnerInFields(this FieldInfo targetField, ref object target)
            {
                if (target == null)
                {
                    return false;
                }

                var fields = target.GetType()
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                foreach (var field in fields)
                {
                    if (field.GetCustomAttribute<SerializeReference>() == null)
                    {
                        continue;
                    }

                    var value = field.GetValue(target);
                    if (value == null)
                    {
                        continue;
                    }

                    if (targetField.DeclaringType.IsAssignableFrom(value.GetType()))
                    {
                        target = value;
                        return true;
                    }

                    if (FindOwnerInFields(targetField, ref value))
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
