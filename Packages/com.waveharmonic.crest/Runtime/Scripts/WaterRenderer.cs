// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using WaveHarmonic.Crest.Internal;
using WaveHarmonic.Crest.RelativeSpace;
using WaveHarmonic.Crest.Utility;

namespace WaveHarmonic.Crest
{
    interface IReportWaveDisplacement
    {
        /// <summary>
        /// Vertical displacement which affects scale via DropDetailHeightBasedOnWaves.
        /// </summary>
        float ReportWaveDisplacement(WaterRenderer water, float displacement);
    }

    /// <summary>
    /// The main script for the water system.
    /// </summary>
    /// <remarks>
    /// Attach this to an object to create water. This script initializes the various
    /// data types and systems and moves/scales the water based on the viewpoint. It
    /// also hosts a number of global settings that can be tweaked here.
    /// </remarks>
    public sealed partial class WaterRenderer : ManagerBehaviour<WaterRenderer>
    {
        internal const string k_RunUpdateMarker = "Crest.WaterRenderer.RunUpdate";

        static readonly Unity.Profiling.ProfilerMarker s_RunUpdateMarker = new(k_RunUpdateMarker);

        internal static partial class ShaderIDs
        {
            public static readonly int s_Center = Shader.PropertyToID("g_Crest_WaterCenter");
            public static readonly int s_Scale = Shader.PropertyToID("g_Crest_WaterScale");
            public static readonly int s_Time = Shader.PropertyToID("g_Crest_Time");
            public static readonly int s_CascadeData = Shader.PropertyToID("g_Crest_CascadeData");
            public static readonly int s_CascadeDataSource = Shader.PropertyToID("g_Crest_CascadeDataSource");
            public static readonly int s_LodChange = Shader.PropertyToID("g_Crest_LodChange");
            public static readonly int s_MeshScaleLerp = Shader.PropertyToID("g_Crest_MeshScaleLerp");
            public static readonly int s_LodCount = Shader.PropertyToID("g_Crest_LodCount");

            public static readonly int s_WaterDepthAtViewer = Shader.PropertyToID("g_Crest_WaterDepthAtViewer");
            public static readonly int s_MaximumVerticalDisplacement = Shader.PropertyToID("g_Crest_MaximumVerticalDisplacement");
            public static readonly int s_HorizonNormal = Shader.PropertyToID("g_Crest_HorizonNormal");

            // Shader Properties
            public static readonly int s_AbsorptionColor = Shader.PropertyToID("_Crest_AbsorptionColor");
            public static readonly int s_Absorption = Shader.PropertyToID("_Crest_Absorption");
            public static readonly int s_Scattering = Shader.PropertyToID("_Crest_Scattering");
            public static readonly int s_Anisotropy = Shader.PropertyToID("_Crest_Anisotropy");
            public static readonly int s_AmbientTerm = Shader.PropertyToID("_Crest_AmbientTerm");
            public static readonly int s_DirectTerm = Shader.PropertyToID("_Crest_DirectTerm");
            public static readonly int s_ShadowsAffectsAmbientFactor = Shader.PropertyToID("_Crest_ShadowsAffectsAmbientFactor");
            public static readonly int s_PlanarReflectionsEnabled = Shader.PropertyToID("_Crest_PlanarReflectionsEnabled");
            public static readonly int s_Occlusion = Shader.PropertyToID("_Crest_Occlusion");
            public static readonly int s_OcclusionUnderwater = Shader.PropertyToID("_Crest_OcclusionUnderwater");

            // Motion Vectors
            public static readonly int s_CenterDelta = Shader.PropertyToID("g_Crest_WaterCenterDelta");
            public static readonly int s_ScaleChange = Shader.PropertyToID("g_Crest_WaterScaleChange");

            // Underwater
            public static readonly int s_VolumeExtinctionLength = Shader.PropertyToID("_Crest_VolumeExtinctionLength");

            // Lighting
            public static readonly int s_PrimaryLightDirection = Shader.PropertyToID("g_Crest_PrimaryLightDirection");
            public static readonly int s_PrimaryLightIntensity = Shader.PropertyToID("g_Crest_PrimaryLightIntensity");
            public static readonly int s_PrimaryLightFallback = Shader.PropertyToID("g_Crest_PrimaryLightFallback");
        }


        //
        // Viewer
        //

        Transform GetViewpoint()
        {
            if (MultipleViewpoints)
            {
                return CurrentCamera == null ? null : CurrentCamera.transform;
            }

#if UNITY_EDITOR
            if (EditorMultipleViewpoints && CurrentCamera != null && CurrentCamera.cameraType == CameraType.SceneView)
            {
                return CurrentCamera.transform;
            }

            if (!EditorMultipleViewpoints && !Application.isPlaying && _FollowSceneCamera && SceneView.lastActiveSceneView != null && IsSceneViewActive)
            {
                return SceneView.lastActiveSceneView.camera.transform;
            }
#endif

            if (_Viewpoint != null)
            {
                return _Viewpoint;
            }

            // Even with performance improvements, it is still good to cache whenever possible.
            var camera = Viewer;

            if (camera != null)
            {
                return camera.transform;
            }

            return null;
        }

        internal Camera GetViewer(bool includeSceneCamera = true, bool initial = false)
        {
            if (!initial && MultipleViewpoints)
            {
                return CurrentCamera;
            }

#if UNITY_EDITOR
            if (includeSceneCamera)
            {
                if (!initial && EditorMultipleViewpoints)
                {
                    if (CurrentCamera != null && CurrentCamera.cameraType == CameraType.SceneView)
                    {
                        return CurrentCamera;
                    }
                }
                else if (_FollowSceneCamera && IsSceneViewActive && !Application.isPlaying && SceneView.lastActiveSceneView != null)
                {
                    return SceneView.lastActiveSceneView.camera;
                }
            }
#endif

            if (_Camera != null)
            {
                return _Camera;
            }

            // Unity has greatly improved performance of this operation in 2019.4.9.
            return Camera.main;
        }

        /// <summary>
        /// The current viewer (center of detail).
        /// </summary>
        internal Camera CurrentCamera { get; private set; }

        readonly SampleCollisionHelper _CenterOfDetailDisplacementCorrectionHelper = new();


        //
        // Viewer Height
        //

        /// <summary>
        /// The water changes scale when viewer changes altitude, this gives the interpolation param between scales.
        /// </summary>
        internal float ViewerAltitudeLevelAlpha { get; private set; }

        /// <summary>
        /// Vertical offset of camera vs water surface.
        /// </summary>
        public float ViewerHeightAboveWater { get; private set; }

        /// <summary>
        /// Vertical offset of viewpoint vs water surface.
        /// </summary>
        public float ViewpointHeightAboveWater { get; private set; }

        /// <summary>
        /// Distance of camera to shoreline. Positive if over water and negative if over land.
        /// </summary>
        public float ViewerDistanceToShoreline { get; private set; }

        /// <summary>
        /// Smoothly varying version of viewpoint height to combat sudden changes in water level that are possible
        /// when there are local bodies of water
        /// </summary>
        float _ViewpointHeightAboveWaterSmooth;

        readonly SampleCollisionHelper _SampleHeightHelper = new();
        readonly SampleDepthHelper _SampleDepthHelper = new();

        internal float _ViewerHeightAboveWaterPerCamera;
        readonly SampleCollisionHelper _SampleHeightHelperPerCamera = new();


        //
        // Teleport Threshold
        //

        float _TeleportTimerForHeightQueries;
        bool _IsFirstFrameSinceEnabled = true;
        internal bool _HasTeleportedThisFrame;
        Vector3 _OldViewpointPosition;

#if d_WaveHarmonic_Crest_ShiftingOrigin
        Vector3 TeleportOriginThisFrame => ShiftingOrigin.ShiftThisFrame;
#else
        Vector3 TeleportOriginThisFrame => Vector3.zero;
#endif

        //
        // Wind
        //

        internal float WindSpeedKPH => _WindSpeed;

#if d_UnityModuleWind
        bool WindSpeedOverriden => _WindZone == null || _OverrideWindZoneWindSpeed;
        bool WindDirectionOverriden => _WindZone == null || _OverrideWindZoneWindDirection;
        bool WindTurbulenceOverriden => _WindZone == null || _OverrideWindZoneWindTurbulence;
#endif

        float GetWindSpeed()
        {
            return
#if d_UnityModuleWind
                !WindSpeedOverriden ? _WindZone.windMain * 3.6f :
#endif
                _WindSpeed;
        }

