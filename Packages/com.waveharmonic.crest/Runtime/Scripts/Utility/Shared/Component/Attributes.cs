// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#pragma warning disable IDE0005 // Using directive is unnecessary.

using System;
using System.Diagnostics;
using UnityEngine;

namespace WaveHarmonic.Crest
{
    static class Symbols
    {
        public const string k_UnityEditor = "UNITY_EDITOR";
    }

    [Conditional(Symbols.k_UnityEditor)]
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    sealed class ExecuteDuringEditMode : Attribute
    {
        [Flags]
        public enum Include
        {
            None,
            PrefabStage,
            BuildPipeline,
            All = PrefabStage | BuildPipeline,
        }

        [Flags]
        public enum Options
        {
            None,
            Singleton,
        }

        public Include _Including;
        public Options _Options;

        public ExecuteDuringEditMode(Include including = Include.PrefabStage, Options options = Options.None)
        {
            _Including = including;
            _Options = options;
        }
    }

    enum Getter
    {
        Default,
        Custom,
    }

    enum Setter
    {
        Default,
        Custom,
        Internal,
        Dirty,
        None,
    }

    [Conditional(Symbols.k_UnityEditor)]
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    sealed class GenerateAPI : Attribute
    {
        public readonly Getter _Getter;
        public readonly Setter _Setter;
        public readonly string _Name;
        public readonly string _ScriptingSymbol;

        public GenerateAPI(Getter getter = Getter.Default, Setter setter = Setter.Default, string name = null, string symbol = null)
        {
            _Getter = getter;
            _Setter = setter;
            _Name = name;
            _ScriptingSymbol = symbol;
        }

        public GenerateAPI(Setter setter, string name = null, string symbol = null)
        {
            _Setter = setter;
            _Name = name;
            _ScriptingSymbol = symbol;
        }
    }

    [Conditional(Symbols.k_UnityEditor)]
    [AttributeUsage(AttributeTargets.Enum, AllowMultiple = false, Inherited = false)]
    sealed class GenerateDoc : Attribute
    {
        public GenerateDoc()
        {

        }
    }

#if !UNITY_EDITOR

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    abstract class Decorator : PropertyAttribute { }

    [Conditional(Symbols.k_UnityEditor)]
    sealed class Order : Decorator
    {
        public enum Placement { Heading, Below, Above }
        public Order(string target, Placement placement = Placement.Heading) { }
    }

    [Conditional(Symbols.k_UnityEditor)]
    sealed class Layer : Decorator { }

    [Conditional(Symbols.k_UnityEditor)]
    sealed class Stripped : Decorator
    {
        public enum Style { None, PlatformTab, }
        public Stripped(Style style = Style.None, bool indent = false) { }
    }

    [Conditional(Symbols.k_UnityEditor)]
    sealed class Delayed : Decorator { }

    [Conditional(Symbols.k_UnityEditor)]
    sealed class Disabled : Decorator { }

    [Conditional(Symbols.k_UnityEditor)]
    sealed class Required : Attribute { }

    [Conditional(Symbols.k_UnityEditor)]
    sealed class PlatformTabs : Attribute { }

    [Conditional(Symbols.k_UnityEditor)]
    sealed class CustomField : Attribute { }

    [Conditional(Symbols.k_UnityEditor)]
    sealed class CustomLabel : Attribute { }

    [Conditional(Symbols.k_UnityEditor)]
    sealed class AttachMaterialEditor : Attribute
    {
        public AttachMaterialEditor(int order = 0) { }
    }

    [Conditional(Symbols.k_UnityEditor)]
    sealed class InlineToggle : Decorator
    {
        public InlineToggle(bool fix = false) { }
    }

    [Conditional(Symbols.k_UnityEditor)]
    sealed class Embedded : Decorator
    {
        public Embedded(int margin = 0, string defaultPropertyName = null) { }
    }

    [Conditional(Symbols.k_UnityEditor)]
    sealed class DecoratedField : Decorator
    {
        public DecoratedField(bool isCustomFoldout = false) { }
    }

    [Conditional(Symbols.k_UnityEditor)]
    sealed class Group : Decorator
    {
        public enum Style { None, Foldout, Accordian, }
        public Group(string title = null, Style style = Style.Foldout, bool isCustomFoldout = false) { }
    }

    [Conditional(Symbols.k_UnityEditor)]
    sealed class Stepped : Decorator
    {
        public Stepped(int minimum, int maximum, int step = 1, bool power = false) { }
    }

