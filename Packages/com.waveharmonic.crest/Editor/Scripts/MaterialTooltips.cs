// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;
using WaveHarmonic.Crest.Editor.Settings;

namespace WaveHarmonic.Crest.Editor
{
    static class MaterialTooltips
    {
        internal static readonly Dictionary<string, string> s_Common = new()
        {
            // Feather
            { "_Crest_Feather", "Feather the edges of the mesh using the texture coordinates. Easiest to understand with a plane" },
            { "_Crest_FeatherWidth", "How far from edge to feather" },
        };

        internal static readonly Dictionary<string, Dictionary<string, string>> s_Grouped = new()
        {
            {
                "Crest/Inputs/Animated Waves/Add From Texture", new()
                {
                    { "_Crest_HeightsOnly", "Treats the texture as a heightmap and reads from the R channel" },
                }
            },
            {
                "Crest/Inputs/Dynamic Waves/Add Bump", new()
                {
                    { "_Crest_Amplitude", "The vertical radius of the bump. How much the surface will rise or fall." },
                    { "_Crest_Radius", "The horizontal radius of the bump." },
                }
            },
            {
                "Crest/Inputs/Flow/Add From Texture", new()
                {
                    { "_Crest_FlipX", "Flips the X direction (R channel)" },
                    { "_Crest_FlipZ", "Flips the Z direction (Y channel)" },
                    { "_Crest_NegativeValues", "Whether the texture supports negative values otherwise assumes data is packed in 0-1 range" },
                }
            },
            {
                "Crest/Inputs/All/Scale", new()
                {
                    { "_Crest_Scale", "Scale the water data. Zero is no data and one leaves data untouched" },
                    { "_Crest_ApplyTexture", "Use the texture instead of the scale value" },
                    { "_Crest_Invert", "Inverts the scale value" },
                }
            },
            {
                "Crest/Inputs/Shape Waves/Add From Geometry", new()
                {
                    { "_Crest_FeatherWaveStart", "Controls ramp distance over which waves grow/fade as they move forwards" },
                }
            },
            {
                UnderwaterRenderer.k_ShaderNameEffect, new()
                {
                    { "_Crest_ExtinctionMultiplier", "Scales the depth fog density. Useful to reduce the intensity of the depth fog when underwater only" },
                    { "_Crest_SunBoost", "Boost the intensity of the sun scattering" },
                    { "_Crest_OutScatteringFactor", "Applied to the water depth when calculating out-scattering. Less means it gets darker deeper" },
                    { "_Crest_OutScatteringExtinctionFactor", "Applied to the distance where the out-scattering gradient is calculated. Lower decreases out-scattering influence" },
                    { "_Crest_DitheringEnabled", "Dithering will reduce banding" },
                    { "_Crest_DitheringIntensity", "Increase if banding persists" },
                    { "_Crest_MeniscusEnabled", "Add a meniscus to the boundary between water and air" },
                    { "_Crest_DataSliceOffset", "How much to smooth water data such as water depth, light scattering, shadowing. Helps to smooth flickering that can occur under camera motion" },
                }
            },
            {
                WaterShaderUtility.k_ShaderName, new()
                {
                    { "_Crest_NormalsStrengthOverall", "Strength of the final surface normal (both wave normal and normal map)" },
                    { "_Crest_ApplyDisplacementNormals", "Whether to sample the displacement maps (Animated Waves and Dynamic Waves) for normal contributions." },
                    { "_Crest_NormalMapEnabled", "Whether to add normal detail from a texture. Can be used to add visual detail to the water surface" },
                    { "_Crest_NormalMapTexture", "Normal map texture" },
                    { "_Crest_NormalMapStrength", "Strength of normal map influence" },
                    { "_Crest_NormalMapScale", "Base scale of multi-scale normal map texture" },
                    { "_Crest_NormalMapScrollSpeed", "Speed of the normal maps scrolling" },
                    { "_Crest_NormalMapTurbulenceEnabled", "Increase chance of sparkles where water is turbulent" },
                    { "_Crest_NormalMapTurbulenceStrength", "Strength of the normal map influence where turbulent" },
                    { "_Crest_NormalMapTurbulenceCoverage", "The threshold where turbulence triggers this effect.\n\nValues above 0.9 may be too high for calmer wave conditions, as it will trigger the effect where water is not turbulent. Furthermore, a value of one or very close one can cause a pop when weighing out waves to zero" },
                    { "_Crest_AbsorptionColor", "Works as a color (ie red adds red rather than subtracts red). This value is converted to real absorption values (proportion of light getting absorbed by water in atoms per meter). Alpha channel is for density. High alpha and darker color reduces transparency" },
                    { "_Crest_Scattering", "Light scattered by the water towards the viewer (in-scattered) per meter. Brighter color reduces transparency" },
                    { "_Crest_Anisotropy", "The directionality of the scattering where zero means scattered in all directions. The further towards one, the less visible soft shadows will be" },
                    { "_Crest_DirectTerm", "Scale direct light contribution to volume lighting" },
                    { "_Crest_DirectTermAdditional", "Scale additional lights contribution to volume lighting" },
                    { "_Crest_AmbientTerm", "Scale ambient light contribution to volume lighting" },
                    { "_Crest_ShadowsAffectsAmbientFactor", "How much shadows affect ambient lighting. Typically this not required, but can help scenes with large shadowed areas" },
                    { "_Crest_AdditionalLightsBlend", "How much the additional light contributions blend with scattering." },
                    { "_Crest_ApplyFresnelToVolumeLighting", "Applies the fresnel to volume lighting to help the surface blend with the horizon in the form of being a mirror. Typically, only suitable with full smoothness (unless skybox is designed for water rather than earth)." },
                    { "_Crest_SSSEnabled", "Whether to to emulate light scattering through waves" },
                    { "_Crest_SSSIntensity", "Direct light contribution intensity. Applied to the scattering color. This effect is best if subtle" },
                    { "_Crest_SSSPinchMinimum", "Higher the value the more scattering is towards the peaks of the waves" },
                    { "_Crest_SSSPinchMaximum", "Higher the value for more scattering" },
                    { "_Crest_SSSPinchFalloff", "Falloff for pinch minimum/maximum" },
                    { "_Crest_SSSDirectionalFalloff", "Falloff for direct light scattering to affect directionality" },
                    { "_Crest_Specular", "Strength of specular lighting response" },
                    { "_Crest_Occlusion", "Strength of reflection" },
                    { "_Crest_OcclusionUnderwater", "Strength of reflection when underwater. Keep this at zero to avoid skybox reflections which look incorrect when underwater, unless you want reflections from Planar Reflections or probes" },
                    { "_Crest_Fresnel", "The fresnel power. Decrease to increase strength of reflections." },
                    { "_Crest_Smoothness", "Smoothness of surface. A value of one is ideal for flat water only" },
                    { "_Crest_SmoothnessFar", "Material smoothness at far distance from camera. Helps to spread out specular highlight in mid-to-background. From a theory point of view, models transfer of normal detail to microfacets in BRDF" },
                    { "_Crest_SmoothnessFarDistance", "Definition of far distance" },
                    { "_Crest_SmoothnessFalloff", "How smoothness varies between near and far distance" },
                    { "_Crest_MinimumReflectionDirectionY", "Limits the reflection direction on the Y axis. Zero prevents reflections below the horizon. Small values above zero can be used to reduce horizon reflection contributions. Values above zero will negatively affect dynamic reflections like planar or SSR" },
                    { "_Crest_PlanarReflectionsEnabled", "Dynamically rendered 'reflection plane' style reflections. Requires Reflections to be enabled on the Water Renderer" },
                    { "_Crest_PlanarReflectionsIntensity", "Intensity of the planar reflections" },
                    { "_Crest_PlanarReflectionsDistortion", "How much the water normal affects the planar reflection" },
                    { "_Crest_PlanarReflectionsApplySmoothness", "Whether to apply smoothness to planar reflection sample via mip-map blending. Enabling will likely incur artifacts with most perturbed water" },
                    { "_Crest_PlanarReflectionsRoughness", "Controls the mipmap range" },
                    { "_Crest_RefractionStrength", "How strongly light is refracted when passing through water surface" },
                    { "_Crest_RefractiveIndexOfWater", "Index of refraction of water - typically left at 1.333. Changing this value can increase/decrease the size of the Snell's window" },
                    { "_Crest_TotalInternalReflectionIntensity", "Zero will make the underwater reflections transparent. Slightly semi-transparency is a zero performance cost alternative to TIR" },
                    { "_Crest_ShadowsEnabled", "Whether to receive shadow data. Does not affect shadow contributions from Unity" },
                    { "_Crest_ShadowCasterThreshold", "Same concept as Alpha Clip Threshold but for foam casted shadows" },
                    { "_Crest_FoamEnabled", "Enable foam layer on water surface" },
                    { "_Crest_FoamTexture", "Foam texture" },
                    { "_Crest_FoamScale", "Scale of multi-scale foam texture" },
                    { "_Crest_FoamScrollSpeed", "Speed of the foam scrolling. This speed is slower than the other scroll speeds, as it scrolls in one direction only" },
                    { "_Crest_FoamFeather", "Controls how gradual the transition is from full foam to no foam. Higher values look more realistic and can help mitigate flow phasing" },
                    { "_Crest_FoamIntensityAlbedo", "Scale intensity of diffuse lighting" },
                    { "_Crest_FoamSmoothness", "Smoothness of foam material" },
                    { "_Crest_FoamNormalStrength", "Strength of the generated normals" },
                    { "_Crest_FoamBioluminescenceEnabled", "Enables a foam-based bioluminescence. This reuses the foam sample" },
                    { "_Crest_FoamBioluminescenceColor", "The color and intensity of the bioluminescence" },
                    { "_Crest_FoamBioluminescenceIntensity", "The intensity of the bioluminescent foam" },
                    { "_Crest_FoamBioluminescenceMaximumDepth", "The maximum water depth where bioluminescence can appear. This can be used to keep bioluminescence localized to the shoreline" },
                    { "_Crest_FoamBioluminescenceSeaLevelOnly", "Only render foam bioluminescence at sea level. This will fade bioluminescence to be fully gone at 1m from sea level." },
                    { "_Crest_FoamBioluminescenceGlowIntensity", "The intensity of the bioluminescent glow" },
                    { "_Crest_FoamBioluminescenceGlowCoverage", "The coverage of the bioluminescent glow. Glow is based on raw foam data which makes it more present than foam" },
                    { "_Crest_FoamBioluminescenceSparklesEnabled", "Whether to apply bioluminescent sparkles. This uses the green channel of the foam texture as an emissive map" },
                    { "_Crest_FoamBioluminescenceSparklesIntensity", "The intensity of the bioluminescent sparkles" },
                    { "_Crest_FoamBioluminescenceSparklesCoverage", "The coverage of the bioluminescent sparkles. Sparkles placement is based on raw foam data which makes it more present than foam" },
                    { "_Crest_CausticsEnabled", "Approximate rays being focused/defocused on underwater surfaces" },
                    { "_Crest_CausticsTexture", "Caustics texture" },
                    { "_Crest_CausticsStrength", "Intensity of caustics effect" },
                    { "_Crest_CausticsTextureScale", "Caustics texture scale" },
                    { "_Crest_CausticsScrollSpeed", "Speed of the caustics scrolling" },
                    { "_Crest_CausticsTextureAverage", "The 'mid' value of the caustics texture, around which the caustic texture values are scaled. Decreasing this value will reduce the caustics darkening underwater surfaces" },
                    { "_Crest_CausticsFocalDepth", "The depth at which the caustics are in focus" },
                    { "_Crest_CausticsDepthOfField", "The range of depths over which the caustics are in focus" },
                    { "_Crest_CausticsForceDistortion", "Forces the distortion texture to be applied for above water. This can be used to distort caustics even when there is no waves" },
                    { "_Crest_CausticsDistortionTexture", "Texture to distort caustics. Only applicable to underwater effect for now" },
                    { "_Crest_CausticsDistortionStrength", "How much the caustics texture is distorted" },
                    { "_Crest_CausticsDistortionScale", "The scale of the distortion pattern used to distort the caustics" },
                    { "_Crest_CausticsMotionBlur", "How much caustics are blurred when advected by flow" },
                    { "_CREST_FLOW_LOD", "Flow is horizontal motion of water. Flow must be enabled on the Water Renderer to generate flow data" },
                    { "_Crest_AlbedoEnabled", "Enable the Albedo simulation layer. Albedo must be enabled on the Water" },
                    { "_Crest_AlbedoIgnoreFoam", "Whether Albedo renders over the top of foam or not." },
                    { "_Crest_TransparencyMinimumAlpha", "The minimum alpha value for transparency. Makes water contribution to the final image stronger instead of the scene behind it" },
                }
            },
        };
    }