        float GetWindDirection()
        {
            return
#if d_UnityModuleWind
                !WindDirectionOverriden ? Mathf.Atan2(_WindZone.transform.forward.z, _WindZone.transform.forward.x) * Mathf.Rad2Deg :
#endif
                _WindDirection;
        }

        float GetWindTurbulence()
        {
            return
#if d_UnityModuleWind
                !WindTurbulenceOverriden ? _WindZone.windTurbulence :
#endif
                _WindTurbulence;
        }


        //
        // Transform
        //

        internal Vector3 Position { get; private set; }
        internal GameObject Container { get; private set; }

        /// <summary>
        /// Sea level is given by y coordinate of GameObject with WaterRenderer script.
        /// </summary>
        public float SeaLevel => Position.y;

        // Anything higher (minus 1 for near plane) will be clipped.
        const float k_RenderAboveSeaLevel = 10000f;
        // Anything lower will be clipped.
        const float k_RenderBelowSeaLevel = 10000f;

        Matrix4x4[] _ProjectionMatrix;
        internal Matrix4x4 GetProjectionMatrix(int slice) => _ProjectionMatrix[slice];

        internal static Matrix4x4 CalculateViewMatrixFromSnappedPositionRHS(Vector3 snapped)
        {
            return Helpers.CalculateWorldToCameraMatrixRHS(snapped + Vector3.up * k_RenderAboveSeaLevel, Quaternion.AngleAxis(90f, Vector3.right));
        }


        //
        // Time Provider
        //

        /// <summary>
        /// Loosely a stack for time providers.
        /// </summary>
        /// <remarks>
        /// The last <see cref="TimeProvider"/> in the list is the active one. When a
        /// <see cref="TimeProvider"/> gets added to the stack, it is bumped to the top of
        /// the list. When a <see cref="TimeProvider"/> is removed, all instances of it are
        /// removed from the stack. This is less rigid than a real stack which would be
        /// harder to use as users have to keep a close eye on the order that things are
        /// pushed/popped.
        /// </remarks>
        public Utility.Internal.Stack<ITimeProvider> TimeProviders { get; private set; } = new();

        /// <summary>
        /// The current time provider.
        /// </summary>
        public ITimeProvider TimeProvider => TimeProviders.Peek();

        internal float CurrentTime => TimeProvider.Time;
        internal float DeltaTime => TimeProvider.Delta;


        //
        // Environment
        //

        /// <summary>
        /// The primary light that affects the water. This should be a directional light.
        /// </summary>
        Light GetPrimaryLight() => _PrimaryLight == null ? RenderSettings.sun : _PrimaryLight;

        /// <summary>
        /// Physics gravity applied to water.
        /// </summary>
        public float Gravity => _GravityMultiplier * Mathf.Abs(_OverrideGravity ? _GravityOverride : Physics.gravity.y);


        //
        // Feature Culling
        //

        [System.Flags]
        internal enum ActiveModules
        {
            Nothing = 0,
            Surface = 1 << 1,
            Volume = 1 << 2,
            SurfaceAndVolume = Surface | Volume,
            Reflections = 1 << 3,
            Portal = 1 << 4,
            Meniscus = 1 << 5,
            Mask = 1 << 6,
            Shadows = 1 << 7,
            Everything = ~0,
        }

        // We store the previous in case of recursive camera render.
        readonly Stack<ActiveModules> _RecursiveActiveModules = new();
        internal ActiveModules _ActiveModules;


        //
        // Rendering
        //

        // Used as an extra check to prevent null exceptions, as the events raised when an
        // RP change happen too late for some things.
        RenderPipeline _SetUpFor;

        internal bool RenderBeforeTransparency =>
#if d_Crest_LegacyUnderwater
            false;
#else
            _InjectionPoint == WaterInjectionPoint.BeforeTransparent;
#endif

        internal MaskRenderer _Mask;

        // Flags
        bool _DonePerCameraHeight;
        internal bool _PerCameraHeightReady;

        bool GetWriteMotionVectors() =>
#if !UNITY_6000_0_OR_NEWER
            !RenderPipelineHelper.IsUniversal &&
#endif
#if CREST_DEBUG
            !_Debug._VisualizeData &&
#endif
            _WriteMotionVectors;

        bool GetWriteToColorTexture()
        {
            return (_WriteToColorTexture && RenderBeforeTransparency) || Meniscus.RequiresOpaqueTexture;
        }

        bool GetWriteToDepthTexture()
        {
            return _WriteToDepthTexture
#if CREST_DEBUG
                && !_Debug._VisualizeData
#endif
                && Surface.Enabled;
        }

        internal static bool ShouldRender(Camera camera)
        {
#if UNITY_EDITOR
            // Preview camera are for preview game view, preview panes, material previews etc.
            if (camera.cameraType == CameraType.Preview)
            {
                return false;
            }
#endif

            return true;
        }

        internal static bool ShouldRender(Camera camera, int layer)
        {
            if (!ShouldRender(camera))
            {
                return false;
            }

            if (!Helpers.MaskIncludesLayer(camera.cullingMask, layer))
            {
                return false;
            }

            return true;
        }

        internal static bool ShouldRender(Camera camera, WaterCameraExclusion exclusion)
        {
            if (camera.cameraType == CameraType.SceneView)
            {
                return true;
            }

            if (camera.TryGetComponent<WaterCamera>(out var wc) && wc.isActiveAndEnabled)
            {
                return true;
            }

            var exclude =
                // Reflection cameras are all typically hidden. We have a separate flag for them.
                (exclusion.HasFlag(WaterCameraExclusion.Hidden) && camera.hideFlags.HasFlag(HideFlags.HideInHierarchy) && camera.cameraType != CameraType.Reflection) ||
                (exclusion.HasFlag(WaterCameraExclusion.Reflection) && camera.cameraType == CameraType.Reflection) ||
                (exclusion.HasFlag(WaterCameraExclusion.NonMainCamera) && !camera.CompareTag("MainCamera"));

            return !exclude;
        }

        internal static bool ShouldRender(Camera camera, int layer, WaterCameraExclusion exclusion)
        {
            if (!ShouldRender(camera, layer))
            {
                return false;
            }

            if (!ShouldRender(camera, exclusion))
            {
                return false;
            }

            return true;
        }

        bool ShouldExecuteViewpoint(Camera camera, int layer, WaterCameraExclusion exclusion)
        {
            if (IsSingleViewpointMode)
            {
                _RenderShadows = true;
                return false;
            }

#if UNITY_EDITOR
            // Editor Multiple Viewpoints is for scene view and viewer only.
            if (!MultipleViewpoints && camera.cameraType != CameraType.SceneView && camera != GetViewer(false))
            {
                return false;
            }
#endif

            if (!ShouldRender(camera, layer, exclusion))
            {
                return false;
            }

            return true;
        }


        //
        // Material
        //

        /// <summary>
        /// Calculates the absorption value from the absorption color.
        /// </summary>
        /// <param name="color">The absorption color.</param>
        /// <returns>The absorption value (XYZ value).</returns>
        public static Vector4 CalculateAbsorptionValueFromColor(Color color)
        {
            return UpdateAbsorptionFromColor(color);
        }

        internal static Vector4 UpdateAbsorptionFromColor(Color color)
        {
            var alpha = Vector3.zero;
            alpha.x = Mathf.Log(Mathf.Max(color.r, 0.0001f));
            alpha.y = Mathf.Log(Mathf.Max(color.g, 0.0001f));
            alpha.z = Mathf.Log(Mathf.Max(color.b, 0.0001f));
            // Magic numbers that make fog density easy to control using alpha channel
            return (-color.a * 32f * alpha / 5f).XYZN(1f);
        }

        internal static void UpdateAbsorptionFromColor(Material material)
        {
            var fogColour = material.GetColor(ShaderIDs.s_AbsorptionColor);
            var alpha = Vector3.zero;
            alpha.x = Mathf.Log(Mathf.Max(fogColour.r, 0.0001f));
            alpha.y = Mathf.Log(Mathf.Max(fogColour.g, 0.0001f));
            alpha.z = Mathf.Log(Mathf.Max(fogColour.b, 0.0001f));
            // Magic numbers that make fog density easy to control using alpha channel
            material.SetVector(ShaderIDs.s_Absorption, UpdateAbsorptionFromColor(fogColour));
        }


        //
        // Simulations
        //

        internal List<Lod> Simulations { get; } = new();


        //
        // Instance
        //

