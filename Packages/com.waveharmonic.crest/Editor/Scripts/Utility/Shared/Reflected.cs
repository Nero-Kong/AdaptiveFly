// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor.Build;
using UnityEngine;

namespace WaveHarmonic.Crest.Editor.Reflected
{
    static class BuildTargetGroup
    {
        public const int k_Server = -2;
    }

    static class BuildPlatform
    {
        internal static readonly Type s_BuildPlatformType = Type.GetType("UnityEditor.Build.BuildPlatform,UnityEditor.CoreModule");
        internal static readonly Type s_BuildPlatformArrayType = s_BuildPlatformType.MakeArrayType();

        static readonly FieldInfo s_NamedBuildTargetField = s_BuildPlatformType.GetField
        (
            "namedBuildTarget",
            BindingFlags.Instance | BindingFlags.Public
        );

        public static NamedBuildTarget GetNamedBuildTarget(object platform)
        {
            return (NamedBuildTarget)s_NamedBuildTargetField.GetValue(platform);
        }
    }

    static class BuildPlatforms
    {
        static readonly Type s_BuildPlatformsType = Type.GetType("UnityEditor.Build.BuildPlatforms,UnityEditor.CoreModule");
        static readonly PropertyInfo s_BuildPlatformsInstanceProperty = s_BuildPlatformsType.GetProperty("instance", BindingFlags.Static | BindingFlags.Public);
        static readonly MethodInfo s_GetValidPlatformsMethod = s_BuildPlatformsType.GetMethod("GetValidPlatforms", new Type[] { });
        static Array s_Platforms; // Should be safe to cache.

        public static Array GetValidPlatforms()
        {
            if (s_Platforms == null)
            {
                var instance = s_BuildPlatformsInstanceProperty.GetValue(null);

                // We cannot just cast to the type we want it seems.
                var enumerable = ((IEnumerable<object>)s_GetValidPlatformsMethod.Invoke(instance, null)).ToList();

                s_Platforms = Array.CreateInstance(BuildPlatform.s_BuildPlatformType, enumerable.Count);

                for (var i = 0; i < enumerable.Count; i++)
                {
                    s_Platforms.SetValue(enumerable[i], i);
                }
            }

            return s_Platforms;
        }
    }

    static class EditorGUILayout
    {
        static readonly MethodInfo s_BeginPlatformGroupingMethod = typeof(UnityEditor.EditorGUILayout).GetMethod
        (
            "BeginPlatformGrouping",
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            new Type[]
            {
                BuildPlatform.s_BuildPlatformArrayType,
                typeof(GUIContent),
            },
            null
        );

        static readonly object[] s_Parameters = new object[2];

        public static int BeginBuildTargetSelectionGrouping(GUIContent defaultTab)
        {
            var platforms = BuildPlatforms.GetValidPlatforms();

            s_Parameters[0] = platforms;
            s_Parameters[1] = defaultTab;

            var index = (int)s_BeginPlatformGroupingMethod.Invoke(null, s_Parameters);

            if (index < 0)
            {
                // Default
                return (int)UnityEditor.BuildTargetGroup.Unknown;
            }

            var target = BuildPlatform.GetNamedBuildTarget(platforms.GetValue(index));

            if (target == NamedBuildTarget.Server)
            {
                // Server
                return BuildTargetGroup.k_Server;
            }

            return (int)target.ToBuildTargetGroup();
        }
    }
}
