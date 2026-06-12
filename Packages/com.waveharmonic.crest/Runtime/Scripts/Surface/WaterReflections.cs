// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

// This script originated from the unity standard assets. It has been modified heavily to be camera-centric (as opposed to
// geometry-centric) and assumes a single main camera which simplifies the code.

using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering.Universal;
using WaveHarmonic.Crest.Internal;

namespace WaveHarmonic.Crest
{
    /// <summary>
    /// What side of the water surface to render planar reflections for.
    /// </summary>
    [@GenerateDoc]
    public enum WaterReflectionSide
    {
        /// <inheritdoc cref="Generated.WaterReflectionSide.Both"/>
        [Tooltip("Both sides. Most expensive.")]
        Both,

        /// <inheritdoc cref="Generated.WaterReflectionSide.Above"/>
        [Tooltip("Above only. Typical for planar reflections.")]
        Above,

        /// <inheritdoc cref="Generated.WaterReflectionSide.Below"/>
        [Tooltip("Below only. For total internal reflections.")]
        Below,
    }

    /// <summary>
    /// Renders reflections for water. Currently on planar reflections.
    /// </summary>
    [Serializable]
    public sealed partial class WaterReflections : Versioned
    {
        [@Space(10)]

        [@Label("Enable")]
        [Tooltip("Whether planar reflections are enabled.\n\nAllocates/releases resources if state has changed.")]
        [@GenerateAPI(Setter.Custom)]
        [@DecoratedField, SerializeField]
        internal bool _Enabled;


        [@Heading("Capture")]

        [Tooltip("What side of the water surface to render planar reflections for.")]
        [@GenerateAPI(Setter.Custom, name: "ReflectionSide")]
        [@DecoratedField, SerializeField]
        internal WaterReflectionSide _Mode = WaterReflectionSide.Above;

        [Tooltip("The layers to rendering into reflections.")]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        LayerMask _Layers = 1; // Default

        [Tooltip("Resolution of the reflection texture.")]
        [@GenerateAPI]
        [@Delayed, SerializeField]
        int _Resolution = 256;

        [Tooltip("Overscan amount to capture off-screen content.\n\nRenders the reflections at a larger viewport size to capture off-screen content when the surface reflects off-screen. This avoids a category of artifacts - especially when looking down. This can be expensive, as the value is a multiplier to the capture size.")]
        [@Range(1, 2)]
        [@GenerateAPI]
        [SerializeField]
        float _Overscan = 1.5f;

        [@Space(10)]

        [Tooltip("Whether to render the sky or fallback to default reflections.\n\nNot rendering the sky can prevent other custom shaders (like tree leaves) from being in the final output. Enable for best compatibility.")]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        internal bool _Sky = true;

        [Tooltip("Disables pixel lights (BIRP only).")]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        bool _DisablePixelLights = true;

#pragma warning disable 414
        [Tooltip("Disables shadows.")]
        [@GenerateAPI(Setter.Custom)]
        [@DecoratedField, SerializeField]
        bool _DisableShadows = true;
#pragma warning restore 414

        [Tooltip("Whether to allow HDR.")]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        bool _HDR = true;

        [Tooltip("Whether to allow stencil operations.")]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        bool _Stencil = false;

        [@Space(10)]

        [Tooltip("Overrides global quality settings.")]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        QualitySettingsOverride _QualitySettingsOverride = new()
        {
            _OverrideLodBias = false,
            _LodBias = 0.5f,
            _OverrideMaximumLodLevel = false,
            _MaximumLodLevel = 1,
            _OverrideTerrainPixelError = false,
            _TerrainPixelError = 10,
        };

        [@Heading("Culling")]

        [Tooltip("The near clip plane clips any geometry before it, removing it from reflections.\n\nCan be used to reduce reflection leaks and support varied water level.")]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        float _ClipPlaneOffset;

        [Tooltip("Anything beyond the far clip plane is not rendered.")]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        float _FarClipPlane = 1000;

        [Tooltip("Disables occlusion culling.")]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        bool _DisableOcclusionCulling = true;


        [@Heading("Refresh Rate")]

        [Tooltip("Refresh reflection every x frames (one is every frame)")]
        [@Enable(nameof(_RenderOnlySingleCamera))]
        [@DecoratedField, SerializeField]
        int _RefreshPerFrames = 1;

        [@Enable(nameof(_RenderOnlySingleCamera))]
        [@DecoratedField, SerializeField]
        int _FrameRefreshOffset = 0;


        [@Heading("Oblique Matrix")]

        [@Label("Enable")]
        [Tooltip("An oblique matrix will clip anything below the surface for free.\n\nDisable if you have problems with certain effects. Disabling can cause other artifacts like objects below the surface to appear in reflections.")]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        bool _UseObliqueMatrix = true;

        [Tooltip("Planar relfections using an oblique frustum for better performance.\n\nThis can cause depth issues for TIRs, especially near the surface.")]
        [@Enable(nameof(_UseObliqueMatrix))]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        bool _NonObliqueNearSurface;

