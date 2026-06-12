// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering.Universal;
using WaveHarmonic.Crest.Editor.Settings;
using WaveHarmonic.Crest.Internal;
using WaveHarmonic.Crest.Watercraft;

using static WaveHarmonic.Crest.Editor.ValidatedHelper;
using MessageType = WaveHarmonic.Crest.Editor.ValidatedHelper.MessageType;

namespace WaveHarmonic.Crest.Editor
{
    static class Validators
    {
        // HDRP sub-shader always first.
        const int k_SubShaderIndexHDRP = 0;
        const string k_NoneQueryProviderCollisionFloatingObjects = "The floating objects in the scene will use a flat horizontal plane.";
        const string k_NoneQueryProviderFlowFloatingObjects = "The floating objects in the scene will not receive motion from flow.";

        internal static WaterRenderer Water => Utility.Water;
        static readonly System.Collections.Generic.List<Terrain> s_Terrains = new();
        static readonly ShaderTagId s_RenderPipelineShaderTagID = new("RenderPipeline");

        [Validator(typeof(LodInput))]
        static bool ValidateTextureInput(LodInput target, ShowMessage messenger)
        {
            if (target.Data is not TextureLodInputData data) return true;

            var isValid = true;

            if (data._Texture == null)
            {
                messenger
                (
                    "Texture mode requires a texture.",
                    "Assign a texture.",
                    MessageType.Error,
                    target
                );
            }

            return isValid;
        }

        [Validator(typeof(LodInput))]
        static bool ValidateGeometryInput(LodInput target, ShowMessage messenger)
        {
            if (target.Data is not GeometryLodInputData data) return true;

            var isValid = true;

            if (data._Geometry == null)
            {
                messenger
                (
                    "Geometry mode requires geometry (ie mesh).",
                    "Assign geometry.",
                    MessageType.Error,
                    target
                );
            }

            return isValid;
        }

        [Validator(typeof(LodInput))]
        static bool ValidateRendererInput(LodInput target, ShowMessage messenger)
        {
            if (target.Data is not RendererLodInputData data) return true;

            // Check if Renderer component is attached.
            var isValid = ValidateRenderer<Renderer>
            (
                target,
                data._Renderer,
                messenger,
                data._CheckShaderPasses && (!data._OverrideShaderPass || data._ShaderPassIndex != -1),
                data._CheckShaderName ? data.ShaderPrefix : string.Empty
            );

            if (data._Renderer == null)
            {
                return isValid;
            }

            // Can cause problems if culling masks are used.
            if (!data._DisableRenderer)
            {
                isValid = ValidateRendererLayer(target.gameObject, messenger, Water) && isValid;
            }

            var isPersistent = target is FoamLodInput or DynamicWavesLodInput or ShadowLodInput;

            var materials = data._Renderer.sharedMaterials;
            for (var i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                if (material == null) continue;
                if (material.shader == null) continue;

                if (data._OverrideShaderPass && data._ShaderPassIndex > material.shader.passCount - 1)
                {
                    messenger
                    (
                        $"The shader <i>{material.shader.name}</i> used by this input has opted for the shader pass " +
                        $"index {data._ShaderPassIndex}, but there is only {material.shader.passCount} passes on the shader.",
                        "Choose a valid shader pass.",
                        MessageType.Error, target
                    );
                }

                if (isPersistent)
                {
                    if (material.shader.name is "Crest/Inputs/All/Utility" or "Crest/Inputs/All/Scale")
                    {
                        messenger
                        (
                            $"The shader <i>{material.shader.name}</i> currently is not supported by this simulation " +
                            "(Foam, Dynamic Waves or Shadow) as the shader does not support time steps.",
                            "Choose a valid shader (not <i>Crest/Inputs/All/Utility</i> or <i>Crest/Inputs/All/Scale</i>).",
                            MessageType.Error, target
                        );
                    }
                }

#if d_UnityHDRP
                if (RenderPipelineHelper.IsHighDefinition)
                {
                    if (AssetDatabase.GetAssetPath(material.shader).EndsWith(".shadergraph") && material.shader.FindSubshaderTagValue(k_SubShaderIndexHDRP, s_RenderPipelineShaderTagID).name == "HDRenderPipeline")
                    {
                        messenger
                        (
                            "It appears you are using Shader Graph with the HDRP target. " +
                            "Make sure to use the Built-In target instead for your Shader Graph to work.",
                            "Remove the HDRP target and add the Built-In target.",
                            MessageType.Warning, material.shader
                        );
                    }
                }
#endif
            }

            return isValid;
        }

        static bool ValidateRendererLayer(GameObject gameObject, ShowMessage messenger, WaterRenderer water)
        {
            if (water != null && gameObject.layer != water.Surface.Layer)
            {
                var layerName = LayerMask.LayerToName(water.Surface.Layer);
                messenger
                (
                    $"The layer is not the same as the <i>{nameof(WaterRenderer)}.{nameof(WaterRenderer.Surface)}.{nameof(SurfaceRenderer.Layer)} ({layerName})</i> which can cause problems if the <i>{layerName}</i> layer is excluded from any culling masks.",
                    $"Set layer to <i>{layerName}</i>.",
                    MessageType.Warning, gameObject,
                    (_, _) =>
                    {
                        Undo.RecordObject(gameObject, $"Change Layer to {layerName}");
                        gameObject.layer = water.Surface.Layer;
                    }
                );
            }

            // Is valid as not outright invalid but could be.
            return true;
        }

        static bool Validate(WaterReflections target, ShowMessage messenger, WaterRenderer water)
        {
            var isValid = true;

            if (!target._Enabled)
            {
                return isValid;
            }

            var material = water.Surface.Material;

            if (material != null)
            {
                if (material.HasProperty(WaterRenderer.ShaderIDs.s_PlanarReflectionsEnabled) && material.GetFloat(WaterRenderer.ShaderIDs.s_PlanarReflectionsEnabled) == 0)
                {
                    messenger
                    (
                        $"<i>Planar Reflections</i> are not enabled on the <i>{material.name}</i> material and will not be visible.",
                        $"Enable <i>Planar Reflections</i> on the material (<i>{material.name}</i>) currently assigned to the <i>{nameof(WaterRenderer)}</i> component.",
                        MessageType.Warning, material
                    );
                }

                if (material.HasProperty(WaterRenderer.ShaderIDs.s_Occlusion) && target._Mode != WaterReflectionSide.Below && material.GetFloat(WaterRenderer.ShaderIDs.s_Occlusion) == 0)
                {
                    messenger
                    (
                        $"<i>Occlusion</i> is set to zero on the <i>{material.name}</i> material. Planar reflections will not be visible.",
                        $"Increase <i>Occlusion</i> on the material (<i>{material.name}</i>) currently assigned to the <i>{nameof(WaterRenderer)}</i> component.",
                        MessageType.Warning, material
                    );
                }

                if (material.HasProperty(WaterRenderer.ShaderIDs.s_OcclusionUnderwater) && target._Mode != WaterReflectionSide.Above && material.GetFloat(WaterRenderer.ShaderIDs.s_OcclusionUnderwater) == 0)
                {
                    messenger
                    (
                        $"<i>Occlusion (U)</i> is set to zero on the <i>{material.name}</i> material. Planar reflections will not be visible.",
                        $"Increase <i>Occlusion (U)</i> on the material (<i>{material.name}</i>) currently assigned to the <i>{nameof(WaterRenderer)}</i> component.",
                        MessageType.Warning, material
                    );
                }
            }

            if (!target._Sky)
            {
                messenger
                (
                    $"<i>Sky</i> on <i>Reflections</i> is not enabled. " +
                    "Any custom shaders which do not write alpha (eg some tree leaves) will not appear in the final reflections.",
                    "Enable <i>Sky</i>.",
                    MessageType.Info, target._Water,
                    (_, y) => y.boolValue = true,
                    $"{nameof(WaterRenderer._Reflections)}.{nameof(WaterReflections._Sky)}"

                );
            }

#if !UNITY_6000_0_OR_NEWER
#if d_UnityHDRP
            if (!target._RenderOnlySingleCamera && RenderPipelineHelper.IsHighDefinition)
            {
                messenger
                (
                    $"Please note that <i>Reflections > Render Only Single Camera</i> has no effect for Unity 2022.3 HDRP. " +
                    "It is forced to enabled, as HDRP cannot render to multiple cameras, as it requires recursive rendering.",
                    "Upgrade to Unity 6 if you need this feature.",
                    MessageType.Info, target._Water
                );
            }
#endif
#endif

            return isValid;
        }

