// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using WaveHarmonic.Crest.Editor.Internal;
using WaveHarmonic.Crest.Internal;

namespace WaveHarmonic.Crest.Editor
{
    /// <summary>
    /// Base editor. Needed as custom drawers require a custom editor to work.
    /// </summary>
    [CustomEditor(typeof(EditorBehaviour), editorForChildClasses: true)]
    partial class Inspector : UnityEditor.Editor
    {
        public const int k_FieldGroupOrder = k_FieldHeadingOrder * 100;
        public const int k_FieldHeadingOrder = k_FieldOrder * 100;
        public const int k_FieldOrder = 1000;
        const string k_NoPrefabModeSupportWarning = "Prefab mode is not supported. Changes made in prefab mode will not be reflected in the scene view. Save this prefab to see changes.";

        internal static Inspector Current { get; private set; }

        readonly Dictionary<FieldInfo, object> _MaterialOwners = new();
        readonly Dictionary<Material, MaterialEditor> _MaterialEditors = new();
        readonly Dictionary<string, DecoratedDrawer> _Lists = new();

        internal readonly List<EmbeddedAssetEditor> _EmbeddedEditors = new();
        internal static readonly List<EmbeddedAssetEditor> s_EmbeddedEditors = new();

        public override bool RequiresConstantRepaint() => TexturePreview.s_ActiveInstance?.Open == true;

        static readonly IOrderedEnumerable<FieldInfo> s_AttachMaterialEditors = TypeCache
            .GetFieldsWithAttribute<AttachMaterialEditor>()
            .OrderBy(x => x.GetCustomAttribute<AttachMaterialEditor>().Order);

        readonly List<string> _Headings = new();
        readonly Utility.SortedList<int, SerializedProperty> _Properties = new(Helpers.DuplicateComparison);

        protected virtual void OnEnable()
        {
            _MaterialOwners.Clear();

            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.undoRedoPerformed += OnUndoRedo;

            foreach (var field in s_AttachMaterialEditors)
            {
                var target = (object)this.target;

                if (field.FindOwner(ref target))
                {
                    _MaterialOwners.Add(field, target);
                }
            }
        }

        public override void OnInspectorGUI()
        {
            // Reset foldout values.
            DecoratedDrawer.s_IsFoldout = false;
            DecoratedDrawer.s_IsFoldoutOpen = false;

            // Make sure we adhere to flags.
            var enabled = GUI.enabled;
            GUI.enabled = (target.hideFlags & HideFlags.NotEditable) == 0;

            RenderBeforeInspectorGUI();

            try
            {
                RenderInspectorGUI();
            }
            catch (InvalidOperationException exception)
            {
                // Happens when using New with embedded editor and not change the filename.
                if (exception.Message != "Stack empty.")
                {
                    throw exception;
                }
            }

            RenderValidationMessages();

            EditorGUI.BeginDisabledGroup(target is Behaviour component && !component.isActiveAndEnabled);
            RenderBottomButtons();
            EditorGUI.EndDisabledGroup();

            RenderAfterInspectorGUI();

            GUI.enabled = enabled;
        }

        protected virtual void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;

            foreach (var (_, editor) in _MaterialEditors)
            {
                Helpers.Destroy(editor);
            }

            foreach (var embedded in _EmbeddedEditors)
            {
                embedded?.OnDisable();
                s_EmbeddedEditors.Remove(embedded);
            }
        }

        protected virtual void OnChange()
        {

        }