        [Tooltip("If within this distance from the surface, disable the oblique matrix.")]
        [@Enable(nameof(_NonObliqueNearSurface))]
        [@Enable(nameof(_UseObliqueMatrix))]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        float _NonObliqueNearSurfaceThreshold = 0.05f;


        [@Heading("Advanced")]

        [Tooltip("Whether to render to the viewer camera only.\n\nWhen disabled, reflections will render for all cameras rendering the water layer, which currently this prevents Refresh Rate from working. Enabling will unlock the Refresh Rate heading.")]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        internal bool _RenderOnlySingleCamera;

        [Tooltip("Renderer index for the reflection camera.")]
        [@Show(RenderPipeline.Universal)]
        [@Minimum(0)]
        [@GenerateAPI(Setter.Custom)]
        [@DecoratedField]
        [@SerializeField]
        int _RendererIndex;

        [@Space(10)]

        [@DecoratedField, SerializeField]
        DebugFields _Debug = new();

        [Serializable]
        sealed class DebugFields
        {
            [@DecoratedField, SerializeField]
            internal bool _ShowHiddenObjects;

            [Tooltip("Rendering reflections per-camera requires recursive rendering. Check this toggle if experiencing issues. The other downside without it is a one-frame delay.")]
            [@DecoratedField, SerializeField]
            internal bool _DisableRecursiveRendering;

            [Tooltip("Whether to create a context more compatible for planar reflections camera. Try enabling this if you are getting exceptions.")]
            [@Show(RenderPipeline.Universal)]
            [@DecoratedField]
            [SerializeField]
            internal bool _ForceCompatibility;
        }

        static class ShaderIDs
        {
            public static int s_ReflectionColorTexture = Shader.PropertyToID("_Crest_ReflectionColorTexture");
            public static int s_ReflectionDepthTexture = Shader.PropertyToID("_Crest_ReflectionDepthTexture");
            public static int s_ReflectionPositionNormal = Shader.PropertyToID("_Crest_ReflectionPositionNormal");
            public static readonly int s_ReflectionMatrixIVP = Shader.PropertyToID("_Crest_ReflectionMatrixIVP");
            public static readonly int s_ReflectionMatrixV = Shader.PropertyToID("_Crest_ReflectionMatrixV");
            public static readonly int s_Crest_ReflectionOverscan = Shader.PropertyToID("_Crest_ReflectionOverscan");

            public static readonly int s_PlanarReflectionsApplySmoothness = Shader.PropertyToID("_Crest_PlanarReflectionsApplySmoothness");
        }

        internal WaterRenderer _Water;
        internal UnderwaterRenderer _UnderWater;

        bool _ApplySmoothness;

        RenderTexture _ColorTexture;
        RenderTexture _DepthTexture;
        internal RenderTexture ColorTexture => _ColorTexture;
        internal RenderTexture DepthTexture => _DepthTexture;
        readonly Vector4[] _ReflectionPositionNormal = new Vector4[2];
        readonly Matrix4x4[] _ReflectionMatrixIVP = new Matrix4x4[2];
        readonly Matrix4x4[] _ReflectionMatrixV = new Matrix4x4[2];

        internal int _ActiveSlice;

        Camera _CameraViewpoint;
        Skybox _CameraViewpointSkybox;
        Camera _CameraReflections;
        Skybox _CameraReflectionsSkybox;
        internal Camera ReflectionCamera => _CameraReflections;

        int RefreshPerFrames => _RenderOnlySingleCamera ? _RefreshPerFrames : 1;
        long _LastRefreshOnFrame = -1;

        internal bool SupportsRecursiveRendering => _Water.SupportsRecursiveRendering && !_Debug._DisableRecursiveRendering;

        readonly float[] _CullDistances = new float[32];

        Texture _CameraDepthTexture;

        /// <summary>
        /// Invoked when the reflection camera is created.
        /// </summary>
        public static Action<Camera> OnCameraAdded { get; set; }

        bool RequireTemporaryTargets =>
#if UNITY_6000_0_OR_NEWER && d_UnityURP
            // As of Unity 6 we can write directly to a slice for URP.
            !RenderPipelineHelper.IsUniversal &&
#endif
            true;

        internal void OnEnable()
        {
            // We initialized here previously to fix the first frame being black, but could not
            // replicate anymore.

#if d_UnityURP
#if UNITY_6000_0_OR_NEWER
            RenderPipelineManager.beginCameraRendering -= CaptureTargetDepth;
            RenderPipelineManager.beginCameraRendering += CaptureTargetDepth;
#endif
#endif
        }

        internal void OnDisable()
        {
            Shader.SetGlobalTexture(ShaderIDs.s_ReflectionColorTexture, Texture2D.blackTexture);
            Shader.SetGlobalTexture(ShaderIDs.s_ReflectionDepthTexture, Texture2D.blackTexture);

#if d_UnityURP
#if UNITY_6000_0_OR_NEWER
            RenderPipelineManager.beginCameraRendering -= CaptureTargetDepth;
#endif
#endif
        }

