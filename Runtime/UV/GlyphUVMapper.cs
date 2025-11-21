using System.Collections.Generic;
using UnityEngine;

namespace LanternPines.GlyphMesh3D.UV
{
    /// <summary>
    /// Static class for generating UV coordinates for 3D glyph meshes.
    /// </summary>
    public static class GlyphUVMapper
    {
        /// <summary>
        /// Generates UV coordinates for multi-section face unwrapping.
        /// </summary>
        /// <param name="vertices">List of mesh vertices.</param>
        /// <param name="frontFaceVertexCount">Number of vertices in the front face.</param>
        /// <param name="backFaceVertexCount">Number of vertices in the back face.</param>
        /// <param name="extrusionSteps">Number of extrusion steps.</param>
        /// <param name="glyphBounds">Bounding box of the glyph.</param>
        /// <returns>List of UV coordinates.</returns>
        public static List<Vector2> GenerateGlyphUVs(List<Vector3> vertices, int frontFaceVertexCount, int backFaceVertexCount, int extrusionSteps, Bounds glyphBounds)
        {
            List<Vector2> uvs = new List<Vector2>(new Vector2[vertices.Count]);

            // Map front face to UV region (0,0.5) to (0.25,1.0)
            MapFaceToUV(vertices, 0, frontFaceVertexCount, new Vector2(0f, 0.5f), new Vector2(0.25f, 1f), glyphBounds, uvs);

            // Map back face to UV region (0,0) to (0.25,0.5)
            MapFaceToUV(vertices, frontFaceVertexCount, backFaceVertexCount, new Vector2(0f, 0f), new Vector2(0.25f, 0.5f), glyphBounds, uvs);

            // Map extrusion strips to remaining UV space
            int perimeterVertCount = vertices.Count - frontFaceVertexCount - backFaceVertexCount;
            for (int i = 0; i < extrusionSteps; i++)
            {
                MapExtrusionStripToUV(vertices, i, extrusionSteps, perimeterVertCount, uvs);
            }

            ValidateUVs(uvs);
            return uvs;
        }

        /// <summary>
        /// Maps a face's vertices to a specific UV region.
        /// </summary>
        private static void MapFaceToUV(List<Vector3> vertices, int startIndex, int count, Vector2 uvMin, Vector2 uvMax, Bounds bounds, List<Vector2> uvs)
        {
            for (int i = 0; i < count; i++)
            {
                int index = startIndex + i;
                uvs[index] = NormalizeVertexToUV(vertices[index], bounds, uvMin, uvMax);
            }
        }

        /// <summary>
        /// Maps an extrusion strip's vertices to a horizontal UV band.
        /// </summary>
        private static void MapExtrusionStripToUV(List<Vector3> vertices, int stripIndex, int totalStrips, int perimeterVertCount, List<Vector2> uvs)
        {
            float uvStart = 0.25f + (0.75f * stripIndex / totalStrips);
            float uvEnd = 0.25f + (0.75f * (stripIndex + 1) / totalStrips);

            int stripStartIndex = perimeterVertCount * stripIndex / totalStrips;
            int stripEndIndex = perimeterVertCount * (stripIndex + 1) / totalStrips;

            for (int i = stripStartIndex; i < stripEndIndex; i++)
            {
                float t = (float)(i - stripStartIndex) / (stripEndIndex - stripStartIndex);
                uvs[i] = new Vector2(Mathf.Lerp(uvStart, uvEnd, t), 0.5f);
            }
        }

        /// <summary>
        /// Normalizes a vertex position to a UV coordinate within a specified region.
        /// </summary>
        private static Vector2 NormalizeVertexToUV(Vector3 vertex, Bounds bounds, Vector2 uvMin, Vector2 uvMax)
        {
            Vector3 size = bounds.size;
            Vector3 normalized = new Vector3(
                size.x > 0 ? (vertex.x - bounds.min.x) / size.x : 0,
                size.y > 0 ? (vertex.y - bounds.min.y) / size.y : 0,
                size.z > 0 ? (vertex.z - bounds.min.z) / size.z : 0
            );
            return new Vector2(
                Mathf.Lerp(uvMin.x, uvMax.x, normalized.x),
                Mathf.Lerp(uvMin.y, uvMax.y, normalized.y)
            );
        }

        /// <summary>
        /// Validates that all UV coordinates are within the range [0,1].
        /// </summary>
        private static void ValidateUVs(List<Vector2> uvs)
        {
            for (int i = 0; i < uvs.Count; i++)
            {
                uvs[i] = new Vector2(Mathf.Clamp01(uvs[i].x), Mathf.Clamp01(uvs[i].y));
            }
        }

        /// <summary>
        /// Overloaded method for custom UV region definitions.
        /// </summary>
        public static List<Vector2> GenerateGlyphUVs(List<Vector3> vertices, List<(int startIndex, int count)> vertexGroups, List<(Vector2 uvMin, Vector2 uvMax)> uvRegions)
        {
            List<Vector2> uvs = new List<Vector2>(new Vector2[vertices.Count]);

            for (int i = 0; i < vertexGroups.Count; i++)
            {
                var group = vertexGroups[i];
                var region = uvRegions[i];
                MapFaceToUV(vertices, group.startIndex, group.count, region.uvMin, region.uvMax, new Bounds(), uvs);
            }

            ValidateUVs(uvs);
            return uvs;
        }
    }
}