        static bool Validate(UnderwaterRenderer target, ShowMessage messenger, WaterRenderer water)
        {
            var isValid = true;

            if (!target._Enabled)
            {
                return isValid;
            }

#if d_Crest_SimpleTransparency
            messenger
            (
                "Currently the underwater effect will force enable both the opaque and depth texture which negates most of the benefit of simple transparency.",
                "Disable underwater.",
                MessageType.Info, water,
                (x, y) => y.boolValue = false,
                nameof(WaterRenderer._Underwater) + "." + nameof(UnderwaterRenderer._Enabled)
            );
#endif

            if (target.Material != null)
            {
                var material = target.Material;

                if (material.shader.name.StartsWithNoAlloc("Crest/") && material.shader.name != "Crest/Underwater")
                {
                    messenger
                    (
                        $"The material {material.name} assigned to Underwater has the wrong shader ({material.shader.name}).",
                        "Use a material with the correct shader (Crest/Underwater).",
                        MessageType.Error, water
                    );

                    isValid = false;
                }
            }

            if (water != null && water.Surface.Material != null)
            {
                var material = water.Surface.Material;

                var cullModeName =
#if d_UnityURP
                    RenderPipelineHelper.IsUniversal ? "_Cull" :
#endif
#if d_UnityHDRP
                    RenderPipelineHelper.IsHighDefinition ? "_CullMode" :
#endif
                    "_BUILTIN_CullMode";

                if (material.HasFloat(cullModeName) && material.GetFloat(cullModeName) == (int)CullMode.Back)
                {
                    messenger
                    (
                        $"<i>Cull Mode</i> is set to <i>Back</i> on material <i>{material.name}</i>. " +
                        "The underside of the water surface will not be rendered.",
                        $"Set <i>Cull Mode</i> to <i>Off</i> (or <i>Front</i>) on <i>{material.name}</i>.",
                        MessageType.Warning, material,
                        (material, _) =>
                        {
                            FixSetMaterialIntProperty(material, "Cull Mode", cullModeName, (int)CullMode.Off);
                            if (RenderPipelineHelper.IsHighDefinition)
                            {
                                // HDRP material will not update without viewing it...
                                Selection.activeObject = material.targetObject;
                            }
                        }
                    );
                }

#if d_UnityHDRP
                if (RenderPipelineHelper.IsHighDefinition)
                {
                    if (material.GetFloat(cullModeName) == (int)CullMode.Off && !material.IsKeywordEnabled("_DOUBLESIDED_ON"))
                    {
                        messenger
                        (
                            $"<i>Double-Sided</i> is not enabled on material <i>{material.name}</i>. " +
                            "The underside of the water surface will not be rendered correctly.",
                            $"Enable <i>Double-Sided</i> on <i>{material.name}</i>.",
                            MessageType.Warning, material,
                            (material, _) =>
                            {
                                FixSetMaterialOptionEnabled(material, "_DOUBLESIDED_ON", "_DoubleSidedEnable", enabled: true);
                                // HDRP material will not update without viewing it...
                                Selection.activeObject = material.targetObject;
                            }
                        );
                    }
                }
#endif
            }

            return isValid;
        }

        static bool Validate(Meniscus target, ShowMessage messenger, WaterRenderer water)
        {
            var isValid = true;

            if (!target._Enabled)
            {
                return isValid;
            }

            if (target._Material == null)
            {
                messenger
                (
                    "The meniscus material is missing. The meniscus will not render.",
                    "Add the default material or your own.",
                    MessageType.Warning,
                    water,
                    (so, sp) =>
                    {
                        sp.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(Meniscus.k_MaterialPath);
                    },
                    $"{nameof(WaterRenderer._Meniscus)}.{nameof(Meniscus._Material)}"
                );
            }

            return isValid;
        }