    static class WaterShaderUtility
    {
        public const string k_ShaderName = "Crest/Water";

        internal static void UpdateAbsorptionFromColor(Material material)
        {
            if (!material.HasProperty(WaterRenderer.ShaderIDs.s_Absorption) || !material.HasProperty(WaterRenderer.ShaderIDs.s_AbsorptionColor))
            {
                return;
            }

            // Convert an authored absorption colour to density values.
            WaterRenderer.UpdateAbsorptionFromColor(material);

            if (!material.IsPropertyOverriden(WaterRenderer.ShaderIDs.s_AbsorptionColor))
            {
                material.RevertPropertyOverride(WaterRenderer.ShaderIDs.s_Absorption);
            }
        }

        internal static readonly List<string> s_HiddenCategories = new();
        static bool s_Initialized;
        internal static void FilterCategories()
        {
            // Only need to do it once after compile.
            if (s_Initialized)
            {
                return;
            }

            var settings = ProjectSettings.Instance.CurrentPlatformSettings;
            s_HiddenCategories.Clear();
            if (!settings.AlbedoSimulation) s_HiddenCategories.Add("Albedo");
            if (!settings.FoamBioluminescence) s_HiddenCategories.Add("Bioluminescence");
            if (!settings.PlanarReflections) s_HiddenCategories.Add("Reflections (Planar)");
            if (!settings.SimpleTransparency) s_HiddenCategories.Add("Simple Transparency");
            s_Initialized = true;
        }

