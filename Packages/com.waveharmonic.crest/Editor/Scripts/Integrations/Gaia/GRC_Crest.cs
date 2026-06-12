// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

using Gaia;
using UnityEditor;
using UnityEngine;

namespace WaveHarmonic.Crest
{
    sealed class GRC_Crest : GaiaRuntimeComponent
    {
#pragma warning disable CS0414
        [SerializeField]
        bool _Wind = true;
#pragma warning restore CS0414

        [SerializeField]
        bool _Swell = true;

#pragma warning disable CS0414
        [SerializeField]
        bool _ShallowWater = true;
#pragma warning restore CS0414

        GUIContent _HelpLink;
        GUIContent _PanelLabel;

        /// <inheritdoc/>
        public override GUIContent PanelLabel
        {
            get
            {
                if (_PanelLabel == null || _PanelLabel.text == "")
                {
                    _PanelLabel = new GUIContent("Crest Water", "Adds Crest Water to your scene.");
                }

                return _PanelLabel;
            }
        }

        /// <inheritdoc/>
        public override void Initialize()
        {
            // Order components appear in the UI. Try to keep in alphabetical order.
            m_orderNumber = 210;

            if (_HelpLink == null || _HelpLink.text == "")
            {
                _HelpLink = new GUIContent("Crest Online documentation", "Opens the documentation for the Crest Water System in your browser.");
            }
        }

        /// <inheritdoc/>
        public override void DrawUI()
        {
            // Displays "?" help button.
            DisplayHelp
            (
                "This module adds the Crest Water System to your scene. Please visit the link to learn more:",
                _HelpLink,
                "https://docs.crest.waveharmonic.com/About/Introduction.html"
            );

            EditorGUI.BeginChangeCheck();

            {
                _Swell = EditorGUILayout.Toggle("Swell Waves", _Swell);
                DisplayHelp("Whether to add swell waves to the scene. Swell waves will come from conditions far away from the scene. Modify the component after creation to customize.");

#if d_UnityModuleWind
                _Wind = EditorGUILayout.Toggle("Wind Waves", _Wind);
                DisplayHelp("Whether to add wind waves to the scene. These waves are based on local wind conditions. Requires Gaia's Wind Zone (note that the defaul wind value will produce no waves). Modify the component after creation to customize.");
#endif

#if d_WaveHarmonic_Crest_ShallowWater
                _ShallowWater = EditorGUILayout.Toggle("Shoreline Simulation", _ShallowWater);
                DisplayHelp("Whether to add a shoreline shallow water simulation to the scene. Modify the component after creation to customize.");
#endif

                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Remove")) RemoveFromScene();
                GUILayout.Space(15);
                if (GUILayout.Button("Apply")) AddToScene();
                GUILayout.EndHorizontal();
            }

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(this);
            }
        }

