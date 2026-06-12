// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

//#define PROFILE_CONSTRUCTION

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using WaveHarmonic.Crest.Internal;

namespace WaveHarmonic.Crest
{
    partial class SurfaceRenderer
    {
        // Keep references to meshes so they can be cleaned up later.
        readonly Mesh[] _Meshes = new Mesh[(int)Builder.PatchType.Count];

        /// <summary>
        /// Instantiates all the water geometry, as a set of tiles.
        /// </summary>
        static class Builder
        {
            // The comments below illustrate case when BASE_VERT_DENSITY = 2. The water mesh is built up from these patches. Rotational symmetry
            // is used where possible to eliminate combinations. The slim variants are used to eliminate overlap between patches.
            internal enum PatchType
            {
                /// <summary>
                /// Adds no skirt. Used in interior of highest detail LOD (0)
                ///
                ///    1 -------
                ///      |  |  |
                ///  z   -------
                ///      |  |  |
                ///    0 -------
                ///      0     1
                ///         x
                ///
                /// </summary>
                Interior,

                /// <summary>
                /// Adds a full skirt all of the way around a patch
                ///
                ///      -------------
                ///      |  |  |  |  |
                ///    1 -------------
                ///      |  |  |  |  |
                ///  z   -------------
                ///      |  |  |  |  |
                ///    0 -------------
                ///      |  |  |  |  |
                ///      -------------
                ///         0     1
                ///            x
                ///
                /// </summary>
                Fat,

                /// <summary>
                /// Adds a skirt on the right hand side of the patch
                ///
                ///    1 ----------
                ///      |  |  |  |
                ///  z   ----------
                ///      |  |  |  |
                ///    0 ----------
                ///      0     1
                ///         x
                ///
                /// </summary>
                FatX,

                /// <summary>
                /// Adds a skirt on the right hand side of the patch, removes skirt from top
                /// </summary>
                FatXSlimZ,

                /// <summary>
                /// Outer most side - this adds an extra skirt on the left hand side of the patch,
                /// which will point outwards and be extended to Zfar
                ///
                ///    1 --------------------------------------------------------------------------------------
                ///      |  |  |                                                                              |
                ///  z   --------------------------------------------------------------------------------------
                ///      |  |  |                                                                              |
                ///    0 --------------------------------------------------------------------------------------
                ///      0     1
                ///         x
                ///
                /// </summary>
                FatXOuter,

                /// <summary>
                /// Adds skirts at the top and right sides of the patch
                /// </summary>
                FatXZ,

                /// <summary>
                /// Adds skirts at the top and right sides of the patch and pushes them to horizon
                /// </summary>
                FatXZOuter,

                /// <summary>
                /// One less set of verts in x direction
                /// </summary>
                SlimX,

                /// <summary>
                /// One less set of verts in both x and z directions
                /// </summary>
                SlimXZ,

                /// <summary>
                /// One less set of verts in x direction, extra verts at start of z direction
                ///
                ///      ----
                ///      |  |
                ///    1 ----
                ///      |  |
                ///  z   ----
                ///      |  |
                ///    0 ----
                ///      0     1
                ///         x
                ///
                /// </summary>
                SlimXFatZ,

                /// <summary>
                /// Number of patch types
                /// </summary>
                Count,
            }

            // Instance Indices:
            // 00 01 02 03
            // 04       05
            // 06       07
            // 08 09 10 11
            static readonly Vector2[] s_Offsets =
            {
                new(-1.5f, +1.5f), new(-0.5f, +1.5f), new(+0.5f, +1.5f), new(+1.5f, +1.5f),
                new(-1.5f, +0.5f),                                       new(+1.5f, +0.5f),
                new(-1.5f, -0.5f),                                       new(+1.5f, -0.5f),
                new(-1.5f, -1.5f), new(-0.5f, -1.5f), new(+0.5f, -1.5f), new(+1.5f, -1.5f),
            };