        protected virtual void RenderBeforeInspectorGUI()
        {
            if (this.target is EditorBehaviour target && target._IsPrefabStageInstance)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(k_NoPrefabModeSupportWarning, MessageType.Warning);
                EditorGUILayout.Space();
            }
        }

        protected virtual void RenderInspectorGUI()
        {
            var previous = Current;
            Current = this;

            _Headings.Clear();
            _Properties.Clear();

            serializedObject.Update();

            EditorGUI.BeginChangeCheck();

            using var iterator = serializedObject.GetIterator();
            if (iterator.NextVisible(true))
            {
                var index = 0;
                var group = 0;
                Type type = null;

                do
                {
                    var property = serializedObject.FindProperty(iterator.name);
                    if (iterator.name == "m_Script")
                    {
#if CREST_DEBUG
                        _Properties.Add(index++ * k_FieldOrder, property);
#endif
                        continue;
                    }

                    var field = property.GetFieldInfo(out _);

                    // If field is on a new type (inheritance) then reset the group to prevent group leakage.
                    if (field.DeclaringType != type)
                    {
                        group = 0;
                        type = field.DeclaringType;
                    }

                    // Null checking but there should always be one DecoratedProperty.
                    var order = field.GetCustomAttribute<Attributes.DecoratedProperty>()?.order ?? 0;
                    group = field.GetCustomAttribute<Group>()?.order * k_FieldGroupOrder ?? group;

                    var headingAttribute = field.GetCustomAttribute<Heading>();
                    if (headingAttribute != null)
                    {
                        _Headings.Add(headingAttribute._Text.text);
                    }

                    var heading = Mathf.Max(0, _Headings.Count - 1);

                    var orderAttribute = field.GetCustomAttribute<Order>();

                    if (orderAttribute != null)
                    {
                        var target = orderAttribute._Target;

                        if (orderAttribute._Placement == Order.Placement.Heading)
                        {
                            heading = _Headings.FindIndex(x => x == target);
                        }
                    }

                    var key = order + group + heading * k_FieldHeadingOrder + index * k_FieldOrder;

                    // Override.
                    if (orderAttribute != null)
                    {
                        var target = orderAttribute._Target;

                        switch (orderAttribute._Placement)
                        {
                            case Order.Placement.Below: key = _Properties.First(x => x.Value.name == target).Key + index; break;
                            case Order.Placement.Above: key = _Properties.First(x => x.Value.name == target).Key - index; break;
                        }
                    }

                    _Properties.Add(key, property);

                    index += 1;
                }
                while (iterator.NextVisible(false));
            }

            foreach (var (_, property) in _Properties)
            {
#if CREST_DEBUG
                using (new EditorGUI.DisabledGroupScope(property.name == "m_Script"))
#endif
                {
                    // Handle lists as PropertyDrawer is not called on the list itself.
                    if (property.isArray)
                    {
                        var field = property.GetFieldInfo(out var _);
                        var attribute = field?.GetCustomAttribute<Attributes.DecoratedProperty>();

                        if (field != null && attribute != null)
                        {
                            var id = GetPropertyIdentifier(property);

                            if (!_Lists.ContainsKey(id))
                            {
                                _Lists[id] = new DecoratedDrawer()
                                {
                                    _Attribute = attribute,
                                    _Field = field,
                                };
                            }

                            DecoratedDrawer.s_IsList = true;
                            var label = new GUIContent(property.displayName);
                            _Lists[id].OnGUI(Rect.zero, property, label);
                            DecoratedDrawer.s_IsList = false;
                            continue;
                        }
                    }

                    // Only support top level ordering for now.
                    EditorGUILayout.PropertyField(property, includeChildren: true);
                }
            }

            // Need to call just in case there is no decorated property.
            serializedObject.ApplyModifiedProperties();

            if (EditorGUI.EndChangeCheck())
            {
                OnChange();
            }

            // Restore previous in case this is a nested editor.
            Current = previous;

            // Fixes indented validation etc.
            EditorGUI.indentLevel = 0;

            _UndoRedo = false;
        }

        protected virtual void RenderBottomButtons()
        {

        }

        protected virtual void RenderAfterInspectorGUI()
        {
            foreach (var mapping in _MaterialOwners)
            {
                var material = (Material)mapping.Key.GetValue(mapping.Value);
                if (material != null)
                {
                    DrawMaterialEditor(material);
                }
            }
        }

        // Adapted from: http://answers.unity.com/answers/975894/view.html
        void DrawMaterialEditor(Material material)
        {
            MaterialEditor editor;
            if (!_MaterialEditors.ContainsKey(material))
            {
                editor = (MaterialEditor)CreateEditor(material);
                _MaterialEditors[material] = editor;
            }
            else
            {
                editor = _MaterialEditors[material];
            }

            // Check material again as sometimes null.
            if (editor != null && material != null)
            {
                EditorGUILayout.Space();

                // Draw the material's foldout and the material shader field. Required to call OnInspectorGUI.
                editor.DrawHeader();

                var path = AssetDatabase.GetAssetPath(material);

                // We need to prevent the user from editing Unity's default materials.
                // Check Editor.IsEnabled in Editor.cs for further filtering.
                var isEditable = (material.hideFlags & HideFlags.NotEditable) == 0;

#if !CREST_DEBUG
                // Do not allow editing of our assets.
                isEditable &= !path.StartsWithNoAlloc("Packages/com.waveharmonic");
#endif

                using (new EditorGUI.DisabledGroupScope(!isEditable))
                {
                    // Draw the material properties. Works only if the foldout of DrawHeader is open.
                    editor.OnInspectorGUI();
                }
            }
        }

        internal virtual void OnCustomField(Rect position, SerializedProperty property, GUIContent label, DecoratedDrawer drawer)
        {

        }

        internal virtual GUIContent OnCustomLabel(SerializedProperty property, GUIContent label, DecoratedDrawer drawer)
        {
            return label;
        }
    }

    // Reflection
    partial class Inspector
    {
        static readonly PropertyInfo s_GUIViewCurrent = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GUIView").GetProperty("current", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        static readonly PropertyInfo s_GUIViewNativeHandle = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GUIView").GetProperty("nativeHandle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        // Adapted from:
        // https://github.com/Unity-Technologies/UnityCsReference/blob/59b03b8a0f179c0b7e038178c90b6c80b340aa9f/Editor/Mono/Inspector/ReorderableListWrapper.cs#L77-L88
        static string GetPropertyIdentifier(SerializedProperty serializedProperty)
        {
            // Property may be disposed.
            try
            {
                var handle = -1;
                var current = s_GUIViewCurrent.GetValue(null);

                if (current != null)
                {
                    handle = ((IntPtr)s_GUIViewNativeHandle.GetValue(current)).ToInt32();
                }

                return serializedProperty?.propertyPath + serializedProperty.serializedObject.targetObject.GetEntityId() + handle;
            }
            catch (NullReferenceException)
            {
                return string.Empty;
            }
        }
    }

    partial class Inspector
    {
        internal bool _UndoRedo;
        void OnUndoRedo()
        {
            _UndoRedo = true;
        }
    }

    // Adapted from:
    // https://gist.github.com/thebeardphantom/1ad9aee0ef8de6271fff39f1a6a3d66d
    static partial class Extensions
    {
        static readonly MethodInfo s_GetFieldInfoFromProperty;

        static Extensions()
        {
            var utility = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.ScriptAttributeUtility");
            Debug.Assert(utility != null, "Crest: ScriptAttributeUtility not found!");

            s_GetFieldInfoFromProperty = utility.GetMethod("GetFieldInfoFromProperty", BindingFlags.NonPublic | BindingFlags.Static);
            Debug.Assert(s_GetFieldInfoFromProperty != null, "Crest: GetFieldInfoFromProperty not found!");
        }

        public static FieldInfo GetFieldInfo(this SerializedProperty property, out Type type)
        {
            type = null;
            return (FieldInfo)s_GetFieldInfoFromProperty.Invoke(null, new object[] { property, type });
        }
    }
}