        [Validator(typeof(WaterRenderer))]
        static bool Validate(WaterRenderer target, ShowMessage messenger)
        {
            var isValid = true;

            var water = target;

            isValid = isValid && Validate(target._Underwater, messenger, target);
            isValid = isValid && Validate(target._Reflections, messenger, target);
            isValid = isValid && Validate(target._Meniscus, messenger, target);
            isValid = isValid && ValidateNoRotation(target, target.transform, messenger);
            isValid = isValid && ValidateNoScale(target, target.transform, messenger);

#if CREST_OCEAN
            messenger
            (
                "The <i>CREST_OCEAN</i> scripting symbol is present from <i>Crest 4</i>. " +
                "This enables migration mode. Please read the documentation for the migration guide.",
                "Remove <i>CREST_OCEAN</i> from <i>Project Settings > Player > Other Settings > Scripting Define Symbols</i> once finished migrating.",
                MessageType.Info, target
            );
#endif

            if (target._Resources == null)
            {
                messenger
                (
                    "The Water Renderer is missing required internal data.",
                    "Populate required internal data.",
                    MessageType.Error, target,
                    (_, y) => y.objectReferenceValue = WaterResources.Instance,
                    nameof(target._Resources)
                );

                isValid = false;
            }

            if (target.Surface.Material == null)
            {
                messenger
                (
                    "No water material specified.",
                    $"Assign a valid water material to the Material property of the <i>{nameof(WaterRenderer)}</i> component.",
                    MessageType.Error, target
                );

                isValid = false;
            }
            else
            {
                isValid = ValidateWaterMaterial(target, messenger, water, target.Surface.Material) && isValid;

                if (RenderPipelineHelper.IsHighDefinition && target.Surface.Material.GetFloat("_RefractionModel") > 0)
                {
                    messenger
                    (
                        $"<i>Refraction Model</i> is not <i>None</i> for <i>{target.Surface.Material}</i>. " +
                        "This is set by default so it is available in the inspector, " +
                        "but it incurs an overhead and will produce a dark edge at the edge of the viewport (see <i>Screen Space Refraction > Screen Weight Distance</i>). " +
                        "Enabling the refraction model is only useful to allow volumetric clouds to render over the water surface when view from above. " +
                        "The refraction model has no effect on refractions.",
                        $"Set <i>Refraction Model</i> to <i>None</i>.",
                        MessageType.Info, target.Surface.Material
                    );
                }

                if (RenderPipelineHelper.IsHighDefinition && target.Surface.Material.HasFloat("_TransparentWritingMotionVec") && target.WriteMotionVectors != (target.Surface.Material.GetFloat("_TransparentWritingMotionVec") == 1f))
                {
                    messenger
                    (
                        $"<i>Water Renderer > Surface Renderer > Motion Vectors</i> and <i>Transparent Writes Motion Vectors</i> on <i>{target.Surface.Material}</i> do not match. ",
                        $"Either disable or enable both <i>Water Renderer > Surface Renderer > Motion Vectors</i> and <i>Transparent Writes Motion Vectors</i>",
                        MessageType.Info, target.Surface.Material
                    );
                }

                ValidateMaterialParent(target.Surface.VolumeMaterial, target.Surface.Material, messenger);
            }

            if (Helpers.FindObjectsByType<WaterRenderer>().Length > 1)
            {
                messenger
                (
                    $"Multiple <i>{nameof(WaterRenderer)}</i> components detected in open scenes, this is not typical - usually only one <i>{nameof(WaterRenderer)}</i> is expected to be present.",
                    $"Remove extra <i>{nameof(WaterRenderer)}</i> components.",
                    MessageType.Warning, target
                );

                isValid = false;
            }

            // Water Detail Parameters
            var baseMeshDensity = target.LodResolution * 0.25f / target._GeometryDownSampleFactor;

            if (baseMeshDensity < 8)
            {
                messenger
                (
                    "Base mesh density is lower than 8. There will be visible gaps in the water surface.",
                    "Increase the <i>LOD Data Resolution</i> or decrease the <i>Geometry Down Sample Factor</i>.",
                    MessageType.Error, target
                );
            }
            else if (baseMeshDensity < 16)
            {
                messenger
                (
                    "Base mesh density is lower than 16. There will be visible transitions when traversing the water surface. ",
                    "Increase the <i>LOD Data Resolution</i> or decrease the <i>Geometry Down Sample Factor</i>.",
                    MessageType.Warning, target
                );
            }

            // We need to find hidden probes too, but do not include assets.
            if (Resources.FindObjectsOfTypeAll<ReflectionProbe>().Count(x => !EditorUtility.IsPersistent(x)) > 0)
            {
                messenger
                (
                    "There are reflection probes in the scene. These can cause tiling to appear on the water surface if not set up correctly.",
                    "For reflections probes that affect the water, they will either need to cover the visible water tiles or water tiles need to ignore reflection probes (can done done with <i>Water Tile Prefab</i> field). " +
                    $"For all reflection probles that include the <i>{LayerMask.LayerToName(target.Surface.Layer)}</i> layer, make sure they are above the water surface as underwater reflections are not supported.",
                    MessageType.Info, target
                );
            }

            // Validate scene view effects options.
            if (SceneView.lastActiveSceneView != null && !Application.isPlaying)
            {
                var sceneView = SceneView.lastActiveSceneView;

                // Validate "Animated Materials".
                if (target != null && !target._ShowWaterProxyPlane && !sceneView.sceneViewState.alwaysRefresh)
                {
                    messenger
                    (
                        "<i>Animated Materials</i> is not enabled on the scene view. The water's framerate will appear low as updates are not real-time.",
                        "Enable <i>Animated Materials</i> on the scene view.",
                        MessageType.Info, target,
                        (_, _) =>
                        {
                            SceneView.lastActiveSceneView.sceneViewState.alwaysRefresh = true;
                            // Required after changing sceneViewState according to:
                            // https://docs.unity3d.com/ScriptReference/SceneView.SceneViewState.html
                            SceneView.RepaintAll();
                        }
                    );
                }

#if d_UnityPostProcessingBroken
                // Validate "Post-Processing".
                // Only check built-in renderer and Camera.main with enabled PostProcessLayer component.
                if (GraphicsSettings.currentRenderPipeline == null && Camera.main != null &&
                    Camera.main.TryGetComponent<UnityEngine.Rendering.PostProcessing.PostProcessLayer>(out var ppLayer)
                    && ppLayer.enabled && sceneView.sceneViewState.showImageEffects)
                {
                    messenger
                    (
                        "<i>Post Processing</i> is enabled on the scene view. " +
                        "There is a Unity bug where gizmos and grid lines will render over opaque objects. " +
                        "This has been resolved in <i>Post Processing</i> version 3.4.0.",
                        "Disable <i>Post Processing</i> on the scene view or upgrade to version 3.4.0.",
                        MessageType.Warning, target,
                        _ =>
                        {
                            sceneView.sceneViewState.showImageEffects = false;
                            // Required after changing sceneViewState according to:
                            // https://docs.unity3d.com/ScriptReference/SceneView.SceneViewState.html
                            SceneView.RepaintAll();
                        }
                    );
                }
#endif
            }

            // Validate simulation settings.
            foreach (var simulation in target.Simulations)
            {
                ExecuteValidators(simulation, messenger);
            }

            // For safety.
            if (target != null && target.Surface.Material != null)
            {
                foreach (var simulation in target.Simulations)
                {
                    ValidateSimulationAndMaterial(OptionalLod.Get(simulation.GetType()), messenger, water, target);
                }
            }

            if (target.PrimaryLight == null)
            {
                messenger
                (
                    "Crest needs to know which light to use as the sun light.",
                    "Please add a Directional Light to the scene.",
                    MessageType.Warning, target
                );
            }

            if (target.Viewer == null && !target.IsRunningWithoutGraphics && !target.MultipleViewpoints)
            {
                messenger
                (
                    "Crest needs to know which camera to use as the main camera.",
                    $"Either tag a camera as <i>Main Camera</i> or assign the camera to the {nameof(WaterRenderer)}.",
                    MessageType.Error, target
                );

                isValid = false;
            }

#if d_UnityHDRP
            if (RenderPipelineHelper.IsHighDefinition)
            {
                var material = target.Surface.Material;
                var camera = target._Camera != null ? target._Camera : Camera.main;
                var hdCamera = camera != null ? HDCamera.GetOrCreate(camera) : null;
                var hdAsset = GraphicsSettings.currentRenderPipeline as HDRenderPipelineAsset;
                var mvs = hdAsset.currentPlatformRenderPipelineSettings.supportMotionVectors;

                // Only check the RP asset for now.
                if (mvs != water.WriteMotionVectors)
                {
                    messenger
                    (
                        $"Motion Vectors are{(mvs ? "" : " not")} enabled in the HD render pipeline asset, but <i>Water Renderer > Surface Renderer > Motion Vectors</i> is{(mvs ? " not" : "")}. " +
                        "Both need to be enabled for motion vectors to work, or both should be disabled to save resources. " +
                        "This can safely be ignored if the setup is intentional.",
                        "Enable or disable both.",
                        MessageType.Info, target
                    );
                }

                if (!hdAsset.currentPlatformRenderPipelineSettings.supportCustomPass)
                {
                    messenger
                    (
                        "Custom passes are disabled. Underwater and other features require them to work.",
                        "Enabled them on the global asset.",
                        MessageType.Error, hdCamera.camera
                    );
                }

                if (target.RenderBeforeTransparency && WaterRenderer.s_CameraMSAA)
                {
                    messenger
                    (
                        $"The water injection point is before transparency and MSAA is enabled for a camera. This combination is not currently supported for HDRP.",
                        "Disable MSAA or change the water injection point.",
                        MessageType.Error, target
                    );
                }

                // Seems that logging is too early for these. And edit mode has false positives.
                if (Application.isPlaying && messenger == ValidatedHelper.HelpBox)
                {
                    if (hdCamera?.frameSettings.IsEnabled(FrameSettingsField.CustomPass) == false)
                    {
                        messenger
                        (
                            $"Custom passes are disabled for the primary camera ({camera}). Underwater and other features require them to work.",
                            "Enable them in the camera frame settings on the camera or the default frame settings in the global settings.",
                            MessageType.Error, hdCamera.camera
                        );
                    }

                    if (hdCamera?.frameSettings.IsEnabled(FrameSettingsField.Refraction) == false && material != null && SurfaceRenderer.IsTransparent(material))
                    {
                        messenger
                        (
                            "Refraction is disabled. Transparency requires it to work.",
                            "Enable it in the camera frame settings on the camera, or the default frame settings in the global settings.",
                            MessageType.Error, hdCamera.camera
                        );
                    }
                }
            }
#endif // d_UnityHDRP

#if d_UnityURP
            if (RenderPipelineHelper.IsUniversal && target.Viewer != null)
            {
                var data = target.Viewer.GetUniversalAdditionalCameraData();

                // Type is internal.
                if (data != null && data.scriptableRenderer.GetType().Name == "Renderer2D")
                {
                    messenger
                    (
                        "Crest does not support 2D rendering.",
                        "Please choose a 3D template.",
                        MessageType.Error, target
                    );

                    isValid = false;
                }
            }
#endif // d_UnityURP

            if (!RenderPipelineHelper.IsHighDefinition && target.Surface.Material != null)
            {
                if (!target.Surface.AllowRenderQueueSorting && !System.Enum.IsDefined(typeof(RenderQueue), target.Surface.Material.renderQueue))
                {
                    var field = nameof(SurfaceRenderer.AllowRenderQueueSorting).Pretty().Italic();
                    messenger
                    (
                        $"The render queue has a sub-sort applied, but {field} is not enabled. Sub-sorting will not work.",
                        $"Enable {field}.",
                        MessageType.Warning, target
                    );
                }
            }

            return isValid;
        }