        bool _Initialized;
        internal bool Active => enabled && this == Instance;


        //
        // Hash
        //

        // A hash of the settings used to generate the water, used to regenerate when necessary
        int _GeneratedSettingsHash;


        //
        // Runtime Environment
        //

        /// <summary>
        /// Is runtime environment without graphics card
        /// </summary>
        public static bool RunningWithoutGraphics
        {
            get
            {
                var noGPU = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;
                var emulateNoGPU = Instance != null && Instance._Debug._ForceNoGraphics;
                return noGPU || emulateNoGPU;
            }
        }

        // No GPU or emulate no GPU.
        internal bool IsRunningWithoutGraphics => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || _Debug._ForceNoGraphics;

        /// <summary>
        /// Is runtime environment non-interactive (not displaying to user).
        /// </summary>
        [System.Obsolete("We no longer care whether Unity is running in non-interactive mode.")]
        public static bool RunningHeadless => false;


        //
        // Frame Timing
        //

        /// <summary>
        /// The frame count for Crest.
        /// </summary>
        public static int FrameCount => Time.frameCount;


        //
        // Level of Detail
        //

        internal static System.Action<WaterRenderer, Camera> s_OnBeforeBuildCommandBuffer;

        internal const string k_DrawLodData = "Crest.LodData";
        internal CommandBuffer SimulationBuffer { get; private set; }

        // Scale, Weight, MaximumWaveLength, Unused
        BufferedData<Vector4[]> _CascadeData;
        internal BufferedData<Vector4[]> CascadeData { get; private set; }

        // NOTE: hardcoded for now. There is typically at least one persistent simulation, and
        // they never go beyond two frames.
        internal int BufferSize => 2;

        internal float MaximumWavelength(int slice, int resolution)
        {
            return MaximumWavelength(CalcLodScale(slice), resolution);
        }

        internal float MaximumWavelength(float scale, int resolution)
        {
            var maximumDiameter = 4f * scale;
            var maximumTexelSize = maximumDiameter / resolution;
            var texelsPerWave = 2f;
            return 2f * maximumTexelSize * texelsPerWave;
        }


        //
        // Scale
        //

        /// <summary>
        /// Current water scale (changes with viewer altitude).
        /// </summary>
        public float Scale { get; private set; }
        internal float CalcLodScale(float slice) => Scale * Mathf.Pow(2f, slice);
        internal float CalcGridSize(int slice) => CalcLodScale(slice) / LodResolution;

        /// <summary>
        /// Could the water horizontal scale increase (for e.g. if the viewpoint gains altitude). Will be false if water already at maximum scale.
        /// </summary>
        internal bool ScaleCouldIncrease => _ScaleRange.y == Mathf.Infinity || Scale < _ScaleRange.y * 0.99f;
        /// <summary>
        /// Could the water horizontal scale decrease (for e.g. if the viewpoint drops in altitude). Will be false if water already at minimum scale.
        /// </summary>
        internal bool ScaleCouldDecrease => Scale > _ScaleRange.x * 1.01f;

        internal int ScaleDifferencePower2 { get; private set; }


        //
        // Query Providers
        //

        /// <summary>
        /// Provides water shape to CPU.
        /// </summary>
        public ICollisionProvider CollisionProvider => AnimatedWavesLod?.Provider;

        /// <summary>
        /// Provides flow to the CPU.
        /// </summary>
        public IFlowProvider FlowProvider => FlowLod?.Provider;

        /// <summary>
        /// Provides water depth and distance to water edge to the CPU.
        /// </summary>
        public IDepthProvider DepthProvider => DepthLod?.Provider;


        //
        // Component
        //

        // Drive state from OnEnable and OnDisable? OnEnable on RegisterLodDataInput seems to get called on script reload
        private protected override void Initialize()
        {
            base.Initialize();

            _SetUpFor = RenderPipelineHelper.RenderPipeline;

            _IsFirstFrameSinceEnabled = true;
            CurrentCamera = GetViewer(initial: true);

            // Recompiled in play mode.
            if (_Mask == null)
            {
                _Initialized = false;
            }

            if (_Initialized)
            {
                Enable();
                return;
            }

            Utility.RTHandles.Initialize();

            _Mask = MaskRenderer.Instantiate(this);

            Meniscus.Initialize(this);

            Surface._Water = this;
            _Reflections._Water = this;
            _Reflections._UnderWater = _Underwater;
            _Underwater._Water = this;
#if d_CrestPortals
            _Underwater._Portals = _Portals;
            _Portals._Water = this;
            _Portals._UnderWater = _Underwater;
#endif

            _DepthLod._Water = this;
            _LevelLod._Water = this;
            _FlowLod._Water = this;
            _DynamicWavesLod._Water = this;
            _AnimatedWavesLod._Water = this;
            _FoamLod._Water = this;
            _ClipLod._Water = this;
            _AbsorptionLod._Water = this;
            _ScatteringLod._Water = this;
            _AlbedoLod._Water = this;
            _ShadowLod._Water = this;

            // Add simulations to a list for common operations. Order is important.
            Simulations.Clear();
            Simulations.Add(_DepthLod);
            Simulations.Add(_LevelLod);
            Simulations.Add(_FlowLod);
            Simulations.Add(_DynamicWavesLod);
            Simulations.Add(_AnimatedWavesLod);
            Simulations.Add(_FoamLod);
            Simulations.Add(_AbsorptionLod);
            Simulations.Add(_ScatteringLod);
            Simulations.Add(_ClipLod);
            Simulations.Add(_AlbedoLod);
            Simulations.Add(_ShadowLod);

            // Setup a default time provider, and add the override one (from the inspector)
            TimeProviders.Clear();

            // Put a base TP that should always be available as a fallback
            TimeProviders.Push(new DefaultTimeProvider());

            // Add the TP from the inspector
            if (_TimeProvider != null)
            {
                TimeProviders.Push(_TimeProvider);
            }

            if (!VerifyRequirements())
            {
                enabled = false;
                return;
            }

            SimulationBuffer ??= new()
            {
                name = k_DrawLodData,
            };

            Container = new()
            {
                name = "Container",
                hideFlags = _Debug._ShowHiddenObjects ? HideFlags.DontSave : HideFlags.HideAndDontSave
            };
            Container.transform.SetParent(transform, worldPositionStays: false);
            this.Manage(Container);

            Scale = Mathf.Clamp(Scale, _ScaleRange.x, _ScaleRange.y);

            foreach (var simulation in Simulations)
            {
                // Bypasses Enabled and has an internal check.
                if (!simulation._Enabled) continue;
                simulation.Initialize();
            }

            _ProjectionMatrix = new Matrix4x4[LodLevels];

            CascadeData = _CascadeData = InitializePerFrameMaterialParameters();

            if (Application.isPlaying && _Debug._AttachDebugGUI && !TryGetComponent<DebugGUI>(out _))
            {
                gameObject.AddComponent<DebugGUI>().hideFlags = HideFlags.DontSave;
            }

            _GeneratedSettingsHash = CalculateSettingsHash();

            if (Surface.Enabled)
            {
                Surface.Initialize();
            }

            foreach (var body in WaterBody.WaterBodies)
            {
                if (body._Material != null)
                {
                    Surface.UpdateMaterial(body._Material, ref body._MotionVectorMaterial);
                }
            }

            Enable();
            _Initialized = true;
        }

        void OnDisable()
        {
            Disable();

            // Always clean up in OnDisable during edit mode as OnDestroy is not always called.
            if (_Debug._DestroyResourcesInOnDisable || !Application.isPlaying)
            {
                Destroy();
            }
        }

        void OnDestroy()
        {
            // Only clean up in OnDestroy when not in edit mode.
            if (_Debug._DestroyResourcesInOnDisable || !Application.isPlaying)
            {
                return;
            }

            Destroy();
        }


        //
        // Methods
        //