        internal void OnDestroy()
        {
            if (_CameraReflections)
            {
                Helpers.Destroy(_CameraReflections.gameObject);
                _CameraReflections = null;
            }

            if (_ColorTexture)
            {
                _ColorTexture.Release();
                Helpers.Destroy(_ColorTexture);
                _ColorTexture = null;
            }

            if (_DepthTexture)
            {
                _DepthTexture.Release();
                Helpers.Destroy(_DepthTexture);
                _DepthTexture = null;
            }
        }

        internal bool ShouldRender(Camera camera)
        {
            if (!_Enabled)
            {
                return false;
            }

            // If no surface, then do not execute the reflection camera.
            if (!_Water._ActiveModules.HasFlag(WaterRenderer.ActiveModules.Surface))
            {
                return false;
            }

            // This method could be executed twice: once by the camera rendering the surface,
            // and once again by the planar reflection camera. For the latter, we do not want
            // to proceed or infinite recursion. For safety.
            if (camera == _CameraReflections)
            {
                return false;
            }

            // Avoid these types for now.
            if (camera.cameraType == CameraType.Reflection)
            {
                return false;
            }

            return true;
        }

        internal void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (SupportsRecursiveRendering)
            {
                // This option only valid for recursive, otherwise, it is always single camera.
                if (_RenderOnlySingleCamera && camera != _Water.Viewer)
                {
                    return;
                }

                _CameraViewpoint = camera;
                LateUpdate(context);
            }

            if (camera == _CameraViewpoint)
            {
                // TODO: Emit an event instead so WBs can listen.
                Shader.SetGlobalTexture(ShaderIDs.s_ReflectionColorTexture, _ColorTexture);
                Shader.SetGlobalTexture(ShaderIDs.s_ReflectionDepthTexture, _DepthTexture);
            }
        }

        internal void OnEndReflectionCameraRendering(Camera camera)
        {
            if (camera == ReflectionCamera)
            {
                // Appears to be the only reasonable way to get camera depth separately for SRPs.
                _CameraDepthTexture = Shader.GetGlobalTexture(Crest.ShaderIDs.Unity.s_CameraDepthTexture);
            }
        }

        internal void OnEndCameraRendering(Camera camera)
        {
            Shader.SetGlobalTexture(ShaderIDs.s_ReflectionColorTexture, Texture2D.blackTexture);
        }

        internal void LateUpdate()
        {
            // Check if enabled for at least one material every frame.
            _ApplySmoothness = false;

            CheckSurfaceMaterial(_Water.Surface.Material);

            foreach (var wb in WaterBody.WaterBodies)
            {
                CheckSurfaceMaterial(wb._Material);
            }

            if (SupportsRecursiveRendering)
            {
                return;
            }

            // Passing a struct.
            LateUpdate(new());
        }

        internal void LateUpdate(ScriptableRenderContext context)
        {
            // Frame rate limiter.
            if (_LastRefreshOnFrame > 0 && RefreshPerFrames > 1)
            {
                // Check whether we need to refresh the frame.
                if (Math.Abs(_FrameRefreshOffset) % _RefreshPerFrames != Time.renderedFrameCount % _RefreshPerFrames)
                {
                    return;
                }
            }

            if (_Water == null)
            {
                return;
            }

            if (!SupportsRecursiveRendering)
            {
                _CameraViewpoint = _Water.Viewer;
            }

            if (_CameraViewpoint == null)
            {
                return;
            }

#if UNITY_EDITOR
            // Fix "Screen position out of view frustum" when 2D view activated.
            {
                var sceneView = UnityEditor.SceneView.lastActiveSceneView;
                if (sceneView != null && sceneView.in2DMode && sceneView.camera == _CameraViewpoint)
                {
                    return;
                }
            }
#endif

            CreateWaterObjects(_CameraViewpoint);

            if (!_CameraReflections)
            {
                return;
            }

            UpdateCameraModes();
            ForceDistanceCulling(_FarClipPlane);

            // TODO: Do not do this every frame.
            if (_Mode != WaterReflectionSide.Both)
            {
                Helpers.ClearRenderTexture(_ColorTexture, Color.clear, depth: true);
                Helpers.ClearRenderTexture(_DepthTexture, Color.clear, depth: true);
            }

            var isActive = _Water.Surface.Root.gameObject.activeSelf;

            // We do not want the water plane when rendering planar reflections.
            _Water.Surface.Root.gameObject.SetActive(false);

            // Optionally disable pixel lights for reflection/refraction
            var oldPixelLightCount = QualitySettings.pixelLightCount;
            if (_DisablePixelLights)
            {
                QualitySettings.pixelLightCount = 0;
            }

            // Optionally disable shadows.
            var oldShadowQuality = QualitySettings.shadows;
            if (_DisableShadows)
            {
                QualitySettings.shadows = UnityEngine.ShadowQuality.Disable;
            }

            _QualitySettingsOverride.Override();

            // Invert culling because view is mirrored. Does not work for HDRP (handled elsewhere).
            var oldCulling = GL.invertCulling;
            GL.invertCulling = !oldCulling;

#if UNITY_EDITOR
            try
#endif
            {
                Render(context);
            }
#if UNITY_EDITOR
            // Ensure that any global settings are restored.
            finally
#endif
            {
                GL.invertCulling = oldCulling;

                // Restore shadows.
                if (_DisableShadows)
                {
                    QualitySettings.shadows = oldShadowQuality;
                }

                // Restore pixel light count
                if (_DisablePixelLights)
                {
                    QualitySettings.pixelLightCount = oldPixelLightCount;
                }

                _QualitySettingsOverride.Restore();

                _Water.Surface.Root.gameObject.SetActive(isActive);

                // Remember this frame as last refreshed.
                _LastRefreshOnFrame = Time.renderedFrameCount;
            }
        }

