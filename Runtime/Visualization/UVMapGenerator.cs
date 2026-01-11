using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LanternPines.GlyphMesh3D.Visualization
{
    [ExecuteInEditMode]
    public class UVMapGenerator : MonoBehaviour
    {
        [Header("UV Map Settings")]
        [Tooltip("Resolution of the generated UV map texture.")]
        public int textureResolution = 512;

        [Tooltip("Color of the UV lines.")]
        public Color uvLineColor = Color.green;

        [Tooltip("Background color of the UV map.")]
        public Color backgroundColor = Color.black;

        [Header("Mesh Reference")]
        [Tooltip("The mesh to generate the UV map for.")]
        public MeshFilter meshFilter;

        [Header("Export Options")]
        [Tooltip("Choose the file format for the UV map.")]
        public UVMapFormat uvMapFormat = UVMapFormat.PNG;

        public enum UVMapFormat
        {
            PNG,
            JPEG
        }

        [ContextMenu("Generate UV Map")]
        public void GenerateUVMap()
        {
#if UNITY_EDITOR
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                return;
            }

            Mesh mesh = meshFilter.sharedMesh;
            List<Vector2> uvs = new List<Vector2>();
            mesh.GetUVs(0, uvs);

            if (uvs.Count == 0)
            {
                return;
            }

            Texture2D uvTexture = new Texture2D(textureResolution, textureResolution);
            ClearTexture(uvTexture, backgroundColor);

            DrawUVs(uvTexture, uvs, uvLineColor);

            SaveUVMap(uvTexture);
            DestroyImmediate(uvTexture);
#endif
        }

        private void ClearTexture(Texture2D texture, Color color)
        {
            Color[] pixels = new Color[texture.width * texture.height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }
            texture.SetPixels(pixels);
            texture.Apply();
        }

        private void DrawUVs(Texture2D texture, List<Vector2> uvs, Color color)
        {
            int width = texture.width;
            int height = texture.height;

            for (int i = 0; i < uvs.Count; i += 3)
            {
                if (i + 2 >= uvs.Count) break;

                Vector2 p0 = uvs[i];
                Vector2 p1 = uvs[i + 1];
                Vector2 p2 = uvs[i + 2];

                DrawLine(texture, p0, p1, color, width, height);
                DrawLine(texture, p1, p2, color, width, height);
                DrawLine(texture, p2, p0, color, width, height);
            }

            texture.Apply();
        }

        private void DrawLine(Texture2D texture, Vector2 uv1, Vector2 uv2, Color color, int width, int height)
        {
            int x1 = Mathf.RoundToInt(uv1.x * width);
            int y1 = Mathf.RoundToInt(uv1.y * height);
            int x2 = Mathf.RoundToInt(uv2.x * width);
            int y2 = Mathf.RoundToInt(uv2.y * height);

            int dx = Mathf.Abs(x2 - x1);
            int dy = Mathf.Abs(y2 - y1);
            int sx = x1 < x2 ? 1 : -1;
            int sy = y1 < y2 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                texture.SetPixel(x1, y1, color);

                if (x1 == x2 && y1 == y2) break;

                int e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    x1 += sx;
                }
                if (e2 < dx)
                {
                    err += dx;
                    y1 += sy;
                }
            }
        }

        private void SaveUVMap(Texture2D texture)
        {
#if UNITY_EDITOR
            string path = EditorUtility.SaveFilePanel("Save UV Map", "Assets", "UVMap", uvMapFormat.ToString().ToLower());
            if (string.IsNullOrEmpty(path)) return;

            byte[] bytes = uvMapFormat == UVMapFormat.PNG ? texture.EncodeToPNG() : texture.EncodeToJPG();
            System.IO.File.WriteAllBytes(path, bytes);
            AssetDatabase.Refresh();
#endif
        }
    }
}