        private protected override void Enable()
        {
            base.Enable();

            // For running the system in the background and fallbacks.
            if (!IsSingleViewpointMode && !RunningWithoutGraphics)
            {
                _EndOfFrame = StartCoroutine(UpdateSkippedCameras());
            }

            // Needs to be first or will get assertions etc. Unity bug likely.
            RenderPipelineManager.activeRenderPipelineTypeChanged -= OnActiveRenderPipelineTypeChanged;
            RenderPipelineManager.activeRenderPipelineTypeChanged += OnActiveRenderPipelineTypeChanged;

            // Needs to run even without graphics to initialize provider.
            foreach (var simulation in Simulations)
            {
                simulation.SetGlobals(enable: true);
                if (!simulation.Enabled) continue;
                simulation.Enable();
            }

            if (IsRunningWithoutGraphics)
            {
                // We need nothing from here on.
                return;
            }

#if d_WaveHarmonic_Crest_ShiftingOrigin
            ShiftingOrigin.OnShift -= OnOriginShift;
            ShiftingOrigin.OnShift += OnOriginShift;
#endif

            // This event should not when not using the built-in renderer, but in some cases it can in the editor like
            // when using scene filtering.
            if (RenderPipelineHelper.IsLegacy)
            {
                Camera.onPreCull -= OnBeginCameraRendering;
                Camera.onPreCull += OnBeginCameraRendering;
                Camera.onPostRender -= OnEndCameraRendering;
                Camera.onPostRender += OnEndCameraRendering;
            }
            else
            {
                RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
                RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
                RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
                RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
            }

#if d_UnityURP
            if (RenderPipelineHelper.IsUniversal)
            {
                // Always enable as it sets requirements.
                SurfaceRenderer.WaterSurfaceRenderPass.Enable(this);
            }
#endif

#if d_UnityHDRP
            if (RenderPipelineHelper.IsHighDefinition)
            {
                if (RenderBeforeTransparency)
                {
                    SurfaceRenderer.WaterSurfaceCustomPass.Enable(this);
                }

                CrestInternalCopyToTextureCustomPass.Enable(this);
            }

#if UNITY_EDITOR
            if (RenderPipelineHelper.IsHighDefinition)
            {
                RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
                RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
            }
#endif
#endif

            Container.SetActive(true);

            _Mask.Enable();

            if (_Underwater._Enabled)
            {
                _Underwater.OnEnable();
            }

            if (Meniscus.Enabled)
            {
                Meniscus.Enable();
            }

#if d_UnityURP
            if (RenderPipelineHelper.IsUniversal)
            {
                if (WriteToColorTexture || WriteToDepthTexture)
                {
                    CopyTargetsRenderPass.Enable(this);
                }
            }
#endif

#if d_CrestPortals
            if (_Portals._Enabled)
            {
                _Portals.OnEnable();
            }
#endif

            if (_Reflections._Enabled)
            {
                _Reflections.OnEnable();
            }
        }

        // Because we cannot pass null when using built-in render pipeline.
        // Being a struct there should not be any side effects.
        internal ScriptableRenderContext _Context = new();

        void OnBeginCameraRendering(Camera camera)
        {
#if CREST_DEBUG
            if (_Debug._SimulatePaused)
            {
                return;
            }
#endif

            if (_SetUpFor != RenderPipelineHelper.RenderPipeline)
            {
                return;
            }

            if (!_Initialized)
            {
                return;
            }

            Utility.RTHandles.OnBeginCameraRendering(camera);

            OnBeginCameraRendering(_Context, camera);
        }

        // OnBeginCameraRendering or OnPreCull
        void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
#if CREST_DEBUG
            if (_Debug._SimulatePaused)
            {
                return;
            }
#endif

            // Guard against being called before the RP change events are raised.
            if (_SetUpFor != RenderPipelineHelper.RenderPipeline)
            {
                return;
            }

            if (!_Initialized)
            {
                return;
            }

#if UNITY_EDITOR
            OnBeginSceneCameraRendering(camera);
#endif

            if (!ShouldRender(camera))
            {
                return;
            }

            _RecursiveActiveModules.Push(_ActiveModules);

            _ActiveModules = ActiveModules.Nothing;

            if (Surface.ShouldRender(camera))
            {
                _ActiveModules |= ActiveModules.Surface;
            }

#if d_CrestPortals
            if (Portals.ShouldRender(camera))
            {
                _ActiveModules |= ActiveModules.Portal;
            }
#endif

            if (Underwater.ShouldRender(camera))
            {
                _ActiveModules |= ActiveModules.Volume;
            }
            else
            {
                // Portal depends on underwater, as the clip surface should be used for cases where
                // underwater is not needed. Except for tunnels, but they are too intertwined to
                // separate yet.
                _ActiveModules &= ~ActiveModules.Portal;
            }

#if UNITY_EDITOR
            // We must do this check separately, as we want to still render the portal to mask
            // the surface.
            if (!Underwater.ShouldRenderForSceneView(camera))
            {
                _ActiveModules &= ~ActiveModules.Volume;
            }
#endif

            var noSurface = !_ActiveModules.HasFlag(ActiveModules.Surface);
            var noVolume = !_ActiveModules.HasFlag(ActiveModules.Volume);

            // MainCamera is a requirement. Guard against this.
            if ((!noSurface || !noVolume) && GetViewer(initial: true) == null)
            {
                noSurface = noVolume = true;
            }

            if (noSurface)
            {
                // For exclusion rules. Redundant in some cases.
                Surface.ForceRenderingOff = true;
            }

            // Nothing to render to this camera.
            if (noSurface && noVolume)
            {
                return;
            }

            if (Meniscus.ShouldRender(camera))
            {
                _ActiveModules |= ActiveModules.Meniscus;
            }

            if (Reflections.ShouldRender(camera))
            {
                _ActiveModules |= ActiveModules.Reflections;
            }

#if d_CrestPortals
            // Must execute before the mask, but after the volume.
            if (_ActiveModules.HasFlag(ActiveModules.Portal))
            {
                Portals.ConfigureInputsFromVolume(Underwater);
            }
#endif

            // Providers/Receivers have registered in OnEnable.
            if (_Mask.ShouldExecute(camera))
            {
                _ActiveModules |= ActiveModules.Mask;
            }

            if (ShadowLod.ShouldRender(camera))
            {
                _ActiveModules |= ActiveModules.Shadows;
            }

            _HasAnyViewerRendered = true;

            if (ShouldExecuteViewpoint(camera, Surface.Layer, _CameraExclusions))
            {
                var last = -1;

                if (_PerCameraLastFrame.ContainsKey(camera))
                {
                    last = _PerCameraLastFrame[camera];
                }
                else
                {
                    _PerCameraLastFrame.Add(camera, last);
                }

                if (last != Time.frameCount)
                {
                    if (!_Cameras.Contains(camera))
                    {
                        _Cameras.Add(camera);
                    }

                    ExecuteViewpoint(camera);

                    _PerCameraLastFrame[camera] = Time.frameCount;
                }

                _RenderShadows = true;
            }

            // Project water normal onto camera plane.
            Shader.SetGlobalVector(ShaderIDs.s_HorizonNormal, new Vector2
            (
                Vector3.Dot(Vector3.up, camera.transform.right),
                Vector3.Dot(Vector3.up, camera.transform.up)
            ));

            // Must render first so that we do not overwrite work below for game camera.
            // Reflections only make sense with an active surface.
            // The following invokes render on the reflection camera, so we must not call it when camera loop is reflection camera.
            if (_ActiveModules.HasFlag(ActiveModules.Reflections))
            {
                _Reflections.OnBeginCameraRendering(context, camera);
            }

            // Must render before the mask, but cannot execute in the mask pass.
            if (_ActiveModules.HasFlag(ActiveModules.Volume))
            {
                Underwater.ExecuteHeightField(camera);
            }

            if (_ActiveModules.HasFlag(ActiveModules.Mask))
            {
                _Mask.OnBeginCameraRendering(camera);
            }

            // Water lighting etc.
            {
                ExecuteLighting(context, camera);
            }

            // Always execute before surface, as order is only important when rendering volume
            // before surface.
            if (_ActiveModules.HasFlag(ActiveModules.Volume))
            {
                Underwater.OnBeginCameraRendering(context, camera);
            }

#if d_CrestPortals
            // Call between volume and surface. Sets water line uniforms.
            if (_ActiveModules.HasFlag(ActiveModules.Portal))
            {
                Portals.OnBeginCameraRendering(camera);
            }
#endif

            if (_ActiveModules.HasFlag(ActiveModules.Surface))
            {
                Surface.OnBeginCameraRendering(context, camera);
            }

            // Update color and/or depth textures.
            {
                UpdateRenderPipelineTextures(context, camera);
            }

            // Execute after copy pass in case refraction.
            if (_ActiveModules.HasFlag(ActiveModules.Meniscus))
            {
                Meniscus.Renderer.OnBeginCameraRendering(camera);
            }

            if (_ActiveModules.HasFlag(ActiveModules.Shadows) && _RenderShadows)
            {
                ShadowLod.OnBeginCameraRendering(context, camera);
            }
        }

