// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace WaveHarmonic.Crest.Editor
{
    partial class Inspector
    {
        static readonly string[] s_FixButtonDropDown = { "Inspect" };
        static readonly GUIContent s_FixButtonContent = new("Fix");
        static readonly GUIContent s_InspectButtonContent = new("Inspect", "Jump to object to resolve issue.");

        protected virtual void RenderValidationMessages()
        {
            // This is a static list so we need to clear it before use. Not sure if this will ever be a threaded
            // operation which would be an issue.
            foreach (var messages in ValidatedHelper.s_Messages)
            {
                messages.Clear();
            }

            ValidatedHelper.ExecuteValidators(target, ValidatedHelper.HelpBox);

            // We only want space before and after the list of help boxes. We don't want space between.
            var needsSpaceAbove = true;

            // Work out if button label needs aligning.
            var needsAlignment = false;
            var hasBoth = false;
            var hasEither = false;
            for (var messageTypeIndex = 0; messageTypeIndex < ValidatedHelper.s_Messages.Length; messageTypeIndex++)
            {
                var messages = ValidatedHelper.s_Messages[messageTypeIndex];

                if (messages.Count > 0)
                {
                    var messageType = (MessageType)ValidatedHelper.s_Messages.Length - messageTypeIndex;

                    foreach (var message in messages)
                    {
                        var hasFix = message._Action != null;
                        var hasInspect = false;

                        if (message._Object != null)
                        {
                            var casted = message._Object as MonoBehaviour;

                            if (Selection.activeObject != message._Object && (casted == null || casted.gameObject != Selection.activeObject))
                            {
                                hasInspect = true;
                            }
                        }

                        if (hasFix && hasInspect) hasBoth = true;
                        if (hasInspect != hasFix) hasEither = true;
                        if (hasBoth && hasEither) goto exit;
                    }
                }
            }

        exit:
            needsAlignment = hasBoth && hasEither;

            // We loop through in reverse order so errors appears at the top.
            for (var messageTypeIndex = 0; messageTypeIndex < ValidatedHelper.s_Messages.Length; messageTypeIndex++)
            {
                var messages = ValidatedHelper.s_Messages[messageTypeIndex];

                if (messages.Count > 0)
                {
                    if (needsSpaceAbove)
                    {
                        EditorGUILayout.Space();
                        needsSpaceAbove = false;
                    }

                    // Map Validated.MessageType to HelpBox.MessageType.
                    var messageType = (MessageType)ValidatedHelper.s_Messages.Length - messageTypeIndex;

                    foreach (var message in messages)
                    {
                        EditorGUILayout.BeginHorizontal();

                        var originalGUIEnabled = GUI.enabled;

                        if ((message._Action == ValidatedHelper.FixAddMissingMathPackage || message._Action == ValidatedHelper.FixAddMissingBurstPackage) && PackageManagerHelpers.IsBusy)
                        {
                            GUI.enabled = false;
                        }

                        if (message._FixDescription != null)
                        {
                            var sanitized = Regex.Replace(message._FixDescription, @"<[^<>]*>", "'");
                            s_FixButtonContent.tooltip = $"Fix: {sanitized}";
                        }
                        else
                        {
                            s_FixButtonContent.tooltip = "Fix issue";
                        }

                        var canFix = message._Action != null;
                        var canInspect = false;

                        // Jump to object button.
                        if (message._Object != null)
                        {
                            // Selection.activeObject can be message._object.gameObject instead of the component
                            // itself. We soft cast to MonoBehaviour to get the gameObject for comparison.
                            // Alternatively, we could always pass gameObject instead of "this".
                            var casted = message._Object as MonoBehaviour;

                            if (Selection.activeObject != message._Object && (casted == null || casted.gameObject != Selection.activeObject))
                            {
                                canInspect = true;
                            }
                        }

                        var result = EditorHelpers.HelpBox
                        (
                            new($"{message._Message} {message._FixDescription}"),
                            messageType,
                            canFix ? s_FixButtonContent : canInspect ? s_InspectButtonContent : null,
                            buttons: canInspect && canFix ? s_FixButtonDropDown : null,
                            buttonCenterLabel: needsAlignment,
                            buttonMinimumWidth: 72
                        );

                        if (canFix && result == -1)
                        {
                            // Run fix function.
                            var serializedObject = new SerializedObject(message._Object);
                            // Property is optional.
                            var property = message._PropertyPath != null ? serializedObject?.FindProperty(message._PropertyPath) : null;
                            var oldValue = property?.boxedValue;
                            message._Action.Invoke(serializedObject, property);
                            if (serializedObject.ApplyModifiedProperties())
                            {
                                // SerializedObject does this for us, but gives the history item a nicer label.
                                Undo.RecordObject(message._Object, s_FixButtonContent.tooltip);
                                DecoratedDrawer.OnChange(property, oldValue);
                            }
                        }
                        else if (canInspect && result != null)
                        {
                            Selection.activeObject = message._Object;
                        }

                        GUI.enabled = originalGUIEnabled;

                        EditorGUILayout.EndHorizontal();
                    }
                }
            }
        }
    }
}