        internal static MaterialProperty[] FilterProperties(MaterialProperty[] properties)
        {
            // Show specular control.
            var specular = true;

            if (!RenderPipelineHelper.IsHighDefinition)
            {
                specular = properties
                    .First(x => x.name == (RenderPipelineHelper.IsLegacy ? "_BUILTIN_WorkflowMode" : "_WorkflowMode")).floatValue == 0;
            }
#if UNITY_6000_0_OR_NEWER
            else
            {
                // Always show specular control for U5, as it cannot be overriden by the material.
                specular = properties
                    .First(x => x.name == "_MaterialID").floatValue == 4;
            }
#endif

            var settings = ProjectSettings.Instance.CurrentPlatformSettings;
            var filtered = properties.Where(x => (specular || x.name != "_Crest_Specular") && x.name != "_Crest_Absorption");

            if (!settings.CausticsForceDistortion)
            {
                filtered = filtered.Where(x => x.name != "_Crest_CausticsForceDistortion");
            }

            if (!settings.NormalMaps)
            {
                filtered = filtered.Where(x => !x.name.StartsWithNoAlloc("_Crest_NormalMap"));
            }

            if (!settings.ShadowSimulation)
            {
                filtered = filtered.Where(x => x.name != "_Crest_ShadowsAffectsAmbientFactor");
            }

            return filtered.ToArray();
        }
    }

