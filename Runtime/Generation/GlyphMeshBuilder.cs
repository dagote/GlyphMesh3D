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
            // If extrusion depth is too shallow (< 0.1), disable extrusion and back face
            float extrusionDepth = settings.ExtrusionProfile?.extrusionDepth ?? 0f;
            bool useCurvedExtrusion = settings.ExtrusionProfile != null &&
                                     settings.ExtrusionProfile.KeyframeCount > 0 &&
                                     extrusionDepth >= 0.1f;

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

            // Correct triangle winding orders to ensure outward-facing normals
            CorrectTriangleWindingOrders(allVertices, submeshData, settings.ExtrusionProfile?.extrusionDepth ?? 0f);

            // Re-assign corrected triangles to submeshes
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

        /// <summary>
        /// Corrects triangle winding orders to ensure consistent outward-facing normals
        /// Only applies to front and back caps - extrusion faces are already correctly oriented
        /// </summary>
        private static void CorrectTriangleWindingOrders(List<Vector3> vertices, Dictionary<Material, List<int>> submeshData, float extrusionDepth)
        {
            float frontZ = 0f;
            float backZ = extrusionDepth * SCALE_FACTOR;
            float zTolerance = 0.001f;

            foreach (var kvp in submeshData)
            {
                var triangles = kvp.Value;

                for (int i = 0; i < triangles.Count; i += 3)
                {
                    int idx0 = triangles[i];
                    int idx1 = triangles[i + 1];
                    int idx2 = triangles[i + 2];

                    Vector3 v0 = vertices[idx0];
                    Vector3 v1 = vertices[idx1];
                    Vector3 v2 = vertices[idx2];

                    // Check if all vertices are at the same Z level (cap face)
                    float minZ = Mathf.Min(v0.z, Mathf.Min(v1.z, v2.z));
                    float maxZ = Mathf.Max(v0.z, Mathf.Max(v1.z, v2.z));
                    float zRange = maxZ - minZ;

                    // Skip extrusion faces (vertices at different Z levels)
                    // Only correct winding for front/back caps (all vertices at same Z)
                    if (zRange > zTolerance)
                        continue;

                    // Calculate face normal
                    Vector3 edge1 = v1 - v0;
                    Vector3 edge2 = v2 - v0;
                    Vector3 faceNormal = Vector3.Cross(edge1, edge2).normalized;

                    // Determine triangle type by Z position
                    float avgZ = (v0.z + v1.z + v2.z) / 3f;
                    bool needsFlip = false;

                    if (Mathf.Abs(avgZ - frontZ) < zTolerance)
                    {
                        // Front cap - normal should point -Z (outward from solid)
                        if (faceNormal.z > 0) needsFlip = true;
                    }
                    else if (Mathf.Abs(avgZ - backZ) < zTolerance)
                    {
                        // Back cap - normal should point +Z (outward from solid)
                        if (faceNormal.z < 0) needsFlip = true;
                    }
                    // Note: Extrusion faces are skipped above and not corrected

                    // Flip winding order if needed
                    if (needsFlip)
                    {
                        triangles[i + 1] = idx2;
                        triangles[i + 2] = idx1;
                    }
                }
            }
        }

        #region Private Mesh Generation

        /// <summary>
        /// Tracks vertex positions and their associated normals to ensure consistency
        /// </summary>
        private class VertexNormalTracker
        {
            private Dictionary<Vector3, Vector3> positionToNormal = new Dictionary<Vector3, Vector3>();
            private const float POSITION_EPSILON = 0.0001f;

            /// <summary>
            /// Try to add a vertex normal at a position, checking for consistency
            /// </summary>
            public bool TryAddVertex(Vector3 position, Vector3 normal)
            {
                Vector3 key = QuantizePosition(position);

                if (positionToNormal.TryGetValue(key, out Vector3 existingNormal))
                {
                    // Check if normals are in same hemisphere (dot product > 0)
                    return Vector3.Dot(normal, existingNormal) > 0;
                }

                positionToNormal[key] = normal;
                return true;
            }

            /// <summary>
            /// Check if a triangle's normal is consistent with existing vertices at those positions
            /// </summary>
            public bool IsTriangleConsistent(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 faceNormal)
            {
                Vector3 k0 = QuantizePosition(v0);
                Vector3 k1 = QuantizePosition(v1);
                Vector3 k2 = QuantizePosition(v2);

                // Check each vertex - if it exists, its normal must be in same hemisphere as face normal
                if (positionToNormal.TryGetValue(k0, out Vector3 n0) && Vector3.Dot(faceNormal, n0) < 0)
                    return false;
                if (positionToNormal.TryGetValue(k1, out Vector3 n1) && Vector3.Dot(faceNormal, n1) < 0)
                    return false;
                if (positionToNormal.TryGetValue(k2, out Vector3 n2) && Vector3.Dot(faceNormal, n2) < 0)
                    return false;

                return true;
            }

            /// <summary>
            /// Add triangle vertices with their face normal
            /// </summary>
            public void AddTriangle(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 faceNormal)
            {
                TryAddVertex(v0, faceNormal);
                TryAddVertex(v1, faceNormal);
                TryAddVertex(v2, faceNormal);
            }

            private Vector3 QuantizePosition(Vector3 pos)
            {
                return new Vector3(
                    Mathf.Round(pos.x / POSITION_EPSILON) * POSITION_EPSILON,
                    Mathf.Round(pos.y / POSITION_EPSILON) * POSITION_EPSILON,
                    Mathf.Round(pos.z / POSITION_EPSILON) * POSITION_EPSILON
                );
            }
        }

        /// <summary>
        /// Calculate face normal from three vertices using right-hand rule
        /// </summary>
        private static Vector3 CalculateFaceNormal(Vector3 v0, Vector3 v1, Vector3 v2)
        {
            Vector3 edge1 = v1 - v0;
            Vector3 edge2 = v2 - v0;
            Vector3 normal = Vector3.Cross(edge1, edge2);
            return normal.magnitude > 0.0001f ? normal.normalized : Vector3.zero;
        }

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
            var tracker = new VertexNormalTracker();

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

            // Front face triangles - use triangulation winding as-is (0, 1, 2)
            // This ensures normals point toward +Z (away from solid volume)
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
                    int idx0 = vertexMap[id0];
                    int idx1 = vertexMap[id1];
                    int idx2 = vertexMap[id2];

                    // Front cap: use winding as-is (0, 1, 2)
                    triangles.Add(idx0);
                    triangles.Add(idx1);
                    triangles.Add(idx2);

                    // Track for coincident vertex validation
                    Vector3 faceNormal = CalculateFaceNormal(vertices[idx0], vertices[idx1], vertices[idx2]);
                    tracker.AddTriangle(vertices[idx0], vertices[idx1], vertices[idx2], faceNormal);
                }
            }

            // Calculate normals from face geometry (all point +Z for planar front cap)
            var meshNormals = new Vector3[vertices.Count];
            for (int i = 0; i < vertices.Count; i++)
            {
                meshNormals[i] = Vector3.forward;
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
            var tracker = new VertexNormalTracker();

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

            // Create additional duplicate vertices for the last band's starting layer
            // This prevents the last band (Q3) from sharing vertices with the second-to-last band (Q2)
            // Without this, the boundary between Q2 and Q3 bands would have mixed quadrant UVs
            Dictionary<long, int> lastBandStartMap = null;
            if (layers.Count > 2) // Only needed when there are 2+ bands (3+ layers)
            {
                int secondToLastLayerIdx = layers.Count - 2;
                lastBandStartMap = new Dictionary<long, int>();
                foreach (var kv in layerVertexMaps[secondToLastLayerIdx])
                {
                    lastBandStartMap[kv.Key] = vertices.Count;
                    vertices.Add(vertices[kv.Value]);
                }
            }

            // Build slot map
            var slotMap = new MaterialSlotMap(layers.Count);
            var materials = settings.Materials ?? new Material[0];
            Material faceMat = slotMap.FaceSlot < materials.Length ? materials[slotMap.FaceSlot] : null;

            // Front and back face triangles
            if (faceMat != null)
            {
                if (!localSubmeshData.ContainsKey(faceMat))
                    localSubmeshData[faceMat] = new List<int>();

                // Front cap triangles - use winding as-is (0, 1, 2) for +Z normals
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
                        int idx0 = frontMap[id0];
                        int idx1 = frontMap[id1];
                        int idx2 = frontMap[id2];

                        // Front cap: use winding as-is (0, 1, 2)
                        localSubmeshData[faceMat].Add(idx0);
                        localSubmeshData[faceMat].Add(idx1);
                        localSubmeshData[faceMat].Add(idx2);

                        // Track for coincident vertex validation
                        Vector3 faceNormal = CalculateFaceNormal(vertices[idx0], vertices[idx1], vertices[idx2]);
                        tracker.AddTriangle(vertices[idx0], vertices[idx1], vertices[idx2], faceNormal);
                    }
                }

                // Back cap triangles - reverse winding (0, 2, 1) for -Z normals
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
                        int idx0 = backMap[id0];
                        int idx1 = backMap[id1];
                        int idx2 = backMap[id2];

                        // Back cap: reverse winding (0, 2, 1)
                        localSubmeshData[faceMat].Add(idx0);
                        localSubmeshData[faceMat].Add(idx2);  // Swapped!
                        localSubmeshData[faceMat].Add(idx1);  // Swapped!

                        // Track for coincident vertex validation
                        Vector3 faceNormal = CalculateFaceNormal(vertices[idx0], vertices[idx2], vertices[idx1]);
                        tracker.AddTriangle(vertices[idx0], vertices[idx2], vertices[idx1], faceNormal);
                    }
                }
            }

            // Track vertex types for UV mapping
            int totalLayerVertices = layerVertexMaps.Sum(m => m.Count);
            int firstSideVertexIndex = totalLayerVertices;

            // Initialize normals with correct directions
            var meshNormals = new Vector3[vertices.Count];
            for (int i = 0; i < vertices.Count; i++)
            {
                if (Mathf.Approximately(vertices[i].z, 0f))
                    meshNormals[i] = Vector3.forward;  // Front faces: +Z (toward camera)
                else if (Mathf.Approximately(vertices[i].z, settings.ExtrusionProfile.extrusionDepth * SCALE_FACTOR))
                    meshNormals[i] = Vector3.back;     // Back faces: -Z (away from camera)
                else
                    meshNormals[i] = Vector3.zero;     // Side faces: set later, do not modify
            }

            // Initialize UVs for all vertices
            var meshUVs = new List<Vector2>(new Vector2[vertices.Count]);

            // Generate face UVs for layer vertices (q1 for front, q4 for back)
            GenerateFaceUVs(vertices, layerVertexMaps, meshUVs, settings.UVResolution);

            // Side faces with stepped UV unwrapping
            // Calculate total number of extrusion bands
            int totalBands = layers.Count - 1;

            for (int b = 0; b < boundaries.Count; b++)
            {
                var boundary = boundaries[b];
                int n = boundary.Count;
                bool isHole = boundaryIsHole[b];
                Vector2 centroid = boundaryCentroids[b];

                // Calculate perimeter for UV unwrapping
                float totalPerimeter = 0f;
                for (int i = 0; i < n; i++)
                {
                    Vector2 p0 = boundary[i];
                    Vector2 p1 = boundary[(i + 1) % n];
                    totalPerimeter += Vector2.Distance(p0, p1);
                }

                float cumulativeDistance = 0f;

                for (int i = 0; i < n; i++)
                {
                    Vector2 p0 = boundary[i];
                    Vector2 p1 = boundary[(i + 1) % n];
                    float edgeLength = Vector2.Distance(p0, p1);

                    for (int layerIdx = 0; layerIdx < totalBands; layerIdx++)
                    {
                        // Use special duplicate vertices for the last band to prevent quadrant mixing
                        bool isLastBand = (layerIdx == totalBands - 1);
                        var currSideMap = (isLastBand && lastBandStartMap != null) ? lastBandStartMap : layerSideVertexMaps[layerIdx];
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

                        // Safety check to prevent crashes from invalid vertex indices
                        // With improved tolerance in FindTriNetIdForPosition, this should rarely trigger
                        if (triNetId0 < 0 || triNetId1 < 0 || !currSideMap.ContainsKey(triNetId0) ||
                            !currSideMap.ContainsKey(triNetId1) || !nextSideMap.ContainsKey(triNetId0) ||
                            !nextSideMap.ContainsKey(triNetId1))
                            continue;

                        int curr0 = currSideMap[triNetId0];
                        int curr1 = currSideMap[triNetId1];
                        int next0 = nextSideMap[triNetId0];
                        int next1 = nextSideMap[triNetId1];

                        // Determine which quadrant this band should use
                        int bandQuadrant = GetBandQuadrant(layerIdx, totalBands);

                        // Debug: Log quadrant assignment
                        if (b == 0 && i == 0) // Only log once per band to avoid spam
                        {
                            Debug.Log($"Band {layerIdx}/{totalBands}: Quadrant {bandQuadrant}, Layer depths: {layers[layerIdx].depth} -> {layers[layerIdx + 1].depth}");
                        }

                        // Map to full quadrant area
                        // U varies (0 to 1) to wrap around the perimeter
                        // V varies (0 to 1) from current layer to next layer
                        // This makes the texture cover the entire quadrant instead of a thin strip
                        float uStart = cumulativeDistance / totalPerimeter;
                        float uEnd = (cumulativeDistance + edgeLength) / totalPerimeter;

                        // V=0 at current layer, V=1 at next layer (varies across depth)
                        meshUVs[curr0] = MapToQuadrant(uStart, 0f, bandQuadrant, settings.UVResolution);
                        meshUVs[curr1] = MapToQuadrant(uEnd, 0f, bandQuadrant, settings.UVResolution);
                        meshUVs[next0] = MapToQuadrant(uStart, 1f, bandQuadrant, settings.UVResolution);
                        meshUVs[next1] = MapToQuadrant(uEnd, 1f, bandQuadrant, settings.UVResolution);

                        int bandSlotIndex = slotMap.GetBandSlot(layerIdx);
                        Material layerMat = bandSlotIndex < materials.Length ? materials[bandSlotIndex] : faceMat;

                        if (layerMat != null)
                        {
                            if (!localSubmeshData.ContainsKey(layerMat))
                                localSubmeshData[layerMat] = new List<int>();

                            // Calculate edge normal pointing outward
                            Vector3 edgeNormal = GlyphExtrusionProcessor.CalculateEdgeNormal(p0, p1, centroid, isHole);

                            if (isHole)
                            {
                                // Hole winding - normals point into the hole (outward from solid)
                                localSubmeshData[layerMat].Add(curr0);
                                localSubmeshData[layerMat].Add(next1);
                                localSubmeshData[layerMat].Add(curr1);
                                localSubmeshData[layerMat].Add(curr0);
                                localSubmeshData[layerMat].Add(next0);
                                localSubmeshData[layerMat].Add(next1);

                                // Track for validation
                                tracker.AddTriangle(vertices[curr0], vertices[next1], vertices[curr1], edgeNormal);
                                tracker.AddTriangle(vertices[curr0], vertices[next0], vertices[next1], edgeNormal);
                            }
                            else
                            {
                                // Outer boundary winding - normals point away from solid
                                localSubmeshData[layerMat].Add(curr0);
                                localSubmeshData[layerMat].Add(curr1);
                                localSubmeshData[layerMat].Add(next1);
                                localSubmeshData[layerMat].Add(curr0);
                                localSubmeshData[layerMat].Add(next1);
                                localSubmeshData[layerMat].Add(next0);

                                // Track for validation
                                tracker.AddTriangle(vertices[curr0], vertices[curr1], vertices[next1], edgeNormal);
                                tracker.AddTriangle(vertices[curr0], vertices[next1], vertices[next0], edgeNormal);
                            }

                            // Set vertex normals
                            meshNormals[curr0] = edgeNormal;
                            meshNormals[curr1] = edgeNormal;
                            meshNormals[next0] = edgeNormal;
                            meshNormals[next1] = edgeNormal;
                        }
                    }

                    cumulativeDistance += edgeLength;
                }
            }

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

        /// <summary>
        /// Map normalized UV coordinates (0-1) to a specific quadrant with 1-pixel padding inset
        /// q1: top-left (U: 0-0.5, V: 0.5-1)
        /// q2: bottom-left (U: 0-0.5, V: 0-0.5) - LOOPING bands
        /// q3: top-right (U: 0.5-1, V: 0.5-1) - FINAL band before back cap
        /// q4: bottom-right (U: 0.5-1, V: 0-0.5)
        /// </summary>
        private static Vector2 MapToQuadrant(float u, float v, int quadrant, float uvResolution = 1024f)
        {
            // Calculate 1 pixel in UV space
            float pixelSize = 1.0f / uvResolution;

            // Scale and offset to account for 1-pixel padding on each side
            // Each quadrant is 0.5 wide/tall
            // After padding on both sides, usable space is (0.5 - 2*pixelSize)
            float usableSize = 0.5f - 2.0f * pixelSize;
            float paddedU = pixelSize + u * usableSize;
            float paddedV = pixelSize + v * usableSize;

            switch (quadrant)
            {
                case 1: // top-left
                    return new Vector2(paddedU, 0.5f + paddedV);
                case 2: // LOOPING
                    return new Vector2(0.5f + paddedU, 0.5f + paddedV);
                case 3: // FINAL
                    return new Vector2(paddedU, paddedV);
                case 4: // bottom-right
                    return new Vector2(0.5f + paddedU, paddedV);
                default:
                    return new Vector2(u, v);
            }
        }

        /// <summary>
        /// Determine which quadrant to use for a specific extrusion band
        /// Pattern:
        /// - 0 bands: none (only caps)
        /// - 1 band: q3 (final before back cap)
        /// - 2 bands: q2, q3
        /// - 3+ bands: q2, q2, ..., q3
        /// Q2 loops for all middle bands, Q3 is the final band before the back cap
        /// </summary>
        private static int GetBandQuadrant(int bandIndex, int totalBands)
        {
            if (totalBands == 0) return 2; // Shouldn't happen, default to q2
            if (totalBands == 1) return 3; // Single band uses q3 (final)
            // Multiple bands: last uses q3 (final), all others use q2 (looping)
            if (bandIndex == totalBands - 1) return 3;
            return 2;
        }

        /// <summary>
        /// Calculate the V range (0-1) for a band within its assigned quadrant
        /// All bands use the full height of their quadrant (typically for solid colors)
        /// </summary>
        private static void GetBandVRange(int bandIndex, int totalBands, out float vMin, out float vMax)
        {
            // All bands always use the full quadrant height
            // Multiple bands in the same quadrant will show the same texture
            vMin = 0f;
            vMax = 1f;
        }

        /// <summary>
        /// Generate UVs for face vertices (front/back caps) in appropriate quadrants
        /// Front cap: q1 (top-left), Back cap: q4 (bottom-right)
        /// </summary>
        private static void GenerateFaceUVs(List<Vector3> vertices, List<Dictionary<long, int>> layerVertexMaps, List<Vector2> uvs, float uvResolution)
        {
            if (layerVertexMaps.Count == 0) return;

            // Calculate bounds for all layer vertices
            var allLayerIndices = new List<int>();
            foreach (var layer in layerVertexMaps)
            {
                allLayerIndices.AddRange(layer.Values);
            }

            if (allLayerIndices.Count == 0) return;

            Vector3 min = vertices[allLayerIndices[0]];
            Vector3 max = vertices[allLayerIndices[0]];

            foreach (int idx in allLayerIndices)
            {
                min.x = Mathf.Min(min.x, vertices[idx].x);
                min.y = Mathf.Min(min.y, vertices[idx].y);
                max.x = Mathf.Max(max.x, vertices[idx].x);
                max.y = Mathf.Max(max.y, vertices[idx].y);
            }

            float width = max.x - min.x;
            float height = max.y - min.y;

            if (width < 0.0001f) width = 1f;
            if (height < 0.0001f) height = 1f;

            // Map front cap to q1, back cap to q4
            int frontLayerIdx = 0;
            int backLayerIdx = layerVertexMaps.Count - 1;

            for (int layerIdx = 0; layerIdx < layerVertexMaps.Count; layerIdx++)
            {
                int quadrant = (layerIdx == frontLayerIdx) ? 1 : 4; // q1 for front, q4 for back

                foreach (int idx in layerVertexMaps[layerIdx].Values)
                {
                    float normalizedX = (vertices[idx].x - min.x) / width;
                    float normalizedY = (vertices[idx].y - min.y) / height;
                    uvs[idx] = MapToQuadrant(normalizedX, normalizedY, quadrant, uvResolution);
                }
            }
        }

        private static List<Vector2> GeneratePlanarUVs(List<Vector3> vertices, bool isFrontFace)
        {
            var uvs = new List<Vector2>(vertices.Count);

            if (vertices.Count == 0)
                return uvs;

            Vector3 min = vertices[0];
            Vector3 max = vertices[0];

            foreach (var v in vertices)
            {
                min.x = Mathf.Min(min.x, v.x);
                min.y = Mathf.Min(min.y, v.y);
                max.x = Mathf.Max(max.x, v.x);
                max.y = Mathf.Max(max.y, v.y);
            }

            float width = max.x - min.x;
            float height = max.y - min.y;

            if (width < 0.0001f) width = 1f;
            if (height < 0.0001f) height = 1f;

            // Map to full quadrant 1 (0-1, 0-1)
            foreach (var v in vertices)
            {
                float normalizedX = (v.x - min.x) / width;
                float normalizedY = (v.y - min.y) / height;
                uvs.Add(new Vector2(normalizedX, normalizedY));
            }

            return uvs;
        }

        #endregion
    }
}