        [Validator(typeof(WaterBody))]
        static bool Validate(WaterBody target, ShowMessage messenger)
        {
            var isValid = true;

            var water = Water;

            if (Helpers.FindObjectsByType<WaterRenderer>(FindObjectsInactive.Include).Length == 0)
            {
                messenger
                (
                    $"Water body <i>{target.gameObject.name}</i> requires a <i>{nameof(WaterRenderer)}</i> component to be present.",
                    $"Create a separate GameObject and add an <i>{nameof(WaterRenderer)}</i> component to it.",
                    MessageType.Error, target
                );

                isValid = false;
            }

            if (Mathf.Abs(target.transform.lossyScale.x) < 2f && Mathf.Abs(target.transform.lossyScale.z) < 2f)
            {
                messenger
                (
                    $"Water body {target.gameObject.name} has a very small size (the size is set by the X & Z scale of its transform), and will be a very small body of water.",
                    "Increase X & Z scale on water body transform (or parents).",
                    MessageType.Error, target
                );

                isValid = false;
            }

            if (target._Material != null)
            {
                isValid = ValidateWaterMaterial(target, messenger, Water, target._Material) && isValid;
                ValidateMaterialParent(target._BelowSurfaceMaterial, target._Material, messenger);
            }

            isValid = isValid && ValidateNoRotation(target, target.transform, messenger);

            if (target.Clipped && water != null)
            {
                // Validate main material, then overriden material.
                ValidateLod(OptionalLod.Get(typeof(ClipLod)), messenger, water, material: target._Material, context: target);

                if (water.ClipLod.DefaultClippingState == DefaultClippingState.NothingClipped)
                {
                    messenger
                    (
                        $"The {nameof(ClipLod.DefaultClippingState)} on the {nameof(WaterRenderer)} is set to {DefaultClippingState.NothingClipped}. " +
                        $"The {nameof(WaterBody.Clipped)} option will have no effect.",
                        $"Disable {nameof(WaterBody.Clipped)} or set {nameof(ClipLod.DefaultClippingState)} to {DefaultClippingState.NothingClipped}.",
                        MessageType.Warning,
                        water,
                        caller: target
                    );
                }
            }

            return isValid;
        }


        /// <summary>
        /// Does validation for a feature on the water component and on the material
        /// </summary>
        internal static bool ValidateLod(OptionalLod target, ShowMessage messenger, WaterRenderer water, string dependent = null, Material material = null, Object context = null)
        {
            var isValid = true;

            if (target == null || water == null)
            {
                return isValid;
            }

            isValid &= ValidateProjectSettings(target, water, messenger, context);

            var simulation = target.GetLod(water);

            var dependentClause = ".";

            if (dependent != null)
            {
                dependentClause = $", as {dependent} needs it.";
            }

            if (!simulation._Enabled && material == null)
            {
                messenger
                (
                    $"<i>{target.PropertyLabel}</i> must be enabled on the <i>{nameof(WaterRenderer)}</i> component{dependentClause}",
                    $"Enable <i>Simulations > {target.PropertyLabel} > Enabled</i> on the <i>{nameof(WaterRenderer)}</i> component.",
                    MessageType.Error, water,
                    (_, y) =>
                    {
                        y.boolValue = true;
                        if (Water.Active)
                        {
                            // ApplyModifiedProperties is called outside of this method but need it for next
                            // call. Then restore so ApplyModifiedProperties check works to add undo entry.
                            simulation._Enabled = true;
                            simulation.Initialize();
                            simulation._Enabled = false;
                        }
                    },
                    $"{target.PropertyName}.{nameof(Lod._Enabled)}",
                    context
                );

                isValid = false;
            }

            if (material == null)
            {
                material = water.Surface.Material;
            }

            if (target.HasMaterialToggle && material != null)
            {
                if (material.HasProperty(target.MaterialProperty) && material.GetFloat(target.MaterialProperty) != 1f)
                {
                    ShowMaterialValidationMessage(target, material, messenger, context);
                    isValid = false;
                }
            }

            if (target.Dependency != null)
            {
                ValidateLod(OptionalLod.Get(target.Dependency), messenger, water, dependent, context: context);
            }

            return isValid;
        }

        static bool ValidateSignedDistanceFieldsLod(ShowMessage messenger, WaterRenderer water, string feature, Object context)
        {
            var isValid = true;

            if (water != null && !water.DepthLod.EnableSignedDistanceFields)
            {
                messenger
                (
                    $"{feature} requires <i>Signed Distance Fields</i> to be enabled on the Water Depth Simulation.",
                    "Enable <i>Signed Distance Fields</i>",
                    MessageType.Error, water,
                    (_, y) => y.boolValue = true,
                    $"{nameof(WaterRenderer._DepthLod)}.{nameof(DepthLod._EnableSignedDistanceFields)}",
                    caller: context
                );

                isValid = false;
            }

            return isValid;
        }

        static void ShowMaterialValidationMessage(OptionalLod target, Material material, ShowMessage messenger, Object caller)
        {
            messenger
            (
                $"{target.PropertyLabel} is not enabled (<i>{target.MaterialPropertyPath}</i>) on the water material and will not be visible.",
                $"Enable <i>{target.PropertyLabel}</i> on the material currently assigned to the <i>{nameof(WaterRenderer)}</i> component.",
                MessageType.Error, material,
                (material, _) => FixSetMaterialOptionEnabled(material, target.MaterialKeyword, target.MaterialProperty, true)
            );
        }

        static bool ValidateProjectSettings(OptionalLod target, WaterRenderer water, ShowMessage messenger, Object caller)
        {
            var isValid = true;

            var lod = target.GetLod(water);

            if (lod._Enabled && !target.GetProjectSettingToggle())
            {
                var platform = ScriptingSymbols.CurrentNamedBuildTarget;

                messenger
                (
                    $"<i>{target.PropertyLabel}</i> must be enabled for this platform in the project settings.",
                    $"Enable <i>Project Settings > Crest > Features > Default/{platform.TargetName} > {target.PropertyLabel}</i>. " +
                    $"It will be in either Default or {platform.TargetName}",
                    MessageType.Error, ProjectSettings.Instance,
                    caller: caller
                );

                isValid = false;
            }

            return isValid;
        }