        void UpdateRenderPipelineTextures(ScriptableRenderContext context, Camera camera)
        {
            // Currently, only do this for the surface.
            if (!_ActiveModules.HasFlag(ActiveModules.Surface))
            {
                return;
            }

#if d_UnityURP
            // Always execute after surface.
            if (RenderPipelineHelper.IsUniversal)
            {
                CopyTargetsRenderPass.Instance?.OnBeginCameraRendering(context, camera);
                return;
            }
#endif

            if (RenderPipelineHelper.IsLegacy)
            {
                OnLegacyCopyPass(camera);
            }
        }

        void OnEndCameraRendering(Camera camera)
        {
#if CREST_DEBUG
            if (_Debug._SimulatePaused)
            {
                return;
            }
#endif

            OnEndCameraRendering(_Context, camera);
        }

        void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
#if CREST_DEBUG
            if (_Debug._SimulatePaused)
            {
                return;
            }
#endif

            _DonePerCameraHeight = false;
            _PerCameraHeightReady = false;

#if UNITY_EDITOR
            OnEndSceneCameraRendering(camera);
#endif

            if (Reflections.ReflectionCamera != camera)
            {
                Surface.ForceRenderingOff = false;
            }

            if (RenderPipelineHelper.IsLegacy)
            {
                OnEndCameraRenderingLegacy(camera);
            }

            if (_ActiveModules.HasFlag(ActiveModules.Mask))
            {
                _Mask.OnEndCameraRendering(camera);
            }

            if (_ActiveModules.HasFlag(ActiveModules.Meniscus))
            {
                Meniscus.Renderer.OnEndCameraRendering(camera);
            }

            if (_ActiveModules.HasFlag(ActiveModules.Volume))
            {
                Underwater.OnEndCameraRendering(camera);
            }

            if (_ActiveModules.HasFlag(ActiveModules.Surface))
            {
                Surface.OnEndCameraRendering(camera);
            }

            if (_ActiveModules.HasFlag(ActiveModules.Reflections))
            {
                _Reflections.OnEndCameraRendering(camera);
            }

            if (_ActiveModules.HasFlag(ActiveModules.Shadows) && _RenderShadows)
            {
                _ShadowLod.OnEndCameraRendering(camera);
            }

#if d_CrestPortals
            if (_ActiveModules.HasFlag(ActiveModules.Portal))
            {
                _Portals.OnEndCameraRendering(camera);
            }
#endif

            // Always call.
            _Reflections.OnEndReflectionCameraRendering(camera);

            if (camera == CurrentCamera)
            {
                IsSeparateViewpointCameraLoop = false;
                CurrentCamera = null;
                _RenderShadows = false;
            }