            // First LOD has inside bit as well:
            // 00 01 02 03
            // 04 05 06 07
            // 08 09 10 11
            // 12 13 14 15
            internal static readonly Vector2[] s_OffsetsFirstLod =
            {
                // Interior first for sorted rendering.
                new(-0.5f, +0.5f), new(+0.5f, +0.5f), new(-0.5f, -0.5f), new(+0.5f, -0.5f),

                // Exterior.
                new(-1.5f, +1.5f), new(-0.5f, +1.5f), new(+0.5f, +1.5f), new(+1.5f, +1.5f),
                new(-1.5f, +0.5f),                                       new(+1.5f, +0.5f),
                new(-1.5f, -0.5f),                                       new(+1.5f, -0.5f),
                new(-1.5f, -1.5f), new(-0.5f, -1.5f), new(+0.5f, -1.5f), new(+1.5f, -1.5f),
            };

            // Usually rings have an extra side of vertices that point inwards. The outermost
            // ring has both the inward vertices and also an additional outwards set of
            // vertices that go to the horizon.
            static readonly PatchType[] s_PatchTypes =
            {
                PatchType.SlimXFatZ, PatchType.SlimX, PatchType.SlimX, PatchType.SlimXZ,
                PatchType.FatX,                                        PatchType.SlimX,
                PatchType.FatX,                                        PatchType.SlimX,
                PatchType.FatXZ,     PatchType.FatX,  PatchType.FatX,  PatchType.FatXSlimZ,
            };

            // All interior - the "side" types have an extra skirt that points inwards - this
            // means that this inner most section does not need any skirting. This is good, as
            // this is the highest density part of the mesh.
            static readonly PatchType[] s_PatchTypesFirstLod =
            {
                PatchType.Interior,  PatchType.Interior, PatchType.Interior, PatchType.Interior,
                PatchType.SlimXFatZ, PatchType.SlimX,    PatchType.SlimX,    PatchType.SlimXZ,
                PatchType.FatX,                                              PatchType.SlimX,
                PatchType.FatX,                                              PatchType.SlimX,
                PatchType.FatXZ,     PatchType.FatX,     PatchType.FatX,     PatchType.FatXSlimZ,
            };

            static readonly PatchType[] s_PatchTypesLastLod =
            {
                PatchType.FatXZOuter, PatchType.FatXOuter, PatchType.FatXOuter, PatchType.FatXZOuter,
                PatchType.FatXOuter,                                            PatchType.FatXOuter,
                PatchType.FatXOuter,                                            PatchType.FatXOuter,
                PatchType.FatXZOuter, PatchType.FatXOuter, PatchType.FatXOuter, PatchType.FatXZOuter,
            };

            static int s_SiblingIndex;

            public static Transform GenerateMesh(WaterRenderer water, SurfaceRenderer surface, List<WaterChunkRenderer> tiles, int lodDataResolution, int geoDownSampleFactor, int lodCount)
            {
                if (lodCount < 1)
                {
                    Debug.LogError("Crest: Invalid LOD count: " + lodCount.ToString(), water);
                    return null;
                }

#if PROFILE_CONSTRUCTION
                var sw = new System.Diagnostics.Stopwatch();
                sw.Start();
#endif

                s_SiblingIndex = 0;

                var root = new GameObject("Root");
                Debug.Assert(root != null, "Crest: The water Root transform could not be immediately constructed. Please report this issue to the Crest developers via our support email or GitHub at https://github.com/wave-harmonic/crest/issues .");

                root.hideFlags = water._Debug._ShowHiddenObjects ? HideFlags.DontSave : HideFlags.HideAndDontSave;
                root.transform.parent = water.Container.transform;
                root.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                root.transform.localScale = Vector3.one;

                // create mesh data
                // 4 tiles across a LOD, and support lowering density by a factor
                var tileResolution = Mathf.Round(0.25f * lodDataResolution / geoDownSampleFactor);
                for (var i = 0; i < (int)PatchType.Count; i++)
                {
                    surface._Meshes[i] = BuildPatch(water, (PatchType)i, tileResolution);
                }

                for (var i = 0; i < lodCount; i++)
                {
                    CreateLOD(water, surface, tiles, root.transform, i, lodCount, surface._Meshes, lodDataResolution, geoDownSampleFactor, surface.Layer);
                }

#if PROFILE_CONSTRUCTION
                sw.Stop();
                Debug.Log( "Crest: Finished generating " + lodCount.ToString() + " LODs, time: " + (1000.0*sw.Elapsed.TotalSeconds).ToString(".000") + "ms" );
#endif

                return root.transform;
            }

