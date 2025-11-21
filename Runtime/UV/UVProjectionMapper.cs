using UnityEngine;

namespace LanternPines.GlyphMesh3D.UV
{
    /// <summary>
    /// Provides simple UV projection mapping methods for mesh UVs.
    /// Supports planar, cylindrical, and box projection modes.
    /// </summary>
    public static class UVProjectionMapper
    {
        public enum ProjectionType
        {
            Planar,
            Cylindrical,
            Box
        }

        /// <summary>
        /// Project vertices onto a plane using their XY coordinates
        /// </summary>
        /// <param name="vertices">Vertex positions</param>
        /// <param name="normal">Projection plane normal (default: forward)</param>
        /// <returns>UV coordinates</returns>
        public static Vector2[] ProjectPlanar(Vector3[] vertices, Vector3 normal)
        {
            if (vertices == null || vertices.Length == 0)
                return new Vector2[0];

            Vector2[] uvs = new Vector2[vertices.Length];

            // Calculate bounds for normalization
            Vector3 min = vertices[0];
            Vector3 max = vertices[0];

            foreach (var v in vertices)
            {
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }

            Vector3 size = max - min;
            if (size.x < 0.0001f) size.x = 1f;
            if (size.y < 0.0001f) size.y = 1f;
            if (size.z < 0.0001f) size.z = 1f;

            // Determine projection axes based on normal
            bool useYZ = Mathf.Abs(normal.x) > 0.9f;
            bool useXZ = Mathf.Abs(normal.y) > 0.9f;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];
                float u, vCoord;

                if (useYZ)
                {
                    // Project onto YZ plane
                    u = (v.y - min.y) / size.y;
                    vCoord = (v.z - min.z) / size.z;
                }
                else if (useXZ)
                {
                    // Project onto XZ plane
                    u = (v.x - min.x) / size.x;
                    vCoord = (v.z - min.z) / size.z;
                }
                else
                {
                    // Project onto XY plane (default)
                    u = (v.x - min.x) / size.x;
                    vCoord = (v.y - min.y) / size.y;
                }

                uvs[i] = new Vector2(u, vCoord);
            }

            return uvs;
        }

        /// <summary>
        /// Project vertices using cylindrical mapping
        /// </summary>
        /// <param name="vertices">Vertex positions</param>
        /// <param name="axis">Cylinder axis (0=X, 1=Y, 2=Z)</param>
        /// <returns>UV coordinates</returns>
        public static Vector2[] ProjectCylindrical(Vector3[] vertices, int axis = 1)
        {
            if (vertices == null || vertices.Length == 0)
                return new Vector2[0];

            Vector2[] uvs = new Vector2[vertices.Length];

            // Calculate bounds for height normalization
            Vector3 min = vertices[0];
            Vector3 max = vertices[0];

            foreach (var v in vertices)
            {
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }

            float height = axis == 0 ? (max.x - min.x) : (axis == 1 ? (max.y - min.y) : (max.z - min.z));
            if (height < 0.0001f) height = 1f;

            Vector3 center = (min + max) * 0.5f;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];
                float u, vCoord;

                if (axis == 0) // X axis
                {
                    Vector2 dir = new Vector2(v.y - center.y, v.z - center.z);
                    float angle = Mathf.Atan2(dir.y, dir.x);
                    u = (angle + Mathf.PI) / (2f * Mathf.PI);
                    vCoord = (v.x - min.x) / height;
                }
                else if (axis == 2) // Z axis
                {
                    Vector2 dir = new Vector2(v.x - center.x, v.y - center.y);
                    float angle = Mathf.Atan2(dir.y, dir.x);
                    u = (angle + Mathf.PI) / (2f * Mathf.PI);
                    vCoord = (v.z - min.z) / height;
                }
                else // Y axis (default)
                {
                    Vector2 dir = new Vector2(v.x - center.x, v.z - center.z);
                    float angle = Mathf.Atan2(dir.y, dir.x);
                    u = (angle + Mathf.PI) / (2f * Mathf.PI);
                    vCoord = (v.y - min.y) / height;
                }

                uvs[i] = new Vector2(u, vCoord);
            }

            return uvs;
        }

        /// <summary>
        /// Project vertices using box mapping (6-sided cube projection)
        /// </summary>
        /// <param name="vertices">Vertex positions</param>
        /// <param name="bounds">Bounding box for normalization</param>
        /// <returns>UV coordinates</returns>
        public static Vector2[] ProjectBox(Vector3[] vertices, Bounds bounds)
        {
            if (vertices == null || vertices.Length == 0)
                return new Vector2[0];

            Vector2[] uvs = new Vector2[vertices.Length];
            Vector3 size = bounds.size;
            Vector3 min = bounds.min;

            if (size.x < 0.0001f) size.x = 1f;
            if (size.y < 0.0001f) size.y = 1f;
            if (size.z < 0.0001f) size.z = 1f;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];
                Vector3 normalized = new Vector3(
                    (v.x - min.x) / size.x,
                    (v.y - min.y) / size.y,
                    (v.z - min.z) / size.z
                );

                // Simple box mapping - use the two coordinates with largest variation
                float u = Mathf.Lerp(normalized.x, normalized.z, 0.5f);
                float vCoord = Mathf.Lerp(normalized.y, normalized.z, 0.5f);

                uvs[i] = new Vector2(u, vCoord);
            }

            return uvs;
        }

        /// <summary>
        /// Normalize UV coordinates to the 0-1 range
        /// </summary>
        public static void NormalizeUVs(Vector2[] uvs)
        {
            if (uvs == null || uvs.Length == 0)
                return;

            Vector2 min = uvs[0];
            Vector2 max = uvs[0];

            foreach (var uv in uvs)
            {
                min = Vector2.Min(min, uv);
                max = Vector2.Max(max, uv);
            }

            Vector2 range = max - min;
            if (range.x < 0.0001f) range.x = 1f;
            if (range.y < 0.0001f) range.y = 1f;

            for (int i = 0; i < uvs.Length; i++)
            {
                uvs[i] = new Vector2(
                    (uvs[i].x - min.x) / range.x,
                    (uvs[i].y - min.y) / range.y
                );
            }
        }
    }
}