        void Render(ScriptableRenderContext context)
        {
            var colorTarget = _ColorTexture;
            var depthTarget = _DepthTexture;

            if (RequireTemporaryTargets)
            {
                var descriptor = _ColorTexture.descriptor;
                descriptor.dimension = TextureDimension.Tex2D;
                descriptor.volumeDepth = 1;
                descriptor.useMipMap = false;
                // No need to clear, as camera clears using the skybox.
                colorTarget = RenderTexture.GetTemporary(descriptor);

                if (RenderPipelineHelper.IsLegacy)
                {
                    descriptor = _DepthTexture.descriptor;
                    descriptor.dimension = TextureDimension.Tex2D;
                    descriptor.volumeDepth = 1;
                    descriptor.useMipMap = false;
                    // No need to clear, as camera clears using the skybox.
                    depthTarget = RenderTexture.GetTemporary(descriptor);
                }
            }

            if (RenderPipelineHelper.IsLegacy)
            {
                // Not documented, but does not work for SRPs.
                _CameraReflections.SetTargetBuffers(colorTarget.colorBuffer, depthTarget.depthBuffer);
            }
            else
            {
                _CameraReflections.targetTexture = colorTarget;
            }

            if (_Mode != WaterReflectionSide.Below)
            {
                if (_UnderWater._Enabled)
                {
                    // Disable underwater layer. It is the only way to exclude probes.
                    _CameraReflections.cullingMask = _Layers & ~(1 << _UnderWater.Layer);
                }

                _ActiveSlice = 0;

                RenderCamera(context, _CameraReflections, Vector3.up, false, 0);

                CopyTargets(colorTarget, depthTarget, 0);

                _ReflectionPositionNormal[0] = ComputeHorizonPositionAndNormal(_CameraReflections, _Water.SeaLevel, 0.5f / _Resolution, false);

                _CameraReflections.ResetProjectionMatrix();
            }

            if (_Mode != WaterReflectionSide.Above)
            {
                if (_UnderWater._Enabled)
                {
                    // Enable underwater layer.
                    _CameraReflections.cullingMask = _Layers | (1 << _UnderWater.Layer);
                    // We need the depth texture for underwater.
                    _CameraReflections.depthTextureMode = DepthTextureMode.Depth;
                }

                _ActiveSlice = 1;

                RenderCamera(context, _CameraReflections, Vector3.down, _NonObliqueNearSurface, 1);

                CopyTargets(colorTarget, depthTarget, 1);

                _ReflectionPositionNormal[1] = ComputeHorizonPositionAndNormal(_CameraReflections, _Water.SeaLevel, -0.05f, true);

                _CameraReflections.ResetProjectionMatrix();
            }

            if (RequireTemporaryTargets)
            {
                RenderTexture.ReleaseTemporary(colorTarget);
                if (RenderPipelineHelper.IsLegacy) RenderTexture.ReleaseTemporary(depthTarget);
            }

#if !d_Crest_DisablePlanarReflectionApplySmoothness
            if (_ApplySmoothness)
            {
                // We are only using mip-maps if applying smoothness/roughness.
                _ColorTexture.GenerateMips();
            }
#endif

            Shader.SetGlobalVectorArray(ShaderIDs.s_ReflectionPositionNormal, _ReflectionPositionNormal);
            Shader.SetGlobalMatrixArray(ShaderIDs.s_ReflectionMatrixIVP, _ReflectionMatrixIVP);
            Shader.SetGlobalMatrixArray(ShaderIDs.s_ReflectionMatrixV, _ReflectionMatrixV);
        }

