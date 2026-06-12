// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.Compilation;
using UnityEngine.Rendering;
using WaveHarmonic.Crest.Editor.Settings;

namespace WaveHarmonic.Crest.Editor
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    sealed class GenerateShaderSettings : System.Attribute { }

    static class ShaderSettingsGenerator
    {
        [DidReloadScripts]
        static void OnReloadScripts()
        {
            EditorApplication.update -= GenerateAfterReloadScripts;
            EditorApplication.update += GenerateAfterReloadScripts;
        }

        static async void GenerateAfterReloadScripts()
        {
            if (EditorApplication.isCompiling)
            {
                return;
            }

            if (EditorApplication.isUpdating)
            {
                return;
            }

            EditorApplication.update -= GenerateAfterReloadScripts;

            // Generate HLSL from C#. Only targets WaveHarmonic.Crest assemblies.
            await ShaderGeneratorUtility.GenerateAll();
            AssetDatabase.Refresh();
        }

        internal static void Generate()
        {
            if (EditorApplication.isCompiling)
            {
                return;
            }

            // Could not ShaderGeneratorUtility.GenerateAll to work without recompiling…
            CompilationPipeline.RequestScriptCompilation();
        }

        sealed class AssetPostProcessor : AssetPostprocessor
        {
            const string k_SettingsPath = "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Settings/";

            static async void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] movedTo, string[] movedFrom, bool domainReload)
            {
                // Unused.
                _ = deleted; _ = movedTo; _ = movedFrom; _ = domainReload;

                if (EditorApplication.isCompiling)
                {
#if CREST_DEBUG
                    if (imported.Count(x => x.StartsWithNoAlloc(k_SettingsPath)) > 0)
                    {
                        UnityEngine.Debug.Log($"Crest: Settings.Crest.hlsl changed during compilation!");
                    }
#endif
                    return;
                }

                if (EditorApplication.isUpdating)
                {
#if CREST_DEBUG
                    if (imported.Count(x => x.StartsWithNoAlloc(k_SettingsPath)) > 0)
                    {
                        UnityEngine.Debug.Log($"Crest: Settings.Crest.hlsl changed during asset database update!");
                    }
#endif
                    return;
                }

                // Regenerate if file changed like re-importing.
                if (imported.Count(x => x.StartsWithNoAlloc(k_SettingsPath)) > 0)
                {
#if CREST_DEBUG
                    UnityEngine.Debug.Log($"Crest: Settings.Crest.hlsl changed!");
#endif
                    // Generate HLSL from C#. Only targets WaveHarmonic.Crest assemblies.
                    await ShaderGeneratorUtility.GenerateAll();
                    AssetDatabase.Refresh();
                }
            }
        }
    }

    [GenerateHLSL(sourcePath = "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Settings/Settings.Crest")]
    sealed class ShaderSettings
    {
        // These two are here for compute shaders.
        public static int s_CrestPackageHDRP = 0
#if d_UnityHDRP
            + 1
#endif
            ;

        public static int s_CrestPackageURP = 0
#if d_UnityURP
            + 1
#endif
            ;

        public static int s_CrestPortals =
#if d_CrestPortals
            1
#else
            0
#endif
        ;

        public static int s_CrestShiftingOrigin =
#if d_WaveHarmonic_Crest_ShiftingOrigin
            1
#else
            0
#endif
        ;

        // Active when build target is activated:
        // https://docs.unity3d.com/6000.3/Documentation/Manual/scripting-symbol-reference.html

        public static int s_CrestPlatformStandalone =
#if PLATFORM_STANDALONE
            1 +
#endif
            0;

        public static int s_CrestPlatformServer =
#if PLATFORM_SERVER
            1 +
#endif
            0;

        public static int s_CrestPlatformAndroid =
#if PLATFORM_ANDROID
            1 +
#endif
            0;

        public static int s_CrestPlatformIOS =
#if PLATFORM_IOS
            1 +
#endif
            0;

        public static int s_CrestPlatformWeb =
#if PLATFORM_WEBGL
            1 +
#endif
            0;

        public static int s_CrestPlatformTVOS =
#if PLATFORM_TVOS
            1 +
#endif
            0;

        public static int s_CrestPlatformVISIONOS =
#if PLATFORM_VISIONOS
            1 +
#endif
            0;

        public static int s_CrestFullPrecisionDisplacement = ProjectSettings.Instance.FullPrecisionDisplacementOnHalfPrecisionPlatforms ? 1 : 0;

        public static int s_CrestDiscardAtmosphericScattering = ProjectSettings.Instance.RenderAtmosphericScatteringWhenUnderWater ? 0 : 1;

        public static int s_CrestLegacyUnderwater = ProjectSettings.Instance.LegacyUnderwater ? 1 : 0;
    }

    [@GenerateShaderSettings]
    [GenerateHLSL(sourcePath = "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Settings/Settings.Crest.Default")]
    sealed partial class ShaderSettingsDefault
    {
        static PlatformSettings Settings => ProjectSettings.Instance._PlatformSettings;
    }

    [@GenerateShaderSettings]
    [GenerateHLSL(sourcePath = "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Settings/Settings.Crest.Standalone")]
    sealed partial class ShaderSettingsStandalone
    {
        static PlatformSettings Settings => ProjectSettings.Instance._PlatformSettingsDesktop;
    }

    [@GenerateShaderSettings]
    [GenerateHLSL(sourcePath = "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Settings/Settings.Crest.Server")]
    sealed partial class ShaderSettingsServer
    {
        static PlatformSettings Settings => ProjectSettings.Instance._PlatformSettingsServer;
    }

    [@GenerateShaderSettings]
    [GenerateHLSL(sourcePath = "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Settings/Settings.Crest.Android")]
    sealed partial class ShaderSettingsAndroid
    {
        static PlatformSettings Settings => ProjectSettings.Instance._PlatformSettingsAndroid;
    }

    [@GenerateShaderSettings]
    [GenerateHLSL(sourcePath = "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Settings/Settings.Crest.iOS")]
    sealed partial class ShaderSettingsIOS
    {
        static PlatformSettings Settings => ProjectSettings.Instance._PlatformSettingsIOS;
    }

    [@GenerateShaderSettings]
    [GenerateHLSL(sourcePath = "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Settings/Settings.Crest.Web")]
    sealed partial class ShaderSettingsWeb
    {
        static PlatformSettings Settings => ProjectSettings.Instance._PlatformSettingsWeb;
    }

    [@GenerateShaderSettings]
    [GenerateHLSL(sourcePath = "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Settings/Settings.Crest.tvOS")]
    sealed partial class ShaderSettingsTVOS
    {
        static PlatformSettings Settings => ProjectSettings.Instance._PlatformSettingsTVOS;
    }

    [@GenerateShaderSettings]
    [GenerateHLSL(sourcePath = "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Settings/Settings.Crest.visionOS")]
    sealed partial class ShaderSettingsVisionOS
    {
        static PlatformSettings Settings => ProjectSettings.Instance._PlatformSettingsVisionOS;
    }
}