    [Conditional(Symbols.k_UnityEditor)]
    sealed class Range : Decorator
    {
        [Flags]
        public enum Clamp { None = 0, Minimum = 1, Maximum = 2, Both = Minimum | Maximum }
        public Range(float minimum, float maximum, Clamp clamp = Clamp.Both, float scale = 1f, bool delayed = false, int step = 0, bool power = false) { }
    }

    [Conditional(Symbols.k_UnityEditor)]
    sealed class Minimum : Decorator
    {
        public Minimum(float minimum) { }
    }

    [Conditional(Symbols.k_UnityEditor)]
    sealed class Maximum : Decorator
    {
        public Maximum(float maximum) { }
    }

    [Conditional(Symbols.k_UnityEditor)]
    sealed class WarnIfAbove : Decorator
    {
        public WarnIfAbove(float maximum) { }
    }

    [Conditional(Symbols.k_UnityEditor)]
    sealed class Filtered : Decorator
    {
        public enum Mode { Include, Exclude, }
        public Filtered(int unset = 0) { }
    }

    [Conditional(Symbols.k_UnityEditor)]
    sealed class FilterEnum : Decorator
    {
        public FilterEnum(string property, Filtered.Mode mode, params int[] values) { }
    }

    [Conditional(Symbols.k_UnityEditor)]
    sealed class Label : Decorator
    {
        public Label(string label) { }
    }

    [Conditional(Symbols.k_UnityEditor)]
    sealed class Heading : Decorator
    {
        public enum Style { Normal, Settings, }
        public Heading(string heading, Style style = Style.Normal, bool alwaysVisible = false, bool alwaysEnabled = false, string helpLink = null) { }
    }

    [Conditional(Symbols.k_UnityEditor)]
    sealed class Space : Decorator
    {
        public Space(float height, bool isAlwaysVisible = false) { }
    }

    [Conditional(Symbols.k_UnityEditor)]
    sealed class Show : Decorator
    {
        public Show(Type type, string member, object value) { }
        public Show(Type type, string member) { }
        public Show(Type type) { }
        public Show(string property) { }
        public Show(string property, object value) { }
        public Show(RenderPipeline rp) { }
    }

    [Conditional(Symbols.k_UnityEditor)]
    sealed class Hide : Decorator
    {
        public Hide(Type type, string member, object value) { }
        public Hide(Type type, string member) { }
        public Hide(Type type) { }
        public Hide(string property) { }
        public Hide(string property, object value) { }
        public Hide(RenderPipeline rp) { }
    }

    [Conditional(Symbols.k_UnityEditor)]
    sealed class Enable : Decorator
    {
        public Enable(Type type, string member, object value) { }
        public Enable(Type type, string member) { }
        public Enable(Type type) { }
        public Enable(string property) { }
        public Enable(string property, object value) { }
        public Enable(RenderPipeline rp) { }
    }

    [Conditional(Symbols.k_UnityEditor)]
    sealed class Disable : Decorator
    {
        public Disable(Type type, string member, object value) { }
        public Disable(Type type, string member) { }
        public Disable(Type type) { }
        public Disable(string property) { }
        public Disable(string property, object value) { }
        public Disable(RenderPipeline rp) { }
    }

    [Conditional(Symbols.k_UnityEditor)]
    sealed class OnChange : Decorator
    {
        public OnChange(bool skipIfInactive = true) { }
        public OnChange(Type type, bool skipIfInactive = true) { }
    }

    [Conditional(Symbols.k_UnityEditor)]
    sealed class HelpURL : Decorator
    {
        public HelpURL(string path = "") { }
    }

    [Conditional(Symbols.k_UnityEditor)]
    sealed class HelpBox : Decorator
    {
        public enum MessageType { Info, Warning, Error, }
        public enum Visibility { Always, PropertyEnabled, PropertyDisabled, }
        public HelpBox(string message, MessageType messageType = MessageType.Info, Visibility visibility = Visibility.Always) { }
    }

    [Conditional(Symbols.k_UnityEditor)]
    sealed class MaterialField : Decorator
    {
        public MaterialField(string shader, string title = "", string name = "", string parent = null) { }
    }

    [Conditional(Symbols.k_UnityEditor)]
    sealed class PrefabField : Decorator
    {
        public PrefabField(string title = "", string name = "") { }
    }

    [Conditional(Symbols.k_UnityEditor)]
    sealed class ShowComputedProperty : Decorator
    {
        public ShowComputedProperty(string name) { }
    }
#endif
}
