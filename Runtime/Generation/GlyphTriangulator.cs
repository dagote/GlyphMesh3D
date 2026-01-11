using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TriangleNet;
using TriangleNet.Geometry;
using TriangleNet.Meshing;

namespace LanternPines.GlyphMesh3D.Generation
{
    /// <summary>
    /// Handles triangulation of 2D contours into triangle meshes using TriangleNet.
    /// Supports outer boundaries and holes.
    /// </summary>
    public static class GlyphTriangulator
    {
        /// <summary>
        /// Triangulated mesh result
        /// </summary>
        public class TriangulationResult
        {
            public List<TriangleNet.Geometry.Vertex> SortedVertices;
            public Dictionary<long, int> VertexMap;
            public TriangleNet.Mesh TriMesh;

            public TriangulationResult()
            {
                SortedVertices = new List<TriangleNet.Geometry.Vertex>();
                VertexMap = new Dictionary<long, int>();
            }
        }

        /// <summary>
        /// Triangulate a set of 2D boundaries (outer + holes) into a triangle mesh
        /// </summary>
        /// <param name="boundaries">List of boundaries (first is outer, rest are holes)</param>
        /// <returns>Triangulation result with sorted vertices and triangle mesh</returns>
        public static TriangulationResult Triangulate(List<List<Vector2>> boundaries)
        {
            if (boundaries == null || boundaries.Count == 0)
                return null;

            try
            {
                var polygon = new TriangleNet.Geometry.Polygon();

                // Add outer boundary
                var outerVerts = boundaries[0].Select(p => new TriangleNet.Geometry.Vertex(p.x, p.y)).ToList();
                polygon.Add(new TriangleNet.Geometry.Contour(outerVerts));

                // Add holes
                for (int h = 1; h < boundaries.Count; h++)
                {
                    var holeVerts = boundaries[h].Select(p => new TriangleNet.Geometry.Vertex(p.x, p.y)).ToList();
                    polygon.Add(new TriangleNet.Geometry.Contour(holeVerts), true);
                }

                var triMesh = (TriangleNet.Mesh)polygon.Triangulate();

                var result = new TriangulationResult
                {
                    TriMesh = triMesh,
                    SortedVertices = triMesh.Vertices.OrderBy(v => v.Y).ThenBy(v => v.X).ToList()
                };

                return result;
            }
            catch (System.Exception ex)
            {
                // Triangulation failed - return null to indicate failure
                return null;
            }
        }

        /// <summary>
        /// Group boundaries into sets of outer + holes based on containment
        /// </summary>
        /// <param name="boundaries">All boundaries to group</param>
        /// <returns>Groups of boundaries (each group has outer boundary + its holes)</returns>
        public static List<List<List<Vector2>>> GroupBoundariesByOuter(List<List<Vector2>> boundaries)
        {
            var groups = new List<List<List<Vector2>>>();
            if (boundaries == null || boundaries.Count == 0) return groups;

            int n = boundaries.Count;
            var areas = boundaries.Select(b => Mathf.Abs(GetSignedArea(b))).ToArray();
            var centroids = new Vector2[n];

            // Use first vertex of each boundary instead of centroid for containment testing
            // Centroids can move outside the polygon after aggressive simplification
            var testPoints = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                if (boundaries[i].Count > 0)
                {
                    testPoints[i] = boundaries[i][0];
                }
            }

            var parent = Enumerable.Repeat(-1, n).ToArray();
            for (int i = 0; i < n; i++)
            {
                int best = -1;
                float bestArea = float.MaxValue;
                for (int j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    if (areas[j] <= areas[i]) continue;

                    bool isInside = IsPointInPolygon(testPoints[i], boundaries[j]);

                    if (isInside)
                    {
                        if (areas[j] < bestArea)
                        {
                            bestArea = areas[j];
                            best = j;
                        }
                    }
                }
                parent[i] = best;
            }

            for (int i = 0; i < n; i++)
            {
                if (parent[i] != -1) continue;
                var group = new List<List<Vector2>> { boundaries[i] };
                int holeCount = 0;
                for (int j = 0; j < n; j++)
                {
                    if (parent[j] == i)
                    {
                        group.Add(boundaries[j]);
                        holeCount++;
                    }
                }
                groups.Add(group);
            }
            return groups;
        }

        /// <summary>
        /// Calculate deterministic vertex ID for position matching
        /// </summary>
        public static long GetDeterministicVertexId(double x, double y)
        {
            long xQuant = (long)System.Math.Round(x * 10000.0);
            long yQuant = (long)System.Math.Round(y * 10000.0);
            return (xQuant & 0xFFFFFFFFL) | ((yQuant & 0xFFFFFFFFL) << 32);
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

        private static bool IsPointInPolygon(Vector2 point, List<Vector2> polygon)
        {
            bool inside = false;
            int n = polygon.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var pi = polygon[i];
                var pj = polygon[j];
                bool intersect = ((pi.y > point.y) != (pj.y > point.y)) &&
                               (point.x < (pj.x - pi.x) * (point.y - pi.y) / (pj.y - pi.y + 1e-12f) + pi.x);
                if (intersect) inside = !inside;
            }
            return inside;
        }

        #endregion
    }
}
