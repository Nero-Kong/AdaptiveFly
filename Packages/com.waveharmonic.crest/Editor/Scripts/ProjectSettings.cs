// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.UIElements;

namespace WaveHarmonic.Crest.Editor.Settings
{
    [System.Flags]
    enum SamplingMethod
    {
        Nothing = 0,

        [InspectorName("Multi-Scale")]
        MultiScale = 1 << 0,

        Stochastic = 1 << 1,

        Everything = ~0,
    }

    [System.Serializable]
    sealed partial class PlatformSettings
    {
        const string k_OverrideTooltip = "Override the feature for this platform";

        [HideInInspector]
        [SerializeField]
        internal bool _Default;


        [@Heading("Simulations", alwaysVisible: true)]

        [Tooltip(k_OverrideTooltip)]
        [@Hide(nameof(_Default))]
        [@InlineToggle]
        [SerializeField]
        internal bool _OverrideAlbedoSimulation;

        [@Label("Albedo")]
        [@DecoratedField]
        [SerializeField]
        internal bool _AlbedoSimulation = true;

        [Tooltip(k_OverrideTooltip)]
        [@Hide(nameof(_Default))]
        [@InlineToggle]
        [SerializeField]
        internal bool _OverrideAbsorptionSimulation;

        [@Label("Absorption")]
        [@DecoratedField]
        [SerializeField]
        internal bool _AbsorptionSimulation = true;

        [Tooltip(k_OverrideTooltip)]
        [@Hide(nameof(_Default))]
        [@InlineToggle]
        [SerializeField]
        internal bool _OverrideScatteringSimulation;

        [@Label("Scattering")]
        [@DecoratedField]
        [SerializeField]
        internal bool _ScatteringSimulation = true;

        [Tooltip(k_OverrideTooltip)]
        [@Hide(nameof(_Default))]
        [@InlineToggle]
        [SerializeField]
        internal bool _OverrideShadowSimulation;

        [@Label("Shadow")]
        [@DecoratedField]
        [SerializeField]
        internal bool _ShadowSimulation = true;


        [@Heading("Water Material", alwaysVisible: true)]

        [Tooltip(k_OverrideTooltip)]
        [@Hide(nameof(_Default))]
        [@InlineToggle]
        [SerializeField]
        internal bool _OverrideOutScattering;

        [@Label("Out-Scattering")]
        [Tooltip("Disables out-scattering for the surface and volume.\n\nOut-scattering darkens the water with depth.")]
        [@DecoratedField]
        [SerializeField]
        internal bool _OutScattering = true;


        [@Heading("Surface Material", alwaysVisible: true)]

        [Tooltip(k_OverrideTooltip)]
        [@Hide(nameof(_Default))]
        [@InlineToggle]
        [SerializeField]
        internal bool _OverrideNormalMaps;

        [@DecoratedField]
        [SerializeField]
        internal bool _NormalMaps = true;

        [Tooltip(k_OverrideTooltip)]
        [@Hide(nameof(_Default))]
        [@InlineToggle]
        [SerializeField]
        internal bool _OverridePlanarReflections;

        [@DecoratedField]
        [SerializeField]
        internal bool _PlanarReflections = true;

        [Tooltip(k_OverrideTooltip)]
        [@Hide(nameof(_Default))]
        [@InlineToggle]
        [SerializeField]
        internal bool _OverridePlanarReflectionsApplySmoothness;

        [@DecoratedField]
        [SerializeField]
        internal bool _PlanarReflectionsApplySmoothness = true;

        [Tooltip(k_OverrideTooltip)]
        [@Hide(nameof(_Default))]
        [@InlineToggle]
        [@SerializeField]
        internal bool _OverrideFoamSampling;

        [Tooltip("Which sampling method to use for foam.\n\nThese additional sampling techniques can be used together. Be wary that this will increase texture samples significantly.\n\nNothing: Uses the sampler set on the foam texture.\n\nMulti-Scale: Scales the foam texture by LOD to make foam pattern more visible at distances and reduces repetitive patterns. Doubles the foam texture samples.\n\nStochastic: reduces repetitive patterns. Triples the foam texture samples.")]
        [@DecoratedField]
        [@SerializeField]
        internal SamplingMethod _FoamSampling = SamplingMethod.MultiScale;

