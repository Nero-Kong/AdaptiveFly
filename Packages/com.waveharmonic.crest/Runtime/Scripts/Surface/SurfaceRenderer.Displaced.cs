// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;
using UnityEngine.Rendering;
using WaveHarmonic.Crest.Internal;

// Possible improvements:
// - Add quality property
// - Add water level separately (seems fine)
// - Loop over all chunks for larger near clip planes

namespace WaveHarmonic.Crest
{
    partial class SurfaceRenderer
    {
        internal const int k_SurfaceDataShaderPass = 2;

        internal static partial class ShaderIDs
        {
            public static int s_WaterLine = Shader.PropertyToID("_Crest_WaterLine");
            public static int s_WaterLineSnappedPosition = Shader.PropertyToID("_Crest_WaterLineSnappedPosition");
            public static int s_WaterLineResolution = Shader.PropertyToID("_Crest_WaterLineResolution");
            public static int s_WaterLineTexel = Shader.PropertyToID("_Crest_WaterLineTexel");
            public static int s_WaterLineFlatWater = Shader.PropertyToID("_Crest_WaterLineFlatWater");
        }

        RenderTexture _HeightRT;
        internal RenderTexture HeightRT { get => _HeightRT; }

        CommandBuffer _BeforeRenderingCommands;
        Material _DisplacedMaterial;

        internal struct SurfaceDataParameters
        {
            public Vector2 _SnappedPosition;
            public Vector2 _Resolution;
            public float _Texel;
        }

        internal SurfaceDataParameters _SurfaceDataParameters;
        internal MaterialPropertyBlock _SurfaceDataMPB;

        internal void BindDisplacedSurfaceData<T>(T properties) where T : IPropertyWrapper
        {
            properties.SetTexture(ShaderIDs.s_WaterLine, HeightRT);
            properties.SetVector(ShaderIDs.s_WaterLineSnappedPosition, _SurfaceDataParameters._SnappedPosition);
            properties.SetVector(ShaderIDs.s_WaterLineResolution, _SurfaceDataParameters._Resolution);
            properties.SetFloat(ShaderIDs.s_WaterLineTexel, _SurfaceDataParameters._Texel);
        }

        internal void UpdateDisplacedSurfaceData(Camera camera)
        {
            Helpers.SetGlobalBoolean(ShaderIDs.s_WaterLineFlatWater, IsQuadMesh);

            if (IsQuadMesh)
            {
                return;
            }

            // World size of the texture. Formula should effectively cover the camera.
            var size = 1f + (camera.nearClipPlane * 2f);

            // Do not use the water position. It will cause a mismatch when using displacement
            // correction.
            var bounds = new Bounds(camera.transform.position, Vector3.one * size);

            if (_DisplacedMaterial == null)
            {
                _DisplacedMaterial = new(WaterResources.Instance.Shaders._UnderwaterMask);
            }

            _BeforeRenderingCommands ??= new();
            var commands = _BeforeRenderingCommands;
            commands.name = "Crest.DrawMask";
            commands.Clear();

            // TODO: add control so users can set this.
            // Diminishing returns beyond 0.0125.
            UpdateDisplacedSurfaceData
            (
                commands,
                bounds,
                "_Crest_WaterLine",
                ref _HeightRT,
                texel: 0.0125f,
                maximumResolution: 2048,
                out _SurfaceDataParameters
            );

            _SurfaceDataMPB ??= new();
            var wrapper = new PropertyWrapperMPB(_SurfaceDataMPB);
            BindDisplacedSurfaceData(wrapper);

            var lod = (int)Builder.PatchType.Interior;
            var mpb = PerCascadeMPB[lod];

            var viewpoint = _Water.Viewpoint;

            if (viewpoint == null || (viewpoint != camera.transform && Vector3.Distance(viewpoint.position, camera.transform.position) > 0.01f))
            {
                foreach (var chunk in _Water.Surface.Chunks)
                {
                    if (!bounds.IntersectsXZ(chunk.Rend.bounds))
                    {
                        continue;
                    }

                    commands.DrawMesh
                    (
                        chunk._Mesh,
                        chunk.transform.localToWorldMatrix,
                        _DisplacedMaterial,
                        submeshIndex: 0,
                        shaderPass: k_SurfaceDataShaderPass,
                        chunk._MaterialPropertyBlock
                    );
                }
            }
            else
            {
                for (var i = 0; i < 4; i++)
                {
                    commands.DrawMesh
                    (
                        _Meshes[lod],
                        Root.localToWorldMatrix * Matrix4x4.TRS(Builder.s_OffsetsFirstLod[i].XNZ(), Quaternion.identity, Vector3.one),
                        _DisplacedMaterial,
                        submeshIndex: 0,
                        k_SurfaceDataShaderPass,
                        mpb
                    );
                }
            }

            Graphics.ExecuteCommandBuffer(commands);
        }

