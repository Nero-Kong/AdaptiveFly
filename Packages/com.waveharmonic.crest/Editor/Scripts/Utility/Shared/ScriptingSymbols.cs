
// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace WaveHarmonic.Crest.Editor.Settings
{
    static class ScriptingSymbols
    {
        // Too lazy to add separate calls just to provide a NamedBuildTarget.
        internal static bool s_OverrideCurrentNamedBuildTarget;
        internal static NamedBuildTarget s_CurrentNamedBuildTargetOverride;

        internal static NamedBuildTarget CurrentNamedBuildTarget
        {
            get
            {
                if (s_OverrideCurrentNamedBuildTarget)
                {
                    return s_CurrentNamedBuildTargetOverride;
                }

#if UNITY_SERVER
                return NamedBuildTarget.Server;
#else
                return NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget));
#endif
            }
        }

        public static string[] Symbols => PlayerSettings.GetScriptingDefineSymbols(CurrentNamedBuildTarget).Split(';');

        public static void Add(string[] symbols)
        {
            // We remove our symbols from the list first to prevent duplicates - just to be safe.
            SetScriptingDefineSymbols(Symbols.Except(symbols).Concat(symbols).ToArray());
        }

        public static void Add(string symbol)
        {
            Add(new string[] { symbol });
        }

        public static void Remove(string[] symbols)
        {
            SetScriptingDefineSymbols(Symbols.Except(symbols).ToArray());
        }

        public static void Remove(string symbol)
        {
            Remove(new string[] { symbol });
        }

        public static void Set(string[] symbols, bool enable)
        {
            if (enable)
            {
                Add(symbols);
            }
            else
            {
                Remove(symbols);
            }
        }

        public static void Set(string symbol, bool enable)
        {
            Set(new string[] { symbol }, enable);
        }

        static void SetScriptingDefineSymbols(string[] symbols)
        {
            SetScriptingDefineSymbols(string.Join(";", symbols));
        }

        static void SetScriptingDefineSymbols(string symbols)
        {
            PlayerSettings.SetScriptingDefineSymbols(CurrentNamedBuildTarget, symbols);
        }
    }
}