            static Mesh BuildPatch(WaterRenderer water, PatchType pt, float vertDensity)
            {
                var verts = new List<Vector3>();
                var indices = new List<int>();

                // stick a bunch of verts into a 1m x 1m patch (scaling happens later)
                var dx = 1f / vertDensity;


                //////////////////////////////////////////////////////////////////////////////////
                // verts

                // see comments within PatchType for diagrams of each patch mesh

                // skirt widths on left, right, bottom and top (in order)
                float skirtXminus = 0f, skirtXplus = 0f;
                float skirtZminus = 0f, skirtZplus = 0f;
                // set the patch size
                if (pt == PatchType.Fat) { skirtXminus = skirtXplus = skirtZminus = skirtZplus = 1f; }
                else if (pt is PatchType.FatX or PatchType.FatXOuter) { skirtXplus = 1f; }
                else if (pt is PatchType.FatXZ or PatchType.FatXZOuter) { skirtXplus = skirtZplus = 1f; }
                else if (pt == PatchType.FatXSlimZ) { skirtXplus = 1f; skirtZplus = -1f; }
                else if (pt == PatchType.SlimX) { skirtXplus = -1f; }
                else if (pt == PatchType.SlimXZ) { skirtXplus = skirtZplus = -1f; }
                else if (pt == PatchType.SlimXFatZ) { skirtXplus = -1f; skirtZplus = 1f; }

                var sideLength_verts_x = 1f + vertDensity + skirtXminus + skirtXplus;
                var sideLength_verts_z = 1f + vertDensity + skirtZminus + skirtZplus;

                var start_x = -0.5f - skirtXminus * dx;
                var start_z = -0.5f - skirtZminus * dx;
                var end_x = 0.5f + skirtXplus * dx;
                var end_z = 0.5f + skirtZplus * dx;

                // With a default value of 100, this will reach the horizon at all levels at
                // a far plane of 200k.
                var extentsMultiplier = water._ExtentsSizeMultiplier * (Lod.k_MaximumSlices + 1 - water.LodLevels);

                for (float j = 0; j < sideLength_verts_z; j++)
                {
                    // interpolate z across patch
                    var z = Mathf.Lerp(start_z, end_z, j / (sideLength_verts_z - 1f));

                    // push outermost edge out to horizon
                    if (pt == PatchType.FatXZOuter && j == sideLength_verts_z - 1f)
                        z *= extentsMultiplier;

                    for (float i = 0; i < sideLength_verts_x; i++)
                    {
                        // interpolate x across patch
                        var x = Mathf.Lerp(start_x, end_x, i / (sideLength_verts_x - 1f));

                        // push outermost edge out to horizon
                        if (i == sideLength_verts_x - 1f && (pt == PatchType.FatXOuter || pt == PatchType.FatXZOuter))
                            x *= extentsMultiplier;

                        // could store something in y, although keep in mind this is a shared mesh that is shared across multiple lods
                        verts.Add(new(x, 0f, z));
                    }
                }


                //////////////////////////////////////////////////////////////////////////////////
                // indices

                var sideLength_squares_x = (int)sideLength_verts_x - 1;
                var sideLength_squares_z = (int)sideLength_verts_z - 1;

                for (var j = 0; j < sideLength_squares_z; j++)
                {
                    for (var i = 0; i < sideLength_squares_x; i++)
                    {
                        var flipEdge = false;

                        if (i % 2 == 1) flipEdge = !flipEdge;
                        if (j % 2 == 1) flipEdge = !flipEdge;

                        var i0 = i + j * (sideLength_squares_x + 1);
                        var i1 = i0 + 1;
                        var i2 = i0 + (sideLength_squares_x + 1);
                        var i3 = i2 + 1;

                        if (!flipEdge)
                        {
                            // tri 1
                            indices.Add(i3);
                            indices.Add(i1);
                            indices.Add(i0);

                            // tri 2
                            indices.Add(i0);
                            indices.Add(i2);
                            indices.Add(i3);
                        }
                        else
                        {
                            // tri 1
                            indices.Add(i3);
                            indices.Add(i1);
                            indices.Add(i2);

                            // tri 2
                            indices.Add(i0);
                            indices.Add(i2);
                            indices.Add(i1);
                        }
                    }
                }


                //////////////////////////////////////////////////////////////////////////////////
                // create mesh

                var mesh = new Mesh();
                if (verts != null && verts.Count > 0)
                {
                    var arrV = new Vector3[verts.Count];
                    verts.CopyTo(arrV);

                    var arrI = new int[indices.Count];
                    indices.CopyTo(arrI);

                    mesh.SetIndices(null, MeshTopology.Triangles, 0);
                    mesh.vertices = arrV;

                    // HDRP needs full data. Do this on a define to keep door open to runtime changing of RP.
#if d_UnityHDRP
                    var norms = new Vector3[verts.Count];
                    for (var i = 0; i < norms.Length; i++) norms[i] = Vector3.up;
                    var tans = new Vector4[verts.Count];
                    for (var i = 0; i < tans.Length; i++) tans[i] = new(1, 0, 0, 1);

                    mesh.normals = norms;
                    mesh.tangents = tans;
#else
                    mesh.normals = null;
#endif

                    mesh.SetIndices(arrI, MeshTopology.Triangles, 0);

                    // recalculate bounds. add a little allowance for snapping. in the chunk renderer script, the bounds will be expanded further
                    // to allow for horizontal displacement
                    mesh.RecalculateBounds();
                    var bounds = mesh.bounds;
                    // Increase snapping allowance (see #1148). Value was chosen by observation with a
                    // custom debug mode to show pixels that were out of bounds.
                    dx *= 3f;
                    bounds.extents = new(bounds.extents.x + dx, bounds.extents.y, bounds.extents.z + dx);
                    mesh.bounds = bounds;
                    mesh.name = pt.ToString();
                }

                return mesh;
            }