        void RenderCamera(ScriptableRenderContext context, Camera camera, Vector3 planeNormal, bool nonObliqueNearSurface, int slice)
        {
            // Find out the reflection plane: position and normal in world space
            var planePosition = _Water.Position;

            var offset = _ClipPlaneOffset;
            {
                var viewpoint = _CameraViewpoint.transform;
                if (offset == 0f && viewpoint.position.y == planePosition.y)
                {
                    // Minor offset to prevent "Screen position out of view frustum". Needs to scale
                    // with distance from center.
                    offset = viewpoint.position.magnitude >= 15000f ? 0.01f : 0.001f;
                }
            }

            // Reflect camera around reflection plane
            var distance = -Vector3.Dot(planeNormal, planePosition) - offset;
            var reflectionPlane = new Vector4(planeNormal.x, planeNormal.y, planeNormal.z, distance);

            var reflection = Matrix4x4.zero;
            CalculateReflectionMatrix(ref reflection, reflectionPlane);

            camera.worldToCameraMatrix = _CameraViewpoint.worldToCameraMatrix * reflection;

            // Setup oblique projection matrix so that near plane is our reflection
            // plane. This way we clip everything below/above it for free.
            var clipPlane = CameraSpacePlane(camera, planePosition, planeNormal, 1.0f);

            if (_UseObliqueMatrix && (!nonObliqueNearSurface || Mathf.Abs(_CameraViewpoint.transform.position.y - planePosition.y) > _NonObliqueNearSurfaceThreshold))
            {
                var matrix = _CameraViewpoint.CalculateObliqueMatrix(clipPlane);
                // Overscan.
                var overscan = 1f - (_Overscan - 1f) * 0.5f;
                matrix[0, 0] *= overscan;
                matrix[1, 1] *= overscan;
                camera.projectionMatrix = matrix;
            }

            // Set custom culling matrix from the current camera
            camera.cullingMatrix = _CameraViewpoint.projectionMatrix * _CameraViewpoint.worldToCameraMatrix;

            camera.transform.position = reflection.MultiplyPoint(_CameraViewpoint.transform.position);
            var euler = _CameraViewpoint.transform.eulerAngles;
            camera.transform.eulerAngles = new(-euler.x, euler.y, euler.z);
            camera.cullingMatrix = camera.projectionMatrix * camera.worldToCameraMatrix;

            _ReflectionMatrixV[slice] = camera.worldToCameraMatrix;
            _ReflectionMatrixIVP[slice] = (GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix).inverse;

            if (SupportsRecursiveRendering)
            {
                Helpers.RenderCamera(camera, context, slice, _Debug._ForceCompatibility);
            }
            else
            {
                camera.Render();
            }
        }

        void CopyTargets(Texture color, Texture depth, int slice)
        {
            if (RequireTemporaryTargets)
            {
                Graphics.CopyTexture(color, 0, 0, 0, 0, _Resolution, _Resolution, _ColorTexture, slice, 0, 0, 0);
            }

            if (!RenderPipelineHelper.IsLegacy)
            {
                depth = _CameraDepthTexture;
            }

            if (Rendering.IsRenderGraph)
            {
                return;
            }

            // This can change between depth and R32 based on settings.
            if (depth != null && depth.graphicsFormat != _DepthTexture.graphicsFormat)
            {
                RecreateDepth(depth);
            }

            if (depth != null && depth.width >= _Resolution)
            {
                Graphics.CopyTexture(depth, 0, 0, 0, 0, _Resolution, _Resolution, _DepthTexture, slice, 0, 0, 0);
            }
        }

        /// <summary>
        /// Limit render distance for reflection camera for first 32 layers
        /// </summary>
        /// <param name="farClipPlane">reflection far clip distance</param>
        void ForceDistanceCulling(float farClipPlane)
        {
            // Cannot use spherical culling with SRPs. Will error.
            if (!RenderPipelineHelper.IsLegacy)
            {
                return;
            }

            for (var i = 0; i < _CullDistances.Length; i++)
            {
                // The culling distance
                _CullDistances[i] = farClipPlane;
            }
            _CameraReflections.layerCullDistances = _CullDistances;
            _CameraReflections.layerCullSpherical = true;
        }

        void UpdateCameraModes()
        {
#if d_UnityHDRP
            if (RenderPipelineHelper.IsHighDefinition)
            {
                if (_CameraReflections.TryGetComponent(out HDAdditionalCameraData additionalCameraData))
                {
                    additionalCameraData.clearColorMode = _Sky ? HDAdditionalCameraData.ClearColorMode.Sky :
                        HDAdditionalCameraData.ClearColorMode.Color;
                }
            }
            else
#endif
            {
                _CameraReflections.clearFlags = _Sky ? CameraClearFlags.Skybox : CameraClearFlags.Color;

                if (_Sky && _CameraViewpoint.TryGetComponent(out _CameraViewpointSkybox))
                {
                    if (_CameraReflectionsSkybox == null)
                    {
                        _CameraReflectionsSkybox = _CameraReflections.gameObject.AddComponent<Skybox>();
                    }

                    _CameraReflectionsSkybox.enabled = _CameraViewpointSkybox.enabled;
                    _CameraReflectionsSkybox.material = _CameraViewpointSkybox.material;
                }
                else
                {
                    // Destroy otherwise skybox will not render if empty.
                    Helpers.Destroy(_CameraViewpointSkybox);
                    _CameraViewpointSkybox = null;
                }
            }

            // Update other values to match current camera.
            // Even if we are supplying custom camera&projection matrices,
            // some of values are used elsewhere (e.g. skybox uses far plane).

            _CameraReflections.farClipPlane = _CameraViewpoint.farClipPlane;
            _CameraReflections.nearClipPlane = _CameraViewpoint.nearClipPlane;
            _CameraReflections.orthographic = _CameraViewpoint.orthographic;
            _CameraReflections.fieldOfView = _CameraViewpoint.fieldOfView;
            _CameraReflections.orthographicSize = _CameraViewpoint.orthographicSize;
            _CameraReflections.allowMSAA = false;
            _CameraReflections.aspect = _CameraViewpoint.aspect;
            _CameraReflections.useOcclusionCulling = !_DisableOcclusionCulling && _CameraViewpoint.useOcclusionCulling;
            _CameraReflections.depthTextureMode = _CameraViewpoint.depthTextureMode;

            // Overscan
            {
                _CameraReflections.usePhysicalProperties = _Overscan > 1f;

                var baseSensor = new Vector2(36f, 24f);
                var focal = (baseSensor.y * 0.5f) / Mathf.Tan(_CameraViewpoint.fieldOfView * 0.5f * Mathf.Deg2Rad);

                var overscan = 1f - (_Overscan - 1f) * 0.5f;
                _CameraReflections.sensorSize = baseSensor / overscan;
                _CameraReflections.focalLength = focal;

                Shader.SetGlobalFloat(ShaderIDs.s_Crest_ReflectionOverscan, overscan);
            }
        }