        internal void UpdateDisplacedSurfaceData(CommandBuffer commands, Bounds bounds, string name, ref RenderTexture target, float texel, int maximumResolution, out SurfaceDataParameters parameters)
        {
            var size = bounds.size.XZ();
            var position = bounds.center.XZ();

            var scale = size;

            var resolution = new Vector2Int
            (
                Mathf.CeilToInt(size.x / texel),
                Mathf.CeilToInt(size.y / texel)
            );

            var largest = Mathf.Max(resolution.x, resolution.y);

            if (largest > maximumResolution)
            {
                texel = Mathf.Max(size.x, size.y) / maximumResolution;

                resolution = new Vector2Int
                (
                    Mathf.CeilToInt(size.x / texel),
                    Mathf.CeilToInt(size.y / texel)
                );
            }

            // Snapping for spatial stability. Different results, but could not tell which is
            // more accurate. At higher resolution, appears negligable anyway.
            var snapped = position - new Vector2(Mathf.Repeat(position.x, texel), Mathf.Repeat(position.y, texel));

            // Store for binding later.
            parameters = new()
            {
                _SnappedPosition = snapped,
                _Resolution = resolution,
                _Texel = texel,
            };

            // FIXME: LOD scale less than two has cut off and fall off at edges.
            var view = WaterRenderer.CalculateViewMatrixFromSnappedPositionRHS(snapped.XNZ());
            var projection = Matrix4x4.Ortho(size.x * -0.5f, size.x * 0.5f, size.y * -0.5f, size.y * 0.5f, 1f, 10000f + 10000f);

            if (target == null)
            {
                target = new(resolution.x, resolution.y, 0)
                {
                    name = name,
                    // Needs this precision.
                    graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R32_SFloat
                };
            }
            else if (target.width != resolution.x || target.height != resolution.y)
            {
                target.Release();
                target.width = resolution.x;
                target.height = resolution.y;
            }

            if (!target.IsCreated())
            {
                target.Create();
            }

#if d_UnityHDRP
            if (RenderPipelineHelper.IsHighDefinition)
            {
                var buffer = new UnityEngine.Rendering.HighDefinition.ShaderVariablesGlobal();

                projection = GL.GetGPUProjectionMatrix(projection, true);

                // If we want to use camera relative rendering, then we should not set the matrix
                // position. Instead set _WorldSpaceCameraPos_Internal.
                buffer._ViewProjMatrix = projection * view;

                ConstantBuffer.PushGlobal(commands, buffer, Crest.ShaderIDs.Unity.s_ShaderVariablesGlobal);
            }
            else
#endif
            {
                commands.SetViewProjectionMatrices(view, projection);
            }

            commands.SetRenderTarget(target);
            commands.ClearRenderTarget(true, true, Color.clear);

            // For mask compute, meniscus etc.
            commands.SetGlobalTexture(ShaderIDs.s_WaterLine, target);
            commands.SetGlobalVector(ShaderIDs.s_WaterLineSnappedPosition, snapped);
            commands.SetGlobalVector(ShaderIDs.s_WaterLineResolution, (Vector2)resolution);
            commands.SetGlobalFloat(ShaderIDs.s_WaterLineTexel, texel);
        }
    }
}
