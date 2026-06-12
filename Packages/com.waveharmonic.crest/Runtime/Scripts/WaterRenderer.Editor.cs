// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;

namespace WaveHarmonic.Crest
{
    [@ExecuteDuringEditMode(ExecuteDuringEditMode.Include.None, ExecuteDuringEditMode.Options.Singleton)]
    [SelectionBase]
    [AddComponentMenu(Constants.k_MenuPrefixScripts + "Water Renderer")]
    [@HelpURL("About/Introduction.html")]
    sealed partial class WaterRenderer
    {
        internal const string k_ProxyShader = "Hidden/Crest/Editor/WaterProxy";
        internal GameObject _ProxyPlane;
        private protected override bool CanRunInEditMode => !_ShowWaterProxyPlane;
        internal bool IsSceneViewActive { get; set; }
        int _LastFrameSceneCamera;
        internal Camera _SceneCameraWithMouseHover;
        internal bool _CurrentSceneCameraHasMouseHover;

        private protected override void Reset()
        {
            _Underwater._Material = AssetDatabase.LoadAssetAtPath<Material>("Packages/com.waveharmonic.crest/Runtime/Materials/Water Volume.mat");

            _Surface.Reset();
            Meniscus.Reset();

            base.Reset();
        }

        // Tracks the scene view rendering to determine if a scene camera is active.
        void OnBeginSceneCameraRendering(Camera camera)
        {
            if (camera.cameraType == CameraType.SceneView)
            {
                IsSceneViewActive = true;
                _LastFrameSceneCamera = Time.frameCount;
            }

            if (_LastFrameSceneCamera < Time.frameCount - 1)
            {
                IsSceneViewActive = false;
            }

            if (camera.cameraType != CameraType.SceneView)
            {
                return;
            }

            foreach (SceneView sceneView in SceneView.sceneViews)
            {
                if (sceneView == null)
                {
                    continue;
                }

                if (sceneView.camera != camera)
                {
                    continue;
                }

                if (sceneView.cameraViewport.Contains(Event.current.mousePosition))
                {
                    _SceneCameraWithMouseHover = sceneView.camera;
                    _CurrentSceneCameraHasMouseHover = true;
                    return;
                }
            }
        }

        void OnEndSceneCameraRendering(Camera camera)
        {
            _CurrentSceneCameraHasMouseHover = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void OnDomainReload()
        {
            AfterRuntimeLoad();
        }

        [UnityEditor.Callbacks.DidReloadScripts]
        static void OnReLoadScripts()
        {
            AfterScriptReload();
        }

        [@OnChange]
        void OnChange(string path, object previous)
        {
            switch (path)
            {
                case nameof(_MultipleViewpoints):
                case nameof(_EditorMultipleViewpoints):
                    Rebuild();
                    break;
                case nameof(_ExtentsSizeMultiplier):
                case nameof(_GeometryDownSampleFactor):
                    Surface._Rebuild = true;
                    break;
                case nameof(_ShowWaterProxyPlane):
                    SetShowProxyPlane((bool)previous, _ShowWaterProxyPlane);
                    break;
            }
        }

        void SetShowProxyPlane(bool previous, bool current)
        {
            if (previous == current) return;
            if (!isActiveAndEnabled || Application.isPlaying) return;
            if (!(runInEditMode = !_ShowWaterProxyPlane)) OnDisable();
        }
    }

    partial class WaterRenderer
    {
        /// <inheritdoc/>
        private protected override void OnValidate()
        {
            base.OnValidate();

            // Must be at least 0.25, and must be on a power of 2
            _ScaleRange.x = Mathf.Pow(2f, Mathf.Round(Mathf.Log(Mathf.Max(_ScaleRange.x, 0.25f), 2f)));

            if (_ScaleRange.y < Mathf.Infinity)
            {
                // otherwise must be at least 0.25, and must be on a power of 2
                _ScaleRange.y = Mathf.Pow(2f, Mathf.Round(Mathf.Log(Mathf.Max(_ScaleRange.y, _ScaleRange.x), 2f)));
            }

            // Gravity 0 makes waves freeze which is weird but doesn't seem to break anything so allowing this for now
            _GravityMultiplier = Mathf.Max(_GravityMultiplier, 0f);

            // LOD data resolution multiple of 2 for general GPU texture reasons (like pixel quads)
            _Resolution -= _Resolution % 2;

            _GeometryDownSampleFactor = Mathf.ClosestPowerOfTwo(Mathf.Max(_GeometryDownSampleFactor, 1));

            var remGeo = _Resolution % _GeometryDownSampleFactor;
            if (remGeo > 0)
            {
                var newLDR = _Resolution - (_Resolution % _GeometryDownSampleFactor);
                Debug.LogWarning
                (
                    $"Crest: Adjusted Lod Data Resolution from {_Resolution} to {newLDR} to ensure the Geometry Down Sample Factor is a factor ({_GeometryDownSampleFactor}).",
                    this
                );

                _Resolution = newLDR;
            }
        }
    }
}

#endif
