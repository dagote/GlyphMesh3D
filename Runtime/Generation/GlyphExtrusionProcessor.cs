using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LanternPines.GlyphMesh3D.Generation
{
    /// <summary>
    /// Handles extrusion processing for 3D glyph mesh generation.
    /// Computes extrusion layers, boundary normals, and side face geometry.
    /// </summary>
    public static class GlyphExtrusionProcessor
    {
        /// <summary>
        /// Represents a single extrusion layer at a specific depth with curve offset
        /// </summary>
        public class ExtrusionLayer
        {
            public float depth;
            public float curveOffset;

            public ExtrusionLayer(float depth, float curveOffset)
            {
                this.depth = depth;
                this.curveOffset = curveOffset;
            }
        }

        /// <summary>
        /// Settings for extrusion profile
        /// </summary>
        public class ExtrusionProfile
        {
            public float extrusionDepth;
            public float extrusionWidth;
            public AnimationCurve curve;

            public ExtrusionProfile(float depth, float width, AnimationCurve profileCurve)
            {
                extrusionDepth = depth;
                extrusionWidth = width;
                curve = profileCurve ?? AnimationCurve.Linear(0f, 0f, 1f, 1f);
            }

            public int KeyframeCount => curve.keys.Length;

            public float GetKeyframeTime(int index)
            {
                if (index < 0 || index >= curve.keys.Length) return 0f;
                return curve.keys[index].time;
            }

            public float GetOffset(float depthT)
            {
                return curve.Evaluate(Mathf.Clamp01(depthT));
            }
        }

        /// <summary>
        /// Result of boundary normal calculation
        /// </summary>
        public class BoundaryNormalsResult
        {
            public Dictionary<long, Vector2> NormalMap;
            public HashSet<long> HoleVertices;

            public BoundaryNormalsResult()
            {
                NormalMap = new Dictionary<long, Vector2>();
                HoleVertices = new HashSet<long>();
            }
        }

        /// <summary>
        /// Build extrusion layers from keyframe profile
        /// </summary>
        /// <param name="profile">Extrusion profile settings</param>
        /// <returns>List of extrusion layers</returns>
        public static List<ExtrusionLayer> BuildExtrusionLayers(ExtrusionProfile profile)
        {
            var layers = new List<ExtrusionLayer>();

            if (profile == null || profile.KeyframeCount == 0)
                return layers;

            for (int i = 0; i < profile.KeyframeCount; i++)
            {
                // Use the keyframe's time (X-axis, 0-1) as the normalized depth position
                float normalizedDepth = profile.GetKeyframeTime(i);
                float depth = normalizedDepth * profile.extrusionDepth;
                float curveValue = profile.GetOffset(normalizedDepth);
                // Apply extrusion width and cube the power for more dramatic effect
                float curveOffset = curveValue * curveValue * curveValue * profile.extrusionWidth;

                layers.Add(new ExtrusionLayer(depth, curveOffset));
            }

            return layers;
        }

        /// <summary>
        /// Calculate boundary normals for perpendicular offset during extrusion
        /// </summary>
        /// <param name="boundaries">List of boundaries (first is outer, rest are holes)</param>
        /// <returns>Result containing normal map and hole vertex set</returns>
        public static BoundaryNormalsResult CalculateBoundaryNormals(List<List<Vector2>> boundaries)
        {
            var result = new BoundaryNormalsResult();

            for (int b = 0; b < boundaries.Count; b++)
            {
                var boundary = boundaries[b];
                int n = boundary.Count;
                bool isHole = b > 0;

                // Calculate centroid for outward direction validation
                Vector2 centroid = CalculateCentroid(boundary);

                for (int i = 0; i < n; i++)
                {
                    Vector2 p = boundary[i];
                    Vector2 prev = boundary[(i - 1 + n) % n];
                    Vector2 next = boundary[(i + 1) % n];

                    // Calculate edge vectors
                    Vector2 edge1 = (p - prev).normalized;
                    Vector2 edge2 = (next - p).normalized;

                    // Perpendicular to edges (rotate 90 degrees)
                    Vector2 normal1 = new Vector2(-edge1.y, edge1.x);
                    Vector2 normal2 = new Vector2(-edge2.y, edge2.x);

                    // Average the normals
                    Vector2 avgNormal = (normal1 + normal2);

                    // Normalize if non-zero
                    if (avgNormal.magnitude > 0.0001f)
                    {
                        avgNormal = avgNormal.normalized;
                    }
                    else
                    {
                        // Edges are opposite directions (sharp corner) - use perpendicular to first edge
                        avgNormal = normal1;
                    }

                    // Ensure normal points outward from centroid
                    Vector2 toCenter = centroid - p;
                    float dotProduct = Vector2.Dot(avgNormal, toCenter);

                    // For outer boundaries, normal should point away from center (dot < 0)
                    // For holes, normal should point toward center (dot > 0)
                    if (!isHole && dotProduct > 0)
                    {
                        avgNormal = -avgNormal;
                    }
                    else if (isHole && dotProduct < 0)
                    {
                        avgNormal = -avgNormal;
                    }

                    long id = GlyphTriangulator.GetDeterministicVertexId(p.x, p.y);
                    result.NormalMap[id] = avgNormal;

                    // Track hole vertices
                    if (isHole)
                    {
                        result.HoleVertices.Add(id);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Create offset boundary with constrained expansion (vertices stop at self-intersections)
        /// </summary>
        public static List<Vector2> CreateOffsetBoundary(List<Vector2> boundary, Dictionary<long, Vector2> normalMap, float offsetDistance, bool isHole)
        {
            int n = boundary.Count;
            var offsetPoints = new List<Vector2>(n);

            // For each vertex, offset with collision detection
            for (int i = 0; i < n; i++)
            {
                Vector2 p = boundary[i];
                long id = GlyphTriangulator.GetDeterministicVertexId(p.x, p.y);

                Vector2 targetPos = p;

                if (normalMap.TryGetValue(id, out Vector2 normal))
                {
                    float offset = isHole ? -offsetDistance : offsetDistance;
                    targetPos = p + normal * offset;

                    // Check if movement path would intersect any edge of the same boundary
                    float maxOffset = offset;
                    float closestIntersection = 1.0f; // Normalized distance along ray (0=start, 1=target)

                    for (int j = 0; j < n; j++)
                    {
                        // Skip edges adjacent to current vertex
                        if (j == i || j == (i - 1 + n) % n) continue;

                        Vector2 edgeStart = boundary[j];
                        Vector2 edgeEnd = boundary[(j + 1) % n];

                        // Check if ray from p to targetPos intersects edge
                        if (RayIntersectsSegment(p, targetPos, edgeStart, edgeEnd, out float t))
                        {
                            if (t < closestIntersection)
                            {
                                closestIntersection = t;
                            }
                        }
                    }

                    // Clamp offset to just before intersection point
                    if (closestIntersection < 1.0f)
                    {
                        // Stop slightly before intersection to avoid exact overlap
                        closestIntersection = Mathf.Max(0, closestIntersection - 0.01f);
                        targetPos = p + normal * (offset * closestIntersection);
                    }
                }

                offsetPoints.Add(targetPos);
            }

            return offsetPoints;
        }

        /// <summary>
        /// Check if a ray from rayStart to rayEnd intersects a line segment
        /// Returns true if intersection found, and t (0-1) representing position along ray
        /// </summary>
        private static bool RayIntersectsSegment(Vector2 rayStart, Vector2 rayEnd, Vector2 segStart, Vector2 segEnd, out float t)
        {
            t = 1.0f;

            Vector2 rayDir = rayEnd - rayStart;
            Vector2 segDir = segEnd - segStart;

            float denominator = (-segDir.x * rayDir.y + rayDir.x * segDir.y);

            // Lines are parallel
            if (Mathf.Abs(denominator) < 1e-6f)
                return false;

            float s = (-rayDir.y * (rayStart.x - segStart.x) + rayDir.x * (rayStart.y - segStart.y)) / denominator;
            float rayT = (segDir.x * (rayStart.y - segStart.y) - segDir.y * (rayStart.x - segStart.x)) / denominator;

            // Check if intersection is within both ray and segment
            // For ray: 0 < rayT < 1 (not including start point)
            // For segment: 0 <= s <= 1
            if (s >= 0 && s <= 1 && rayT > 0.01f && rayT <= 1.0f)
            {
                t = rayT;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Find TriNet vertex ID for a given position within tolerance
        /// </summary>
        public static long FindTriNetIdForPosition(Dictionary<long, int> vertexMap, List<Vector3> vertices,
            Vector2 pos, float zValue, float scaleFactor = 0.01f)
        {
            float tol = 1e-3f * scaleFactor;
            foreach (var kv in vertexMap)
            {
                Vector3 v = vertices[kv.Value];
                if (Mathf.Abs(v.z - zValue) > 1e-4f) continue;
                if (Vector2.Distance(new Vector2(v.x, v.y), pos) <= tol) return kv.Key;
            }
            return -1;
        }

        /// <summary>
        /// Calculate centroid of a boundary
        /// </summary>
        public static Vector2 CalculateCentroid(List<Vector2> boundary)
        {
            Vector2 centroid = Vector2.zero;
            foreach (var p in boundary) centroid += p;
            centroid /= boundary.Count;
            return centroid;
        }

        /// <summary>
        /// Calculate edge normal pointing toward or away from centroid
        /// </summary>
        public static Vector3 CalculateEdgeNormal(Vector2 p0, Vector2 p1, Vector2 centroid, bool isHole)
        {
            Vector2 edge = (p1 - p0).normalized;
            Vector3 edgeNormal = new Vector3(edge.y, -edge.x, 0f).normalized;

            Vector2 midpoint = (p0 + p1) / 2f;
            Vector2 toCentroid = centroid - midpoint;
            float dot = Vector2.Dot(edgeNormal, toCentroid.normalized);
            bool pointsTowardCentroid = dot > 0f;

            Vector3 useNormal = isHole ?
                (pointsTowardCentroid ? edgeNormal : -edgeNormal) :
                (pointsTowardCentroid ? -edgeNormal : edgeNormal);

            return useNormal;
        }

        #region Private Utilities

        private static float GetSignedArea(List<Vector2> polygon)
        {
            if (polygon == null || polygon.Count < 3) return 0f;
            float area = 0f;
            for (int i = 0; i < polygon.Count; i++)
            {
                int next = (i + 1) % polygon.Count;
                area += polygon[i].x * polygon[next].y;
                area -= polygon[next].x * polygon[i].y;
            }
            return area * 0.5f;
        }

        #endregion
    }
}