        [Tooltip(k_OverrideTooltip)]
        [@Hide(nameof(_Default))]
        [@InlineToggle]
        [SerializeField]
        internal bool _OverrideFoamBioluminescence;

        [@DecoratedField]
        [SerializeField]
        internal bool _FoamBioluminescence = true;

        [Tooltip(k_OverrideTooltip)]
        [@Hide(nameof(_Default))]
        [@InlineToggle]
        [SerializeField]
        internal bool _OverrideCausticsForceDistortion;

        [@DecoratedField]
        [SerializeField]
        internal bool _CausticsForceDistortion = true;

        [Tooltip(k_OverrideTooltip)]
        [@Hide(nameof(_Default))]
        [@InlineToggle]
        [SerializeField]
        internal bool _OverrideAdditionalLights;

        [Tooltip("Whether to calculate scattering from additional lights.")]
        [@DecoratedField]
        [SerializeField]
        internal bool _AdditionalLights = true;


        [@Heading("Rendering", alwaysVisible: true)]

        [Tooltip(k_OverrideTooltip)]
        [@Hide(nameof(_Default))]
        [@InlineToggle]
        [SerializeField]
        internal bool _OverrideSimpleTransparency;

        [Tooltip("Refraction like transparency without requiring the Opaque or Depth Texture.\n\nRequires a populated Water Depth Simulation to render correctly. See the Main sample for a working scene.")]
        [@DecoratedField]
        [SerializeField]
        internal bool _SimpleTransparency;

        PlatformSettings Default => ProjectSettings.Instance._PlatformSettings;
    }

    [FilePath(k_Path, FilePathAttribute.Location.ProjectFolder)]
    sealed partial class ProjectSettings : ScriptableSingleton<ProjectSettings>
    {
#pragma warning disable IDE0032 // Use auto property

        [@Heading("Variant Stripping", Heading.Style.Settings)]

        [@Group]

        [@DecoratedField, SerializeField]
        bool _DebugEnableStrippingLogging;

        [@Enable(nameof(_DebugEnableStrippingLogging))]
        [@DecoratedField, SerializeField]
        bool _DebugOnlyLogRemainingVariants;

        [Tooltip("Whether to strip broken variants.\n\nCurrently, the only known case is the point cookie variant being broken on Xbox.")]
        [@DecoratedField, SerializeField]
        bool _StripBrokenVariants = true;

        [@Heading("Features", Heading.Style.Settings)]

        [@Group]

        [Tooltip("Whether to use full precision sampling for half precision platforms (typically mobile).\n\nThis will solve rendering artifacts like minor bumps and staircasing.")]
        [@DecoratedField, SerializeField]
        bool _FullPrecisionDisplacementOnHalfPrecisionPlatforms = true;

        [Tooltip("Whether to render atmospheric scattering (ie fog) for pixels receiving aquatic scattering (underwater only).\n\nWhen disabled, if a pixel is receiving aquatic scattering, then it will not receive atmospheric scattering.")]
        [@DecoratedField, SerializeField]
        bool _RenderAtmosphericScatteringWhenUnderWater;

        [Tooltip("Renders the underwater effect after transparency and uses the more expensive mask.\n\nOne benefit is that transparent objects will be fogged (albeit incorrectly).\n\nThe downsides are that there can be artifacts if waves are very choppy, has a less impressive meniscus, and generally more expensive to execute.")]
        [@DecoratedField, SerializeField]
        bool _LegacyUnderwater;

        [@Space(10)]

        [@PlatformTabs]
        [SerializeField]
        internal int _Platforms;

        [@Label("Overriden Settings for Windows, Mac and Linux")]
        [@Show(nameof(_Platforms), (int)BuildTargetGroup.Standalone)]
        [@Stripped(Stripped.Style.PlatformTab, indent: true)]
        [SerializeField]
        internal PlatformSettings _PlatformSettingsDesktop = new();

        [@Label("Overriden Settings for Dedicated Server")]
        [@Show(nameof(_Platforms), -2)]
        [@Stripped(Stripped.Style.PlatformTab, indent: true)]
        [SerializeField]
        internal PlatformSettings _PlatformSettingsServer = new();

        [@Label("Overriden Settings for Android")]
        [@Show(nameof(_Platforms), (int)BuildTargetGroup.Android)]
        [@Stripped(Stripped.Style.PlatformTab, indent: true)]
        [SerializeField]
        internal PlatformSettings _PlatformSettingsAndroid = new();

