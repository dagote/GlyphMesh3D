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

        [Tooltip("Font size in points, following TextMeshPro scaling conventions (36pt ≈ 1 unit cap height)")]
        public float fontSize = 36f;

        // Cached components
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;

        private const float UnitsPerPoint = 1f / 36f;

        // Track previous state for change detection
        private string previousText;
        private GlyphText3DAsset previousAsset;
        private float previousFontSize = 36f;

        // Track instantiated character GameObjects
        private List<GameObject> instantiatedGlyphs = new List<GameObject>();

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

        private void Update()
        {
            // In edit mode, continuously check for text changes
            // OnValidate isn't always called for every character typed in TextArea
            #if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (text != previousText || asset != previousAsset || !Mathf.Approximately(fontSize, previousFontSize))
                {
                    Debug.Log($"GlyphText3D.Update: Text changed from '{previousText}' to '{text}'");
                    RegenerateMeshFromAsset();
                    previousText = text;
                    previousAsset = asset;
                    previousFontSize = fontSize;
                }
            }
            #endif
        }

        private void OnValidate()
        {
            // Detect changes and regenerate
            if (text != previousText || asset != previousAsset || !Mathf.Approximately(fontSize, previousFontSize))
            {
                Debug.Log($"GlyphText3D.OnValidate: Text changed from '{previousText}' to '{text}'");
                RegenerateMeshFromAsset();
                previousText = text;
                previousAsset = asset;
                previousFontSize = fontSize;
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
                ClearInstantiatedGlyphs();
                return;
            }

            if (string.IsNullOrEmpty(text))
            {
                Debug.Log("GlyphText3D: Text is empty.");
                ClearMesh();
                ClearInstantiatedGlyphs();
                return;
            }

            if (asset.glyphMeshes == null || asset.glyphMeshes.Length == 0)
            {
                Debug.LogWarning($"GlyphText3D: Asset '{asset.name}' has no glyph data. Please regenerate the asset using the GlyphText3DGenerator.");
                ClearMesh();
                ClearInstantiatedGlyphs();
                return;
            }

            Debug.Log($"GlyphText3D: Building text '{text}' using asset '{asset.name}' with {asset.glyphMeshes.Length} glyphs");

            // Build combined mesh from glyphs
            try
            {
                var combinedMesh = BuildCombinedMesh();

                if (combinedMesh != null)
                {
                    // Clear old mesh
                    ClearMesh();

                    // Assign new mesh
                    meshFilter.sharedMesh = combinedMesh;

                    // Update materials
                    UpdateMaterials();
                }
                else
                {
                    ClearMesh();
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"GlyphText3D: Failed to build combined mesh - {ex.Message}\n{ex.StackTrace}");
                ClearMesh();
            }

            // Clear any leftover individual glyphs from previous mode
            ClearInstantiatedGlyphs();

            previousText = text;
            previousAsset = asset;
            previousFontSize = fontSize;
        }

        /// <summary>
        /// TEMPORARY: Instantiate each glyph as a separate child GameObject for testing.
        /// This bypasses the mesh combining logic to isolate the issue.
        /// </summary>
        private void InstantiateIndividualGlyphs()
        {
            // Incremental update: only add/remove glyphs that changed
            int targetGlyphCount = 0;
            float currentXOffset = 0f;
            float scale = fontSize * UnitsPerPoint;

            // First pass: count how many glyphs we need and update positions
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                GlyphMeshData glyphData = asset.GetGlyphData(c);

                if (glyphData == null)
                {
                    Debug.LogWarning($"GlyphText3D: Character '{c}' (code: {(int)c}) not found in asset. Skipping.");
                    continue;
                }

                // Check if we already have this glyph at this position
                if (targetGlyphCount < instantiatedGlyphs.Count)
                {
                    GameObject existingGlyph = instantiatedGlyphs[targetGlyphCount];

                    // Update existing glyph
                    if (glyphData.mesh != null)
                    {
                        existingGlyph.name = $"Glyph_{c}";
                        existingGlyph.transform.localPosition = new Vector3((currentXOffset + glyphData.bearingX) * scale, glyphData.baselineOffset * scale, 0f);

                        MeshFilter glyphMeshFilter = existingGlyph.GetComponent<MeshFilter>();
                        if (glyphMeshFilter.sharedMesh != glyphData.mesh)
                        {
                            glyphMeshFilter.sharedMesh = glyphData.mesh;
                        }

                        targetGlyphCount++;
                        Debug.Log($"  Updated glyph '{c}' at position ({currentXOffset}, 0, 0)");
                    }
                }
                else
                {
                    // Create new glyph
                    if (glyphData.mesh != null)
                    {
                        Debug.Log($"  Creating new glyph '{c}': mesh={glyphData.mesh.name}, vertices={glyphData.mesh.vertexCount}, position=({currentXOffset}, 0, 0)");

                        GameObject glyphObject = new GameObject($"Glyph_{c}");
                        glyphObject.transform.SetParent(transform, false);
                        glyphObject.transform.localPosition = new Vector3((currentXOffset + glyphData.bearingX) * scale, glyphData.baselineOffset * scale, 0f);

                        MeshFilter glyphMeshFilter = glyphObject.AddComponent<MeshFilter>();
                        MeshRenderer glyphMeshRenderer = glyphObject.AddComponent<MeshRenderer>();

                        glyphMeshFilter.sharedMesh = glyphData.mesh;

                        // Copy materials from asset
                        if (asset.materials != null && asset.materials.Length > 0)
                        {
                            glyphMeshRenderer.sharedMaterials = asset.materials;
                        }
                        else if (meshRenderer != null && meshRenderer.sharedMaterial != null)
                        {
                            glyphMeshRenderer.sharedMaterial = meshRenderer.sharedMaterial;
                        }

                        instantiatedGlyphs.Add(glyphObject);
                        targetGlyphCount++;
                    }
                }

                // Advance position using stored advance width from asset
                if (glyphData.mesh == null)
                {
                    Debug.Log($"  Glyph '{c}': no mesh (space?), advanceWidth={glyphData.advanceWidth}");
                }
                currentXOffset += glyphData.advanceWidth;
            }

            // Remove excess glyphs if text got shorter
            while (instantiatedGlyphs.Count > targetGlyphCount)
            {
                int lastIndex = instantiatedGlyphs.Count - 1;
                GameObject toRemove = instantiatedGlyphs[lastIndex];
                instantiatedGlyphs.RemoveAt(lastIndex);

                if (toRemove != null)
                {
                    Debug.Log($"  Removing excess glyph at index {lastIndex}");
                    if (Application.isPlaying)
                        Destroy(toRemove);
                    else
                        DestroyImmediate(toRemove);
                }
            }

            Debug.Log($"GlyphText3D: Incremental update complete - {targetGlyphCount} character GameObjects");
        }

        /// <summary>
        /// Build a combined mesh from pre-generated glyph mesh assets.
        /// </summary>
        private Mesh BuildCombinedMesh()
        {
            float scale = fontSize * UnitsPerPoint;
            float currentXOffset = 0f;
            int processedGlyphs = 0;

            // Collect glyph data and positions for all valid characters
            var glyphsToRender = new List<(GlyphMeshData data, Vector3 position)>();

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

                // If this is an empty glyph (like space) or no mesh, just advance the position
                if (glyphData.mesh == null)
                {
                    Debug.Log($"  Glyph '{c}': no mesh (space?), advanceWidth={glyphData.advanceWidth}");
                    currentXOffset += glyphData.advanceWidth;
                    continue;
                }

                Debug.Log($"  Glyph '{c}': mesh={glyphData.mesh.name}, vertices={glyphData.mesh.vertexCount}, advanceWidth={glyphData.advanceWidth}");
                Debug.Log($"  Positioning glyph '{c}' at xOffset={currentXOffset}, bearingX={glyphData.bearingX}, baselineOffset={glyphData.baselineOffset}, will advance by {glyphData.advanceWidth}");

                // Store this glyph and its position
                // Apply bearingX for horizontal positioning and baselineOffset for vertical positioning
                glyphsToRender.Add((glyphData, new Vector3((currentXOffset + glyphData.bearingX) * scale, glyphData.baselineOffset * scale, 0f)));

                // Advance position using stored advance width from asset
                currentXOffset += glyphData.advanceWidth;
            }

            // If no meshes were added, return null
            if (glyphsToRender.Count == 0)
            {
                Debug.LogWarning($"GlyphText3D: No meshes found. Processed {processedGlyphs} glyphs from text '{text}'");
                return null;
            }

            Debug.Log($"GlyphText3D: Combining {glyphsToRender.Count} glyph meshes");

            // Determine the number of submeshes from the first glyph
            int submeshCount = glyphsToRender[0].Item1.mesh.subMeshCount;
            Debug.Log($"GlyphText3D: Expected submesh count = {submeshCount}");

            // Create the combined mesh
            var mesh = new Mesh();
            mesh.name = "GlyphText3D_Combined";

            // Combine meshes per-submesh to preserve material slots
            var allVertices = new List<Vector3>();
            var allNormals = new List<Vector3>();
            var allUVs = new List<Vector2>();
            var allColors = new List<Color>();
            var submeshTriangles = new List<int>[submeshCount];

            for (int i = 0; i < submeshCount; i++)
            {
                submeshTriangles[i] = new List<int>();
            }

            // Combine all glyphs
            foreach (var glyph in glyphsToRender)
            {
                int vertexOffset = allVertices.Count;
                var glyphData = glyph.Item1;  // Use .Item1 instead of .data for tuple access
                var position = glyph.Item2;    // Use .Item2 instead of .position for tuple access
                var glyphMesh = glyphData.mesh;

                // Add vertices (transformed by position)
                var vertices = glyphMesh.vertices;
                for (int i = 0; i < vertices.Length; i++)
                {
                    allVertices.Add(vertices[i] * scale + position);
                }

                // Add normals
                if (glyphMesh.normals != null && glyphMesh.normals.Length > 0)
                {
                    allNormals.AddRange(glyphMesh.normals);
                }

                // Add UVs
                if (glyphMesh.uv != null && glyphMesh.uv.Length > 0)
                {
                    allUVs.AddRange(glyphMesh.uv);
                }

                // Add colors
                if (glyphMesh.colors != null && glyphMesh.colors.Length > 0)
                {
                    allColors.AddRange(glyphMesh.colors);
                }

                // Add triangles for each submesh
                for (int subIdx = 0; subIdx < submeshCount && subIdx < glyphMesh.subMeshCount; subIdx++)
                {
                    var triangles = glyphMesh.GetTriangles(subIdx);
                    foreach (var tri in triangles)
                    {
                        submeshTriangles[subIdx].Add(tri + vertexOffset);
                    }
                }
            }

            // Assign to mesh
            mesh.vertices = allVertices.ToArray();

            if (allNormals.Count == allVertices.Count)
                mesh.normals = allNormals.ToArray();

            if (allUVs.Count == allVertices.Count)
                mesh.uv = allUVs.ToArray();

            if (allColors.Count == allVertices.Count)
                mesh.colors = allColors.ToArray();

            // Set submeshes
            mesh.subMeshCount = submeshCount;
            for (int i = 0; i < submeshCount; i++)
            {
                mesh.SetTriangles(submeshTriangles[i].ToArray(), i);
            }

            mesh.RecalculateBounds();

            Debug.Log($"GlyphText3D: Combined mesh has {mesh.vertexCount} vertices, {mesh.subMeshCount} submeshes, bounds = {mesh.bounds}");

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
        /// Clear instantiated glyph GameObjects.
        /// </summary>
        private void ClearInstantiatedGlyphs()
        {
            foreach (var glyphObject in instantiatedGlyphs)
            {
                if (glyphObject != null)
                {
                    if (Application.isPlaying)
                        Destroy(glyphObject);
                    else
                        DestroyImmediate(glyphObject);
                }
            }
            instantiatedGlyphs.Clear();
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
            ClearInstantiatedGlyphs();
        }
    }
}