    /// <summary>
    /// Supports tooltips and skips rendering render pipeline properties like "Queue".
    /// </summary>
    sealed class CustomShaderGUI : ShaderGUI
    {
        static readonly GUIContent s_Label = new();

        public override void OnMaterialPreviewGUI(MaterialEditor materialEditor, Rect r, GUIStyle background)
        {

        }

        public override void OnGUI(MaterialEditor editor, MaterialProperty[] properties)
        {
            var material = editor.target as Material;
            var shader = material.shader;
            var grouped = MaterialTooltips.s_Grouped.GetValueOrDefault(shader.name, null);

            WaterShaderUtility.UpdateAbsorptionFromColor((Material)editor.target);

            foreach (var property in properties)
            {
#if UNITY_6000_2_OR_NEWER
                if ((property.propertyFlags & UnityEngine.Rendering.ShaderPropertyFlags.HideInInspector) != 0) continue;
#else
                if ((property.flags & MaterialProperty.PropFlags.HideInInspector) != 0) continue;
#endif

                var name = property.name;
                s_Label.text = property.displayName;
                s_Label.tooltip = grouped?.GetValueOrDefault(name, null);
                s_Label.tooltip ??= MaterialTooltips.s_Common.GetValueOrDefault(name, null);
                editor.ShaderProperty(property, s_Label);
            }
        }
    }

#if d_UnityShaderGraph
    class LegacyCustomShaderGUI : ShaderGraph.CustomBuiltInLitGUI
    {
        MaterialEditor _Editor;
        MaterialProperty[] _Properties;
        protected string _ShaderName;

        public override void OnMaterialPreviewGUI(MaterialEditor materialEditor, Rect r, GUIStyle background)
        {

        }

        public override void OnGUI(MaterialEditor editor, MaterialProperty[] properties)
        {
            properties = properties.Where(x => x.name != "_Crest_Version").ToArray();

            _Editor = editor;
            _Properties = properties;
            base.OnGUI(editor, properties);
        }

        protected override void DrawSurfaceInputs(Material material)
        {
            WaterShaderUtility.FilterCategories();
            ShaderGraphPropertyDrawers.DrawShaderGraphGUI(_Editor, _Properties, MaterialTooltips.s_Grouped[_ShaderName], WaterShaderUtility.s_HiddenCategories);
        }
    }