        static bool ValidateSimulationAndMaterial(OptionalLod target, ShowMessage messenger, WaterRenderer water, Object caller)
        {
            if (target == null)
            {
                return true;
            }

            if (!ValidateProjectSettings(target, water, messenger, caller))
            {
                return false;
            }

            if (!target.HasMaterialToggle)
            {
                return true;
            }

            // These checks are not necessary for our material but there may be custom materials.
            if (!water.Surface.Material.HasProperty(target.MaterialProperty))
            {
                return true;
            }

            var feature = target.GetLod(water);

            // There is only a problem if there is a mismatch.
            if (feature._Enabled == (water.Surface.Material.GetFloat(target.MaterialProperty) == 1f))
            {
                return true;
            }

            if (feature._Enabled)
            {
                ShowMaterialValidationMessage(target, water.Surface.Material, messenger, caller);
            }
            else if (messenger != DebugLog)
            {
                messenger
                (
                    $"The <i>{target.PropertyLabel}</i> feature is disabled on the <i>{nameof(WaterRenderer)}</i> but is enabled on the water material.",
                    $"If this is not intentional, either enable <i>{target.PropertyLabel}</i> on the <i>{nameof(WaterRenderer)}</i> to turn it on, or disable <i>{target.MaterialPropertyPath}</i> on the water material to save performance.",
                    MessageType.Warning, water
                );
            }

            return false;
        }

        [Validator(typeof(ShapeWaves))]
        static bool Validate(ShapeWaves target, ShowMessage messenger)
        {
            var isValid = true;

            var water = Object.FindAnyObjectByType<WaterRenderer>(FindObjectsInactive.Include);

            var windSpeedSource = target.GetWindSpeedSource();

            if (windSpeedSource != ShapeWaves.WindSpeedSource.None)
            {
                messenger
                (
                    WaveSpectrumEditor.GetWindSpeedText(target, windSpeedSource),
                    WaveSpectrumEditor.GetWindSpeedFixText(windSpeedSource),
                    MessageType.Info,
                    caller: target
                );
            }

            if (target.Blend == LodInputBlend.AlphaClip && target.Mode is not (LodInputMode.Texture or LodInputMode.Paint))
            {
                messenger
                (
                    $"Only {LodInputMode.Texture} mode supports {nameof(LodInputBlend.AlphaClip)}.",
                    $"Change Blend to {nameof(LodInputBlend.Alpha)}.",
                    MessageType.Error, target,
                    (_, y) => y.enumValueIndex = (int)LodInputBlend.Alpha,
                    nameof(ShapeWaves._Blend)
                );
            }

            if (Water != null)
            {
                isValid &= ValidateLod(OptionalLod.Get(typeof(AnimatedWavesLod)), messenger, Water, context: target);
            }

            return isValid;
        }

        [Validator(typeof(ShapeFFT))]
        static bool Validate(ShapeFFT target, ShowMessage messenger)
        {
            if (target.Spectrum != null && target.Spectrum._ShowAdvancedControls && !target.ApplyAdvancedSpectrumControls)
            {
                messenger
                (
                    $"{nameof(ShapeFFT.ApplyAdvancedSpectrumControls).Pretty().Italic()} needs to be enabled for anything exposed by {nameof(target.Spectrum._ShowAdvancedControls).Pretty().Italic()} to apply.",
                    string.Empty,
                    MessageType.Info,
                    caller: target
                );
            }

            return true;
        }

        [Validator(typeof(SphereWaterInteraction))]
        static bool Validate(SphereWaterInteraction target, ShowMessage messenger)
        {
            var isValid = true;

            // Validate require water feature.
            if (Water != null)
            {
                isValid &= ValidateLod(OptionalLod.Get(typeof(DynamicWavesLod)), messenger, Water, context: target);
            }

            return isValid;
        }

        [Validator(typeof(WatertightHull))]
        static bool Validate(WatertightHull target, ShowMessage messenger)
        {
            var isValid = true;

            // Validate require water feature.
            if (Water != null)
            {
                isValid &= !target.UsesClip || ValidateLod(OptionalLod.Get(typeof(ClipLod)), messenger, Water, context: target);
                isValid &= !target.UsesDisplacement || ValidateLod(OptionalLod.Get(typeof(AnimatedWavesLod)), messenger, Water, context: target);
                isValid &= !target.UsesDisplacement || ValidateCollisionLayer(CollisionLayer.AfterDynamicWaves, target, messenger, "mode", target.Mode, required: true);
            }

            return isValid;
        }

        internal static void FixSetQuerySourceToCompute(SerializedObject _, SerializedProperty property)
        {
            if (Water != null)
            {
                property.enumValueIndex = (int)LodQuerySource.GPU;
            }
        }

        [Validator(typeof(FloatingObject))]
        static bool Validate(FloatingObject target, ShowMessage messenger)
        {
            var isValid = true;

            isValid &= ValidateComponent(target, messenger, target.RigidBody);

            if (Water == null)
            {
                return isValid;
            }

            isValid &= ValidateCollisionLayer(target.Layer, target, messenger, "layer", target.Layer, required: false);
            isValid &= ValidateQuerySource(target, messenger, Water.AnimatedWavesLod, k_NoneQueryProviderCollisionFloatingObjects);
            isValid &= ValidateQuerySource(target, messenger, Water.FlowLod, k_NoneQueryProviderFlowFloatingObjects);

            return isValid;
        }

        [Validator(typeof(CollisionAreaVisualizer))]
        static bool Validate(CollisionAreaVisualizer target, ShowMessage messenger)
        {
            var isValid = true;

            if (Water == null)
            {
                return isValid;
            }

            isValid &= ValidateCollisionLayer(target._Layer, target, messenger, "layer", target._Layer, required: false);
            isValid &= ValidateQuerySource(target, messenger, Water.AnimatedWavesLod, k_NoneQueryProviderCollisionFloatingObjects);
            isValid &= ValidateQuerySource(target, messenger, Water.FlowLod, k_NoneQueryProviderFlowFloatingObjects);

            return isValid;
        }

        [Validator(typeof(Controller))]
        static bool Validate(Controller target, ShowMessage messenger)
        {
            var isValid = true;

            isValid &= ValidateComponent(target, messenger, target.Control);
            isValid &= ValidateComponent(target, messenger, target.FloatingObject);

            isValid &= isValid && target.TryGetComponent(out FloatingObject fo) && fo.RigidBody != null;

            return isValid;
        }

