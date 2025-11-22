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

                for (int i = 0; i < n; i++)
                {
                    Vector2 p = boundary[i];
                    Vector2 prev = boundary[(i - 1 + n) % n];
                    Vector2 next = boundary[(i + 1) % n];

                    // Calculate edge normals
                    Vector2 edge1 = (p - prev).normalized;
                    Vector2 edge2 = (next - p).normalized;

                    // Perpendicular to edges (rotate 90 degrees)
                    Vector2 normal1 = new Vector2(-edge1.y, edge1.x);
                    Vector2 normal2 = new Vector2(-edge2.y, edge2.x);

                    // Average the normals
                    Vector2 avgNormal = (normal1 + normal2).normalized;

                    // For outer boundaries (CCW), perpendicular points inward, so flip to point outward
                    // For holes (CW), perpendicular already points outward (into hole)
                    float signedArea = GetSignedArea(boundary);
                    if (signedArea > 0) // Counter-clockwise (outer boundary)
                    {
                        // Flip to make normal point outward
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
        /// Create offset boundary with uniform expansion and intersection handling
        /// </summary>
        public static List<Vector2> CreateOffsetBoundary(List<Vector2> boundary, Dictionary<long, Vector2> normalMap, float offsetDistance, bool isHole)
        {
            int n = boundary.Count;
            var offsetPoints = new List<Vector2>(n);

            // First pass: offset all vertices uniformly
            for (int i = 0; i < n; i++)
            {
                Vector2 p = boundary[i];
                long id = GlyphTriangulator.GetDeterministicVertexId(p.x, p.y);

                if (normalMap.TryGetValue(id, out Vector2 normal))
                {
                    float offset = isHole ? -offsetDistance : offsetDistance;
                    offsetPoints.Add(p + normal * offset);
                }
                else
                {
                    offsetPoints.Add(p);
                }
            }

            // Second pass: detect and handle edge intersections
            var cleanedPoints = HandleSelfIntersections(offsetPoints);

            return cleanedPoints;
        }

        /// <summary>
        /// Detect and handle self-intersections in an offset boundary
        /// </summary>
        private static List<Vector2> HandleSelfIntersections(List<Vector2> points)
        {
            if (points.Count < 3) return points;

            var result = new List<Vector2>();
            int n = points.Count;
            bool[] removed = new bool[n];

            // Check each edge against non-adjacent edges for intersections
            for (int i = 0; i < n; i++)
            {
                if (removed[i]) continue;

                Vector2 p0 = points[i];
                Vector2 p1 = points[(i + 1) % n];

                bool foundIntersection = false;

                // Check against edges that are at least 2 edges away
                for (int j = i + 2; j < n; j++)
                {
                    if (removed[j]) continue;
                    if (j == (i + n - 1) % n) continue; // Skip adjacent edge

                    Vector2 p2 = points[j];
                    Vector2 p3 = points[(j + 1) % n];

                    if (LineSegmentsIntersect(p0, p1, p2, p3, out Vector2 intersection))
                    {
                        // Found intersection - mark intermediate vertices for removal
                        result.Add(p0);
                        result.Add(intersection);

                        // Mark vertices between i+1 and j (inclusive) for removal
                        for (int k = (i + 1) % n; k != j; k = (k + 1) % n)
                        {
                            removed[k] = true;
                        }

                        // Skip to j
                        i = j - 1;
                        foundIntersection = true;
                        break;
                    }
                }

                if (!foundIntersection && !removed[i])
                {
                    result.Add(p0);
                }
            }

            // If no intersections found, return original
            if (result.Count == 0)
                return points;

            return result;
        }

        /// <summary>
        /// Check if two line segments intersect and return the intersection point
        /// </summary>
        private static bool LineSegmentsIntersect(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, out Vector2 intersection)
        {
            intersection = Vector2.zero;

            Vector2 s1 = p1 - p0;
            Vector2 s2 = p3 - p2;

            float denominator = (-s2.x * s1.y + s1.x * s2.y);

            // Lines are parallel
            if (Mathf.Abs(denominator) < 1e-6f)
                return false;

            float s = (-s1.y * (p0.x - p2.x) + s1.x * (p0.y - p2.y)) / denominator;
            float t = (s2.x * (p0.y - p2.y) - s2.y * (p0.x - p2.x)) / denominator;

            // Check if intersection is within both line segments
            if (s >= 0 && s <= 1 && t >= 0 && t <= 1)
            {
                intersection = p0 + (t * s1);
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