    // Warning! Renaming this class is a breaking change due to users potentially
    // exporting the shader to integrate with other assets.
    sealed class LegacyWaterShaderGUI : LegacyCustomShaderGUI
    {
        public override void OnGUI(MaterialEditor editor, MaterialProperty[] properties)
        {
            _ShaderName = WaterShaderUtility.k_ShaderName;
            properties = WaterShaderUtility.FilterProperties(properties);

            base.OnGUI(editor, properties);
            WaterShaderUtility.UpdateAbsorptionFromColor(editor.target as Material);
        }
    }

#if d_UnityURP
    class UniversalCustomShaderGUI : ShaderGraphLitGUI
    {
        MaterialEditor _Editor;
        MaterialProperty[] _Properties;
        protected string _ShaderName;

        public override void OnMaterialPreviewGUI(MaterialEditor materialEditor, Rect r, GUIStyle background)
        {

        }

        public override void OnGUI(MaterialEditor editor, MaterialProperty[] properties)
        {
            properties = properties.Where(x => x.name != "_Crest_Version").ToArray();

            _Editor = editor;
            _Properties = properties;

            base.OnGUI(editor, properties);
        }

        public override void DrawSurfaceInputs(Material material)
        {
            WaterShaderUtility.FilterCategories();
            ShaderGraphPropertyDrawers.DrawShaderGraphGUI(_Editor, _Properties, MaterialTooltips.s_Grouped[_ShaderName], WaterShaderUtility.s_HiddenCategories);
        }
    }

    // Warning! Renaming this class is a breaking change due to users potentially
    // exporting the shader to integrate with other assets.
    sealed class UniversalWaterShaderGUI : UniversalCustomShaderGUI
    {
        public override void OnGUI(MaterialEditor editor, MaterialProperty[] properties)
        {
            _ShaderName = WaterShaderUtility.k_ShaderName;
            properties = WaterShaderUtility.FilterProperties(properties);

            base.OnGUI(editor, properties);
            WaterShaderUtility.UpdateAbsorptionFromColor(editor.target as Material);
        }
    }
#endif // d_UnityURP

#if d_UnityHDRP
    sealed class CustomShaderGraphUIBlock : MaterialUIBlock
    {
        public override void LoadMaterialProperties() { }

        public override void OnGUI()
        {
            using var header = new MaterialHeaderScope("Exposed Properties", (uint)ExpandableBit.ShaderGraph, materialEditor);

            if (!header.expanded)
            {
                return;
            }

            WaterShaderUtility.FilterCategories();

            var name = (materialEditor.customShaderGUI as HighDefinitionCustomShaderGUI)._ShaderName;
            ShaderGraphPropertyDrawers.DrawShaderGraphGUI(materialEditor, properties, MaterialTooltips.s_Grouped[name], WaterShaderUtility.s_HiddenCategories);
        }
    }

    class HighDefinitionCustomShaderGUI : LightingShaderGraphGUI
    {
        internal string _ShaderName;

        public HighDefinitionCustomShaderGUI()
        {
            // Add refraction block.
            uiBlocks.Insert(1, new TransparencyUIBlock(MaterialUIBlock.ExpandableBit.Transparency, TransparencyUIBlock.Features.Refraction));
            // Remove the ShaderGraphUIBlock to avoid having duplicated properties in the UI.
            uiBlocks.RemoveAll(x => x is ShaderGraphUIBlock);
            // Insert the custom block just after the Surface Option block.
            uiBlocks.Insert(1, new CustomShaderGraphUIBlock());
        }

        protected override void OnMaterialGUI(MaterialEditor editor, MaterialProperty[] properties)
        {
            properties = properties.Where(x => x.name != "_Crest_Version").ToArray();

            base.OnMaterialGUI(editor, properties);
            WaterShaderUtility.UpdateAbsorptionFromColor(editor.target as Material);
        }
    }

    // Warning! Renaming this class is a breaking change due to users potentially
    // exporting the shader to integrate with other assets.
    sealed class HighDefinitionWaterShaderGUI : HighDefinitionCustomShaderGUI
    {
        protected override void OnMaterialGUI(MaterialEditor editor, MaterialProperty[] properties)
        {
            _ShaderName = WaterShaderUtility.k_ShaderName;
            properties = WaterShaderUtility.FilterProperties(properties);

            base.OnMaterialGUI(editor, properties);
            WaterShaderUtility.UpdateAbsorptionFromColor(editor.target as Material);
        }
    }
#endif // d_UnityHDRP
#endif // d_UnityShaderGraph
}