        [Validator(typeof(LodInput))]
        static bool Validate(LodInput target, ShowMessage messenger)
        {
            var isValid = true;

            var isDataInput = target.Mode is LodInputMode.Spline or LodInputMode.Texture or LodInputMode.Renderer or LodInputMode.Paint;

            if (isDataInput)
            {
                // Find the type associated with the input type and mode.
                var self = target.GetType();
                var types = TypeCache.GetTypesWithAttribute<ForLodInput>();
                System.Type type = null;
                foreach (var t in types)
                {
                    var attributes = t.GetCustomAttributes<ForLodInput>();
                    foreach (var attribute in attributes)
                    {
                        if (!attribute._Type.IsAssignableFrom(self)) continue;
                        if (attribute._Mode != target.Mode) continue;
                        type = t;
                        goto exit;
                    }
                }

            exit:
                isValid = type != null;

#if !d_CrestPaint
                if (!isValid && target.Mode == LodInputMode.Paint)
                {
                    messenger
                    (
                        "Missing the <i>Crest: Paint</i> package.",
                        $"Install the missing package or select a valid <i>Input Mode</i> such as {target.DefaultMode} to use this input.",
                        MessageType.Error,
                        target,
                        (_, y) => y.enumValueIndex = (int)target.DefaultMode,
                        nameof(target.Mode)
                    );

                    return isValid;
                }
#endif

#if !d_CrestSpline
                if (!isValid && target.Mode == LodInputMode.Spline)
                {
                    messenger
                    (
                        "Missing the <i>Crest: Spline</i> package.",
                        $"Install the missing package or select a valid <i>Input Mode</i> such as {target.DefaultMode} to use this input.",
                        MessageType.Error,
                        target,
                        (_, y) => y.enumValueIndex = (int)target.DefaultMode,
                        nameof(target.Mode)
                    );

                    return isValid;
                }
#endif

                if (!isValid)
                {
                    messenger
                    (
                        "Invalid or unset <i>Input Mode</i> setting.",
                        $"Select a valid <i>Input Mode</i> such as {target.DefaultMode} to use this input.",
                        MessageType.Error,
                        target,
                        (_, y) => y.enumValueIndex = (int)target.DefaultMode,
                        nameof(target._Mode)
                    );

                    return isValid;
                }

                isValid = target.Data != null;

                if (!isValid)
                {
                    var isPrefabInstance = PrefabUtility.IsPartOfPrefabInstance(target);
                    messenger
                    (
                        "Missing internal data or data type was renamed.",
                        isPrefabInstance ? "Repair the component in the prefab." : "Repair component.",
                        MessageType.Error,
                        target,
                        isPrefabInstance ? null : (_, _) =>
                        {
                            Undo.RecordObject(target, "Repair");
                            target.ChangeMode(target.Mode);
                            EditorUtility.SetDirty(target);
                        }
                    );

                    return isValid;
                }

                isValid = target.Data.GetType() == type;

                // This might happen if scripting is used.
                if (!isValid)
                {
                    messenger
                    (
                        $"Instance set to <i>{nameof(LodInput.Data)}</i> as incorrect type.",
                        "Set the correct instance type.",
                        MessageType.Error,
                        target,
                        (_, _) =>
                        {
                            Undo.RecordObject(target, "Repair");
                            target.ChangeMode(target.Mode);
                            EditorUtility.SetDirty(target);
                        }
                    );

                    return isValid;
                }
            }

            isValid &= ValidateFilteredChoice((int)target.Blend, "_Blend", target, messenger);

            // Validate that any water feature required for this input is enabled, if any
            if (Water != null)
            {
                isValid &= ValidateLod(OptionalLod.Get(target.GetType()), messenger, Water, context: target);
            }

            return isValid;
        }

        [Validator(typeof(LevelLodInput))]
        static bool ValidateLevelLodInput(LevelLodInput target, ShowMessage messenger)
        {
            var isValid = true;

            if (target.Mode is LodInputMode.Geometry or LodInputMode.Spline)
            {
                if (target.Blend is LodInputBlend.Minimum or LodInputBlend.Maximum && target.Weight < 1f)
                {
                    messenger
                    (
                        "Weight with minimum or maximum blend modes do not always behave correctly. " +
                        "Any weight less than one will move the value in the simulation towards zero, rather than towards the existing value in the simulation. " +
                        "For example, this input with a zero weight, and with a blend mode set to maximum, will replace any negative water level instead of not doing anything.",
                        "", // Nothing to suggest yet, as in cases this is still valid.
                        MessageType.Warning,
                        target
                    );
                }
            }

            var water = Water;

            if (water == null)
            {
                return isValid;
            }

            ValidateDisplacementPrecisionIssues(water, target, messenger);
            ValidateWaterLevelPrecisionIssues(water, target, messenger);

            return isValid;
        }

        [Validator(typeof(DepthProbe))]
        static bool Validate(DepthProbe target, ShowMessage messenger)
        {
            var isValid = true;

            messenger
            (
                "If you see an error <i>RenderTexture color format cannot be set to a depth/stencil format</i> or <i>RenderTexture.Create failed</i>, this is likely a bug with Unity (grab pass) or third-party, as they may be registered to execute a custom pass to the DepthProbe camera.", "", MessageType.Info, target
            );

            if (target.Outdated && (messenger != DebugLog || WaterRendererEditor.ManualValidation))
            {
                messenger
                (
                    "<i>Depth Probe</i> is outdated.",
                    "Click <i>Populate</i> or re-bake the probe to bring the probe up-to-date with component changes.",
                    MessageType.Warning, target,
                    (_, _) => target.Populate()
                );
            }

            if (target.Type == DepthProbeMode.Baked)
            {
                messenger
                (
                    "To change any read-only settings, switch back to real-time, adjust settings, and re-bake.",
                    "",
                    MessageType.Info, target
                );

                if (target.SavedTexture == null)
                {
                    messenger
                    (
                        "Depth probe type is <i>Baked</i> but no saved probe data is provided.",
                        "Assign a saved probe asset.",
                        MessageType.Error, target
                    );

                    isValid = false;
                }
            }
            else
            {
                if (target._Layers == 0)
                {
                    messenger
                    (
                        "No layers specified for rendering into depth probe.",
                        "Specify one or many layers using the Layers field.",
                        MessageType.Error, target
                    );

                    isValid = false;
                }
#if d_Unity_Terrain
                else
                {
                    Terrain.GetActiveTerrains(s_Terrains);
                    foreach (var terrain in s_Terrains)
                    {
                        if (Helpers.MaskIncludesLayer(target.Layers, terrain.gameObject.layer))
                        {
                            continue;
                        }

                        messenger
                        (
                           $"There are terrains on a layer that is not in {nameof(DepthProbe)}.{nameof(DepthProbe.Layers)}.",
                            "This is typically mistake leading to no data (ie no shorelines). Please ignore if intentional.",
                            MessageType.Info, target
                        );

                        break;
                    }
                }
#endif // d_Unity_Terrain

                if (target._Resolution < 4)
                {
                    messenger
                    (
                        $"Probe resolution {target._Resolution} is very low, which may not be intentional.",
                        "Increase the probe resolution.",
                        MessageType.Error, target
                    );

                    isValid = false;
                }

                if (!Mathf.Approximately(target.Scale.x, target.Scale.y))
                {
                    messenger
                    (
                        $"The <i>{nameof(DepthProbe)}</i> in real-time only supports a uniform scale for X and Z. " +
                        "These values currently do not match. " +
                        $"Its current scale in the hierarchy is: X = {target.Scale.x} Z = {target.Scale.y}.",
                        "Ensure the X & Z scale values are equal on this object and all parents in the hierarchy.",
                        MessageType.Error, target
                    );

                    isValid = false;
                }

                // We used to test if nothing is present that would render into the probe, but these could probably come from other scenes.
            }

            if (!target.Managed && target.transform.lossyScale.XZ().magnitude < 5f)
            {
                messenger
                (
                    $"<i>{nameof(DepthProbe)}</i> transform scale is small and will capture a small area of the world. The scale sets the size of the area that will be probed, and this probe is set to render a very small area.",
                    "Increase the X & Z scale to increase the size of the probe.",
                    MessageType.Warning, target
                );

                isValid = false;
            }

            if (!target.Managed && target.transform.lossyScale.y <= 0f)
            {
                messenger
                (
                    $"<i>{nameof(DepthProbe)}</i> scale is zero or negative. Y should be set to 1.0, but can be other values providing it is greater than zero. Its current scale in the hierarchy is {target.transform.lossyScale.y}.",
                    "Set the Y scale to 1.0.",
                    MessageType.Error, target
                );

                isValid = false;
            }

#if d_UnityURP
#if !UNITY_6000_0_OR_NEWER
#if UNITY_2022_3_OR_NEWER
            if (int.Parse(Application.unityVersion.Substring(7, 2)) < 23)
            {
                // Asset based validation.
                foreach (var asset in GraphicsSettings.allConfiguredRenderPipelines)
                {
                    if (asset is UniversalRenderPipelineAsset urpAsset)
                    {
                        var urpRenderers = Helpers.UniversalRendererData(urpAsset);

                        foreach (var renderer in urpRenderers)
                        {
                            var urpRenderer = (UniversalRendererData)renderer;

                            if (urpRenderer.depthPrimingMode != DepthPrimingMode.Disabled)
                            {
                                messenger
                                (
                                    $"<i>{nameof(DepthPrimingMode)}</i> is not set to <i>{nameof(DepthPrimingMode.Disabled)}</i>. " +
                                    $"This can cause the <i>{nameof(DepthProbe)}</i> not to work. " +
                                    $"Unity fixed this in 2022.3.23f1.",
                                    $"If you are experiencing problems, disable depth priming or upgrade Unity.",
                                    MessageType.Info, urpRenderer,
                                    caller: target
                                );
                            }

                            foreach (var feature in renderer.rendererFeatures)
                            {
                                if (feature.GetType().Name == "ScreenSpaceAmbientOcclusion" && feature.isActive)
                                {
                                    messenger
                                    (
                                        $"<i>ScreenSpaceAmbientOcclusion</i> is is active. " +
                                        $"This can cause the <i>{nameof(DepthProbe)}</i> not to work. " +
                                        $"Unity fixed this in 2022.3.23f1.",
                                        $"If you are experiencing problems, disable SSAO or upgrade Unity.",
                                        MessageType.Info, urpRenderer,
                                        caller: target
                                    );
                                }
                            }
                        }
                    }
                }
            }
#endif
#endif
#endif

            // Check that there are no renderers in descendants.
            var renderers = target.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                foreach (var renderer in renderers)
                {
                    messenger
                    (
                        "It is not expected that a depth probe object has a Renderer component in its hierarchy." +
                        "The probe is typically attached to an empty GameObject. Please refer to the example content.",
                        "Remove the Renderer component from this object or its children.",
                        MessageType.Warning, renderer,
                        caller: target
                    );

                    // Reporting only one renderer at a time will be enough to avoid overwhelming user and UI.
                    break;
                }

                isValid = false;
            }