        [@Label("Overriden Settings for iOS")]
        [@Show(nameof(_Platforms), (int)BuildTargetGroup.iOS)]
        [@Stripped(Stripped.Style.PlatformTab, indent: true)]
        [SerializeField]
        internal PlatformSettings _PlatformSettingsIOS = new();

        [@Label("Overriden Settings for tvOS")]
        [@Show(nameof(_Platforms), (int)BuildTargetGroup.tvOS)]
        [@Stripped(Stripped.Style.PlatformTab, indent: true)]
        [SerializeField]
        internal PlatformSettings _PlatformSettingsTVOS = new();

        [@Label("Overriden Settings for visionOS")]
        [@Show(nameof(_Platforms), (int)BuildTargetGroup.VisionOS)]
        [@Stripped(Stripped.Style.PlatformTab, indent: true)]
        [SerializeField]
        internal PlatformSettings _PlatformSettingsVisionOS = new();

        // Web has hard limitations on number of sampled textures. Set defaults with that
        // in mind so the surface renders.
        [@Label("Overriden Settings for Web")]
        [@Show(nameof(_Platforms), (int)BuildTargetGroup.WebGL)]
        [@Stripped(Stripped.Style.PlatformTab, indent: true)]
        [SerializeField]
        internal PlatformSettings _PlatformSettingsWeb = new()
        {
            _OverrideAbsorptionSimulation = true,
            _AbsorptionSimulation = false,
            _OverrideAlbedoSimulation = true,
            _AlbedoSimulation = false,
            _OverrideScatteringSimulation = true,
            _ScatteringSimulation = false,
            _OverrideShadowSimulation = true,
            _ShadowSimulation = false,

            _OverrideCausticsForceDistortion = true,
            _CausticsForceDistortion = false,
            _OverridePlanarReflections = true,
            _PlanarReflections = false,
            _OverrideFoamBioluminescence = true,
            _FoamBioluminescence = false,
        };

        // This will show if nothing else shows.
        [@Label("Default Settings")]
        [@Hide(nameof(_Platforms), (int)BuildTargetGroup.Standalone)]
        [@Hide(nameof(_Platforms), Reflected.BuildTargetGroup.k_Server)]
        [@Hide(nameof(_Platforms), (int)BuildTargetGroup.Android)]
        [@Hide(nameof(_Platforms), (int)BuildTargetGroup.iOS)]
        [@Hide(nameof(_Platforms), (int)BuildTargetGroup.WebGL)]
        [@Hide(nameof(_Platforms), (int)BuildTargetGroup.tvOS)]
        [@Hide(nameof(_Platforms), (int)BuildTargetGroup.VisionOS)]
        [@Stripped(Stripped.Style.PlatformTab, indent: true)]
        [SerializeField]
        internal PlatformSettings _PlatformSettings = new() { _Default = true };

#pragma warning restore IDE0032 // Use auto property

        internal const string k_Path = "ProjectSettings/Packages/com.waveharmonic.crest/Settings.asset";

        internal enum State
        {
            Dynamic,
            Disabled,
            Enabled,
        }

        internal static ProjectSettings Instance => instance;

        internal bool StripBrokenVariants => _StripBrokenVariants;
        internal bool DebugEnableStrippingLogging => _DebugEnableStrippingLogging;
        internal bool LogStrippedVariants => _DebugEnableStrippingLogging && !_DebugOnlyLogRemainingVariants;
        internal bool LogKeptVariants => _DebugEnableStrippingLogging && _DebugOnlyLogRemainingVariants;
        internal bool FullPrecisionDisplacementOnHalfPrecisionPlatforms => _FullPrecisionDisplacementOnHalfPrecisionPlatforms;
        internal bool RenderAtmosphericScatteringWhenUnderWater => _RenderAtmosphericScatteringWhenUnderWater;
        internal bool LegacyUnderwater => _LegacyUnderwater;

        internal PlatformSettings CurrentPlatformSettings =>
#if   PLATFORM_STANDALONE
            _PlatformSettingsDesktop;
#elif PLATFORM_SERVER
            _PlatformSettingsServer;
#elif PLATFORM_ANDROID
            _PlatformSettingsAndroid;
#elif PLATFORM_IOS
            _PlatformSettingsIOS;
#elif PLATFORM_TVOS
            _PlatformSettingsTVOS;
#elif PLATFORM_VISIONOS
            _PlatformSettingsVisionOS;
#else
            _PlatformSettings;
#endif