            // Restore parent camera state, if available.
            _RecursiveActiveModules.TryPop(out _ActiveModules);
        }

        internal void UpdatePerCameraHeight(Camera camera)
        {
            if (_DonePerCameraHeight)
            {
                return;
            }

            // This will be 1-frame behind. We allow multiple calls because the camera may
            // render multiple times per frame. One case is the scene camera when focus is
            // gained or using the frame debugger.
            var viewpoint = camera.transform.position;
            _PerCameraHeightReady |= _SampleHeightHelperPerCamera.SampleHeight(System.HashCode.Combine(_SampleHeightHelperPerCamera, camera), viewpoint, out var height, allowMultipleCallsPerFrame: true);
            _ViewerHeightAboveWaterPerCamera = viewpoint.y - height;
            // Planar camera mirrors current camera, but treat at same location.
            if (camera == Reflections.ReflectionCamera) _ViewerHeightAboveWaterPerCamera = -_ViewerHeightAboveWaterPerCamera;

            _DonePerCameraHeight = true;
        }

        void OnActiveRenderPipelineTypeChanged()
        {
            _Mask?.Destroy();
            _Mask = MaskRenderer.Instantiate(this);

            Meniscus.OnActiveRenderPipelineTypeChanged();

            if (isActiveAndEnabled)
            {
                // Must destroy as there is still some state left like buffer count.
                Disable();
                Destroy();
                Initialize();
            }
        }

        internal void Rebuild()
        {
            Disable();
            Destroy();
            OnEnable();
        }

        bool VerifyRequirements()
        {
            if (!RunningWithoutGraphics)
            {
                if (Application.platform == RuntimePlatform.WebGLPlayer && !Helpers.IsWebGPU)
                {
                    Debug.LogError("Crest: Crest does not support WebGL backends.", this);
                    return false;
                }
#if UNITY_EDITOR
                if (SystemInfo.graphicsDeviceType is GraphicsDeviceType.OpenGLES3 or GraphicsDeviceType.OpenGLCore)
                {
                    Debug.LogError("Crest: Crest does not support OpenGL backends.", this);
                    return false;
                }
#endif
                if (SystemInfo.graphicsShaderLevel < 45)
                {
                    Debug.LogError("Crest: Crest requires graphics devices that support shader level 4.5 or above.", this);
                    return false;
                }
                if (!SystemInfo.supportsComputeShaders)
                {
                    Debug.LogError("Crest: Crest requires graphics devices that support compute shaders.", this);
                    return false;
                }
                if (!SystemInfo.supports2DArrayTextures)
                {
                    Debug.LogError("Crest: Crest requires graphics devices that support 2D array textures.", this);
                    return false;
                }
            }

            return true;
        }

        int CalculateSettingsHash()
        {
            var settingsHash = Hash.CreateHash();

            // Add all the settings that require rebuilding..
            Hash.AddInt(_Resolution, ref settingsHash);
            Hash.AddInt(_Slices, ref settingsHash);
            Hash.AddBool(WriteMotionVectors, ref settingsHash);
            Hash.AddBool(_Debug._ForceNoGraphics, ref settingsHash);
            Hash.AddBool(_Debug._ShowHiddenObjects, ref settingsHash);

            return settingsHash;
        }

        // Queries keeps LateUpdate running regardless of editor settings. If queries
        // stops, then the water will pause.
        private protected override void LateUpdate()
        {
#if CREST_DEBUG
#if UNITY_EDITOR
            if (_SkipForTesting)
            {
                return;
            }
#endif
#endif

            // Rebuild if needed. Needs to run in builds (for MVs at the very least).
            if (CalculateSettingsHash() != _GeneratedSettingsHash)
            {
                Rebuild();
            }

            if (RunningWithoutGraphics)
            {
                // All we need for servers.
                BroadcastUpdate();
                Position = new(0f, transform.position.y, 0f);
            }
            else
            {
                BroadcastUpdate();

                if (IsSingleViewpointMode)
                {
                    ExecuteViewpoint(Viewer);
                }
                else
                {
                    if (FallBackRequired)
                    {
                        ExecuteViewpoint(GetViewer(initial: true));
                    }

                    PruneCameraData();

                    _HasAnyViewpointExecuted = false;
                    _HasAnyViewerRendered = false;
                }
            }

            // This use to execute after the system command buffer, after all properties had
            // been updated. But none of the calls used any of that data.
            base.LateUpdate();

            // Run queries at end of update. For CollProviderBakedFFT calling this kicks off
            // collision processing job, and the next call to Query() will force a complete, and
            // we don't want that to happen until they've had a chance to run, so schedule them
            // late.
            if (AnimatedWavesLod.QuerySource == LodQuerySource.CPU)
            {
                AnimatedWavesLod.Provider?.UpdateQueries(this);
            }
        }

        // Only called for separate viewpoints. This will never be recursive, as any camera
        // executed within this call is ours.
        void ExecuteViewpoint(Camera camera)
        {
            if (camera == _Reflections.ReflectionCamera)
            {
                return;
            }

            // Only set to a camera which is a center of detail.
            CurrentCamera = camera;

            s_RunUpdateMarker.Begin(this);

            LoadCameraData(camera);

            if (!_Debug._DisableFollowViewpoint && CurrentCamera != null)
            {
                LateUpdatePosition();
                LateUpdateViewerHeight();
                LateUpdateScale();
            }
            else
            {
                Position = new(0f, transform.position.y, 0f);
            }

            // Set global shader params
            Shader.SetGlobalFloat(ShaderIDs.s_Time, CurrentTime);
            Shader.SetGlobalInteger(ShaderIDs.s_LodCount, LodLevels);

            // Construct the command buffer and attach it to the camera so that it will be executed in the render.
            {
                SimulationBuffer.Clear();

                // Needs updated transform values like scale.
                WritePerFrameMaterialParams(SimulationBuffer);

                s_OnBeforeBuildCommandBuffer?.Invoke(this, camera);

                foreach (var simulation in Simulations)
                {
                    if (!simulation.Enabled) continue;
                    if (_IsEndOfFrame && simulation.SkipEndOfFrame) continue;
                    simulation.BuildCommandBuffer(this, SimulationBuffer);
                }

                // This will execute at the beginning of the frame before the graphics queue.
                Graphics.ExecuteCommandBuffer(SimulationBuffer);

                foreach (var simulation in Simulations)
                {
                    if (!simulation.Enabled) continue;
                    if (_IsEndOfFrame && simulation.SkipEndOfFrame) continue;
                    simulation.AfterExecute();
                }
            }

            // Call after LateUpdate so chunk bounds are updated.
            if (Surface.Enabled)
            {
                Surface.LateUpdate();
            }

            if (_Reflections._Enabled)
            {
                _Reflections.LateUpdate();
            }

            _IsFirstFrameSinceEnabled = false;
            _HasAnyViewpointExecuted = true;

            StoreCameraData(camera);

            s_RunUpdateMarker.End();
        }

        BufferedData<Vector4[]> InitializePerFrameMaterialParameters()
        {
            var data = new BufferedData<Vector4[]>(BufferSize, () => new Vector4[Lod.k_MaximumSlices + 1]);

            for (var i = data.Size - 1; i >= 0; --i)
            {
                UpdatePerFrameMaterialParameters(data.Previous(i));
            }

            return data;
        }

        void UpdatePerFrameMaterialParameters(Vector4[] current)
        {
            var levels = LodLevels;

            for (var slice = 0; slice < levels; slice++)
            {
                var scale = CalcLodScale(slice);

                // NOTE: MaximumWavelength is only used by SphereWaterInteraction.
                current[slice] = new Vector4(scale, 1f, MaximumWavelength(scale, DynamicWavesLod.Resolution), 0f);

                _ProjectionMatrix[slice] = Matrix4x4.Ortho(-2f * scale, 2f * scale, -2f * scale, 2f * scale, 1f, k_RenderAboveSeaLevel + k_RenderBelowSeaLevel);
            }

            // Duplicate last element so that things can safely read off the end of the cascades
            current[levels] = current[levels - 1].XNZW(0f);
        }

        void WritePerFrameMaterialParams(CommandBuffer commands)
        {
            CascadeData.Flip();

            var current = CascadeData.Current;

            // Update rendering parameters.
            {
                UpdatePerFrameMaterialParameters(current);
                commands.SetGlobalFloat(ShaderIDs.s_Scale, current[0].x);

                // Duplicate last element so that things can safely read off the end of the cascades
                current[LodLevels] = current[LodLevels - 1].XNZW(0f);
            }

            commands.SetGlobalVectorArray(ShaderIDs.s_CascadeData, current);
            commands.SetGlobalVectorArray(ShaderIDs.s_CascadeDataSource, CascadeData.Previous(1));
        }

        void LateUpdatePosition()
        {
            var position = Viewpoint.position;

            var hash = System.HashCode.Combine(_CenterOfDetailDisplacementCorrectionHelper, Viewpoint);

            // This will cause artifacts in motion vectors debug view, but are likely negligible.
            if (_CenterOfDetailDisplacementCorrection && _CenterOfDetailDisplacementCorrectionHelper.SampleDisplacement(hash, position, out var displacement, allowMultipleCallsPerFrame: true))
            {
                position = new(position.x - displacement.x, position.y, position.z - displacement.z);
            }

            // maintain y coordinate - sea level
            position.y = transform.position.y;

            // Don't land very close to regular positions where things are likely to snap to, because different tiles might
            // land on either side of a snap boundary due to numerical error and snap to the wrong positions. Nudge away from
            // common by using increments of 1/60 which have lots of factors.
            // :WaterGridPrecisionErrors
            if (Mathf.Abs(position.x * 60f - Mathf.Round(position.x * 60f)) < 0.001f)
            {
                position.x += 0.002f;
            }
            if (Mathf.Abs(position.z * 60f - Mathf.Round(position.z * 60f)) < 0.001f)
            {
                position.z += 0.002f;
            }

            Shader.SetGlobalVector(ShaderIDs.s_CenterDelta, (position - Position).XZ());

            Position = position;
            Shader.SetGlobalVector(ShaderIDs.s_Center, Position);
        }

        void LateUpdateScale()
        {
            var viewerHeight = _ViewpointHeightAboveWaterSmooth;

            // Drop Detail Height Based On Waves.
            {
                var displacement = 0f;

                foreach (var (_, input) in AnimatedWavesLod.s_Inputs)
                {
                    if (input.WaveDisplacementReporter == null)
                    {
                        continue;
                    }

                    displacement = input.WaveDisplacementReporter.ReportWaveDisplacement(this, displacement);
                }

                // Reach maximum detail at slightly below sea level. this should combat cases where visual range can be lost
                // when water height is low and camera is suspended in air. i tried a scheme where it was based on difference
                // to water height but this does help with the problem of horizontal range getting limited at bad times.
                viewerHeight += displacement * _DropDetailHeightBasedOnWaves;

                Shader.SetGlobalFloat(ShaderIDs.s_MaximumVerticalDisplacement, displacement);
            }

            var camDistance = Mathf.Abs(viewerHeight);

            // offset level of detail to keep max detail in a band near the surface
            camDistance = Mathf.Max(camDistance - 4f, 0f);

            var range = _ScaleRange;

#if CREST_DEBUG
            if (_Debug._OverrideScale)
            {
                range = Vector2.one * _Debug._ScaleOverride;
            }
#endif

            // scale water mesh based on camera distance to sea level, to keep uniform detail.
            var level = camDistance;
            level = Mathf.Max(level, range.x);
            if (range.y < Mathf.Infinity) level = Mathf.Min(level, 1.99f * range.y);

            var l2 = Mathf.Log(level) / Mathf.Log(2f);
            var l2f = Mathf.Floor(l2);

            ViewerAltitudeLevelAlpha = l2 - l2f;

            var newScale = Mathf.Pow(2f, l2f);

            if (Scale > 0f)
            {
                var ratio = newScale / Scale;
                var ratioL2 = Mathf.Log(ratio) / Mathf.Log(2f);
                ScaleDifferencePower2 = Mathf.RoundToInt(ratioL2);
                Shader.SetGlobalFloat(ShaderIDs.s_LodChange, ScaleDifferencePower2);
                Shader.SetGlobalFloat(ShaderIDs.s_ScaleChange, ratio);

#if UNITY_EDITOR
#if CREST_DEBUG
                if (ratio != 1f)
                {
                    EditorApplication.isPaused = EditorApplication.isPaused || _Debug._PauseOnScaleChange;
                    if (_Debug._LogScaleChange) Debug.Log($"Scale Change: {newScale} / {Scale} = {ratio}. LOD Change: {ScaleDifferencePower2}");
                }
#endif
#endif
            }

            Scale = newScale;

            // LOD 0 is blended in/out when scale changes, to eliminate pops. Here we set it as
            // a global, whereas in WaterChunkRenderer it is applied to LOD0 tiles only through
            // instance data. This global can be used in compute, where we only apply this
            // factor for slice 0.
            Shader.SetGlobalFloat(ShaderIDs.s_MeshScaleLerp, ScaleCouldIncrease ? ViewerAltitudeLevelAlpha : 0f);
        }

        void LateUpdateViewerHeight()
        {
            var viewpoint = Viewpoint;

            var viewpointHashCode = System.HashCode.Combine(_SampleHeightHelper, viewpoint);

            _SampleHeightHelper.SampleHeight(viewpointHashCode, viewpoint.position, out var waterHeight, allowMultipleCallsPerFrame: true);
            ViewerHeightAboveWater = ViewpointHeightAboveWater = viewpoint.position.y - waterHeight;

            var viewerHeightAboveWaterOrTerrain = ViewpointHeightAboveWater;

            if (viewpoint != CurrentCamera.transform)
            {
                var viewer = CurrentCamera.transform;
                // Reuse sampler. Combine hash codes to avoid pontential conflict.
                _SampleHeightHelper.SampleHeight(System.HashCode.Combine(_SampleHeightHelper, viewer), viewpoint.position, out waterHeight, allowMultipleCallsPerFrame: true);
                ViewerHeightAboveWater = viewer.position.y - waterHeight;
            }

#if d_Unity_Terrain
            // Also use terrain height for scale. Viewpoint is absolute if set.
            if (_SampleTerrainHeightForScale && LevelLod.Enabled && _Viewpoint == null)
            {
                var viewerPosition = viewpoint.position;
                var viewerHeight = viewerPosition.y;

                var viewerHeightAboveTerrain = Mathf.Infinity;
                var terrain = Helpers.GetTerrainAtPosition(viewerPosition.XZ());
                if (terrain != null)
                {
                    var terrainHeight = terrain.GetPosition().y + terrain.SampleHeight(viewerPosition);
                    var heightAbove = viewerHeight - terrainHeight;

                    // Ignore if viewer is under terrain.
                    if (heightAbove >= 0f)
                    {
                        viewerHeightAboveTerrain = heightAbove;
                    }
                }

                if (viewerHeightAboveTerrain < Mathf.Abs(viewerHeightAboveWaterOrTerrain))
                {
                    viewerHeightAboveWaterOrTerrain = viewerHeightAboveTerrain;
                }
            }
#endif // d_Unity_Terrain

            // Calculate teleport distance and create window for height queries to return a height change.
            {
                if (_TeleportTimerForHeightQueries > 0f)
                {
                    _TeleportTimerForHeightQueries -= Time.deltaTime;
                }

                var hasTeleported = _IsFirstFrameSinceEnabled;
                if (!_IsFirstFrameSinceEnabled)
                {
                    // Find the distance. Adding the FO offset will exclude FO shifts so we can determine a normal teleport.
                    // FO shifts are visually the same position and it is incorrect to treat it as a normal teleport.
                    var teleportDistanceSqr = (_OldViewpointPosition - viewpoint.position - TeleportOriginThisFrame).sqrMagnitude;
                    // Threshold as sqrMagnitude.
                    var thresholdSqr = _TeleportThreshold * _TeleportThreshold;
                    hasTeleported = teleportDistanceSqr > thresholdSqr;
                }

                if (hasTeleported)
                {
                    // Height queries can take a few frames so a one second window should be plenty.
                    _TeleportTimerForHeightQueries = 1f;
                }

                _HasTeleportedThisFrame = hasTeleported;

                _OldViewpointPosition = viewpoint.position;
            }

            // Smoothly varying version of viewer height to combat sudden changes in water level that are possible
            // when there are local bodies of water
            _ViewpointHeightAboveWaterSmooth = Mathf.Lerp
            (
                _ViewpointHeightAboveWaterSmooth,
                viewerHeightAboveWaterOrTerrain,
                _TeleportTimerForHeightQueries > 0f || !(_ForceScaleChangeSmoothing || (LevelLod.Enabled && !_SampleTerrainHeightForScale)) ? 1f : 0.01f
            );

#if CREST_DEBUG
            if (_Debug._IgnoreWavesForScaleChange)
            {
                _ViewpointHeightAboveWaterSmooth = Viewpoint.transform.position.y - SeaLevel;
            }
#endif

            _SampleDepthHelper.Sample(System.HashCode.Combine(_SampleDepthHelper, CurrentCamera), CurrentCamera.transform.position, out var result, allowMultipleCallsPerFrame: true);
            ViewerDistanceToShoreline = result.y;
            Shader.SetGlobalFloat(ShaderIDs.s_WaterDepthAtViewer, result.x);
        }

        void Destroy()
        {
            foreach (var simulation in Simulations)
            {
                if (!simulation.Enabled) continue;
                simulation.Destroy();
            }
            Simulations.Clear();

            _Mask?.Destroy();

            Meniscus.Destroy();

            // Clean up modules.
#if d_CrestPortals
            _Portals.OnDestroy();
#endif
            _Underwater.OnDestroy();
            _Reflections.OnDestroy();
            Surface.OnDestroy();

            if (Container)
            {
                Helpers.Destroy(Container);
                Container = null;
            }

            _Cameras.Clear();
            _PerCameraData.Clear();

            _Initialized = false;
        }

        private protected override void Disable()
        {
            if (_EndOfFrame != null)
            {
                StopCoroutine(_EndOfFrame);
            }

            foreach (var simulation in Simulations)
            {
                simulation.SetGlobals(enable: false);
                if (!simulation.Enabled) continue;
                simulation.Disable();
            }

            if (RenderPipelineHelper.IsLegacy && Viewer != null)
            {
                // Need to call to prevent crash.
                OnEndCameraRenderingLegacy(Viewer);
            }

            Camera.onPreCull -= OnBeginCameraRendering;
            Camera.onPostRender -= OnEndCameraRendering;

#if d_UnityHDRP
            SurfaceRenderer.WaterSurfaceCustomPass.Disable();
            CrestInternalCopyToTextureCustomPass.Disable();
#if UNITY_EDITOR
            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
#endif
#endif

#if d_WaveHarmonic_Crest_ShiftingOrigin
            ShiftingOrigin.OnShift -= OnOriginShift;
#endif

            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            RenderPipelineManager.activeRenderPipelineTypeChanged -= OnActiveRenderPipelineTypeChanged;

            _Mask?.Disable();

#if d_CrestPortals
            if (_Portals._Enabled) _Portals.OnDisable();
#endif
            if (_Underwater._Enabled) _Underwater.OnDisable();
            if (_Reflections._Enabled) _Reflections.OnDisable();

            if (Meniscus.Enabled)
            {
                Meniscus.Disable();
            }

            if (Container != null)
            {
                Container.SetActive(false);
            }

            base.Disable();
        }