            var water = Water;

            // Validate require water feature.
            if (water != null)
            {
                isValid = isValid && ValidateLod(OptionalLod.Get(typeof(DepthLod)), messenger, water, context: target);

                if (!water._DepthLod._EnableSignedDistanceFields && target._GenerateSignedDistanceField)
                {
                    isValid = isValid && ValidateSignedDistanceFieldsLod(messenger, water, "Generate Signed Distance Field", target);
                }

                if (water.DepthLod.IncludeTerrainHeight && Object.FindAnyObjectByType<Terrain>(FindObjectsInactive.Include) != null)
                {
                    messenger
                    (
                        "The Water Depth data is configured to automatically include terrain height via <i>Include Terrain Height</i>. " +
                        "Using a DepthProbe is still valid to capture non-terrain details like rocks. " +
                        "But typically, if you are using a DepthProbe, it is best to capture the terrain too, as it is more accurate. " +
                        "One reason to use a DepthProbe together with the auto capture is for better real-time/on-demand depth capture performance.",
                        string.Empty,
                        MessageType.Info, water,
                        caller: target
                    );
                }
            }


            return isValid;
        }

        [Validator(typeof(QueryEvents))]
        static bool Validate(QueryEvents target, ShowMessage messenger)
        {
            var isValid = true;
            var water = Water;

            if (!target._DistanceFromEdge.IsEmpty())
            {
                isValid = isValid && ValidateLod(OptionalLod.Get(typeof(DepthLod)), messenger, water, context: target);
                isValid = isValid && ValidateSignedDistanceFieldsLod(messenger, water, "Distance From Edge", target);
            }

            if (!target._DistanceFromSurface.IsEmpty())
            {
                isValid &= ValidateCollisionLayer(target._Layer, target, messenger, "layer", target._Layer, required: false);
                isValid &= ValidateQuerySource(target, messenger, Water.AnimatedWavesLod, k_NoneQueryProviderCollisionFloatingObjects);
                isValid &= ValidateQuerySource(target, messenger, Water.FlowLod, k_NoneQueryProviderFlowFloatingObjects);
            }

            return isValid;
        }

        [Validator(typeof(FoamLodSettings))]
        static bool Validate(FoamLodSettings target, ShowMessage messenger)
        {
            var isValid = true;

            if (Water == null)
            {
                return isValid;
            }

            if (target.FilterWaves > Water.LodLevels - 2)
            {
                messenger
                (
                    "<i>Filter Waves</i> is higher than the recommended maximum (LOD count - 2). There will be no whitecaps.",
                    "Reduce <i>Filter Waves</i>.",
                    MessageType.Warning, target
                );
            }

            return isValid;
        }

        [Validator(typeof(Lod))]
        static bool Validate(Lod target, ShowMessage messenger)
        {
            var isValid = true;

            if (!target._Enabled)
            {
                return isValid;
            }

            var optional = OptionalLod.Get(target.GetType());

            if (Water != null && optional.Dependency != null)
            {
                isValid &= ValidateLod(OptionalLod.Get(optional.Dependency), messenger, Water, target.Name);
            }

            return isValid;
        }

        [Validator(typeof(AnimatedWavesLod))]
        static bool Validate(AnimatedWavesLod target, ShowMessage messenger)
        {
            var isValid = true;

#if !d_CrestCPUQueries
            if (target.QuerySource == LodQuerySource.CPU)
            {
                messenger
                (
                    "Collision Source is set to CPU but the <i>CPU Queries</i> package is not installed.",
                    "Install the <i>CPU Queries</i> package or switch to GPU queries.",
                    MessageType.Warning, target.Water,
                    FixSetQuerySourceToCompute
                );
            }
#endif

            isValid &= ValidateQuerySource(target.Water, messenger, target, k_NoneQueryProviderCollisionFloatingObjects);

            return isValid;
        }

        [Validator(typeof(LevelLod))]
        static bool Validate(LevelLod target, ShowMessage messenger)
        {
            var water = Water;

            if (water == null)
            {
                return true;
            }

            ValidateDisplacementPrecisionIssues(water, water, messenger);
            ValidateWaterLevelPrecisionIssues(water, water, messenger);

            return true;
        }

        [Validator(typeof(FlowLod))]
        static bool Validate(FlowLod target, ShowMessage messenger)
        {
            return ValidateQuerySource(target.Water, messenger, target, k_NoneQueryProviderFlowFloatingObjects);
        }

        [Validator(typeof(ScatteringLod))]
        static bool Validate(ScatteringLod target, ShowMessage messenger)
        {
            var isValid = true;

            var water = Water;

            if (!target.Enabled)
            {
                return isValid;
            }

            if (target._ShorelineColorSource != ShorelineVolumeColorSource.None)
            {
                if (!water._DepthLod._Enabled)
                {
                    ShowDependentPropertyMessage
                    (
                        "Shoreline Scattering",
                        "Water Depth",
                        $"{nameof(WaterRenderer._DepthLod)}.{nameof(Lod._Enabled)}",
                        messenger,
                        water
                    );
                }
                else if (target._ShorelineColorSource == ShorelineVolumeColorSource.Distance && !water._DepthLod._EnableSignedDistanceFields)
                {
                    ShowDependentPropertyMessage
                    (
                        "Shoreline Distance Scattering",
                        "Signed Distance Fields",
                        $"{nameof(WaterRenderer._DepthLod)}.{nameof(WaterRenderer._DepthLod._EnableSignedDistanceFields)}",
                        messenger,
                        water
                    );
                }
            }

            return isValid;
        }

