using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LanternPines.GlyphMesh3D.Core
{
    /// <summary>
    /// Runtime component that instantiates and positions 3D glyph meshes from a GlyphText3DAsset.
    /// Lightweight implementation designed for easy extension.
    /// </summary>
    [ExecuteInEditMode]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class GlyphText3D : MonoBehaviour
    {
        [Header("Asset Reference")]
        [Tooltip("The generated glyph asset containing pre-baked meshes and settings")]
        public GlyphText3DAsset asset;

        [Header("Text Content")]
        [TextArea(3, 10)]
        [Tooltip("The text to display using the asset's glyphs")]
        public string text = "Sample";

        // Cached components
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;

        // Track previous state for change detection
        private string previousText;
        private GlyphText3DAsset previousAsset;

#if UNITY_EDITOR
        [MenuItem("GameObject/3D Object/Glyph Text 3D")]
        private static void CreateGlyphText3DObject()
        {
            GameObject go = new GameObject("GlyphText3D");
            GlyphText3D component = go.AddComponent<GlyphText3D>();

            if (Selection.activeTransform != null)
                go.transform.SetParent(Selection.activeTransform);

            Selection.activeGameObject = go;

            // Set up default material
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                // Try URP Lit first, fall back to Standard
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                    shader = Shader.Find("Standard");

                if (shader != null)
                {
                    Material defaultMat = new Material(shader);
                    renderer.sharedMaterial = defaultMat;
                }
            }
        }
#endif

        private void OnEnable()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            RegenerateMeshFromAsset();
        }

        private void OnValidate()
        {
            // Detect changes and regenerate
            if (text != previousText || asset != previousAsset)
            {
                RegenerateMeshFromAsset();
                previousText = text;
                previousAsset = asset;
            }
        }

        /// <summary>
        /// Public API to update the displayed text.
        /// </summary>
        public void SetText(string newText)
        {
            if (text != newText)
            {
                text = newText;
                RegenerateMeshFromAsset();
            }
        }

        /// <summary>
        /// Public API to force mesh regeneration.
        /// </summary>
        public void Rebuild()
        {
            RegenerateMeshFromAsset();
        }

        /// <summary>
        /// Main method: builds mesh from asset glyphs based on current text.
        /// </summary>
        private void RegenerateMeshFromAsset()
        {
            // Ensure components are cached
            if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
            if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();

            // Validation
            if (asset == null)
            {
                Debug.LogWarning("GlyphText3D: No asset assigned.");
                ClearMesh();
                return;
            }

            if (string.IsNullOrEmpty(text))
            {
                Debug.Log("GlyphText3D: Text is empty.");
                ClearMesh();
                return;
            }

            if (asset.glyphMeshes == null || asset.glyphMeshes.Length == 0)
            {
                Debug.LogWarning($"GlyphText3D: Asset '{asset.name}' has no glyph data. Please regenerate the asset using the GlyphText3DGenerator.");
                ClearMesh();
                return;
            }

            Debug.Log($"GlyphText3D: Building mesh for text '{text}' using asset '{asset.name}' with {asset.glyphMeshes.Length} glyphs");

            // Build combined mesh from individual glyphs
            try
            {
                var combinedMesh = BuildCombinedMesh();
                if (combinedMesh != null)
                {
                    Debug.Log($"GlyphText3D: Created mesh with {combinedMesh.vertexCount} vertices, {combinedMesh.subMeshCount} submeshes");

                    // Clear old mesh if exists, but DON'T destroy it yet
                    if (meshFilter.sharedMesh != null)
                    {
                        if (Application.isPlaying)
                            Destroy(meshFilter.sharedMesh);
                        else
                            DestroyImmediate(meshFilter.sharedMesh);
                    }

                    // Assign new mesh
                    meshFilter.sharedMesh = combinedMesh;

                    // Verify assignment worked
                    if (meshFilter.sharedMesh == null)
                    {
                        Debug.LogError("GlyphText3D: Mesh assignment FAILED! MeshFilter.sharedMesh is NULL after assignment!");
                    }
                    else
                    {
                        Debug.Log($"GlyphText3D: Mesh assigned successfully. MeshFilter.sharedMesh = {meshFilter.sharedMesh.name}, vertices = {meshFilter.sharedMesh.vertexCount}");
                    }

                    // Update materials if available
                    UpdateMaterials();
                }
                else
                {
                    Debug.LogWarning("GlyphText3D: BuildCombinedMesh returned null - no vertices were generated");
                    ClearMesh();
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"GlyphText3D: Failed to generate mesh - {ex.Message}\n{ex.StackTrace}");
                ClearMesh();
            }
        }

        /// <summary>
        /// Build a combined mesh from all glyphs in the text string.
        /// </summary>
        private Mesh BuildCombinedMesh()
        {
            var allVertices = new List<Vector3>();
            var allNormals = new List<Vector3>();
            var allUVs = new List<Vector2>();
            var allColors = new List<Color>();
            var submeshTriangles = new List<List<int>>();

            float currentXOffset = 0f;
            int maxSubmeshCount = 0;
            int processedGlyphs = 0;

            // Process each character in the text
            foreach (char c in text)
            {
                // Get glyph data for this character
                GlyphMeshData glyphData = asset.GetGlyphData(c);

                if (glyphData == null)
                {
                    Debug.LogWarning($"GlyphText3D: Character '{c}' (code: {(int)c}) not found in asset. Skipping.");
                    continue;
                }

                processedGlyphs++;

                Debug.Log($"  Glyph '{c}': vertices={glyphData.vertices?.Length ?? 0}, advanceWidth={glyphData.advanceWidth}, submeshCount={glyphData.submeshCount}");

                // Track maximum submesh count
                if (glyphData.submeshCount > maxSubmeshCount)
                {
                    maxSubmeshCount = glyphData.submeshCount;
                }

                // If this is an empty glyph (like space), just advance the position
                if (glyphData.vertices == null || glyphData.vertices.Length == 0)
                {
                    currentXOffset += glyphData.advanceWidth;
                    continue;
                }

                // Calculate offset for this glyph
                int vertexOffset = allVertices.Count;
                Vector3 positionOffset = new Vector3(currentXOffset, 0f, 0f);

                Debug.Log($"  Positioning glyph '{c}' at xOffset={currentXOffset}, will advance by {glyphData.advanceWidth}");

                // Add vertices with position offset
                foreach (var vert in glyphData.vertices)
                {
                    allVertices.Add(vert + positionOffset);
                }

                // Add normals
                allNormals.AddRange(glyphData.normals);

                // Add UVs
                allUVs.AddRange(glyphData.uvs);

                // Add colors
                if (glyphData.colors != null && glyphData.colors.Length > 0)
                {
                    allColors.AddRange(glyphData.colors);
                }

                // Initialize submesh lists if needed
                while (submeshTriangles.Count < glyphData.submeshCount)
                {
                    submeshTriangles.Add(new List<int>());
                }

                // Add triangles for each submesh, offsetting indices
                for (int subIdx = 0; subIdx < glyphData.submeshCount; subIdx++)
                {
                    if (glyphData.submeshTriangles[subIdx] != null)
                    {
                        foreach (var tri in glyphData.submeshTriangles[subIdx])
                        {
                            submeshTriangles[subIdx].Add(tri + vertexOffset);
                        }
                    }
                }

                // Advance position for next character
                currentXOffset += glyphData.advanceWidth;
            }

            // If no vertices were added, return null
            if (allVertices.Count == 0)
            {
                Debug.LogWarning($"GlyphText3D: No vertices generated. Processed {processedGlyphs} glyphs from text '{text}'");
                return null;
            }

            Debug.Log($"GlyphText3D: Processed {processedGlyphs} glyphs, generated {allVertices.Count} vertices, {submeshTriangles.Count} submeshes");

            // Create the combined mesh
            var mesh = new Mesh();
            mesh.name = "GlyphText3D_Combined";

            mesh.vertices = allVertices.ToArray();
            mesh.normals = allNormals.ToArray();
            mesh.uv = allUVs.ToArray();

            if (allColors.Count > 0)
            {
                mesh.colors = allColors.ToArray();
            }

            // Set submeshes
            mesh.subMeshCount = submeshTriangles.Count;
            for (int i = 0; i < submeshTriangles.Count; i++)
            {
                mesh.SetTriangles(submeshTriangles[i].ToArray(), i);
            }

            mesh.RecalculateBounds();

            Debug.Log($"GlyphText3D: Mesh bounds = {mesh.bounds} (center: {mesh.bounds.center}, size: {mesh.bounds.size})");

            return mesh;
        }

        /// <summary>
        /// Update materials from the asset or create default ones.
        /// </summary>
        private void UpdateMaterials()
        {
            if (meshRenderer == null || asset == null) return;

            // Use materials from the asset if available
            if (asset.materials != null && asset.materials.Length > 0)
            {
                // Apply materials from asset
                meshRenderer.sharedMaterials = asset.materials;
            }
            else
            {
                // No materials in asset - create default materials based on slot count
                int slotCount = asset.materialSlotCount > 0 ? asset.materialSlotCount : 1;
                var defaultMaterials = new Material[slotCount];

                // Find appropriate shader
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                    shader = Shader.Find("Standard");

                if (shader != null)
                {
                    // Create a default material for each slot
                    for (int i = 0; i < slotCount; i++)
                    {
                        defaultMaterials[i] = new Material(shader);
                        defaultMaterials[i].name = $"GlyphMaterial_Slot{i}";
                    }
                }

                meshRenderer.sharedMaterials = defaultMaterials;
            }
        }

        /// <summary>
        /// Clear the current mesh.
        /// </summary>
        private void ClearMesh()
        {
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                if (Application.isPlaying)
                    Destroy(meshFilter.sharedMesh);
                else
                    DestroyImmediate(meshFilter.sharedMesh);

                meshFilter.sharedMesh = null;
            }
        }

        private void OnDestroy()
        {
            ClearMesh();
        }
    }
}