        void RecreateDepth(Texture depth)
        {
            if (_DepthTexture != null && _DepthTexture.IsCreated())
            {
                _DepthTexture.Release();
                _DepthTexture.descriptor = depth.GetDescriptor();
            }
            else
            {
                _DepthTexture = new(depth.GetDescriptor());
            }

            _DepthTexture.name = "_Crest_ReflectionDepth";
            _DepthTexture.width = _DepthTexture.height = _Resolution;
            _DepthTexture.isPowerOfTwo = true;
            _DepthTexture.useMipMap = false;
            _DepthTexture.autoGenerateMips = false;
            _DepthTexture.filterMode = FilterMode.Point;
            _DepthTexture.volumeDepth = 2;
            _DepthTexture.dimension = TextureDimension.Tex2DArray;
            _DepthTexture.Create();
        }

        // On-demand create any objects we need for water
        void CreateWaterObjects(Camera currentCamera)
        {
            // We cannot exclude stencil for URP, as the depth texture format always has it.
            var colorFormat = Rendering.GetDefaultColorFormat(_HDR);
            var depthFormat = Rendering.GetDefaultDepthFormat(_Stencil || RenderPipelineHelper.IsUniversal);

            // Reflection render texture
            if (!_ColorTexture || _ColorTexture.width != _Resolution || _ColorTexture.graphicsFormat != colorFormat || _ColorTexture.depthStencilFormat != depthFormat)
            {
                if (_ColorTexture)
                {
                    Helpers.Destroy(_ColorTexture);
                    Helpers.Destroy(_DepthTexture);
                }

                var descriptor = new RenderTextureDescriptor(_Resolution, _Resolution)
                {
                    dimension = TextureDimension.Tex2DArray,
                    volumeDepth = 2,
                    depthStencilFormat = depthFormat,
                    msaaSamples = 1,
                    useMipMap = false,
                };

                _ColorTexture = new(descriptor)
                {
                    name = "_Crest_ReflectionColor",
                    graphicsFormat = colorFormat,
                    isPowerOfTwo = true,
#if !d_Crest_DisablePlanarReflectionApplySmoothness
                    useMipMap = true,
#endif
                    autoGenerateMips = false,
                    filterMode = FilterMode.Trilinear,
                };
                _ColorTexture.Create();

                _DepthTexture = new(descriptor)
                {
                    name = "_Crest_ReflectionDepth",
                    graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.None,
                    isPowerOfTwo = true,
                    useMipMap = false,
                    autoGenerateMips = false,
                    filterMode = FilterMode.Point,
                };

                if (RenderPipelineHelper.IsHighDefinition)
                {
                    _DepthTexture.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R32_SFloat;
                    _DepthTexture.depthStencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.None;
                }

                _DepthTexture.Create();
            }

            var create = _CameraReflections == null;

            // Camera for reflection
            if (create)
            {
                var go = new GameObject("_Crest_WaterReflectionCamera");
                go.transform.SetParent(_Water.Container.transform, worldPositionStays: true);
                _CameraReflections = go.AddComponent<Camera>();
                _CameraReflections.enabled = false;
                _CameraReflections.cameraType = CameraType.Reflection;
                _CameraReflections.backgroundColor = Color.clear;

                if (RenderPipelineHelper.IsLegacy)
                {
#pragma warning disable IDE0079
#pragma warning disable CS0618 // Type or member is obsolete
                    _CameraReflections.gameObject.AddComponent<FlareLayer>();
#pragma warning restore CS0618 // Type or member is obsolete
#pragma warning restore IDE0079
                }

#if d_UnityHDRP
                if (RenderPipelineHelper.IsHighDefinition)
                {
                    var additionalCameraData = _CameraReflections.gameObject.AddComponent<HDAdditionalCameraData>();
                    additionalCameraData.invertFaceCulling = true;
                    additionalCameraData.defaultFrameSettings = FrameSettingsRenderType.RealtimeReflection;
                    additionalCameraData.backgroundColorHDR = Color.clear;
                    additionalCameraData.customRenderingSettings = true;
                    additionalCameraData.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.CustomPass] = true;
                    additionalCameraData.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.CustomPass, true);
                }
#endif

#if d_UnityURP
                if (RenderPipelineHelper.IsUniversal)
                {
                    var additionalCameraData = _CameraReflections.gameObject.AddComponent<UniversalAdditionalCameraData>();
                    additionalCameraData.requiresDepthTexture = true;
                }
#endif