        [Validator(typeof(CutsceneTimeProvider))]
        static bool Validate(CutsceneTimeProvider target, ShowMessage messenger)
        {
            var isValid = true;

            var water = Water;
            if (water == null)
            {
                messenger
                (
                    $"No water present. {nameof(CutsceneTimeProvider)} will have no effect.",
                    "", MessageType.Warning,
                    caller: target
                );

                isValid = false;
            }

#if d_ModuleUnityDirector
            if (target._PlayableDirector == null)
            {
                messenger
                (
                    $"No {nameof(UnityEngine.Playables.PlayableDirector)} component assigned. {nameof(CutsceneTimeProvider)} will have no effect.",
                    $"Add a {nameof(UnityEngine.Playables.PlayableDirector)}",
                    MessageType.Error,
                    caller: target
                );

                isValid = false;
            }
#else
            messenger
            (
                $"This component requires the com.unity.modules.director built-in module to function.",
                $"Enable the com.unity.modules.director built-in module.",
                MessageType.Error,
                caller: target
            );

            isValid = false;
#endif

            return isValid;
        }

        static bool ValidateWaterMaterial(Object target, ShowMessage messenger, WaterRenderer water, Material material)
        {
            var isValid = true;

            // TODO: We could be even more granular with what needs this property.
            if (water._Underwater._Enabled && !material.HasVector(WaterRenderer.ShaderIDs.s_Absorption))
            {
                messenger
                (
                    $"Material <i>{material.name}</i> does not have <i>Crest Absorption</i> property. " +
                    "Several features require absorption like underwater culling and lighting.",
                    $"Assign a valid water material.",
                    MessageType.Warning, target
                );
            }

            if (water.RenderBeforeTransparency && !SurfaceRenderer.IsTransparent(material))
            {
                messenger
                (
                    $"The {nameof(WaterRenderer.InjectionPoint).Pretty()} on the {nameof(WaterRenderer).Pretty()} is set to {nameof(WaterInjectionPoint.BeforeTransparent).Pretty()}, but it requires the water surface material to be set to transparent. " +
                    "The water will not render.",
                    $"Set to transparent or set {nameof(WaterRenderer.InjectionPoint).Pretty()} to {nameof(WaterInjectionPoint.Default).Pretty()}.",
                    MessageType.Warning,
                    material,
                    caller: water
                );
            }

            return isValid;
        }

        static bool ValidateMaterialParent(Material child, Material parent, ShowMessage messenger)
        {
            var isValid = true;

            if (child != null && child.parent != parent)
            {
                messenger
                (
                    $"The <i>{child}</i> does not have <i>{parent}</i> as a parent. " +
                    "Linking these materials is typically how these are used to avoid trying to keep properties in sync.",
                    $"Parent <i>{parent}</i> to <i>{child}</i>.",
                    MessageType.Info, parent,
                    (_, _) =>
                    {
                        Undo.RecordObject(child, "Assign parent");
                        child.parent = parent;
                    }
                );
            }

            return isValid;
        }

        static bool ValidateComponent<T, C>(T target, ShowMessage messenger, C @object)
            where T : Component
            where C : Component
        {
            var isValid = true;

            if (@object == null && !target.gameObject.TryGetComponent<C>(out _))
            {
                messenger
                (
                    $"{typeof(T).Name} requires a {typeof(C).Name} to be set or present on same object.",
                    $"Set the {typeof(C).Name} property or add a {typeof(C).Name}.",
                    MessageType.Error,
                    caller: target
                );

                isValid = false;
            }

            return isValid;
        }

        static bool ValidateCollisionLayer(CollisionLayer layer, Object target, ShowMessage messenger, string label, object value, bool required)
        {
            if (Water == null)
            {
                return true;
            }

            var layers = Water.AnimatedWavesLod._CollisionLayers;

            if (layer == CollisionLayer.Everything)
            {
                return true;
            }

            var flag = (CollisionLayers)((int)layer << 1);

            if (!layers.HasFlag(flag))
            {
                var fix = $"Enable the {flag} layer on the {nameof(WaterRenderer)}.";
                if (!required) fix += " You can safely ignore this warning.";

                messenger
                (
                    $"The {value} {label} requires the {flag} layer which is not enabled.",
                    fix,
                    required ? MessageType.Error : MessageType.Warning, Water,
                    (_, y) => y.intValue = (int)(layers | flag),
                    $"{nameof(WaterRenderer._AnimatedWavesLod)}.{nameof(WaterRenderer._AnimatedWavesLod._CollisionLayers)}",
                    caller: target
                );

                return !required;
            }

            return true;
        }

        static bool ValidateQuerySource(Object target, ShowMessage messenger, IQueryableLod<IQueryProvider> lod, string extra)
        {
            if (Water == null)
            {
                return true;
            }

            if (!lod.Enabled)
            {
                return true;
            }

            if (lod.QuerySource == LodQuerySource.None)
            {
                messenger
                (
                    $"<i>{nameof(WaterRenderer)} > {lod.Name} > {nameof(lod.QuerySource)}</i> is set to <i>None</i>. {extra}",
                    $"Set the <i>{nameof(lod.QuerySource)}</i> to <i>{nameof(LodQuerySource.GPU)}</i> to use data.",
                    MessageType.Info, Water,
                    FixSetQuerySourceToCompute,
                    $"_{lod.GetType().Name}._{nameof(lod.QuerySource)}",
                    caller: target
                );
            }

            return true;
        }

        static bool ValidateFilteredChoice(int choice, string property, Object target, ShowMessage messenger)
        {
            var filter = target
                .GetType()
                .GetCustomAttributes<FilterEnum>(inherit: true)
                .FirstOrDefault(x => x._Property == property);

            if (filter?._Values.Contains(choice) == false)
            {
                var label = property[1..];

                messenger
                (
                    $"The {label} property is invalid.",
                    $"Choose a correct {label} property.",
                    MessageType.Error,
                    target
                );

                return false;
            }

            return true;
        }

        static void ShowDependentPropertyMessage(string dependentLabel, string dependencyLabel, string dependencyPropertyPath, ShowMessage messenger, Object dependencyContext)
        {
            messenger
            (
                $"{dependencyLabel} is not enabled, but {dependentLabel} requires it.",
                $"Enable {dependencyLabel}.",
                MessageType.Warning, dependencyContext,
                (_, y) => y.boolValue = true,
                dependencyPropertyPath
            );
        }

        static void ValidateWaterLevelPrecisionIssues(WaterRenderer water, Object caller, ShowMessage messenger)
        {
            if (water.LevelLod.Enabled && !water.LevelLod.Blur)
            {
                messenger
                (
                    $"If you are seeing bump artifacts on the water surface, try enabling {nameof(Lod.Blur)} on the {water.LevelLod.Name} simulation.",
                    "Enable blur.",
                    MessageType.Info,
                    water,
                    (x, y) => y.boolValue = true,
                    nameof(water._LevelLod) + "." + Lod.k_BlurField,
                    caller
                );
            }
        }

        static void ValidateDisplacementPrecisionIssues(WaterRenderer water, Object caller, ShowMessage messenger)
        {
            if (water.AnimatedWavesLod.Enabled && water.AnimatedWavesLod.TextureFormatMode != LodTextureFormatMode.Precision)
            {
                messenger
                (
                    $"If you are seeing bump artifacts on the water surface, try setting {nameof(AnimatedWavesLod.TextureFormatMode)} to {LodTextureFormatMode.Precision} in {nameof(AnimatedWavesLod)}.",
                    $"Set {nameof(AnimatedWavesLod.TextureFormatMode)} to {LodTextureFormatMode.Precision}.",
                    MessageType.Info,
                    water,
                    (x, y) => y.intValue = (int)LodTextureFormatMode.Precision,
                    nameof(water._AnimatedWavesLod) + "." + Lod.k_TextureFormatModeField,
                    caller
                );
            }
        }
    }
}
