using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LanternPines.GlyphMesh3D.Generation
{
    /// <summary>
    /// Orchestrates the complete mesh building process for 3D glyphs.
    /// Combines contours, triangulation, extrusion, and UV generation.
    /// </summary>
    public static class GlyphMeshBuilder
    {
        private const float SCALE_FACTOR = 0.01f; // 1/100th scale

        /// <summary>
        /// Material slot mapping information
        /// </summary>
        public class MaterialSlotMap
        {
            public int KeyframeCount { get; private set; }

            public MaterialSlotMap(int keyframeCount)
            {
                KeyframeCount = Mathf.Max(1, keyframeCount);
            }

            public int TotalSlots => KeyframeCount;
            public int FaceSlot => 0;

            public int GetBandSlot(int bandIndex)
            {
                return bandIndex + 1;
            }

            public int BandCount => Mathf.Max(0, KeyframeCount - 1);

            public bool IsFaceSlot(int slotIndex) => slotIndex == FaceSlot;
            public bool IsBandSlot(int slotIndex) => slotIndex >= 1 && slotIndex < TotalSlots;

            public int GetBandIndexFromSlot(int slotIndex)
            {
                if (!IsBandSlot(slotIndex)) return -1;
                return slotIndex - 1;
            }
        }

        /// <summary>
        /// Settings for mesh building
        /// </summary>
        public class MeshBuildSettings
        {
            public GlyphExtrusionProcessor.ExtrusionProfile ExtrusionProfile;
            public Material[] Materials;
            public bool UseXAtlasUV;
            public int UVPadding;
            public int UVResolution;
            public float TexelsPerUnit;

            public MeshBuildSettings()
            {
                UseXAtlasUV = true;
                UVPadding = 4;
                UVResolution = 1024;
                TexelsPerUnit = 1.0f;
            }
        }

        /// <summary>
        /// Build a complete 3D mesh from boundaries
        /// </summary>
        /// <param name="boundaries">Contour boundaries (outer + holes)</param>
        /// <param name="settings">Mesh build settings</param>
        /// <returns>Complete Unity mesh</returns>
        public static Mesh BuildMesh(List<List<Vector2>> boundaries, MeshBuildSettings settings)
        {
            if (boundaries == null || boundaries.Count == 0)
                return null;

            var combinedMesh = new Mesh();
            var allVertices = new List<Vector3>();
            var allNormals = new List<Vector3>();
            var allUVs = new List<Vector2>();
            var submeshData = new Dictionary<Material, List<int>>();

            // Determine if we use curved extrusion
            bool useCurvedExtrusion = settings.ExtrusionProfile != null && settings.ExtrusionProfile.KeyframeCount > 0;

            if (useCurvedExtrusion)
            {
                GenerateCurvedMesh(boundaries, settings, allVertices, allNormals, allUVs, submeshData);
            }
            else
            {
                GenerateStraightMesh(boundaries, settings, allVertices, allNormals, allUVs, submeshData);
            }

            if (allVertices.Count == 0 || submeshData.Count == 0)
                return null;

            // Assign mesh data
            combinedMesh.vertices = allVertices.ToArray();
            combinedMesh.normals = allNormals.ToArray();
            combinedMesh.uv = allUVs.ToArray();

            // Build submeshes based on material slot map
            var slotMap = new MaterialSlotMap(settings.ExtrusionProfile != null ? settings.ExtrusionProfile.KeyframeCount : 1);
            var materials = settings.Materials ?? new Material[0];

            combinedMesh.subMeshCount = slotMap.TotalSlots;

            for (int slotIdx = 0; slotIdx < slotMap.TotalSlots; slotIdx++)
            {
                Material slotMat = slotIdx < materials.Length ? materials[slotIdx] : null;

                var tris = new int[0];
                if (slotMat != null && submeshData.ContainsKey(slotMat))
                {
                    tris = submeshData[slotMat].ToArray();
                }

                combinedMesh.SetTriangles(tris, slotIdx);
            }

            combinedMesh.RecalculateBounds();

            return combinedMesh;
        }

        #region Private Mesh Generation

        private static void GenerateStraightMesh(List<List<Vector2>> boundaries, MeshBuildSettings settings,
            List<Vector3> allVertices, List<Vector3> allNormals, List<Vector2> allUVs,
            Dictionary<Material, List<int>> submeshData)
        {
            var triangulation = GlyphTriangulator.Triangulate(boundaries);
            if (triangulation == null)
                return;

            int vertexOffset = allVertices.Count;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var vertexMap = new Dictionary<long, int>();

            // Only create front face vertices (no extrusion)
            foreach (var v in triangulation.SortedVertices)
            {
                long id = GlyphTriangulator.GetDeterministicVertexId(v.X, v.Y);
                if (!vertexMap.ContainsKey(id))
                {
                    vertexMap[id] = vertices.Count;
                    vertices.Add(new Vector3((float)v.X * SCALE_FACTOR, (float)v.Y * SCALE_FACTOR, 0f));
                }
            }

            // Front face triangles only
            foreach (var t in triangulation.TriMesh.Triangles)
            {
                var v0 = t.GetVertex(0);
                var v1 = t.GetVertex(1);
                var v2 = t.GetVertex(2);

                long id0 = GlyphTriangulator.GetDeterministicVertexId(v0.X, v0.Y);
                long id1 = GlyphTriangulator.GetDeterministicVertexId(v1.X, v1.Y);
                long id2 = GlyphTriangulator.GetDeterministicVertexId(v2.X, v2.Y);

                if (vertexMap.ContainsKey(id0) && vertexMap.ContainsKey(id1) && vertexMap.ContainsKey(id2))
                {
                    // Reversed winding for front face
                    triangles.Add(vertexMap[id2]);
                    triangles.Add(vertexMap[id1]);
                    triangles.Add(vertexMap[id0]);
                }
            }

            // All normals point backward (toward camera)
            var meshNormals = new Vector3[vertices.Count];
            for (int i = 0; i < vertices.Count; i++)
            {
                meshNormals[i] = Vector3.back;
            }

            // Simple planar UV projection
            var meshUVs = GeneratePlanarUVs(vertices, true);

            allVertices.AddRange(vertices);
            allNormals.AddRange(meshNormals);
            allUVs.AddRange(meshUVs);

            // Add triangles to face material (slot 0)
            var materials = settings.Materials ?? new Material[0];
            Material faceMat = materials.Length > 0 ? materials[0] : null;
            if (faceMat != null)
            {
                if (!submeshData.ContainsKey(faceMat))
                    submeshData[faceMat] = new List<int>();

                for (int i = 0; i < triangles.Count; i++)
                {
                    submeshData[faceMat].Add(triangles[i] + vertexOffset);
                }
            }
        }

        private static void GenerateCurvedMesh(List<List<Vector2>> boundaries, MeshBuildSettings settings,
            List<Vector3> allVertices, List<Vector3> allNormals, List<Vector2> allUVs,
            Dictionary<Material, List<int>> submeshData)
        {
            var triangulation = GlyphTriangulator.Triangulate(boundaries);
            if (triangulation == null)
                return;

            int vertexOffset = allVertices.Count;
            var vertices = new List<Vector3>();
            var localSubmeshData = new Dictionary<Material, List<int>>();

            // Build extrusion layers
            var layers = GlyphExtrusionProcessor.BuildExtrusionLayers(settings.ExtrusionProfile);
            if (layers.Count == 0)
                return;

            // Calculate boundary normals
            var normalsResult = GlyphExtrusionProcessor.CalculateBoundaryNormals(boundaries);

            // Calculate boundary centroids
            var boundaryCentroids = boundaries.Select(b => GlyphExtrusionProcessor.CalculateCentroid(b)).ToList();
            var boundaryIsHole = Enumerable.Range(0, boundaries.Count).Select(i => i > 0).ToList();

            // Create vertex maps for each layer
            var layerVertexMaps = new List<Dictionary<long, int>>();
            for (int layerIdx = 0; layerIdx < layers.Count; layerIdx++)
            {
                var layer = layers[layerIdx];
                var vertexMap = new Dictionary<long, int>();

                foreach (var v in triangulation.SortedVertices)
                {
                    long id = GlyphTriangulator.GetDeterministicVertexId(v.X, v.Y);
                    if (!vertexMap.ContainsKey(id))
                    {
                        Vector2 basePos = new Vector2((float)v.X, (float)v.Y);

                        // Apply perpendicular offset
                        Vector2 offsetPos = basePos;
                        if (normalsResult.NormalMap.TryGetValue(id, out Vector2 normal))
                        {
                            float offset = normalsResult.HoleVertices.Contains(id) ? -layer.curveOffset : layer.curveOffset;
                            offsetPos = basePos + normal * offset;
                        }

                        vertexMap[id] = vertices.Count;
                        vertices.Add(new Vector3(offsetPos.x * SCALE_FACTOR, offsetPos.y * SCALE_FACTOR, layer.depth * SCALE_FACTOR));
                    }
                }

                layerVertexMaps.Add(vertexMap);
            }

            // Create side vertex duplicates
            var layerSideVertexMaps = new List<Dictionary<long, int>>();
            for (int layerIdx = 0; layerIdx < layers.Count; layerIdx++)
            {
                var sideMap = new Dictionary<long, int>();
                foreach (var kv in layerVertexMaps[layerIdx])
                {
                    sideMap[kv.Key] = vertices.Count;
                    vertices.Add(vertices[kv.Value]);
                }
                layerSideVertexMaps.Add(sideMap);
            }

            // Build slot map
            var slotMap = new MaterialSlotMap(layers.Count);
            var materials = settings.Materials ?? new Material[0];
            Material faceMat = slotMap.FaceSlot < materials.Length ? materials[slotMap.FaceSlot] : null;

            // Front face triangles
            if (faceMat != null)
            {
                if (!localSubmeshData.ContainsKey(faceMat))
                    localSubmeshData[faceMat] = new List<int>();

                foreach (var t in triangulation.TriMesh.Triangles)
                {
                    var v0 = t.GetVertex(0);
                    var v1 = t.GetVertex(1);
                    var v2 = t.GetVertex(2);

                    long id0 = GlyphTriangulator.GetDeterministicVertexId(v0.X, v0.Y);
                    long id1 = GlyphTriangulator.GetDeterministicVertexId(v1.X, v1.Y);
                    long id2 = GlyphTriangulator.GetDeterministicVertexId(v2.X, v2.Y);

                    var frontMap = layerVertexMaps[0];
                    if (frontMap.ContainsKey(id0) && frontMap.ContainsKey(id1) && frontMap.ContainsKey(id2))
                    {
                        localSubmeshData[faceMat].Add(frontMap[id2]);
                        localSubmeshData[faceMat].Add(frontMap[id1]);
                        localSubmeshData[faceMat].Add(frontMap[id0]);
                    }
                }

                // Back face triangles
                foreach (var t in triangulation.TriMesh.Triangles)
                {
                    var v0 = t.GetVertex(0);
                    var v1 = t.GetVertex(1);
                    var v2 = t.GetVertex(2);

                    long id0 = GlyphTriangulator.GetDeterministicVertexId(v0.X, v0.Y);
                    long id1 = GlyphTriangulator.GetDeterministicVertexId(v1.X, v1.Y);
                    long id2 = GlyphTriangulator.GetDeterministicVertexId(v2.X, v2.Y);

                    var backMap = layerVertexMaps[layerVertexMaps.Count - 1];
                    if (backMap.ContainsKey(id0) && backMap.ContainsKey(id1) && backMap.ContainsKey(id2))
                    {
                        localSubmeshData[faceMat].Add(backMap[id0]);
                        localSubmeshData[faceMat].Add(backMap[id1]);
                        localSubmeshData[faceMat].Add(backMap[id2]);
                    }
                }
            }

            // Initialize normals
            var meshNormals = new Vector3[vertices.Count];
            for (int i = 0; i < vertices.Count; i++)
            {
                if (Mathf.Approximately(vertices[i].z, 0f))
                    meshNormals[i] = Vector3.back;
                else if (Mathf.Approximately(vertices[i].z, settings.ExtrusionProfile.extrusionDepth * SCALE_FACTOR))
                    meshNormals[i] = Vector3.forward;
                else
                    meshNormals[i] = Vector3.zero;
            }

            // Side faces
            for (int b = 0; b < boundaries.Count; b++)
            {
                var boundary = boundaries[b];
                int n = boundary.Count;
                bool isHole = boundaryIsHole[b];
                Vector2 centroid = boundaryCentroids[b];

                for (int i = 0; i < n; i++)
                {
                    Vector2 p0 = boundary[i];
                    Vector2 p1 = boundary[(i + 1) % n];

                    for (int layerIdx = 0; layerIdx < layers.Count - 1; layerIdx++)
                    {
                        var currSideMap = layerSideVertexMaps[layerIdx];
                        var nextSideMap = layerSideVertexMaps[layerIdx + 1];

                        Vector2 offsetP0 = p0;
                        Vector2 offsetP1 = p1;

                        long baseId0 = GlyphTriangulator.GetDeterministicVertexId(p0.x, p0.y);
                        long baseId1 = GlyphTriangulator.GetDeterministicVertexId(p1.x, p1.y);

                        if (normalsResult.NormalMap.TryGetValue(baseId0, out Vector2 normal0))
                        {
                            float offset0 = normalsResult.HoleVertices.Contains(baseId0) ? -layers[layerIdx].curveOffset : layers[layerIdx].curveOffset;
                            offsetP0 = p0 + normal0 * offset0;
                        }
                        if (normalsResult.NormalMap.TryGetValue(baseId1, out Vector2 normal1))
                        {
                            float offset1 = normalsResult.HoleVertices.Contains(baseId1) ? -layers[layerIdx].curveOffset : layers[layerIdx].curveOffset;
                            offsetP1 = p1 + normal1 * offset1;
                        }

                        long triNetId0 = GlyphExtrusionProcessor.FindTriNetIdForPosition(layerVertexMaps[layerIdx], vertices,
                            offsetP0 * SCALE_FACTOR, layers[layerIdx].depth * SCALE_FACTOR, SCALE_FACTOR);
                        long triNetId1 = GlyphExtrusionProcessor.FindTriNetIdForPosition(layerVertexMaps[layerIdx], vertices,
                            offsetP1 * SCALE_FACTOR, layers[layerIdx].depth * SCALE_FACTOR, SCALE_FACTOR);

                        if (triNetId0 < 0 || triNetId1 < 0) continue;

                        int curr0 = currSideMap[triNetId0];
                        int curr1 = currSideMap[triNetId1];
                        int next0 = nextSideMap[triNetId0];
                        int next1 = nextSideMap[triNetId1];

                        int bandSlotIndex = slotMap.GetBandSlot(layerIdx);
                        Material layerMat = bandSlotIndex < materials.Length ? materials[bandSlotIndex] : faceMat;

                        if (layerMat != null)
                        {
                            if (!localSubmeshData.ContainsKey(layerMat))
                                localSubmeshData[layerMat] = new List<int>();

                            if (isHole)
                            {
                                localSubmeshData[layerMat].Add(curr0);
                                localSubmeshData[layerMat].Add(next1);
                                localSubmeshData[layerMat].Add(curr1);
                                localSubmeshData[layerMat].Add(curr0);
                                localSubmeshData[layerMat].Add(next0);
                                localSubmeshData[layerMat].Add(next1);
                            }
                            else
                            {
                                localSubmeshData[layerMat].Add(curr0);
                                localSubmeshData[layerMat].Add(curr1);
                                localSubmeshData[layerMat].Add(next1);
                                localSubmeshData[layerMat].Add(curr0);
                                localSubmeshData[layerMat].Add(next1);
                                localSubmeshData[layerMat].Add(next0);
                            }

                            Vector3 edgeNormal = GlyphExtrusionProcessor.CalculateEdgeNormal(p0, p1, centroid, isHole);
                            meshNormals[curr0] = edgeNormal;
                            meshNormals[curr1] = edgeNormal;
                            meshNormals[next0] = edgeNormal;
                            meshNormals[next1] = edgeNormal;
                        }
                    }
                }
            }

            // Generate UVs (placeholder - will be enhanced with XAtlas)
            var meshUVs = GeneratePlanarUVs(vertices, true);

            allVertices.AddRange(vertices);
            allNormals.AddRange(meshNormals);
            allUVs.AddRange(meshUVs);

            foreach (var kvp in localSubmeshData)
            {
                if (!submeshData.ContainsKey(kvp.Key))
                    submeshData[kvp.Key] = new List<int>();

                for (int i = 0; i < kvp.Value.Count; i++)
                {
                    submeshData[kvp.Key].Add(kvp.Value[i] + vertexOffset);
                }
            }
        }

        private static List<Vector2> GeneratePlanarUVs(List<Vector3> vertices, bool isFrontFace)
        {
            var uvs = new List<Vector2>(vertices.Count);

            if (vertices.Count == 0)
                return uvs;

            // Calculate bounds in XY plane (ignore Z for UV mapping)
            Vector3 min = vertices[0];
            Vector3 max = vertices[0];

            foreach (var v in vertices)
            {
                min.x = Mathf.Min(min.x, v.x);
                min.y = Mathf.Min(min.y, v.y);
                min.z = Mathf.Min(min.z, v.z);
                max.x = Mathf.Max(max.x, v.x);
                max.y = Mathf.Max(max.y, v.y);
                max.z = Mathf.Max(max.z, v.z);
            }

            float width = max.x - min.x;
            float height = max.y - min.y;

            if (width < 0.0001f) width = 1f;
            if (height < 0.0001f) height = 1f;

            // Determine if this is a face or side based on Z position
            bool isFace = Mathf.Approximately(vertices[0].z, min.z) || Mathf.Approximately(vertices[0].z, max.z);

            foreach (var v in vertices)
            {
                float u, vCoord;

                if (isFace)
                {
                    // Front and back faces: use full UV space (0-1) for proper texture mapping
                    float normalizedX = (v.x - min.x) / width;
                    float normalizedY = (v.y - min.y) / height;
                    u = normalizedX;
                    vCoord = normalizedY;
                }
                else
                {
                    // Side faces: use position-based UV mapping
                    // U coordinate based on position around perimeter
                    // V coordinate based on depth (Z)
                    float depthT = (v.z - min.z) / Mathf.Max(max.z - min.z, 0.0001f);

                    // Calculate perimeter position for U coordinate
                    float perimeterU = (v.x - min.x) / width;

                    u = perimeterU;
                    vCoord = depthT;
                }

                uvs.Add(new Vector2(u, vCoord));
            }

            return uvs;
        }

        #endregion
    }
}