            static void CreateLOD(WaterRenderer water, SurfaceRenderer surface, List<WaterChunkRenderer> tiles, Transform parent, int lodIndex, int lodCount, Mesh[] meshData, int lodDataResolution, int geoDownSampleFactor, int layer)
            {
                var horizScale = Mathf.Pow(2f, lodIndex);

                var isBiggestLOD = lodIndex == lodCount - 1;
                var generateSkirt = isBiggestLOD;

#if CREST_DEBUG
                generateSkirt = generateSkirt && !surface._Debug._DisableSkirt;
#endif

                Vector2[] offsets;
                PatchType[] patchTypes;

                if (lodIndex != 0)
                {
                    offsets = s_Offsets;
                    patchTypes = generateSkirt ? s_PatchTypesLastLod : s_PatchTypes;
                }
                else
                {
                    offsets = s_OffsetsFirstLod;
                    patchTypes = s_PatchTypesFirstLod;
                }

#if CREST_DEBUG
                // debug toggle to force all patches to be the same. they'll be made with a surrounding skirt to make sure patches
                // overlap
                if (surface._Debug._UniformTiles)
                {
                    patchTypes = new PatchType[patchTypes.Length];
                    System.Array.Fill(patchTypes, PatchType.Fat);
                }
#endif

                // create the water patches
                for (var i = 0; i < offsets.Length; i++)
                {
                    // instantiate and place patch
                    var patch = surface._ChunkTemplate
                        ? Helpers.InstantiatePrefab(surface._ChunkTemplate)
                        : new();
                    // Also applying the hide flags to the chunk will prevent it from being pickable in the editor.
                    patch.hideFlags = water._Debug._ShowHiddenObjects ? HideFlags.DontSave : HideFlags.HideAndDontSave;
                    patch.name = $"Tile_L{lodIndex}_{patchTypes[i]}";
                    patch.layer = layer;
                    patch.transform.parent = parent;
                    var pos = offsets[i];
                    patch.transform.localPosition = horizScale * new Vector3(pos.x, 0f, pos.y);
                    // scale only horizontally, otherwise culling bounding box will be scaled up in y
                    patch.transform.localScale = new(horizScale, 1f, horizScale);

                    if (!patch.TryGetComponent<MeshRenderer>(out var mr))
                    {
                        mr = patch.AddComponent<MeshRenderer>();
                        // I don't think one would use light probes for a purely specular water surface? (although diffuse
                        // foam shading would benefit).
                        mr.lightProbeUsage = LightProbeUsage.Off;
                    }

                    var order = -lodCount + (patchTypes[i] == PatchType.Interior ? -1 : lodIndex);

                    var chunk = patch.AddComponent<WaterChunkRenderer>();
                    var mesh = meshData[(int)patchTypes[i]];

                    {
                        patch.AddComponent<MeshFilter>().sharedMesh = mesh;

                        chunk._Water = water;
                        chunk._SortingOrder = order;
                        chunk._SiblingIndex = s_SiblingIndex++;

                        chunk.Initialize(lodIndex, mr, mesh);

                        // When custom rendering, we loop over chunks to render, which means these need to
                        // be optimally sorted. We statically sort by LOD. Sub-sort is only done for LOD0,
                        // where interior tiles are placed first. Further sorting must be done dynamically.
                        tiles.Add(chunk);

                        chunk._DrawRenderBounds = water.Surface._Debug._DrawRendererBounds;
                    }

                    // Sorting order to stop unity drawing it back to front. Make the innermost four tiles draw first,
                    // followed by the rest of the tiles by LOD index.
                    if (RenderPipelineHelper.IsHighDefinition)
                    {
                        // HDRP has a different rendering priority system:
                        // https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@10.10/manual/Renderer-And-Material-Priority.html#sorting-by-renderer
                        mr.rendererPriority = order;
                    }
                    else if (!water.Surface.AllowRenderQueueSorting)
                    {
                        // Sorting order to stop unity drawing it back to front. make the innermost 4 tiles draw first, followed by
                        // the rest of the tiles by LOD index. all this happens before layer 0 - the sorting layer takes priority over the
                        // render queue it seems! ( https://cdry.wordpress.com/2017/04/28/unity-render-queues-vs-sorting-layers/ ). This pushes
                        // water rendering way early, so transparent objects will by default render afterwards, which is typical for water rendering.
                        mr.sortingOrder = order;
                    }

                    mr.shadowCastingMode = water.Surface.CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;

                    // This setting is ignored by Unity for the transparent water shader.
                    mr.receiveShadows = false;

                    mr.motionVectorGenerationMode = !water.WriteMotionVectors
                        ? MotionVectorGenerationMode.ForceNoMotion
                        : MotionVectorGenerationMode.Object;

                    mr.material = water.Surface.Material;

                    OnCreateChunkRenderer?.Invoke(mr);

                    // rotate side patches to point the +x side outwards
                    var rotateXOutwards = patchTypes[i] is PatchType.FatX or PatchType.FatXOuter or PatchType.SlimX or PatchType.SlimXFatZ;
                    if (rotateXOutwards)
                    {
                        if (Mathf.Abs(pos.y) >= Mathf.Abs(pos.x))
                            patch.transform.localEulerAngles = 90f * Mathf.Sign(pos.y) * -Vector3.up;
                        else
                            patch.transform.localEulerAngles = pos.x < 0f ? Vector3.up * 180f : Vector3.zero;
                    }

                    // rotate the corner patches so the +x and +z sides point outwards
                    var rotateXZOutwards = patchTypes[i] is PatchType.FatXZ or PatchType.SlimXZ or PatchType.FatXSlimZ or PatchType.FatXZOuter;
                    if (rotateXZOutwards)
                    {
                        // xz direction before rotation
                        var from = new Vector3(1f, 0f, 1f).normalized;
                        // target xz direction is outwards vector given by local patch position - assumes this patch is a corner (checked below)
                        var to = patch.transform.localPosition.normalized;
                        if (Mathf.Abs(patch.transform.localPosition.x) < 0.0001f || Mathf.Abs(Mathf.Abs(patch.transform.localPosition.x) - Mathf.Abs(patch.transform.localPosition.z)) > 0.001f)
                        {
                            Debug.LogWarning("Crest: Skipped rotating a patch because it isn't a corner, click here to highlight.", patch);
                            continue;
                        }

                        // Detect 180 degree rotations as it doesn't always rotate around Y
                        if (Vector3.Dot(from, to) < -0.99f)
                            patch.transform.localEulerAngles = Vector3.up * 180f;
                        else
                            patch.transform.localRotation = Quaternion.FromToRotation(from, to);
                    }

                    // Pre-rotate bounds.
                    {
                        var bounds = mesh.bounds;
                        bounds = bounds.Rotate(chunk.transform.rotation);
                        chunk._LocalBounds = bounds;
                        chunk._LocalScale = chunk.transform.localScale.x;
                    }
                }
            }
        }
    }
}
