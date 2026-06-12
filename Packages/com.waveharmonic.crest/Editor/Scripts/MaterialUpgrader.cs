// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEditor;
using UnityEngine;
using WaveHarmonic.Crest.Editor.Settings;

namespace WaveHarmonic.Crest.Editor
{
    static partial class MaterialUpgrader
    {
        [InitializeOnLoadMethod]
        static void OnLoad()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        static void OnEditorUpdate()
        {
            if (Time.renderedFrameCount <= 0)
            {
                return;
            }

            if (EditorApplication.isUpdating)
            {
                return;
            }

            if (ProjectSettings.Instance._MaterialVersion > 0)
            {
                EditorApplication.update -= OnEditorUpdate;
                return;
            }

            UpgradeMaterials(force: false);

            EditorApplication.update -= OnEditorUpdate;
        }

        [MenuItem("Edit/Crest/Materials/Upgrade Materials")]
        static void OnMenuSelect()
        {
            UpgradeMaterials(force: true);
        }

        static void UpgradeMaterials(bool force)
        {
            var dirty = false;
            var version = force ? 0 : ProjectSettings.Instance._MaterialVersion;

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Material"))
                {
                    var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                    dirty |= UpgradeMaterial(material, version);
                }

                ProjectSettings.Instance._MaterialVersion = k_MaterialVersion;
                ProjectSettings.Save();

                if (dirty)
                {
                    AssetDatabase.SaveAssets();
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }
    }

    static partial class MaterialUpgrader
    {
        enum SerializedType
        {
            Boolean,
            Integer,
            Float,
            Vector,
            Color,
            Texture,
            Keyword,
        }

        static bool TryFindBase(SerializedObject material, SerializedType type, out SerializedProperty propertyBase)
        {
            propertyBase = material.FindProperty("m_SavedProperties");

            switch (type)
            {
                case SerializedType.Integer:
                    propertyBase = propertyBase.FindPropertyRelative("m_Ints");
                    return true;
                case SerializedType.Boolean:
                case SerializedType.Float:
                    propertyBase = propertyBase.FindPropertyRelative("m_Floats");
                    return true;
                case SerializedType.Color:
                case SerializedType.Vector:
                    propertyBase = propertyBase.FindPropertyRelative("m_Colors");
                    return true;
                case SerializedType.Texture:
                    propertyBase = propertyBase.FindPropertyRelative("m_TexEnvs");
                    return true;
            }

            return false;
        }

        static SerializedProperty FindBase(SerializedObject material, SerializedType type)
        {
            if (!TryFindBase(material, type, out var root))
            {
                throw new System.ArgumentException($"Unknown SerializedType {type}");
            }

            return root;
        }

        static bool TryFindProperty(SerializedObject material, string name, SerializedType type, out SerializedProperty property, out int index, SerializedProperty root)
        {
            var isKeyword = type == SerializedType.Keyword;

            property = null;
            var size = root.arraySize;
            for (index = 0; index < size; ++index)
            {
                property = root.GetArrayElementAtIndex(index);
                if (isKeyword ? property.stringValue == name : property.FindPropertyRelative("first").stringValue == name)
                {
                    break;
                }
            }

            if (index == size)
            {
                return false;
            }

            if (!isKeyword)
            {
                property = property.FindPropertyRelative("second");
            }

            return true;
        }

        static bool TryFindProperty(SerializedObject material, string name, SerializedType type, out SerializedProperty property, out int index, out SerializedProperty root)
        {
            if (type == SerializedType.Keyword)
            {
                root = material.FindProperty("m_ValidKeywords");
                if (TryFindProperty(material, name, type, out property, out index, root))
                {
                    return true;
                }

                root = material.FindProperty("m_InvalidKeywords");
                if (TryFindProperty(material, name, type, out property, out index, root))
                {
                    return true;
                }

                return false;
            }

            root = FindBase(material, type);
            return TryFindProperty(material, name, type, out property, out index, root);
        }

        static bool RenameFloat(SerializedObject so, Material material, string old, string @new)
        {
            if (!TryFindProperty(so, old, SerializedType.Float, out var oldProperty, out var oldIndex, out var parent))
            {
                return false;
            }

            var oldValue = oldProperty.floatValue;
            parent.DeleteArrayElementAtIndex(oldIndex);
            parent.InsertArrayElementAtIndex(0);
            var newProperty = parent.GetArrayElementAtIndex(0);
            newProperty.FindPropertyRelative("first").stringValue = @new;
            newProperty.FindPropertyRelative("second").floatValue = oldValue;
            return true;
        }

        static bool RenameKeyword(SerializedObject so, Material material, string old, string @new)
        {
            if (!TryFindProperty(so, old, SerializedType.Keyword, out var oldProperty, out var oldIndex, out var parent))
            {
                return false;
            }

            parent.DeleteArrayElementAtIndex(oldIndex);
            parent.InsertArrayElementAtIndex(0);
            var keyword = parent.GetArrayElementAtIndex(0);
            keyword.stringValue = @new;
            return true;
        }
    }

    // Upgrades
    static partial class MaterialUpgrader
    {
        public const int k_MaterialVersion = 1;

        static bool UpgradeMaterial(Material material, int version)
        {
            var so = new SerializedObject(material);
            var dirty = false;

            // Upgrade materials.
            // Version is for all materials.
            if (version < 1)
            {
                switch (material.shader.name)
                {
                    case "Crest/Water":
                        dirty |= RenameKeyword(so, material, "CREST_FLOW_ON", "_CREST_FLOW_LOD");
                        dirty |= RenameFloat(so, material, "CREST_FLOW", "_CREST_FLOW_LOD");
                        break;
                }
            }

            if (dirty)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(material);
            }

            return dirty;
        }
    }
}
