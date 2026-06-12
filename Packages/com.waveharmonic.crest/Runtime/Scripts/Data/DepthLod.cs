// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using WaveHarmonic.Crest.Internal;
using WaveHarmonic.Crest.Utility;

namespace WaveHarmonic.Crest
{
    /// <summary>
    /// Data that gives depth of the water (height of sea level above water floor).
    /// </summary>
    [FilterEnum(nameof(_QuerySource), Filtered.Mode.Exclude, (int)LodQuerySource.CPU)]
    [FilterEnum(nameof(_TextureFormatMode), Filtered.Mode.Exclude, (int)LodTextureFormatMode.Automatic)]
    public sealed partial class DepthLod : Lod<IDepthProvider>
    {
        [@Space(10)]

        [Tooltip("Whether to include the terrain height automatically.\n\nThis will not include terrain details, nor will it produce a signed-distance field. There may also be a slight deviation due to differences in height data and terrain mesh. In these cases, please use the DepthProbe.")]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        internal bool _IncludeTerrainHeight = true;

        [Tooltip("Support signed distance field data generated from the depth probes.\n\nRequires a two component texture format.")]
        [@GenerateAPI(Setter.Custom)]
        [@DecoratedField, SerializeField]
        internal bool _EnableSignedDistanceFields = true;

        // NOTE: Must match CREST_WATER_DEPTH_BASELINE in Constants.hlsl.
        internal const float k_DepthBaseline = Mathf.Infinity;
        internal static readonly Color s_GizmoColor = new(1f, 0f, 0f, 0.5f);
        // We want the clear color to be the mininimum terrain height (-1000m).
        // Mathf.Infinity can cause problems for distance.
        static readonly Color s_NullColor = new(-k_DepthBaseline, k_DepthBaseline, 0, 0);
        static Color NullColor => Helpers.IsWebGPU ? new(float.MinValue, float.MaxValue, 0, 0) : s_NullColor;

        internal override string ID => "Depth";
        internal override string Name => "Water Depth";
        internal override Color GizmoColor => s_GizmoColor;
        private protected override Color ClearColor => NullColor;
        private protected override bool NeedToReadWriteTextureData => true;

        private protected override GraphicsFormat RequestedTextureFormat => _TextureFormatMode switch
        {
            LodTextureFormatMode.Automatic or
            LodTextureFormatMode.Performance => _EnableSignedDistanceFields ? GraphicsFormat.R16G16_SFloat : GraphicsFormat.R16_SFloat,
            LodTextureFormatMode.Precision => _EnableSignedDistanceFields ? GraphicsFormat.R32G32_SFloat : GraphicsFormat.R32_SFloat,
            LodTextureFormatMode.Manual => _TextureFormat,
            _ => throw new System.NotImplementedException(),
        };

        Texture2DArray _NullTexture;
        private protected override Texture2DArray NullTexture
        {
            get
            {
                if (_NullTexture == null)
                {
                    var texture = TextureArrayHelpers.CreateTexture2D(NullColor, UnityEngine.TextureFormat.RFloat);
                    texture.name = $"_Crest_{ID}LodTemporaryDefaultTexture";
                    _NullTexture = TextureArrayHelpers.CreateTexture2DArray(texture, k_MaximumSlices);
                    _NullTexture.name = $"_Crest_{ID}LodDefaultTexture";
                    Helpers.Destroy(texture);
                }

                return _NullTexture;
            }
        }

        internal DepthLod()
        {
            _Enabled = true;
            _TextureFormat = GraphicsFormat.R16G16_SFloat;
            _MaximumQueryCount = 512;
        }

        private protected override IDepthProvider CreateProvider(bool onEnable)
        {
            Queryable?.CleanUp();
            // Depth is GPU only, and can only be queried using the compute path.
            return onEnable && Enabled && QuerySource == LodQuerySource.GPU
                ? IDepthProvider.Create(_Water)
                : IDepthProvider.None;
        }

        internal static readonly SortedList<int, ILodInput> s_Inputs = new(Helpers.DuplicateComparison);
        private protected override SortedList<int, ILodInput> Inputs => s_Inputs;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void OnLoad()
        {
            s_Inputs.Clear();
        }

        void SetEnableSignedDistanceFields(bool previous, bool current)
        {
            if (previous == current) return;
            if (_Water == null || !_Water.isActiveAndEnabled || !Enabled) return;

            ReAllocate();
        }

#if d_Unity_Terrain
        TerrainDepthInput _TerrainDepthInput;

        internal override void Enable()
        {
            base.Enable();

            if (Enabled)
            {
                _TerrainDepthInput ??= new(this);
                Inputs.Add(_TerrainDepthInput.Queue, _TerrainDepthInput);
            }
        }

        internal override void Disable()
        {
            base.Disable();

            Inputs.Remove(_TerrainDepthInput);
        }

        sealed class TerrainDepthInput : ILodInput
        {
            public bool Enabled => _DepthLod._IncludeTerrainHeight;
            public bool IsCompute => true;
            public int Queue => int.MinValue;
            public int Pass => -1;
            public Rect Rect => Rect.zero;
            public MonoBehaviour Component => null;
            public float Filter(WaterRenderer water, int slice) => 1f;

            readonly DepthLod _DepthLod;
            readonly System.Collections.Generic.List<Terrain> _Terrains = new();

            public TerrainDepthInput(DepthLod lod)
            {
                _DepthLod = lod;
            }

            public void Draw(Lod lod, CommandBuffer buffer, RenderTargetIdentifier target, int pass = -1, float weight = 1, int slices = -1)
            {
                var resources = WaterResources.Instance;
                var wrapper = new PropertyWrapperCompute(buffer, resources.Compute._DepthTexture, 0);

                var threads = lod.Resolution / k_ThreadGroupSize;

                wrapper.SetTexture(Crest.ShaderIDs.s_Target, target);
                wrapper.SetVector(Crest.ShaderIDs.s_TextureRotation, new(0, 1));
                wrapper.SetBoolean(DepthLodInput.ShaderIDs.s_SDF, false);
                wrapper.SetKeyword(resources.Keywords.DepthTextureSDF, lod._Water._DepthLod._EnableSignedDistanceFields);

                Terrain.GetActiveTerrains(_Terrains);
                foreach (var terrain in _Terrains)
                {
                    var data = terrain.terrainData;
                    if (data == null) continue;
                    var size = data.size;
                    var position = terrain.GetPosition();

                    wrapper.SetFloat(DepthLodInput.ShaderIDs.s_HeightOffset, position.y);
                    wrapper.SetVector(Crest.ShaderIDs.s_Multiplier, new(size.y * 2f, 1, 1, 1));
                    wrapper.SetVector(Crest.ShaderIDs.s_TexturePosition, position.XZ() + (size.XZ() * 0.5f));
                    wrapper.SetVector(Crest.ShaderIDs.s_TextureSize, size.XZ());
                    wrapper.SetTexture(Crest.ShaderIDs.s_Texture, data.heightmapTexture);
                    wrapper.Dispatch(threads, threads, slices);
                }
            }
        }
#endif // d_Unity_Terrain

#if UNITY_EDITOR
        [@OnChange]
        private protected override void OnChange(string propertyPath, object previousValue)
        {
            base.OnChange(propertyPath, previousValue);

            switch (propertyPath)
            {
                case nameof(_EnableSignedDistanceFields):
                    SetEnableSignedDistanceFields((bool)previousValue, _EnableSignedDistanceFields);
                    break;
            }
        }
#endif
    }
}