                _UpdateCamera = true;
            }

            if (_UpdateCamera)
            {
                _CameraReflections.gameObject.hideFlags = _Debug._ShowHiddenObjects ? HideFlags.DontSave : HideFlags.HideAndDontSave;

#if d_UnityURP
                if (RenderPipelineHelper.IsUniversal)
                {
                    var additionalCameraData = _CameraReflections.GetUniversalAdditionalCameraData();
                    additionalCameraData.SetRenderer(_RendererIndex);
                    additionalCameraData.renderShadows = !_DisableShadows; // Does not appear to work!
                    additionalCameraData.requiresColorTexture = _Mode != WaterReflectionSide.Above; // or incur assertions
                }
#endif

                _UpdateCamera = false;
            }

            if (create)
            {
                OnCameraAdded?.Invoke(_CameraReflections);
            }
        }

        // Given position/normal of the plane, calculates plane in camera space.
        Vector4 CameraSpacePlane(Camera cam, Vector3 pos, Vector3 normal, float sideSign)
        {
            var offset = _ClipPlaneOffset;
            {
                var viewpoint = _CameraViewpoint.transform;
                if (offset == 0f && viewpoint.position.y == 0f && viewpoint.rotation.eulerAngles.y == 0f)
                {
                    // Minor offset to prevent "Screen position out of view frustum". Smallest number
                    // to work with both above and below. Smallest number to work with both above and
                    // below. Could be BIRP only.
                    offset = 0.00001f;
                }
            }

            var offsetPos = pos + normal * offset;
            var m = cam.worldToCameraMatrix;
            var cpos = m.MultiplyPoint(offsetPos);
            var cnormal = m.MultiplyVector(normal).normalized * sideSign;
            return new(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
        }

        // Calculates reflection matrix around the given plane
        static void CalculateReflectionMatrix(ref Matrix4x4 reflectionMat, Vector4 plane)
        {
            reflectionMat.m00 = 1F - 2F * plane[0] * plane[0];
            reflectionMat.m01 = -2F * plane[0] * plane[1];
            reflectionMat.m02 = -2F * plane[0] * plane[2];
            reflectionMat.m03 = -2F * plane[3] * plane[0];

            reflectionMat.m10 = -2F * plane[1] * plane[0];
            reflectionMat.m11 = 1F - 2F * plane[1] * plane[1];
            reflectionMat.m12 = -2F * plane[1] * plane[2];
            reflectionMat.m13 = -2F * plane[3] * plane[1];

            reflectionMat.m20 = -2F * plane[2] * plane[0];
            reflectionMat.m21 = -2F * plane[2] * plane[1];
            reflectionMat.m22 = 1F - 2F * plane[2] * plane[2];
            reflectionMat.m23 = -2F * plane[3] * plane[2];

            reflectionMat.m30 = 0F;
            reflectionMat.m31 = 0F;
            reflectionMat.m32 = 0F;
            reflectionMat.m33 = 1F;
        }

        /// <summary>
        /// Compute intersection between the frustum far plane and given plane, and return view space
        /// position and normal for this horizon line.
        /// </summary>
        static Vector4 ComputeHorizonPositionAndNormal(Camera camera, float positionY, float offset, bool flipped)
        {
            var position = Vector2.zero;
            var normal = Vector2.zero;

            // Set up back points of frustum.
            var positionNDC = new NativeArray<Vector3>(4, Allocator.Temp);
            var positionWS = new NativeArray<Vector3>(4, Allocator.Temp);
            try
            {

                var farPlane = camera.farClipPlane;
                positionNDC[0] = new(0f, 0f, farPlane);
                positionNDC[1] = new(0f, 1f, farPlane);
                positionNDC[2] = new(1f, 1f, farPlane);
                positionNDC[3] = new(1f, 0f, farPlane);

                // Project out to world.
                for (var i = 0; i < positionWS.Length; i++)
                {
                    // Eye parameter works for BIRP. With it we could skip setting matrices.
                    // In HDRP it doesn't work for XR MP. And completely breaks horizon in XR SPI.
                    positionWS[i] = camera.ViewportToWorldPoint(positionNDC[i]);
                }

                var intersectionsScreen = new NativeArray<Vector2>(2, Allocator.Temp);
                // This is only used to disambiguate the normal later. Could be removed if we were
                // more careful with point order/indices below.
                var intersectionsWorld = new NativeArray<Vector3>(2, Allocator.Temp);
                try
                {
                    var count = 0;

                    // Iterate over each back point
                    for (var i = 0; i < 4; i++)
                    {
                        // Get next back point, to obtain line segment between them.
                        var next = (i + 1) % 4;

                        // See if one point is above and one point is below sea level - then sign of the two differences
                        // will be different, and multiplying them will give a negative.
                        if ((positionWS[i].y - positionY) * (positionWS[next].y - positionY) < 0f)
                        {
                            // Proportion along line segment where intersection occurs.
                            var proportion = Mathf.Abs((positionY - positionWS[i].y) / (positionWS[next].y - positionWS[i].y));
                            intersectionsScreen[count] = Vector2.Lerp(positionNDC[i], positionNDC[next], proportion);
                            intersectionsWorld[count] = Vector3.Lerp(positionWS[i], positionWS[next], proportion);

                            count++;
                        }
                    }

                    // Two distinct results - far plane intersects water.
                    if (count == 2)
                    {
                        position = intersectionsScreen[0];
                        var tangent = intersectionsScreen[0] - intersectionsScreen[1];
                        normal.x = -tangent.y;
                        normal.y = tangent.x;

                        // Disambiguate the normal. The tangent normal might go from left to right or right
                        // to left since we do not handle ordering of intersection points.
                        if (Vector3.Dot(intersectionsWorld[0] - intersectionsWorld[1], camera.transform.right) > 0f)
                        {
                            normal = -normal;
                        }

                        // Invert the normal if camera is upside down.
                        if (camera.transform.up.y <= 0f)
                        {
                            normal = -normal;
                        }

                        // The above will sometimes produce a normal that is inverted around 90° along the
                        // Z axis. Here we are using world up to make sure that water is world down.
                        {
                            var cameraFacing = Vector3.Dot(camera.transform.right, Vector3.up);
                            var normalFacing = Vector2.Dot(normal, Vector2.right);

                            if (cameraFacing > 0.75f && normalFacing > 0.9f)
                            {
                                normal = -normal;
                            }
                            else if (cameraFacing < -0.75f && normalFacing < -0.9f)
                            {
                                normal = -normal;
                            }
                        }

                        // Minor offset helps.
                        position += normal.normalized * offset;
                    }
                }
                finally
                {
                    intersectionsScreen.Dispose();
                    intersectionsWorld.Dispose();
                }
            }
            finally
            {
                positionNDC.Dispose();
                positionWS.Dispose();
            }

            normal = normal.normalized;

            if (flipped)
            {
                normal = -normal;
            }
            else if (position.y == 0f)
            {
                // Sample anywhere if pointing downwards.
                position.y = 1f;
            }

            return new(position.x, position.y, normal.x, normal.y);
        }

        void CheckSurfaceMaterial(Material material)
        {
            if (material == null)
            {
                return;
            }

            if (!_ApplySmoothness)
            {
                _ApplySmoothness = material.GetBoolean(ShaderIDs.s_PlanarReflectionsApplySmoothness);
            }
        }

        void SetEnabled(bool previous, bool current)
        {
            if (previous == current) return;
            if (_Water == null || !_Water.isActiveAndEnabled) return;
            if (_Enabled) OnEnable(); else OnDisable();
        }

        bool _UpdateCamera;

        void SetReflectionSide(WaterReflectionSide previous, WaterReflectionSide current)
        {
            if (previous == current) return;
            _UpdateCamera = true;
        }

        void SetDisableShadows(bool previous, bool current)
        {
            if (previous == current) return;
            _UpdateCamera = true;
        }

        void SetRendererIndex(int previous, int current)
        {
            if (previous == current) return;
            _UpdateCamera = true;
        }

#if UNITY_EDITOR
        [@OnChange]
        void OnChange(string propertyPath, object previousValue)
        {
            switch (propertyPath)
            {
                case nameof(_Enabled):
                    SetEnabled((bool)previousValue, _Enabled);
                    break;
                case nameof(_Debug) + "." + nameof(DebugFields._ShowHiddenObjects):
                    _UpdateCamera = true;
                    break;
                case nameof(_Mode):
                    SetReflectionSide((WaterReflectionSide)previousValue, _Mode);
                    break;
                case nameof(_DisableShadows):
                    SetDisableShadows((bool)previousValue, _DisableShadows);
                    break;
                case nameof(_RendererIndex):
                    SetRendererIndex((int)previousValue, _RendererIndex);
                    break;
            }
        }
#endif
    }

    partial class WaterReflections
    {
        // MSAA would require separate textures to resolve to. Not worth the expense.
        [HideInInspector]
        [Obsolete("MSAA for the planar reflection camera is no longer supported. This setting will be ignored.")]
        [Tooltip("Whether to allow MSAA.")]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        bool _AllowMSAA;

        /// <summary>
        /// What side of the water surface to render planar reflections for.
        /// </summary>
        [Obsolete("Please use ReflectionSide instead.")]
        public WaterReflectionSide Mode { get => _Mode; set => _Mode = value; }
    }
}
