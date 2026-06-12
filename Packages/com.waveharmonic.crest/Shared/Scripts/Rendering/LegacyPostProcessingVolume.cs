// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#if d_UnityPostProcessing

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using WaveHarmonic.Crest.Internal;

namespace WaveHarmonic.Crest.Examples
{
    // ExecuteDuringEditMode does not work with scene camera.
#if !CREST_DEBUG
    [AddComponentMenu("")]
#endif
    [ExecuteAlways, RequireComponent(typeof(PostProcessVolume))]
    sealed class LegacyPostProcessingVolume : CustomBehaviour
    {
        [@Layer]
        [SerializeField]
        int _Layer;

        readonly List<PostProcessVolume> _QuickVolumes = new();

        private protected override void OnEnable()
        {
            base.OnEnable();

            if (!RenderPipelineHelper.IsLegacy)
            {
                return;
            }

            if (Helpers.IsWebGPU)
            {
                return;
            }

            _QuickVolumes.Clear();

            foreach (var volume in GetComponents<PostProcessVolume>())
            {
                if (volume.sharedProfile == null) continue;
                _QuickVolumes.Add(PostProcessManager.instance.QuickVolume(_Layer, volume.priority, volume.sharedProfile.settings.ToArray()));
            }
        }

        void OnDisable()
        {
            foreach (var volume in _QuickVolumes)
            {
                if (volume == null) continue;
                Helpers.Destroy(volume.profile);
                var gameObject = volume.gameObject;
                Helpers.Destroy(volume);
                Helpers.Destroy(gameObject);
            }
        }
    }
}

#else

using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using WaveHarmonic.Crest.Editor;

namespace WaveHarmonic.Crest.Examples
{
#if !CREST_DEBUG
    [AddComponentMenu("")]
#endif
    [ExecuteAlways]
    sealed class LegacyPostProcessingVolume : MonoBehaviour
    {
        [@Layer]
        [SerializeField]
        int _Layer;

        static string s_SceneName;

        void Awake()
        {
            // Ask only once per scene load.
            var scene = SceneManager.GetActiveScene();
            if (!RenderPipelineHelper.IsLegacy || s_SceneName == scene.name)
            {
                return;
            }

            s_SceneName = scene.name;

#if UNITY_EDITOR
            var install = EditorUtility.DisplayDialog
            (
                "Missing Package",
                "This sample scene requires the post-processing package when using the built-in renderer. Without it the scene will be overexposed. Would you like to install it?",
                "Install",
                "Ignore"
            );

            if (install)
            {
                PackageManagerHelpers.AddMissingPackage("com.unity.postprocessing");
            }
#endif
        }

        void OnEnable()
        {
            if (!RenderPipelineHelper.IsLegacy)
            {
                return;
            }

            Debug.LogWarning("Crest: This scene requires the post-processing package. Without it the scene will be overexposed.");
        }
    }
}

#endif