        /// Called when either "Apply" or "Create Runtime" is pressed.
        /// <inheritdoc/>
        public override void AddToScene()
        {
            // Re-initialize to keep user's changes.
            var water = FindAnyObjectByType<WaterRenderer>(FindObjectsInactive.Include);

            if (water == null)
            {
                water = new GameObject("Water").AddComponent<WaterRenderer>();
            }

            // Sea level is height above terrain bottom.
            var seaLevel = GaiaAPI.GetSeaLevel();

            water.transform.position = new Vector3(0f, seaLevel, 0f);

            var managed = water.transform.Find("Managed");

            if (managed == null)
            {
                managed = new GameObject("Managed").transform;
            }

            managed.SetParent(water.transform, worldPositionStays: false);

#if d_UnityModuleWind
            // Wind
            if (_Wind)
            {
                var wind = FindAnyObjectByType<WindManager>();

                if (wind != null)
                {
                    water.WindZone = wind.GetComponent<WindZone>();
                }
            }
#endif

            // Depth
            water.DepthLod.IncludeTerrainHeight = false;

            foreach (var terrain in Helpers.FindObjectsByType<Terrain>(FindObjectsInactive.Include))
            {
                var dp = terrain.GetComponentInChildren<DepthProbe>(includeInactive: true);

                if (dp == null)
                {
                    dp = new GameObject("WaterDepthProbe").AddComponent<DepthProbe>();
                }

                dp.gameObject.layer = water.Surface.Layer;
                dp.transform.SetParent(terrain.transform, worldPositionStays: false);
                dp.transform.localPosition = terrain.terrainData.size * 0.5f;
                var position = dp.transform.position;
                position.y = seaLevel;
                dp.transform.position = position;
                dp.transform.localScale = new(terrain.terrainData.size.x, 1f, terrain.terrainData.size.z);
                dp.Layers = 1 << terrain.gameObject.layer;
                // 1m below terrain bottom to 1m above maximum terrain height.
                dp.CaptureRange = new(-seaLevel + -1f, terrain.terrainData.size.y - seaLevel + 1);
                dp.Resolution = terrain.terrainData.heightmapResolution - 1;
                dp.Populate();
            }

#if d_UnityModuleWind
            // Wind Waves
            if (_Wind && water.WindZone != null)
            {
                GetOrAddComponentToScene<ShapeFFT>(managed, "WaterWindWaves", out _);
            }
            else
#endif
            {
                RemoveComponentFromScene<ShapeFFT>(managed);
            }

            // Swell Waves
            if (_Swell)
            {
                GetOrAddComponentToScene<ShapeGerstner>(managed, "WaterSwellWaves", out var waves);

                waves.OverrideGlobalWindDirection = true;
                waves.OverrideGlobalWindSpeed = true;
                waves.ReverseWaveWeight = 0;
                waves.Swell = true;

                if (!waves.TryGetComponent<ShapeFFT>(out var fft))
                {
                    fft = waves.gameObject.AddComponent<ShapeFFT>();
                    fft.Spectrum = AssetDatabase.LoadAssetAtPath<WaveSpectrum>("Packages/com.waveharmonic.crest/Runtime/Data/WaveSpectra/WavesSwell.asset");
                }

                fft.OverrideGlobalWindDirection = true;
                fft.OverrideGlobalWindSpeed = true;
                fft.OverrideGlobalWindTurbulence = true;
                fft.WindAlignment = 0.5f;
            }
            else
            {
                RemoveComponentFromScene<ShapeGerstner>(managed);
            }

#if d_WaveHarmonic_Crest_ShallowWater
            if (_ShallowWater)
            {
                water.FlowLod.Enabled = true;

                if (GetOrAddComponentToScene<ShallowWater.ShallowWaterSimulation>(managed, "ShorelineSimulation", out var sws))
                {
                    water.Surface.Material = AssetDatabase.LoadAssetAtPath<Material>("Packages/com.waveharmonic.crest/Runtime/Materials/Water (Flow).mat");
                    sws.Width = 256;
                }

                if (!sws.TryGetComponent<DepthProbe>(out var dp))
                {
                    dp = sws.gameObject.AddComponent<DepthProbe>();
                }

                dp.GenerateSignedDistanceField = false;

                sws.Preset = ShallowWater.ShallowWaterSimulationPreset.Shoreline;
                sws.Placement = Placement.Viewpoint;
                sws.DynamicSeabed = true;
            }
            else
            {
                RemoveComponentFromScene<ShallowWater.ShallowWaterSimulation>(managed);
            }
#endif
        }

        bool GetOrAddComponentToScene<T>(Transform managed, string name, out T component) where T : MonoBehaviour
        {
            component = managed.GetComponentInChildren<T>();

            var create = component == null;

            if (create)
            {
                component = new GameObject(name).AddComponent<T>();
                component.transform.SetParent(managed.transform, worldPositionStays: false);
            }

            return create;
        }

        void RemoveComponentFromScene<T>(Transform managed) where T : MonoBehaviour
        {
            var component = managed.GetComponentInChildren<T>();

            if (component != null)
            {
                DestroyImmediate(component.gameObject);
            }
        }

        /// Called when "Remove" is pressed.
        /// <inheritdoc/>
        public override void RemoveFromScene()
        {
            var water = FindAnyObjectByType<WaterRenderer>(FindObjectsInactive.Include);
            if (water != null) DestroyImmediate(water.gameObject);

            foreach (var terrain in Helpers.FindObjectsByType<Terrain>(FindObjectsInactive.Include))
            {
                var depthCache = terrain.GetComponentInChildren<DepthProbe>(includeInactive: true);
                if (depthCache != null) DestroyImmediate(depthCache.gameObject);
            }
        }
    }
}