#if d_WaveHarmonic_Crest_ShiftingOrigin
        /// <summary>
        /// Notify water of origin shift
        /// </summary>
        void OnOriginShift(Vector3 newOrigin)
        {
            foreach (var simulation in Simulations)
            {
                if (!simulation.Enabled) continue;
                simulation.SetOrigin(newOrigin);
            }
        }
#endif
    }

    // Per Camera Data.
    partial class WaterRenderer
    {
        sealed class PerCameraData
        {
            // Historic
            public bool _RenderedThisFrame;
            public bool _ExecutedThisFrame;
            public float _Scale = 1f;
            public Vector3 _Position;
            public Vector3 _OldViewpointPosition;
            public float _ViewpointHeightAboveWaterSmooth;
            public bool _IsFirstFrameSinceEnabled = true;
            public BufferedData<Vector4[]> _CascadeData;

            // Non-Historic (for external access)
            public float _ViewerHeightAboveWater;
            public float _ViewerDistanceToShoreline;

            public int _LastFrame = -1;
        }

        internal static System.Action<Camera> s_OnLoadCameraData;
        internal static System.Action<Camera> s_OnStoreCameraData;
        internal static System.Action<Camera> s_OnRemoveCameraData;

        readonly List<Camera> _Cameras = new();
        PerCameraData _CurrentPerCameraData;
        readonly Dictionary<Camera, PerCameraData> _PerCameraData = new();
        readonly Dictionary<Camera, int> _PerCameraLastFrame = new();
        Coroutine _EndOfFrame;
        bool _IsEndOfFrame;

        // Shadows execute outside the system loop, so we need a separate flag. Camera data
        // will be loaded from system loop, but needs to be stored at end of rendering.
        bool _RenderShadows;

        // Fallback Flags
        // If a camera is rendering the surface and/or volume, then it needs a viewpoint to
        // have executed or there will be NaNs and possibly null exceptions.

        // Tracks if data has executed.
        bool _HasAnyViewpointExecuted;

        // Tracks if a camera that will render the water has rendered.
        bool _HasAnyViewerRendered;

        bool FallBackRequired => _HasAnyViewerRendered && !_HasAnyViewpointExecuted;

        /// <summary>
        /// Whether the current data loop is being executed as a separate viewpoint from within the camera loop.
        /// </summary>
        internal bool IsSeparateViewpointCameraLoop { get; private set; }

        /// <summary>
        /// Whether we support and opted for multiple viewpoints (including editor only).
        /// </summary>
        internal bool IsMultipleViewpointMode => MultipleViewpoints || EditorMultipleViewpoints;

        /// <summary>
        /// Whether we only support and opted for a single viewpoint.
        /// </summary>
        internal bool IsSingleViewpointMode => !IsMultipleViewpointMode;

        internal bool SupportsRecursiveRendering =>
#if !UNITY_6000_0_OR_NEWER
            // HDRP cannot recursive render for 2022.
            !RenderPipelineHelper.IsHighDefinition &&
#endif
            true;

        bool EditorMultipleViewpoints =>
#if UNITY_EDITOR
            (_EditorMultipleViewpoints && SupportsRecursiveRendering) ||
#endif
            false;

        /// <summary>
        /// Whether we support and opted for multiple viewpoints (excluding editor only).
        /// </summary>
        internal bool MultipleViewpoints => _MultipleViewpoints && SupportsRecursiveRendering;

        bool ShouldExecuteSkippedFrame(Camera camera)
        {
            // Let's not do this if only for scene cameras.
            if (!MultipleViewpoints)
            {
                return false;
            }

            if (_DataBackgroundMode == WaterDataBackgroundMode.Never)
            {
                return false;
            }

            // Always execute for the scene camera.
            if (camera.cameraType == CameraType.SceneView)
            {
                return true;
            }

            if (_DataBackgroundMode == WaterDataBackgroundMode.Always)
            {
                return true;
            }

            if (_DataBackgroundMode == WaterDataBackgroundMode.Inactive && camera.isActiveAndEnabled)
            {
                return true;
            }

            if (_DataBackgroundMode == WaterDataBackgroundMode.Disabled && !camera.enabled)
            {
                return true;
            }

            return false;
        }

        internal bool ShouldExecuteQueries(Camera camera)
        {
            return camera != null && _PerCameraData.ContainsKey(camera) && _PerCameraData[camera]._ExecutedThisFrame;
        }

        System.Collections.IEnumerator UpdateSkippedCameras()
        {
            while (true)
            {
                // There is no better way:
                // • Awaitable.EndOfFrameAsync is more restrictive.
                // • "yield return null" is next frame Update.
                // • RenderPipelineManager.endContextRendering is SRP only and not guaranteed to
                //   execute after all cameras have rendered (each view has its own context).
                yield return Helpers.WaitForEndOfFrame;

                if (IsSingleViewpointMode)
                {
                    // This should not happen, as enumerator not registered.
                    continue;
                }

                _IsEndOfFrame = true;

                foreach (var camera in _Cameras)
                {
                    if (camera == null) continue;
                    if (!_PerCameraData.ContainsKey(camera)) continue;

                    var data = _PerCameraData[camera];

                    data._ExecutedThisFrame = data._RenderedThisFrame;

                    if (!data._RenderedThisFrame && ShouldExecuteSkippedFrame(camera))
                    {
                        ExecuteViewpoint(camera);
                    }

                    data._RenderedThisFrame = false;
                }

                _IsEndOfFrame = false;
            }
        }

        void LoadCameraData(Camera camera)
        {
            if (IsSingleViewpointMode)
            {
                return;
            }

            if (camera == null)
            {
                return;
            }

            if (!_PerCameraData.ContainsKey(camera))
            {
                _PerCameraData.Add(camera, new()
                {
                    // The extra LOD accounts for reading off the cascade (eg CurrentIndex + LodChange + 1).
                    _CascadeData = InitializePerFrameMaterialParameters(),
                });
            }

            _CurrentPerCameraData = _PerCameraData[camera];

            CascadeData = _CurrentPerCameraData._CascadeData;
            Scale = _CurrentPerCameraData._Scale;
            Position = _CurrentPerCameraData._Position;
            _OldViewpointPosition = _CurrentPerCameraData._OldViewpointPosition;
            _ViewpointHeightAboveWaterSmooth = _CurrentPerCameraData._ViewpointHeightAboveWaterSmooth;
            _IsFirstFrameSinceEnabled = _CurrentPerCameraData._IsFirstFrameSinceEnabled;

            foreach (var simulation in Simulations)
            {
                if (!simulation.Enabled) continue;
                simulation.LoadCameraData(camera);
            }

            _CurrentPerCameraData._RenderedThisFrame = true;
            _CurrentPerCameraData._ExecutedThisFrame = true;

            // We are in the camera loop AND there is additional camera data.
            IsSeparateViewpointCameraLoop = true;

            s_OnLoadCameraData?.Invoke(camera);
        }

        void StoreCameraData(Camera camera)
        {
            if (IsSingleViewpointMode)
            {
                return;
            }

            _CurrentPerCameraData._Scale = Scale;
            _CurrentPerCameraData._Position = Position;
            _CurrentPerCameraData._OldViewpointPosition = _OldViewpointPosition;
            _CurrentPerCameraData._ViewpointHeightAboveWaterSmooth = _ViewpointHeightAboveWaterSmooth;
            _CurrentPerCameraData._IsFirstFrameSinceEnabled = _IsFirstFrameSinceEnabled;
            _CurrentPerCameraData._ViewerHeightAboveWater = ViewerHeightAboveWater;
            _CurrentPerCameraData._ViewerDistanceToShoreline = ViewerDistanceToShoreline;

            foreach (var simulation in Simulations)
            {
                if (!simulation.Enabled) continue;
                simulation.StoreCameraData(camera);
            }

            s_OnStoreCameraData?.Invoke(camera);
        }

        /// <summary>
        /// Cleans up data for a particular camera if no longer rendering water.
        /// </summary>
        /// <param name="camera">The camera to clean up data for.</param>
        void RemoveCameraData(Camera camera)
        {
            // NOTE: Handles all camera data. Add more as needed!

            Surface.RemoveCameraData(camera);

            foreach (var lods in Simulations)
            {
                lods.RemoveCameraData(camera);
            }

            if (_PerCameraData.ContainsKey(camera))
            {
                _PerCameraData.Remove(camera);
            }

            s_OnRemoveCameraData?.Invoke(camera);
        }

        void PruneCameraData()
        {
            var length = _Cameras.Count;
            for (var i = length - 1; i >= 0; i--)
            {
                var camera = _Cameras[i];

                // Check against surface rendering and whether we executed, as if we did not, then
                // the data is no longer synced anyway.
                if (camera == null || !ShouldRender(camera, Surface._Layer, _CameraExclusions) || !_PerCameraData[camera]._ExecutedThisFrame)
                {
                    // Do not prune the fallback camera!
                    if (FallBackRequired && GetViewer(initial: true) == camera)
                    {
                        continue;
                    }

                    RemoveCameraData(camera);
                    _Cameras.RemoveAt(i);
                }
            }

            // Load single camera data to prevent null exceptions.
            if (_Cameras.Count <= 0)
            {
                CascadeData = _CascadeData;
            }
        }

        internal bool GetViewerHeightAboveWater(Camera camera, out float height)
        {
            height = SeaLevel;

            if (!IsMultipleViewpointMode)
            {
                height = ViewerHeightAboveWater;
                return true;
            }

            if (!_PerCameraData.ContainsKey(camera))
            {
                return false;
            }

            height = _PerCameraData[camera]._ViewerHeightAboveWater;
            return true;
        }

        internal bool GetViewerDistanceToShoreline(Camera camera, out float distance)
        {
            distance = SeaLevel;

            if (!IsMultipleViewpointMode)
            {
                distance = ViewerDistanceToShoreline;
                return true;
            }

            if (!_PerCameraData.ContainsKey(camera))
            {
                return false;
            }

            distance = _PerCameraData[camera]._ViewerDistanceToShoreline;
            return true;
        }

        internal Transform GetClosestViewpoint(Vector3 position)
        {
            if (!IsMultipleViewpointMode)
            {
                return Viewpoint;
            }

            var furthest = Mathf.Infinity;
            Camera result = null;

            foreach (var camera in _Cameras)
            {
                if (camera == null)
                {
                    continue;
                }

                var distance = Mathf.Abs((camera.transform.position - position).sqrMagnitude);

                if (distance < furthest)
                {
                    result = camera;
                    furthest = distance;
                }
            }

            if (result == null)
            {
                return null;
            }

            return result.transform;
        }

        internal bool IsClosestViewpoint(Vector3 position)
        {
            if (!IsMultipleViewpointMode)
            {
                return true;
            }

            var viewpoint = Viewpoint;

            return viewpoint == GetClosestViewpoint(position);
        }
    }

#if CREST_DEBUG
#if UNITY_EDITOR
    // Tests.
    partial class WaterRenderer
    {
        internal bool _SkipForTesting;

        private protected override void FixedUpdate()
        {
            if (_SkipForTesting)
            {
                return;
            }

            base.FixedUpdate();
        }

        internal void TestFixedUpdate()
        {
            _SkipForTesting = false;
            FixedUpdate();
            _SkipForTesting = true;
        }

        internal void TestLateUpdate()
        {
            _SkipForTesting = false;
            LateUpdate();
            _SkipForTesting = true;
        }
    }
#endif
#endif
}
