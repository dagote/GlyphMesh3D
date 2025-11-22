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
            public Dictionary<long, float> MaxOffsetMap;
            public HashSet<long> HoleVertices;

            public BoundaryNormalsResult()
            {
                NormalMap = new Dictionary<long, Vector2>();
                MaxOffsetMap = new Dictionary<long, float>();
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

                    // Calculate edge vectors
                    Vector2 edge1 = (p - prev);
                    Vector2 edge2 = (next - p);
                    float edgeLen1 = edge1.magnitude;
                    float edgeLen2 = edge2.magnitude;
                    edge1 = edge1.normalized;
                    edge2 = edge2.normalized;

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

                    // Calculate maximum safe offset to prevent self-intersection
                    // Based on angle between edges and edge lengths
                    float maxOffset = CalculateMaxSafeOffset(edge1, edge2, edgeLen1, edgeLen2, avgNormal);

                    long id = GlyphTriangulator.GetDeterministicVertexId(p.x, p.y);
                    result.NormalMap[id] = avgNormal;
                    result.MaxOffsetMap[id] = maxOffset;

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
        /// Calculate the maximum safe offset distance for a vertex to prevent self-intersections
        /// </summary>
        private static float CalculateMaxSafeOffset(Vector2 edge1, Vector2 edge2, float edgeLen1, float edgeLen2, Vector2 offsetNormal)
        {
            // Calculate the angle between edges
            float dot = Vector2.Dot(edge1, edge2);
            float angle = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f));

            // At sharp angles, the offset normal gets longer relative to perpendicular distance
            // The actual perpendicular offset is: offset / sin(angle/2)
            // So max safe offset is: min(edge_length) * sin(angle/2)

            float halfAngle = angle * 0.5f;
            float sinHalfAngle = Mathf.Sin(halfAngle);

            // Prevent division by very small numbers
            if (sinHalfAngle < 0.01f)
            {
                sinHalfAngle = 0.01f;
            }

            // Use the shorter adjacent edge as the limiting factor
            float minEdgeLen = Mathf.Min(edgeLen1, edgeLen2);

            // Maximum offset is limited by edge length and angle
            // Use a safety factor of 0.4 to be conservative
            float maxOffset = minEdgeLen * sinHalfAngle * 0.4f;

            // Also limit based on overall edge lengths to prevent extreme offsets
            maxOffset = Mathf.Min(maxOffset, minEdgeLen * 0.45f);

            return maxOffset;
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