        internal bool _IsPlatformTabChange;
        readonly Dictionary<NamedBuildTarget, PlatformSettings> _PlatformSettingsMap = new();

        void OnEnable()
        {
            // Fixes not being editable.
            hideFlags = HideFlags.HideAndDontSave & ~HideFlags.NotEditable;

            _PlatformSettingsMap.Clear();
            _PlatformSettingsMap.Add(NamedBuildTarget.Standalone, _PlatformSettingsDesktop);
            _PlatformSettingsMap.Add(NamedBuildTarget.Server, _PlatformSettingsServer);
            _PlatformSettingsMap.Add(NamedBuildTarget.Android, _PlatformSettingsAndroid);
            _PlatformSettingsMap.Add(NamedBuildTarget.iOS, _PlatformSettingsIOS);
            _PlatformSettingsMap.Add(NamedBuildTarget.tvOS, _PlatformSettingsTVOS);
            _PlatformSettingsMap.Add(NamedBuildTarget.VisionOS, _PlatformSettingsVisionOS);
        }


        internal static void Save()
        {
            instance.Save(saveAsText: true);
        }

        [@OnChange(skipIfInactive: false)]
        void OnChange(string path, object previous)
        {
            _IsPlatformTabChange = path == nameof(_Platforms);

            if (path.StartsWithNoAlloc("_PlatformSettings"))
            {
                UpdateSymbols();
                return;
            }

            switch (path)
            {
                case nameof(_FullPrecisionDisplacementOnHalfPrecisionPlatforms):
                case nameof(_RenderAtmosphericScatteringWhenUnderWater):
                case nameof(_LegacyUnderwater):
                    UpdateSymbols();
                    break;
            }
        }

        void UpdateScriptingSymbols()
        {
            foreach (var build in _PlatformSettingsMap.Keys)
            {
                ScriptingSymbols.s_OverrideCurrentNamedBuildTarget = true;
                ScriptingSymbols.s_CurrentNamedBuildTargetOverride = build;
                ScriptingSymbols.Set(ProjectSymbols.k_LegacyUnderwaterScriptingSymbol, _LegacyUnderwater);
                ScriptingSymbols.Set(ProjectSymbols.k_SimpleTransparencyScriptingSymbol, _PlatformSettingsMap[build].SimpleTransparency);
                ScriptingSymbols.Set(ProjectSymbols.k_PlanarReflectionApplySmoothnessScriptingSymbol, !_PlatformSettingsMap[build].PlanarReflectionsApplySmoothness);
                ScriptingSymbols.s_OverrideCurrentNamedBuildTarget = false;
            }
        }

        void UpdateSymbols()
        {
            UpdateScriptingSymbols();
            ShaderSettingsGenerator.Generate();
        }

        sealed class ProjectSymbols : AssetModificationProcessor
        {
            // The default value should not produce a symbol.
            public const string k_LegacyUnderwaterScriptingSymbol = "d_Crest_LegacyUnderwater";
            public const string k_SimpleTransparencyScriptingSymbol = "d_Crest_SimpleTransparency";
            public const string k_PlanarReflectionApplySmoothnessScriptingSymbol = "d_Crest_DisablePlanarReflectionApplySmoothness";

            static FileSystemWatcher s_Watcher;

            // Will run on load and recompile preventing symbol removal in player settings.
            [InitializeOnLoadMethod]
            static void OnLoad()
            {
                if (Instance != null)
                {
                    Instance.UpdateScriptingSymbols();
                }

                Directory.CreateDirectory(Path.GetDirectoryName(k_Path));

                s_Watcher = new(Path.GetDirectoryName(k_Path))
                {
                    Filter = Path.GetFileName(k_Path),
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };

                s_Watcher.Changed -= OnChanged;
                s_Watcher.Changed += OnChanged;
            }

            // Handle external edits. Possibly unreliable, but not important if fails.
            static void OnChanged(object sender, FileSystemEventArgs e)
            {
                EditorApplication.delayCall += () =>
                {
                    if (Instance != null && Instance._IsPlatformTabChange)
                    {
                        return;
                    }

                    // Destroy instance to reflect changes.
                    Helpers.Destroy(Instance);
                    typeof(ScriptableSingleton<ProjectSettings>)
                        .GetField("s_Instance", BindingFlags.Static | BindingFlags.NonPublic)
                        .SetValue(null, null);
                    Instance.UpdateSymbols();
                };
            }

