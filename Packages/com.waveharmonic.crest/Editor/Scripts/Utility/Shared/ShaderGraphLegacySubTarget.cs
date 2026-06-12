// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Rendering.BuiltIn;
using UnityEditor.Rendering.BuiltIn.ShaderGraph;
using UnityEditor.ShaderGraph;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_2022_3_OR_NEWER
using UnityEngine.UIElements;
#else
using UnityEngine.UIElements;
using UnityEditor.UIElements;
#endif

using UnityBuiltInLitSubTarget = UnityEditor.Rendering.BuiltIn.ShaderGraph.BuiltInLitSubTarget;

namespace WaveHarmonic.Crest.Editor.ShaderGraph
{
    sealed class MaterialModificationProcessor : AssetModificationProcessor
    {
        static void OnWillCreateAsset(string asset)
        {
            if (!asset.ToLowerInvariant().EndsWith(".mat"))
            {
                return;
            }

            MaterialPostProcessor.s_CreatedAssets.Add(asset);
        }
    }

    sealed class MaterialPostProcessor : AssetPostprocessor
    {
        public override int GetPostprocessOrder()
        {
            return 1;
        }

        internal static readonly List<string> s_CreatedAssets = new();

        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            foreach (var asset in importedAssets)
            {
                // We only care about materials
                if (!asset.EndsWith(".mat", System.StringComparison.InvariantCultureIgnoreCase))
                {
                    continue;
                }

                // Load the material and look for it's BuiltIn ShaderID.
                // We only care about versioning materials using a known BuiltIn ShaderID.
                // This skips any materials that only target other render pipelines, are user shaders,
                // or are shaders we don't care to version
                var material = (Material)AssetDatabase.LoadAssetAtPath(asset, typeof(Material));
                var shaderID = ShaderUtils.GetShaderID(material.shader);
                if (shaderID == ShaderUtils.ShaderID.Unknown)
                {
                    continue;
                }

                if (material.shader == null || material.shader.name != "Crest/Water")
                {
                    continue;
                }

                // Look for the BuiltIn AssetVersion
                AssetVersion assetVersion = null;
                var allAssets = AssetDatabase.LoadAllAssetsAtPath(asset);
                foreach (var subAsset in allAssets)
                {
                    if (subAsset is AssetVersion sub)
                    {
                        assetVersion = sub;
                    }
                }

                if (!assetVersion)
                {
                    if (s_CreatedAssets.Contains(asset))
                    {
                        s_CreatedAssets.Remove(asset);
                        CustomBuiltInLitGUI.UpdateMaterial(material);
                    }
                }
            }
        }
    }

    class CustomBuiltInLitGUI : BuiltInLitGUI
    {
        MaterialEditor _MaterialEditor;
        MaterialProperty[] _Properties;

        static readonly GUIContent s_WorkflowModeText = EditorGUIUtility.TrTextContent
        (
            "Workflow Mode",
            "Select a workflow that fits your textures. Choose between Metallic or Specular."
        );

        static readonly GUIContent s_TransparentReceiveShadowsText = EditorGUIUtility.TrTextContent
        (
            "Receives Shadows",
            "When enabled, other GameObjects can cast shadows onto this GameObject."
        );

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            _MaterialEditor = materialEditor;
            _Properties = properties;

            base.OnGUI(materialEditor, properties);
        }

        public override void ValidateMaterial(Material material)
        {
            base.ValidateMaterial(material);
            UpdateMaterial(material);
        }

        public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
        {
            base.AssignNewShaderToMaterial(material, oldShader, newShader);
            UpdateMaterial(material);
        }

        protected override void DrawSurfaceOptions(Material material)
        {
            var materialEditor = _MaterialEditor;
            var properties = _Properties;

            var workflowProperty = FindProperty(Property.SpecularWorkflowMode(), properties, false);
            if (workflowProperty != null)
            {
                DoPopup(s_WorkflowModeText, materialEditor, workflowProperty, System.Enum.GetNames(typeof(WorkflowMode)));
            }

            base.DrawSurfaceOptions(material);

            var surfaceTypeProp = FindProperty(Property.Surface(), properties, false);
            if (surfaceTypeProp != null && (SurfaceType)surfaceTypeProp.floatValue == SurfaceType.Transparent)
            {
                var trsProperty = FindProperty(BuiltInLitSubTarget.s_TransparentReceiveShadowsProperty, properties, false);
                DrawFloatToggleProperty(s_TransparentReceiveShadowsText, trsProperty);
            }
        }

        // Should be called by ShaderGraphMaterialsUpdater, but we will never upgrade.
        public static new void UpdateMaterial(Material material)
        {
            if (material.HasProperty(Property.SpecularWorkflowMode()))
            {
                var workflow = (WorkflowMode)material.GetFloat(Property.SpecularWorkflowMode());
                CoreUtils.SetKeyword(material, BuiltInLitSubTarget.LitDefines.s_SpecularSetup.referenceName, workflow == WorkflowMode.Specular);
            }

            if (material.HasProperty(BuiltInLitSubTarget.s_TransparentReceiveShadowsProperty))
            {
                var receive = material.GetFloat(BuiltInLitSubTarget.s_TransparentReceiveShadowsProperty) == 1f;
                CoreUtils.SetKeyword(material, BuiltInLitSubTarget.LitDefines.s_TransparentReceivesShadows.referenceName, receive);
            }
        }
    }

    sealed class BuiltInLitSubTarget : BuiltInSubTarget
    {
        const string k_ShaderPath = "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Legacy";
        const string k_TemplatePath = "Packages/com.waveharmonic.crest/Editor/Shaders/Templates";

        readonly UnityBuiltInLitSubTarget _BuiltInLitSubTarget;

#pragma warning disable IDE0032, IDE1006
        [SerializeField]
        WorkflowMode m_WorkflowMode = WorkflowMode.Metallic;

        [SerializeField]
        NormalDropOffSpace m_NormalDropOffSpace = NormalDropOffSpace.Tangent;

        [SerializeField]
        bool m_TransparentReceiveShadows = true;
#pragma warning restore IDE0032, IDE1006

        public static readonly string s_TransparentReceiveShadowsProperty = "_BUILTIN_TransparentReceiveShadows";

        public BuiltInLitSubTarget()
        {
            _BuiltInLitSubTarget = new();
            displayName = _BuiltInLitSubTarget.displayName;
        }

        protected override ShaderUtils.ShaderID shaderID => ShaderUtils.ShaderID.SG_Lit;
        public override bool IsActive() => true;

        WorkflowMode WorkflowMode
        {
            get => m_WorkflowMode;
            set => m_WorkflowMode = value;
        }

        NormalDropOffSpace NormalDropOffSpace
        {
            get => m_NormalDropOffSpace;
            set => m_NormalDropOffSpace = value;
        }

        bool TransparentReceiveShadows
        {
            get => m_TransparentReceiveShadows;
            set => m_TransparentReceiveShadows = value;
        }

#if UNITY_2022_3_OR_NEWER
        static FieldInfo s_CustomEditorForRenderPipelines;
        static FieldInfo CustomEditorForRenderPipelines => s_CustomEditorForRenderPipelines ??= typeof(TargetSetupContext).GetField("customEditorForRenderPipelines", BindingFlags.NonPublic | BindingFlags.Instance);
#endif

        public override void Setup(ref TargetSetupContext context)
        {
            _BuiltInLitSubTarget.target = target;
            _BuiltInLitSubTarget.normalDropOffSpace = NormalDropOffSpace;
            _BuiltInLitSubTarget.Setup(ref context);

            // Caused a crash: !context.HasCustomEditorForRenderPipeline(null)
            if (string.IsNullOrEmpty(target.customEditorGUI))
            {
#if UNITY_2022_3_OR_NEWER
                var editors = (List<ShaderCustomEditor>)CustomEditorForRenderPipelines.GetValue(context);
                if (editors.Count > 0)
                {
                    editors.RemoveAt(editors.Count - 1);
                }

                context.AddCustomEditorForRenderPipeline(typeof(CustomBuiltInLitGUI).FullName, "");
#else
                if (context.customEditorForRenderPipelines.Count > 0)
                {
                    context.customEditorForRenderPipelines.RemoveAt(context.customEditorForRenderPipelines.Count - 1);
                }

                context.customEditorForRenderPipelines.Add((typeof(CustomBuiltInLitGUI).FullName, ""));
#endif
            }

            context.subShaders.RemoveAt(0);
            context.AddSubShader(SubShaders.Lit(this));
        }

        public override void ProcessPreviewMaterial(Material material)
        {
            _BuiltInLitSubTarget.target = target;
            _BuiltInLitSubTarget.normalDropOffSpace = NormalDropOffSpace;
            _BuiltInLitSubTarget.ProcessPreviewMaterial(material);
            CustomBuiltInLitGUI.UpdateMaterial(material);
        }

        public override void GetFields(ref TargetFieldContext context)
        {
            _BuiltInLitSubTarget.target = target;
            _BuiltInLitSubTarget.normalDropOffSpace = NormalDropOffSpace;
            _BuiltInLitSubTarget.GetFields(ref context);
            // Do not use this, as we handle this properly.
            context.AddField(BuiltInFields.SpecularSetup, false);
        }

        public override void GetActiveBlocks(ref TargetActiveBlockContext context)
        {
            _BuiltInLitSubTarget.target = target;
            _BuiltInLitSubTarget.normalDropOffSpace = NormalDropOffSpace;
            _BuiltInLitSubTarget.GetActiveBlocks(ref context);

            context.activeBlocks.Remove(BlockFields.SurfaceDescription.Metallic);
            var insertion = context.activeBlocks.FindIndex(x => x == BlockFields.SurfaceDescription.Occlusion) + 1;

            if ((WorkflowMode == WorkflowMode.Specular) || target.allowMaterialOverride)
            {
                context.activeBlocks.Insert(insertion, BlockFields.SurfaceDescription.Specular);
            }

            if ((WorkflowMode == WorkflowMode.Metallic) || target.allowMaterialOverride)
            {
                context.activeBlocks.Insert(insertion, BlockFields.SurfaceDescription.Metallic);
            }
        }

        public override void CollectShaderProperties(PropertyCollector collector, GenerationMode generationMode)
        {
            if (target.allowMaterialOverride)
            {
                collector.AddFloatProperty(Property.SpecularWorkflowMode(), (float)WorkflowMode);
            }

            _BuiltInLitSubTarget.target = target;
            _BuiltInLitSubTarget.normalDropOffSpace = NormalDropOffSpace;
            _BuiltInLitSubTarget.CollectShaderProperties(collector, generationMode);

            if (target.allowMaterialOverride)
            {
                collector.AddFloatProperty(s_TransparentReceiveShadowsProperty, TransparentReceiveShadows ? 1f : 0f);
            }

            // LEqual
            collector.AddFloatProperty(SubShaders.k_ShadowCasterZTest, 4, UnityEditor.ShaderGraph.Internal.HLSLDeclaration.UnityPerMaterial);
        }

        public override void GetPropertiesGUI(ref TargetPropertyGUIContext context, System.Action onChange, System.Action<string> registerUndo)
        {
            target.AddDefaultMaterialOverrideGUI(ref context, onChange, registerUndo);

            context.AddProperty("Workflow", new EnumField(WorkflowMode.Metallic) { value = WorkflowMode }, (evt) =>
            {
                if (Equals(WorkflowMode, evt.newValue))
                    return;

                registerUndo("Change Workflow");
                WorkflowMode = (WorkflowMode)evt.newValue;
                onChange();
            });

            target.GetDefaultSurfacePropertiesGUI(ref context, onChange, registerUndo);

            context.AddProperty("Transparent Receives Shadows", new Toggle() { value = TransparentReceiveShadows }, (evt) =>
            {
                if (Equals(TransparentReceiveShadows, evt.newValue))
                    return;

                registerUndo("Change Transparent Receives Shadows");
                TransparentReceiveShadows = evt.newValue;
                onChange();
            });

            context.AddProperty("Fragment Normal Space", new EnumField(NormalDropOffSpace.Tangent) { value = NormalDropOffSpace }, (evt) =>
            {
                if (Equals(NormalDropOffSpace, evt.newValue))
                    return;

                registerUndo("Change Fragment Normal Space");
                NormalDropOffSpace = (NormalDropOffSpace)evt.newValue;
                _BuiltInLitSubTarget.normalDropOffSpace = NormalDropOffSpace;
                onChange();
            });
        }

        static class SubShaders
        {
            static readonly string s_ShaderPathDefines = $"{k_ShaderPath}/Defines.hlsl";
            static readonly string s_ShaderPathBuilding = $"{k_ShaderPath}/LegacyBuilding.hlsl";

            // SetShaderPassEnabled on ShadowCaster pass does not work for BIRP. We set ZTest
            // to Never which is the best we can do. We are still incurring the draw call cost.
            // This is an issue because of the way we trigger motion vectors, but is a bug with
            // Unity and should be reported.
            internal const string k_ShadowCasterZTest = "_Crest_BUILTIN_ShadowCasterZTest";

            internal static System.Type s_SubShadersType;
            internal static System.Type SubShadersType => s_SubShadersType ??= typeof(UnityBuiltInLitSubTarget).GetNestedType("SubShaders", BindingFlags.Static | BindingFlags.NonPublic);
            internal static MethodInfo s_LitMethod;
            internal static MethodInfo LitMethod => s_LitMethod ??= SubShadersType.GetMethod("Lit", BindingFlags.Static | BindingFlags.Public);

            static void PatchIncludes(ref PassDescriptor result)
            {
                var includes = new IncludeCollection();

                includes.Add(s_ShaderPathDefines, IncludeLocation.Pregraph);
                includes.Add("Packages/com.unity.shadergraph/Editor/Generation/Targets/BuiltIn/Editor/ShaderGraph/Includes/ShaderPass.hlsl", IncludeLocation.Pregraph);

                foreach (var include in result.includes)
                {
                    includes.AddInternal(include.guid, include.path, include.location, include.fieldConditions);
                }

                result.includes = includes;
            }

            static void PatchSpecularIncludes(ref PassDescriptor result, string file)
            {
                var ic = new IncludeCollection();
                foreach (var include in result.includes)
                {
                    if (include.path.EndsWith(file))
                    {
                        ic.Add(s_ShaderPathBuilding, include.location);
                        ic.AddInternal(include.guid, include.path, include.location, include.fieldConditions);
                    }
                    else
                    {
                        ic.AddInternal(include.guid, include.path, include.location, include.fieldConditions);
                    }
                }

                result.includes = ic;
            }

            static readonly Dictionary<string, string> s_Mappings = new()
            {
                { "SHADERPASS_FORWARD",     "PBRForwardPass.hlsl" },
                { "SHADERPASS_FORWARD_ADD", "PBRForwardAddPass.hlsl" },
                { "SHADERPASS_DEFERRED",    "PBRDeferredPass.hlsl" },
            };

            static readonly string[] s_SkipVariants = new string[]
            {
                "LIGHTMAP_ON",
                "LIGHTMAP_SHADOW_MIXING",
                "DIRLIGHTMAP_COMBINED",
                "DYNAMICLIGHTMAP_ON",
                "SHADOWS_SHADOWMASK",
            };

            public static SubShaderDescriptor Lit(BuiltInLitSubTarget subtarget)
            {
                var target = subtarget.target;
                var ssd = (SubShaderDescriptor)LitMethod.Invoke(null, new object[] { target, target.renderType, target.renderQueue });

                PassCollection passes = new();

                foreach (var item in ssd.passes)
                {
                    // Many artifacts in U6 if our Write Depth enabled.
                    // Caused by _SURFACE_TYPE_TRANSPARENT in m_ValidKeywords.
                    if (item.descriptor.referenceName == "SceneSelectionPass")
                    {
                        continue;
                    }

                    var result = item.descriptor;

                    var keywords = new KeywordCollection();

                    foreach (var keyword in result.keywords)
                    {
                        // All others are either duplicate or unused.
                        if (!keyword.descriptor.referenceName.StartsWith("_BUILTIN_"))
                        {
                            continue;
                        }

                        keywords.Add(keyword.descriptor, keyword.fieldConditions);
                    }

                    result.keywords = keywords;

                    switch (item.descriptor.referenceName)
                    {
                        case "SHADERPASS_FORWARD":
                        case "SHADERPASS_FORWARD_ADD":
                        case "SHADERPASS_DEFERRED":
                            AddWorkflowModeControlToPass(ref result, target, subtarget.WorkflowMode);
                            PatchSpecularIncludes(ref result, s_Mappings[item.descriptor.referenceName]);

                            var pragmas = new PragmaCollection();
                            foreach (var pragma in result.pragmas)
                            {
                                // For UAVs (RWStructuredBuffer).
                                if (pragma.descriptor.value.StartsWithNoAlloc("target"))
                                {
                                    pragmas.Add(Pragma.Target(ShaderModel.Target45));
                                    continue;
                                }

                                if (pragma.descriptor.value.StartsWithNoAlloc("vertex"))
                                {
                                    pragmas.Add(Pragma.SkipVariants(s_SkipVariants));
                                }

                                pragmas.Add(pragma.descriptor, pragma.fieldConditions);
                            }
                            result.pragmas = pragmas;

                            goto default;
                        default:
                            PatchIncludes(ref result);
                            break;
                    }

                    switch (item.descriptor.referenceName)
                    {
                        case "SHADERPASS_FORWARD":
                        case "SHADERPASS_FORWARD_ADD":
                            AddReceivesShadowsControlToPass(ref result, target, subtarget.TransparentReceiveShadows);
                            break;
                        case "SHADERPASS_SHADOWCASTER":
                            var states = new RenderStateCollection();
                            foreach (var state in result.renderStates)
                            {
                                if (state.descriptor.type == RenderStateType.ZTest)
                                {
                                    states.Add(RenderState.ZTest($"[{k_ShadowCasterZTest}]"));
                                    continue;
                                }

                                states.Add(state.descriptor, state.fieldConditions);
                            }

                            result.renderStates = states;
                            break;
                    }

                    // Add missing cull render state.
                    if (item.descriptor.referenceName == "SHADERPASS_FORWARD_ADD")
                    {
                        CoreRenderStates.AddUberSwitchedCull(target, result.renderStates);
                    }

                    // Inject MV before DO pass.
                    if (item.descriptor.referenceName == "SHADERPASS_DEPTHONLY")
                    {
                        var mv = LitPasses.MotionVectors(target);
                        PatchIncludes(ref mv);
                        passes.Add(mv);
                    }

                    // Fix XR SPI.
                    if (result.requiredFields != null)
                    {
                        var found = false;

                        foreach (var collection in result.requiredFields)
                        {
                            if (collection.field == StructFields.Attributes.instanceID)
                            {
                                found = true;
                                break;
                            }
                        }

                        if (!found)
                        {
                            result.requiredFields.Add(StructFields.Attributes.instanceID);
                        }
                    }

                    passes.Add(result);
                }

                ssd.passes = passes;

                return ssd;
            }

            static void AddWorkflowModeControlToPass(ref PassDescriptor pass, BuiltInTarget target, WorkflowMode workflowMode)
            {
                if (target.allowMaterialOverride)
                {
                    pass.keywords.Add(LitDefines.s_SpecularSetup);
                }
                else if (workflowMode == WorkflowMode.Specular)
                {
                    pass.defines.Add(LitDefines.s_SpecularSetup, 1);
                }
            }

            static void AddReceivesShadowsControlToPass(ref PassDescriptor pass, BuiltInTarget target, bool receives)
            {
                if (target.allowMaterialOverride)
                {
                    pass.keywords.Add(LitDefines.s_TransparentReceivesShadows);
                    pass.keywords.Add(LitDefines.s_ShadowsSingleCascade);
                    pass.keywords.Add(LitDefines.s_ShadowsSplitSpheres);
                    pass.keywords.Add(LitDefines.s_ShadowsSoft);
                }
                else if (receives)
                {
                    pass.defines.Add(LitDefines.s_TransparentReceivesShadows, 1);
                    pass.keywords.Add(LitDefines.s_ShadowsSingleCascade);
                    pass.keywords.Add(LitDefines.s_ShadowsSplitSpheres);
                    pass.keywords.Add(LitDefines.s_ShadowsSoft);
                }
            }
        }

        static class LitPasses
        {
            static readonly string s_ShaderPathMotionVectorCommon = $"{k_ShaderPath}/MotionVectorCommon.hlsl";
            static readonly string s_ShaderPathMotionVectorPass = $"{k_ShaderPath}/MotionVectorPass.hlsl";

            public static RenderStateDescriptor UberSwitchedCullRenderState(BuiltInTarget target)
            {
                if (target.allowMaterialOverride)
                {
                    return RenderState.Cull(CoreRenderStates.Uniforms.cullMode);
                }
                else
                {
                    return RenderState.Cull(CoreRenderStates.RenderFaceToCull(target.renderFace));
                }
            }

            public static PassDescriptor MotionVectors(BuiltInTarget target)
            {
                var result = new PassDescriptor()
                {
                    // Definition
                    displayName = "BuiltIn MotionVectors",
                    referenceName = "SHADERPASS_MOTION_VECTORS",
                    lightMode = "MotionVectors",
                    useInPreview = false,

                    // Template
                    passTemplatePath = BuiltInTarget.kTemplatePath,
                    sharedTemplateDirectories = BuiltInTarget.kSharedTemplateDirectories.Union
                    (
                        new string[]
                        {
                            k_TemplatePath,
                            "Packages/com.unity.shadergraph/Editor/Generation/Targets/BuiltIn/Editor/ShaderGraph"
                        }
                    ).ToArray(),

                    // Port Mask
                    validVertexBlocks = new BlockFieldDescriptor[]
                    {
                        BlockFields.VertexDescription.Position,
                    },
                    validPixelBlocks = CoreBlockMasks.FragmentAlphaOnly,

                    // Fields
                    structs = CoreStructCollections.Default,
                    requiredFields = new()
                    {
                        // Needed for XR, but not sure if correct.
                        StructFields.Attributes.instanceID,
                    },
                    fieldDependencies = CoreFieldDependencies.Default,

                    // Conditional State
                    renderStates = new()
                    {
                        { RenderState.ZTest(ZTest.LEqual) },
                        { RenderState.ZWrite(ZWrite.On) },
                        { UberSwitchedCullRenderState(target) },
                        // MVs write to the depth buffer causing z-fighting. Luckily, the depth texture has
                        // already been updated, and will not be updated before water renders.
                        { RenderState.ColorMask("ColorMask RG\nOffset 1, 1") },
                    },

                    pragmas = new()
                    {
                        { Pragma.Target(ShaderModel.Target35) }, // NOTE: SM 2.0 only GL
                        { Pragma.MultiCompileInstancing },
                        { Pragma.Vertex("vert") },
                        { Pragma.Fragment("frag") },
                    },

                    defines = new() { CoreDefines.BuiltInTargetAPI },
                    keywords = new(),
                    includes = new()
                    {
                        // Pre-graph
                        { CoreIncludes.CorePregraph },
                        { CoreIncludes.ShaderGraphPregraph },

                        // Post-graph
                        { s_ShaderPathMotionVectorCommon, IncludeLocation.Postgraph },
                        { CoreIncludes.CorePostgraph },
                        { s_ShaderPathMotionVectorPass, IncludeLocation.Postgraph },
                    },

                    // Custom Interpolator Support
                    customInterpolators = CoreCustomInterpDescriptors.Common,
                };

                // Only support time for now.
                result.defines.Add(LitDefines.s_AutomaticTimeBasedMotionVectors, 1);

                CorePasses.AddAlphaClipControlToPass(ref result, target);
                return result;
            }
        }

        internal static class LitDefines
        {
            public static readonly KeywordDescriptor s_AutomaticTimeBasedMotionVectors = new()
            {
                displayName = "Automatic Time-Based Motion Vectors",
                referenceName = "AUTOMATIC_TIME_BASED_MOTION_VECTORS",
                type = KeywordType.Boolean,
                definition = KeywordDefinition.Predefined,
                scope = KeywordScope.Local,
                stages = KeywordShaderStage.Vertex,
            };

            public static readonly KeywordDescriptor s_SpecularSetup = new()
            {
                displayName = "Specular Setup",
                referenceName = "_BUILTIN_SPECULAR_SETUP",
                type = KeywordType.Boolean,
                definition = KeywordDefinition.ShaderFeature,
                scope = KeywordScope.Local,
                stages = KeywordShaderStage.Fragment
            };

            public static readonly KeywordDescriptor s_TransparentReceivesShadows = new()
            {
                displayName = "Transparent Receives Shadows",
                referenceName = "_BUILTIN_TRANSPARENT_RECEIVES_SHADOWS",
                type = KeywordType.Boolean,
                definition = KeywordDefinition.ShaderFeature,
                scope = KeywordScope.Local,
                stages = KeywordShaderStage.Fragment
            };

            public static readonly KeywordDescriptor s_ShadowsSingleCascade = new()
            {
                displayName = "Single Cascade Shadows",
                referenceName = "SHADOWS_SINGLE_CASCADE",
                type = KeywordType.Boolean,
                definition = KeywordDefinition.MultiCompile,
                scope = KeywordScope.Global,
                stages = KeywordShaderStage.All,
            };

            public static readonly KeywordDescriptor s_ShadowsSoft = new()
            {
                displayName = "Soft Shadows",
                referenceName = "SHADOWS_SOFT",
                type = KeywordType.Boolean,
                definition = KeywordDefinition.MultiCompile,
                scope = KeywordScope.Global,
                stages = KeywordShaderStage.All,
            };

            public static readonly KeywordDescriptor s_ShadowsSplitSpheres = new()
            {
                displayName = "Stable Fit Shadows",
                referenceName = "SHADOWS_SPLIT_SPHERES",
                type = KeywordType.Boolean,
                definition = KeywordDefinition.MultiCompile,
                scope = KeywordScope.Global,
                stages = KeywordShaderStage.All,
            };
        }
    }
}
