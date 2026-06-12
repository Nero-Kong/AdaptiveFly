// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

// How to use:
// Use or inherit from Crest.Editor.Inspector to support validation messages.
// Then create a static method with Validator attribute.

using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using WaveHarmonic.Crest.Editor;
using WaveHarmonic.Crest.Internal;

namespace WaveHarmonic.Crest.Editor
{
    using FixValidation = System.Action<SerializedObject, SerializedProperty>;

    [System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    sealed class Validator : System.Attribute
    {
        public readonly System.Type _Type;

        public Validator(System.Type type)
        {
            _Type = type;
        }
    }

    // Holds the shared list for messages
    static class ValidatedHelper
    {
        public enum MessageType
        {
            Error,
            Warning,
            Info,
        }

        public struct HelpBoxMessage
        {
            public string _Message;
            public string _FixDescription;
            public Object _Object;
            public FixValidation _Action;
            public string _PropertyPath;
        }

        // This is a shared resource. It will be cleared before use. It is only used by the HelpBox delegate since we
        // want to group them by severity (MessageType). Make sure length matches MessageType length.
        public static readonly List<HelpBoxMessage>[] s_Messages = new List<HelpBoxMessage>[]
        {
            new(),
            new(),
            new(),
        };

        public delegate void ShowMessage(string message, string fixDescription, MessageType type, Object @object = null, FixValidation action = null, string property = null, Object caller = null);

        public static void DebugLog(string message, string fixDescription, MessageType type, Object @object = null, FixValidation action = null, string property = null, Object caller = null)
        {
            // Never log info validation to console.
            if (type == MessageType.Info)
            {
                return;
            }

            // Always link back to the caller so developers know the origin. They can always
            // use the help box "Inspect" once there to get to the object to fix. Even better,
            // they can use any available fix buttons too.
            var context = caller != null ? caller : @object;

            message = $"<b>Crest Validation:</b> {message} {fixDescription} Click this message to highlight the problem object.";

            switch (type)
            {
                case MessageType.Error: Debug.LogError(message, context); break;
                case MessageType.Warning: Debug.LogWarning(message, context); break;
                default: Debug.Log(message, context); break;
            }
        }

        public static void HelpBox(string message, string fixDescription, MessageType type, Object @object = null, FixValidation action = null, string property = null, Object caller = null)
        {
            s_Messages[(int)type].Add(new() { _Message = message, _FixDescription = fixDescription, _Object = @object, _Action = action, _PropertyPath = property });
        }

        public static void Suppressed(string _0, string _1, MessageType _2, Object _3 = null, FixValidation _4 = null, string _5 = null, Object _6 = null)
        {
        }

        public static T FixAttachComponent<T>(SerializedObject componentOrGameObject)
            where T : Component
        {
            return Undo.AddComponent<T>(EditorHelpers.GetGameObject(componentOrGameObject));
        }

        internal static void FixSetMaterialOptionEnabled(SerializedObject material, string keyword, string floatParam, bool enabled)
        {
            var mat = material.targetObject as Material;
            Undo.RecordObject(mat, $"Enable keyword {keyword}");
            mat.SetBoolean(Shader.PropertyToID(floatParam), enabled);
            if (ArrayUtility.Contains(mat.shader.keywordSpace.keywordNames, keyword))
            {
                mat.SetKeyword(keyword, enabled);
            }
        }

        internal static void FixSetMaterialIntProperty(SerializedObject material, string label, string intParam, int value)
        {
            var mat = material.targetObject as Material;
            Undo.RecordObject(mat, $"change {label}");
            mat.SetInteger(intParam, value);
        }

        public static void FixAddMissingMathPackage(SerializedObject _0, SerializedProperty _1)
        {
            PackageManagerHelpers.AddMissingPackage("com.unity.mathematics");
        }

        public static void FixAddMissingBurstPackage(SerializedObject _0, SerializedProperty _1)
        {
            PackageManagerHelpers.AddMissingPackage("com.unity.burst");
        }

        public static bool ValidateNoScale(Object @object, Transform transform, ShowMessage showMessage)
        {
            if (transform.lossyScale != Vector3.one)
            {
                showMessage
                (
                    $"There must be no scale on the <i>{@object.GetType().Name}</i> Transform or any of its parents." +
                    $"The current scale is <i>{transform.lossyScale}</i>.",
                    "Reset the scale on this Transform and all parents to one.",
                    MessageType.Error, @object
                );

                return false;
            }

            return true;
        }

        public static bool ValidateNoRotation(Object @object, Transform transform, ShowMessage showMessage)
        {
            if (transform.eulerAngles.magnitude > 0.0001f)
            {
                showMessage
                (
                    $"There must be no rotation on the <i>{@object.GetType().Name}</i> Transform or any of its parents." +
                    $"The current rotation is <i>{transform.eulerAngles}.</i>",
                    "Reset the rotation on this Transform and all parents to zero.",
                    MessageType.Error, @object
                );

                return false;
            }

            return true;
        }



        public static bool ValidateRenderer<T>
        (
            Component component,
            Renderer renderer,
            ShowMessage showMessage,
            bool checkShaderPasses,
            string shaderPrefix = null
        )
            where T : Renderer
        {
            if (renderer == null)
            {
                var type = typeof(T);
                var name = type.Name;

                // Give users a hint as to what "Renderer" really means.
                if (type == typeof(Renderer))
                {
                    name += " (Mesh, Trail etc)";
                }

                showMessage
                (
                    $"A <i>{name}</i> component is required but none is assigned.",
                    "Provide a renderer.",
                    MessageType.Error, component
                );

                return false;
            }

            var materials = renderer.sharedMaterials;
            for (var i = 0; i < materials.Length; i++)
            {
                // Empty material slots is a user error. Unity complains about it so we should too.
                if (materials[i] == null)
                {
                    showMessage
                    (
                        $"<i>{renderer.GetType().Name}</i> used by this input (<i>{component.GetType().Name}</i>) has empty material slots.",
                        "Remove these slots or fill them with a material.",
                        MessageType.Error, renderer
                    );
                }
            }

            if (renderer is MeshRenderer)
            {
                renderer.gameObject.TryGetComponent<MeshFilter>(out var mf);
                if (mf == null)
                {
                    showMessage
                    (
                        $"A <i>MeshRenderer</i> component is being used by this input but no <i>MeshFilter</i> component was found so there may not be any valid geometry to render.",
                        "Attach a <i>MeshFilter</i> component.",
                        MessageType.Error, renderer.gameObject,
                        (_, _) => Undo.AddComponent<MeshFilter>(renderer.gameObject)
                    );

                    return false;
                }
                else if (mf.sharedMesh == null)
                {
                    showMessage
                    (
                        $"A <i>MeshRenderer</i> component is being used by this input but no mesh is assigned to the <i>MeshFilter</i> component.",
                        "Assign the geometry to be rendered to the <i>MeshFilter</i> component.",
                        MessageType.Error, renderer.gameObject
                    );

                    return false;
                }
            }

            if (!ValidateMaterial(renderer.gameObject, showMessage, renderer.sharedMaterial, shaderPrefix, checkShaderPasses))
            {
                return false;
            }

            return true;
        }

        public static bool ValidateMaterial(GameObject gameObject, ShowMessage showMessage, Material material, string shaderPrefix, bool checkShaderPasses)
        {
            if (shaderPrefix == null && material == null)
            {
                showMessage
                (
                    $"<i>Mesh Renderer</i> requires a material.",
                    "Assign a material.",
                    MessageType.Error, gameObject
                );

                return false;
            }

            if (!material || material.shader && (!material.shader.name.StartsWithNoAlloc(shaderPrefix) && !material.shader.name.StartsWithNoAlloc($"Hidden/{shaderPrefix}") && !material.shader.name.Contains("/All/")))
            {
                showMessage
                (
                    $"Shader assigned to water input expected to be of type <i>{shaderPrefix}</i>.",
                    "Assign a material that uses a shader of this type.",
                    MessageType.Error, gameObject
                );

                return false;
            }

            if (checkShaderPasses && material.passCount > 1)
            {
                showMessage
                (
                    $"The shader <i>{material.shader.name}</i> for material <i>{material.name}</i> has multiple passes which might not work as expected as only the first pass is executed. " +
                    "This can be ignored in most cases, like Shader Graph, as only one pass is often required.",
                    "To have all passes execute then set <i>Shader Pass Index</i> to <i>-1</i>.",
                    MessageType.Info, gameObject
                );
            }

            return true;
        }

        public static bool ExecuteValidators(object target, ShowMessage messenger)
        {
            var isValid = true;
            var type = target.GetType();
            var validators = TypeCache.GetMethodsWithAttribute<Validator>();
            foreach (var validator in validators)
            {
                var attribute = validator.GetCustomAttribute<Validator>();
                if (attribute._Type.IsAssignableFrom(type))
                {
                    isValid = (bool)validator.Invoke(null, new object[] { target, messenger }) && isValid;
                }
            }

            // NOTE: Nested components do not descend from Object, but they could and this
            // would work for them.
            if (target is Object @object)
            {
                foreach (var field in TypeCache.GetFieldsWithAttribute<Validated>())
                {
                    if (field.DeclaringType != type)
                    {
                        continue;
                    }

                    foreach (var attribute in field.GetCustomAttributes<Validated>())
                    {
                        isValid &= attribute.Validate(@object, field, messenger);
                    }
                }
            }

            return isValid;
        }

        public static bool ExecuteValidators(Object target)
        {
            return ExecuteValidators(target, DebugLog);
        }
    }

    abstract class Validated : System.Attribute
    {
        public abstract bool Validate(Object target, FieldInfo property, ValidatedHelper.ShowMessage messenger);
    }
}

namespace WaveHarmonic.Crest
{
    /// <summary>
    /// Validates that field is not null.
    /// </summary>
    [System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = false)]
    sealed class Required : Validated
    {
        public override bool Validate(Object target, FieldInfo field, ValidatedHelper.ShowMessage messenger)
        {
            var isValid = true;

            if ((Object)field.GetValue(target) == null)
            {
                var typeName = EditorHelpers.Pretty(target.GetType().Name);
                var fieldName = EditorHelpers.Pretty(field.Name);

                messenger
                (
                    $"<i>{fieldName}</i> is required for the <i>{typeName}</i> component to function.",
                    $"Please set <i>{fieldName}</i>.",
                    ValidatedHelper.MessageType.Error,
                    target
                );

                isValid = false;
            }

            return isValid;
        }
    }

    /// <summary>
    /// Shows a info message if field is null.
    /// </summary>
    [System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = false)]
    sealed class Optional : Validated
    {
        readonly string _Message;

        public Optional(string message)
        {
            _Message = message;
        }

        public override bool Validate(Object target, FieldInfo field, ValidatedHelper.ShowMessage messenger)
        {
            var value = field.GetValue(target);

            if (value is ICollection<Object> list)
            {
                if (list != null && list.Count > 0)
                {
                    return true;
                }
            }
            else
            {
                if (value is Object @object && @object != null)
                {
                    return true;
                }
            }

            var typeName = EditorHelpers.Pretty(target.GetType().Name);
            var fieldName = EditorHelpers.Pretty(field.Name);

            messenger
            (
                $"<i>{fieldName}</i> is not set for the <i>{typeName}</i> component. " + _Message,
                string.Empty,
                ValidatedHelper.MessageType.Info,
                target
            );

            return true;
        }
    }
}