            static AssetDeleteResult OnWillDeleteAsset(string path, RemoveAssetOptions options)
            {
                // Only remove symbols if this file is deleted.
                if (Path.GetFullPath(path) == GetCurrentFileName())
                {
                    ScriptingSymbols.Remove(ScriptingSymbols.Symbols.Where(x => x.StartsWith("d_Crest_")).ToArray());
                }

                return AssetDeleteResult.DidNotDelete;
            }

            static string GetCurrentFileName([System.Runtime.CompilerServices.CallerFilePath] string fileName = null)
            {
                return fileName;
            }
        }
    }

    sealed class SettingsProvider : UnityEditor.SettingsProvider
    {
        static readonly string[] s_ShaderGraphs = new string[]
        {
            "Packages/com.waveharmonic.crest/Runtime/Shaders/Surface/Water.shadergraph",
            "Packages/com.waveharmonic.crest/Shared/Shaders/Lit.shadergraph",
            "Packages/com.waveharmonic.crest.paint/Samples/Colorado/Shaders/SpeedTree8_PBRLit.shadergraph",
            "Packages/com.waveharmonic.crest.paint/Samples/Colorado/Shaders/Environment (Splat Map).shadergraph",
        };

        UnityEditor.Editor _Editor;

        SettingsProvider(string path, SettingsScope scope = SettingsScope.User) : base(path, scope)
        {
            // Empty
        }

        static bool IsSettingsAvailable()
        {
            return File.Exists(ProjectSettings.k_Path);
        }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            base.OnActivate(searchContext, rootElement);
            _Editor = UnityEditor.Editor.CreateEditor(ProjectSettings.Instance);
            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        public override void OnDeactivate()
        {
            base.OnDeactivate();
            Helpers.Destroy(_Editor);
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        void OnUndoRedo()
        {
            ProjectSettings.Save();
        }

        public override void OnGUI(string searchContext)
        {
            if (_Editor.target == null)
            {
                Helpers.Destroy(_Editor);
                _Editor = UnityEditor.Editor.CreateEditor(ProjectSettings.Instance);
                return;
            }

            // Pad similar to settings header.
            var style = new GUIStyle();
            style.padding.left = 8;
            EditorGUILayout.BeginVertical(style);

            // Same label with as other settings.
            EditorGUIUtility.labelWidth = 251;

            EditorGUI.BeginChangeCheck();

            _Editor.OnInspectorGUI();

            GUILayout.Space(10 * 2);

            if (GUILayout.Button("Repair Shaders"))
            {
                foreach (var path in s_ShaderGraphs)
                {
                    if (!File.Exists(path)) continue;
                    AssetDatabase.ImportAsset(path);
                }
            }

            EditorGUILayout.EndVertical();
        }

        [SettingsProvider]
        static UnityEditor.SettingsProvider Create()
        {
            if (ProjectSettings.Instance)
            {
                var provider = new SettingsProvider("Project/Crest", SettingsScope.Project);
                provider.keywords = GetSearchKeywordsFromSerializedObject(new(ProjectSettings.Instance));
                return provider;
            }

            // Settings Asset doesn't exist yet; no need to display anything in the Settings window.
            return null;
        }
    }

    [CustomEditor(typeof(ProjectSettings))]
    sealed class ProjectSettingsEditor : Inspector
    {
        protected override void OnChange()
        {
            base.OnChange();

            // Commit all changes. Normally settings are written when user hits save or exits
            // without any undo/redo entry and dirty state. No idea how to do the same.
            // SaveChanges and hasUnsavedChanges on custom editor did not work.
            // Not sure if hooking into EditorSceneManager.sceneSaving is correct.
            ProjectSettings.Save();
        }
    }

    partial class ProjectSettings : ISerializationCallbackReceiver
    {
        [SerializeField, HideInInspector]
        int _Version = 0;

        [SerializeField, HideInInspector]
        internal int _MaterialVersion = MaterialUpgrader.k_MaterialVersion;

        public void OnAfterDeserialize()
        {
            if (_Version == 0)
            {
                _MaterialVersion = 0;
            }

            _Version = 1;
        }

        public void OnBeforeSerialize()
        {

        }
    }
}
