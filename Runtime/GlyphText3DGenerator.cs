using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using TriangleNet;
using TriangleNet.Geometry;
using TriangleNet.Meshing;
using UnityEngine;
using Mesh = UnityEngine.Mesh;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Complete source of truth for material slot layout.
/// Drives all decisions about mesh generation, submesh order, and material assignment.
/// </summary>
[System.Serializable]
public class MaterialSlotMap
{
    /// <summary>
    /// Number of keyframes in the extrusion profile.
    /// Total slots = keyframeCount
    /// Slot 0 = front/back faces
    /// Slots 1..N-1 = extrusion bands (band i is between layer i and layer i+1)
    /// </summary>
    public int KeyframeCount { get; private set; }

    public MaterialSlotMap(int keyframeCount)
    {
        KeyframeCount = Mathf.Max(1, keyframeCount);
    }

    /// <summary>
    /// Get the total number of material slots needed.
    /// Always equals KeyframeCount.
    /// </summary>
    public int TotalSlots => KeyframeCount;

    /// <summary>
    /// Get which material slot corresponds to the front/back face.
    /// Always slot 0.
    /// </summary>
    public int FaceSlot => 0;

    /// <summary>
    /// Get which material slot corresponds to a given extrusion band.
    /// Band i (between layer i and layer i+1) uses slot i+1.
    /// </summary>
    public int GetBandSlot(int bandIndex)
    {
        return bandIndex + 1;
    }

    /// <summary>
    /// Get the number of extrusion bands.
    /// With N keyframes, there are N-1 bands.
    /// </summary>
    public int BandCount => Mathf.Max(0, KeyframeCount - 1);

    /// <summary>
    /// Get all slots in order: [FaceSlot, BandSlot(0), BandSlot(1), ...]
    /// </summary>
    public List<int> GetAllSlots()
    {
        var slots = new List<int> { FaceSlot };
        for (int i = 0; i < BandCount; i++)
        {
            slots.Add(GetBandSlot(i));
        }
        return slots;
    }

    /// <summary>
    /// Check if a slot is a face slot.
    /// </summary>
    public bool IsFaceSlot(int slotIndex)
    {
        return slotIndex == FaceSlot;
    }

    /// <summary>
    /// Check if a slot is a band slot.
    /// </summary>
    public bool IsBandSlot(int slotIndex)
    {
        return slotIndex >= 1 && slotIndex < TotalSlots;
    }

    /// <summary>
    /// Get which band a given slot corresponds to (inverse of GetBandSlot).
    /// Returns -1 if the slot is not a band slot.
    /// </summary>
    public int GetBandIndexFromSlot(int slotIndex)
    {
        if (!IsBandSlot(slotIndex)) return -1;
        return slotIndex - 1;
    }

    public override string ToString()
    {
        return $"MaterialSlotMap(KeyframeCount={KeyframeCount}, TotalSlots={TotalSlots}, FaceSlot={FaceSlot}, BandCount={BandCount})";
    }
}

[System.Serializable]
public class ExtrusionProfileCurve
{
    [SerializeField]
    private AnimationCurve curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    // Track last known keyframe count to detect changes
    private int lastSyncedKeyframeCount = -1;

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

    /// <summary>
    /// Check if keyframe count has changed since last sync and return true if it has.
    /// Call this in OnValidate to detect curve edits.
    /// </summary>
    public bool HasKeyframeCountChanged()
    {
        bool changed = KeyframeCount != lastSyncedKeyframeCount;
        if (changed)
        {
            lastSyncedKeyframeCount = KeyframeCount;
        }
        return changed;
    }

    public void EnsureLinearTangents()
    {
        // Detect if curve is flat (all Y values the same) and fix it
        bool isFlat = true;
        if (curve.keys.Length > 0)
        {
            float firstY = curve.keys[0].value;
            for (int i = 1; i < curve.keys.Length; i++)
            {
                if (!Mathf.Approximately(curve.keys[i].value, firstY))
                {
                    isFlat = false;
                    break;
                }
            }

            // If flat and has 2 keyframes, reset to linear progression
            if (isFlat && curve.keys.Length == 2 && Mathf.Approximately(firstY, 0f))
            {
                curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            }
        }
    }
}

[ExecuteInEditMode]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class GlyphText3DGenerator : MonoBehaviour
{
    // Material slot mapping: keyframe count determines total slots
    // 2 keyframes (no extrusion) → 2 slots: [0] = front/back face
    // 2 keyframes (1 extrusion) → 2 slots: [0] = front/back, [1] = extrusion 0
    // 3 keyframes (2 extrusions) → 3 slots: [0] = front/back, [1] = extrusion 0, [2] = extrusion 1
    // etc. Slot 0 always handles front and back faces.

    [Header("Text Settings")]
    [SerializeField] private TMP_FontAsset fontAsset;
    [SerializeField] private string text = "Sample Text";

    [Header("Extrusion Settings")]
    [Range(0f, 100f)]
    [SerializeField] private float extrusionDepth = 10f;
    [Range(0f, 50f)]
    [Tooltip("Controls the width of the extrusion offset perpendicular to the boundary")]
    [SerializeField] private float extrusionWidth = 0f;

    [Tooltip("Curve controls perpendicular offset progression (X: 0-1 depth, Y: 0-1 offset strength). Keyframe count determines extrusion layer count.")]
    [SerializeField] internal ExtrusionProfileCurve extrusionProfile = new ExtrusionProfileCurve();

    [Header("Advanced Simplification")]
    [Range(0f, 5f)]
    [SerializeField] private float simplifyArcLength = 1.5f;
    [Range(40f, 100f)]
    [SerializeField] private float cornerAngleThreshold = 80f;
    [Range(0f, 5f)]
    [SerializeField] private float postDpEpsilon = 1.2f;

    [Header("UV Unwrapping (XAtlas)")]
    [Tooltip("Enable xatlas UV unwrapping for better UV layout. Falls back to simple projection if disabled or unavailable.")]
    [SerializeField] private bool useXAtlasUVUnwrapping = true;
    [Range(1, 16)]
    [Tooltip("Padding between UV islands in pixels to prevent texture bleeding")]
    [SerializeField] private int uvPadding = 4;
    [Range(256, 4096)]
    [Tooltip("Target texture resolution for UV atlas packing")]
    [SerializeField] private int uvResolution = 1024;
    [Range(0.1f, 10f)]
    [Tooltip("Texels per unit for consistent texel density across the mesh")]
    [SerializeField] private float texelsPerUnit = 1.0f;

    // Auto-assign and debug features removed; manual renderer material control is recommended.

    // Internal state
    private MeshFilter meshFilter;
    internal MeshRenderer meshRenderer;
    private string previousText = "";
    private const float SCALE_FACTOR = 0.01f; // 1/100th scale
    private bool pendingMeshRegeneration = false;

    [MenuItem("GameObject/3D Object/Glyph Text 3D Generator")]
    private static void CreateGlyphText3D()
    {
        GameObject go = new GameObject("GlyphText3DGenerator");
        GlyphText3DGenerator glyphText = go.AddComponent<GlyphText3DGenerator>();
        if (Selection.activeTransform != null)
            go.transform.SetParent(Selection.activeTransform);
        Selection.activeGameObject = go;

        // Try to find and assign default TMP font
        TMP_FontAsset defaultFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (defaultFont == null)
        {
            // Try to find any TMP font in the project
            string[] guids = AssetDatabase.FindAssets("t:TMP_FontAsset");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                defaultFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            }
        }

        if (defaultFont != null)
        {
            glyphText.fontAsset = defaultFont;
        }

        // Set up default material
        MeshRenderer renderer = go.GetComponent<MeshRenderer>();
        if (renderer != null && renderer.sharedMaterial == null)
        {
            // Try URP Lit first, fall back to Standard
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
        }

        // Set default values matching screenshot
        glyphText.text = "Sample Text";
        glyphText.extrusionDepth = 20f;
        glyphText.simplifyArcLength = 1.5f;
        glyphText.cornerAngleThreshold = 80f;
        glyphText.postDpEpsilon = 1.2f;

        // Set default extrusion profile (linear 0-1)
        glyphText.extrusionProfile = new ExtrusionProfileCurve();

        // Force mesh update
        glyphText.RegenerateMesh();
    }

    private void Awake()
    {
        InitializeComponents();
    }

    private void OnValidate()
    {
        if (!Application.isPlaying && enabled)
        {
            InitializeComponents();

            // Build slot map from current keyframe count - this is the SOURCE OF TRUTH
            var slotMap = new MaterialSlotMap(extrusionProfile != null ? extrusionProfile.KeyframeCount : 1);

            // Ensure linear tangents on the extrusion profile
            if (extrusionProfile != null)
            {
                extrusionProfile.EnsureLinearTangents();
            }

            // Sync renderer material slots to match the slot map
            if (meshRenderer != null)
            {
                int currentSlots = meshRenderer.sharedMaterials != null ? meshRenderer.sharedMaterials.Length : 0;
                if (currentSlots != slotMap.TotalSlots)
                {
                    EnsureRendererMaterialSlotCount(slotMap.TotalSlots);
                }
            }

            RegenerateMesh();
        }
    }

    private void InitializeComponents()
    {
        if (meshFilter == null)
            meshFilter = GetComponent<MeshFilter>();
        if (meshRenderer == null)
            meshRenderer = GetComponent<MeshRenderer>();
    }

    internal void RegenerateMesh()
    {
        // Safety checks
        if (fontAsset == null)
        {
            ClearMesh();
            return;
        }

        if (string.IsNullOrEmpty(text))
        {
            ClearMesh();
            return;
        }

        try
        {
            GenerateMeshForText();
            previousText = text;
        }
        catch (System.Exception)
        {
            ClearMesh();
        }
    }

    /// <summary>
    /// Request mesh regeneration on the next frame (deferred).
    /// Use this after material slots change to ensure slots are applied first.
    /// </summary>
    internal void RequestMeshRegeneration()
    {
        pendingMeshRegeneration = true;
    }

    private void LateUpdate()
    {
        // Process deferred mesh regeneration (happens after all slot changes are applied)
        if (pendingMeshRegeneration)
        {
            pendingMeshRegeneration = false;
            RegenerateMesh();
        }
    }

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

    /// <summary>
    /// Generates UV coordinates for front or back face vertices.
    /// Projects vertices onto XY plane and maps to appropriate UV region.
    /// </summary>
    /// <param name="vertices">List of vertices for the face</param>
    /// <param name="isFrontFace">True for front face (top region), false for back face (bottom region)</param>
    /// <returns>List of UV coordinates</returns>
    private List<Vector2> GenerateFaceUVs(List<Vector3> vertices, bool isFrontFace)
    {
        var uvs = new List<Vector2>(vertices.Count);

        if (vertices.Count == 0)
            return uvs;

        // Calculate bounds for UV normalization
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

        // Avoid division by zero
        if (width < 0.0001f) width = 1f;
        if (height < 0.0001f) height = 1f;

        // UV region for faces: 0-0.25 (U)
        // Front face: 0.5-1.0 (V), Back face: 0-0.5 (V)
        float uMin = 0f;
        float uMax = 0.25f;
        float vMin = isFrontFace ? 0.5f : 0f;
        float vMax = isFrontFace ? 1.0f : 0.5f;

        // Generate UVs by projecting vertices onto XY plane and normalizing to face region
        foreach (var v in vertices)
        {
            float normalizedX = (v.x - min.x) / width;
            float normalizedY = (v.y - min.y) / height;
            float u = Mathf.Lerp(uMin, uMax, normalizedX);
            float vCoord = Mathf.Lerp(vMin, vMax, normalizedY);
            uvs.Add(new Vector2(u, vCoord));
        }

        return uvs;
    }

    /// <summary>
    /// Generates UV coordinates for curved mesh with front/back faces and extrusion bands.
    /// </summary>
    private List<Vector2> GenerateCurvedMeshUVs(List<Vector3> vertices, List<ExtrusionLayer> layers,
        List<Dictionary<long, int>> layerVertexMaps, List<Dictionary<long, int>> layerSideVertexMaps,
        List<TriangleNet.Geometry.Vertex> sortedVertices)
    {
        var uvs = new List<Vector2>(new Vector2[vertices.Count]);

        if (layers.Count == 0)
            return uvs;

        // Calculate bounds for face UV normalization
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        foreach (var v in sortedVertices)
        {
            minX = Mathf.Min(minX, (float)v.X * SCALE_FACTOR);
            minY = Mathf.Min(minY, (float)v.Y * SCALE_FACTOR);
            maxX = Mathf.Max(maxX, (float)v.X * SCALE_FACTOR);
            maxY = Mathf.Max(maxY, (float)v.Y * SCALE_FACTOR);
        }

        float width = maxX - minX;
        float height = maxY - minY;
        if (width < 0.0001f) width = 1f;
        if (height < 0.0001f) height = 1f;

        // Generate UVs for each layer's face vertices (front and back faces)
        for (int layerIdx = 0; layerIdx < layers.Count; layerIdx++)
        {
            bool isFrontFace = (layerIdx == 0);
            float uMin = 0f;
            float uMax = 0.25f;
            float vMin = isFrontFace ? 0.5f : 0f;
            float vMax = isFrontFace ? 1.0f : 0.5f;

            foreach (var kv in layerVertexMaps[layerIdx])
            {
                int vertIdx = kv.Value;
                Vector3 v = vertices[vertIdx];
                float normalizedX = (v.x - minX) / width;
                float normalizedY = (v.y - minY) / height;
                float u = Mathf.Lerp(uMin, uMax, normalizedX);
                float vCoord = Mathf.Lerp(vMin, vMax, normalizedY);
                uvs[vertIdx] = new Vector2(u, vCoord);
            }
        }

        // Generate UVs for side vertices (extrusion bands)
        // Each band gets a horizontal strip in the right region (0.25-1.0 U)
        int bandCount = layers.Count - 1;
        if (bandCount > 0)
        {
            float bandUWidth = 0.75f / bandCount;

            for (int layerIdx = 0; layerIdx < layers.Count; layerIdx++)
            {
                // Determine which band this layer belongs to for UV mapping
                // Layers create bands between them, so we need to map each layer's side vertices
                int bandIdx = layerIdx; // Side vertices from layer i contribute to band i

                float bandUMin = 0.25f + (bandIdx * bandUWidth);
                float bandUMax = bandUMin + bandUWidth;

                // For side vertices, unwrap them based on their position along the perimeter
                // We'll use a simple approach: normalize based on vertex index within the layer
                var sideMap = layerSideVertexMaps[layerIdx];
                int sideVertCount = sideMap.Count;

                int idx = 0;
                foreach (var kv in sideMap)
                {
                    int vertIdx = kv.Value;
                    Vector3 v = vertices[vertIdx];

                    // Calculate U based on vertex position within the band
                    float t = sideVertCount > 1 ? (float)idx / (sideVertCount - 1) : 0.5f;
                    float u = Mathf.Lerp(bandUMin, bandUMax, t);

                    // V coordinate based on depth (Z position)
                    float normalizedDepth = extrusionDepth > 0 ? v.z / (extrusionDepth * SCALE_FACTOR) : 0f;
                    float vCoord = normalizedDepth;

                    uvs[vertIdx] = new Vector2(u, vCoord);
                    idx++;
                }
            }
        }

        return uvs;
    }

    private void GenerateMeshForText()
    {
        float xOffset = 0f;
        var allBoundaries = new List<List<Vector2>>();

        foreach (char c in text)
        {
            if (!fontAsset.characterLookupTable.TryGetValue(c, out TMPro.TMP_Character glyphChar))
                continue;

            UnityEngine.TextCore.Glyph glyph = GetGlyph(glyphChar);
            if (glyph == null)
                continue;

            Texture2D atlasTexture = fontAsset.atlasTexture;
            if (atlasTexture == null)
                continue;

            var glyphRect = glyph.glyphRect;
            if (glyphRect.width == 0 || glyphRect.height == 0)
                continue;

            // Extract glyph edge pixels
            var edgePixels = ExtractGlyphEdgePixels(atlasTexture, glyphRect);
            if (edgePixels == null || edgePixels.Count == 0)
                continue;

            // Find chains and create boundaries
            var chains = FindChains(edgePixels);
            foreach (var chain in chains)
            {
                var ordered = OrderChain(chain);
                if (ordered.Count < 3) continue;

                var pts = ordered.Select(p => new Vector2(p.x + xOffset, p.y)).ToList();
                var simplified = SimplifyHybrid(pts, simplifyArcLength, cornerAngleThreshold, postDpEpsilon);

                if (simplified.Count >= 3)
                {
                    float signedArea = GetSignedArea(simplified);
                    if (signedArea < 0f) simplified.Reverse();
                    allBoundaries.Add(simplified);
                }
            }

            xOffset += glyph.metrics.horizontalAdvance + 5f;
        }

        if (allBoundaries.Count > 0)
        {
            // Group boundaries and generate combined mesh
            var groups = GroupBoundariesByOuter(allBoundaries);
            var combinedMesh = new Mesh();
            var allVertices = new List<Vector3>();
            var allNormals = new List<Vector3>();
            var allUVs = new List<Vector2>();
            var submeshData = new Dictionary<Material, List<int>>();

            foreach (var group in groups)
            {
                GenerateMeshFromBoundaries(group, allVertices, allNormals, allUVs, submeshData);
            }

            if (allVertices.Count > 0 && submeshData.Count > 0)
            {
                combinedMesh.vertices = allVertices.ToArray();
                combinedMesh.normals = allNormals.ToArray();
                combinedMesh.uv = allUVs.ToArray();

                // Use MaterialSlotMap as source of truth for structure
                var slotMap = new MaterialSlotMap(extrusionProfile != null ? extrusionProfile.KeyframeCount : 1);
                var materialToTriangles = new Dictionary<Material, List<int>>(submeshData);

                // Build a mapping from material to its assigned slots
                // This preserves the slot structure even if materials are null
                var materialToSlots = new Dictionary<Material, List<int>>();

                // First pass: figure out which materials belong in which slots based on slot map
                var rendererMaterials = meshRenderer != null && meshRenderer.sharedMaterials != null
                    ? meshRenderer.sharedMaterials
                    : new Material[0];

                for (int slotIdx = 0; slotIdx < slotMap.TotalSlots; slotIdx++)
                {
                    Material slotMat = slotIdx < rendererMaterials.Length ? rendererMaterials[slotIdx] : null;
                    if (slotMat != null)
                    {
                        if (!materialToSlots.ContainsKey(slotMat))
                            materialToSlots[slotMat] = new List<int>();
                        materialToSlots[slotMat].Add(slotIdx);
                    }
                }

                // Create submeshes in strict slot order
                // This ensures submesh[i] always corresponds to slot i
                combinedMesh.subMeshCount = slotMap.TotalSlots;

                for (int slotIdx = 0; slotIdx < slotMap.TotalSlots; slotIdx++)
                {
                    Material slotMat = slotIdx < rendererMaterials.Length ? rendererMaterials[slotIdx] : null;

                    // Get geometry for this slot's material
                    var tris = new int[0];
                    if (slotMat != null && materialToTriangles.ContainsKey(slotMat))
                    {
                        tris = materialToTriangles[slotMat].ToArray();
                    }

                    combinedMesh.SetTriangles(tris, slotIdx);
                }

                combinedMesh.RecalculateBounds();

                // Clean up old mesh
                ClearMesh();
                meshFilter.sharedMesh = combinedMesh;

                // Apply materials to renderer only when explicitly requested (auto assignment is deprecated)
                // User-managed material assignment: manual control only. No automatic assignment.
            }
        }
        else
        {
            ClearMesh();
        }
    }

    private UnityEngine.TextCore.Glyph GetGlyph(TMPro.TMP_Character glyphChar)
    {
        if (fontAsset.glyphLookupTable != null && fontAsset.glyphLookupTable.ContainsKey(glyphChar.glyphIndex))
        {
            return fontAsset.glyphLookupTable[glyphChar.glyphIndex];
        }
        return fontAsset.glyphTable.FirstOrDefault(g => g.index == glyphChar.glyphIndex);
    }


    private List<Vector2Int> ExtractGlyphEdgePixels(Texture2D atlasTexture, UnityEngine.TextCore.GlyphRect glyphRect)
    {
        try
        {
            RenderTexture rt = RenderTexture.GetTemporary(atlasTexture.width, atlasTexture.height, 0);
            Graphics.Blit(atlasTexture, rt);
            RenderTexture.active = rt;
            Texture2D readable = new Texture2D(atlasTexture.width, atlasTexture.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, atlasTexture.width, atlasTexture.height), 0, 0);
            readable.Apply();
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);

            Color[] pixels = readable.GetPixels(glyphRect.x, glyphRect.y, glyphRect.width, glyphRect.height);
            const float threshold = 0.5f;
            List<Vector2Int> edgePixels = new List<Vector2Int>();

            for (int y = 0; y < glyphRect.height; y++)
            {
                int flippedY = glyphRect.height - 1 - y;
                for (int x = 0; x < glyphRect.width; x++)
                {
                    float alpha = pixels[flippedY * glyphRect.width + x].a;
                    if (alpha > threshold && IsEdge(x, flippedY, glyphRect.width, glyphRect.height, pixels, threshold))
                    {
                        edgePixels.Add(new Vector2Int(x, flippedY));
                    }
                }
            }

            DestroyImmediate(readable);
            return edgePixels;
        }
        catch (System.Exception)
        {
            return new List<Vector2Int>();
        }
    }
    

    private bool IsEdge(int x, int y, int width, int height, Color[] pixels, float threshold)
    {
        int[] dx = { -1, 0, 1, -1, 1, -1, 0, 1 };
        int[] dy = { -1, -1, -1, 0, 0, 1, 1, 1 };

        for (int i = 0; i < 8; i++)
        {
            int nx = x + dx[i];
            int ny = y + dy[i];

            if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                return true;

            if (pixels[ny * width + nx].a <= threshold)
                return true;
        }
        return false;
    }

    private List<List<Vector2Int>> FindChains(List<Vector2Int> pixels)
    {
        var pixelSet = new HashSet<Vector2Int>(pixels);
        var visited = new HashSet<Vector2Int>();
        var chains = new List<List<Vector2Int>>();
        Vector2Int[] ortho = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };


        foreach (var start in pixels)
        {
            if (visited.Contains(start)) continue;

            var chain = new List<Vector2Int>();
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(start);
            visited.Add(start);
            chain.Add(start);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var dir in ortho)
                {
                    var neighbor = current + dir;
                    if (pixelSet.Contains(neighbor) && !visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                        chain.Add(neighbor);
                    }
                }
            }
            chains.Add(chain);
        }
        return chains;
    }

    private List<Vector2Int> OrderChain(List<Vector2Int> chain)
    {
        var ordered = new List<Vector2Int>();
        var orderedSet = new HashSet<Vector2Int>();
        if (chain == null || chain.Count == 0) return ordered;

        var set = new HashSet<Vector2Int>(chain);
        Vector2Int start = chain.OrderBy(p => p.y).ThenBy(p => p.x).First();
        ordered.Add(start);
        orderedSet.Add(start);
        var current = start;
        Vector2Int prev = new Vector2Int(int.MinValue, int.MinValue);

        int safety = 0;
        while (true)
        {
            safety++;
            if (safety > 10000) break;

            var neighbors = GetNeighbors8(current).Where(n => set.Contains(n)).ToList();
            neighbors.RemoveAll(n => n == prev);

            if (neighbors.Count == 0) break;

            Vector2Int next = neighbors[0];
            var unvisited = neighbors.Where(n => !orderedSet.Contains(n)).ToList();

            if (unvisited.Count > 0)
            {
                Vector2 dirPrev = prev.x == int.MinValue ? Vector2.zero : new Vector2(current.x - prev.x, current.y - prev.y);
                float bestDot = float.MinValue;
                Vector2Int best = unvisited[0];

                foreach (var cand in unvisited)
                {
                    if (dirPrev == Vector2.zero) { best = cand; break; }
                    Vector2 dir = new Vector2(cand.x - current.x, cand.y - current.y);
                    float dot = Vector2.Dot(dir.normalized, dirPrev.normalized);
                    if (dot > bestDot)
                    {
                        bestDot = dot;
                        best = cand;
                    }
                }
                next = best;
            }
            else
            {
                if (next == start && ordered.Count > 2) break;
                break;
            }

            ordered.Add(next);
            orderedSet.Add(next);
            prev = current;
            current = next;
        }
        return ordered;
    }


    private IEnumerable<Vector2Int> GetNeighbors8(Vector2Int p)
    {
        yield return new Vector2Int(p.x - 1, p.y - 1);
        yield return new Vector2Int(p.x, p.y - 1);
        yield return new Vector2Int(p.x + 1, p.y - 1);
        yield return new Vector2Int(p.x - 1, p.y);
        yield return new Vector2Int(p.x + 1, p.y);
        yield return new Vector2Int(p.x - 1, p.y + 1);
        yield return new Vector2Int(p.x, p.y + 1);
        yield return new Vector2Int(p.x + 1, p.y + 1);
    }

    private List<Vector2> SimplifyHybrid(List<Vector2> points, float targetDistance, float angleThresholdDeg, float dpEps)
    {
        if (points == null || points.Count < 2) return new List<Vector2>(points);

        int n = points.Count;
        var anchors = new List<int> { 0 };

        for (int i = 1; i < n - 1; i++)
        {
            Vector2 v1 = points[i] - points[i - 1];
            Vector2 v2 = points[i + 1] - points[i];
            if (v1.sqrMagnitude == 0f || v2.sqrMagnitude == 0f) continue;
            float angle = Vector2.Angle(v1, v2);
            if (angle >= angleThresholdDeg) anchors.Add(i);
        }

        if (!anchors.Contains(n - 1)) anchors.Add(n - 1);

        var result = new List<Vector2>();
        for (int ai = 0; ai < anchors.Count - 1; ai++)
        {
            int a = anchors[ai];
            int b = anchors[ai + 1];
            if (a == b) continue;

            var segment = points.GetRange(a, b - a + 1);
            var sampled = SimplifyByArcLength(segment, targetDistance);

            if (result.Count > 0 && result[result.Count - 1] == sampled[0])
                result.AddRange(sampled.Skip(1));
            else
                result.AddRange(sampled);
        }

        if (result.Count == 0) return new List<Vector2>(points);
        if (result[0] != points[0]) result.Insert(0, points[0]);
        if (result[result.Count - 1] != points[n - 1]) result.Add(points[n - 1]);

        if (dpEps > 0f)
            result = DouglasPeucker(result, dpEps);

        return result;
    }

    private List<Vector2> SimplifyByArcLength(List<Vector2> points, float targetDistance)
    {
        if (points == null || points.Count < 2) return new List<Vector2>(points);

        var result = new List<Vector2> { points[0] };
        float currentDistance = 0f;

        for (int i = 1; i < points.Count; i++)
        {
            float dist = Vector2.Distance(points[i - 1], points[i]);
            currentDistance += dist;

            if (currentDistance >= targetDistance)
            {
                result.Add(points[i]);
                currentDistance = 0f;
            }
        }

        if (!result[result.Count - 1].Equals(points[points.Count - 1]))
            result.Add(points[points.Count - 1]);

        return result;
    }

    private List<Vector2> DouglasPeucker(List<Vector2> points, float epsilon)
    {
        if (points == null || points.Count < 3) return new List<Vector2>(points);

        int index = -1;
        float maxDist = 0f;
        for (int i = 1; i < points.Count - 1; i++)
        {
            float dist = PerpendicularDistance(points[i], points[0], points[points.Count - 1]);
            if (dist > maxDist)
            {
                index = i;
                maxDist = dist;
            }
        }

        if (maxDist > epsilon)
        {
            var left = DouglasPeucker(points.GetRange(0, index + 1), epsilon);
            var right = DouglasPeucker(points.GetRange(index, points.Count - index), epsilon);
            var result = new List<Vector2>(left);
            result.RemoveAt(result.Count - 1);
            result.AddRange(right);
            return result;
        }
        else
        {
            return new List<Vector2> { points[0], points[points.Count - 1] };
        }
    }

    private float PerpendicularDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        float dx = b.x - a.x;
        float dy = b.y - a.y;
        if (dx == 0 && dy == 0) return Vector2.Distance(p, a);
        float t = ((p.x - a.x) * dx + (p.y - a.y) * dy) / (dx * dx + dy * dy);
        Vector2 proj = new Vector2(a.x + t * dx, a.y + t * dy);
        return Vector2.Distance(p, proj);
    }

    private List<Vector2> SmoothPathPreserveCorners(List<Vector2> points, int iterations, float tension)
    {
        if (points == null || points.Count < 2 || iterations <= 0) return new List<Vector2>(points);

        int n = points.Count;
        bool isClosed = Mathf.Abs(GetSignedArea(points)) > 1e-4f;

        var isCorner = new bool[n];
        for (int i = 0; i < n; i++)
        {
            Vector2 prev = isClosed ? points[(i - 1 + n) % n] : (i == 0 ? points[i] : points[i - 1]);
            Vector2 cur = points[i];
            Vector2 next = isClosed ? points[(i + 1) % n] : (i == n - 1 ? points[i] : points[i + 1]);
            Vector2 v1 = (cur - prev).magnitude > 0f ? (cur - prev).normalized : Vector2.zero;
            Vector2 v2 = (next - cur).magnitude > 0f ? (next - cur).normalized : Vector2.zero;
            float angle = (v1 == Vector2.zero || v2 == Vector2.zero) ? 0f : Vector2.Angle(v1, v2);
            isCorner[i] = angle >= cornerAngleThreshold;
        }

        var cornerIndices = new List<int>();
        for (int i = 0; i < n; i++) if (isCorner[i]) cornerIndices.Add(i);

        if (!isClosed)
        {
            if (cornerIndices.Count == 0 || cornerIndices[0] != 0) cornerIndices.Insert(0, 0);
            if (cornerIndices.Count == 0 || cornerIndices[cornerIndices.Count - 1] != n - 1) cornerIndices.Add(n - 1);
        }

        if (cornerIndices.Count == 0)
        {
            return isClosed ? SmoothClosedChaikin(points, iterations, tension) : SmoothSegmentChaikin(points, iterations, tension);
        }

        var result = new List<Vector2>();
        for (int ci = 0; ci < cornerIndices.Count; ci++)
        {
            int a = cornerIndices[ci];
            int b = (ci + 1 < cornerIndices.Count) ? cornerIndices[ci + 1] : (isClosed ? cornerIndices[0] : -1);
            if (b < 0) break;
            if (a == b) continue;

            var seg = new List<Vector2>();
            int idxA = a;
            seg.Add(points[idxA]);
            int cur = (idxA + 1) % n;
            while (true)
            {
                seg.Add(points[cur]);
                if (cur == b) break;
                cur = (cur + 1) % n;
                if (!isClosed && cur == 0) break;
                if (seg.Count > n) break;
            }

            if (seg.Count <= 2)
            {
                if (result.Count == 0 || result[result.Count - 1] != seg[0]) result.Add(seg[0]);
                if (seg.Count == 2 && (result.Count == 0 || result[result.Count - 1] != seg[1])) result.Add(seg[1]);
                continue;
            }

            var smoothedSeg = SmoothSegmentChaikin(seg, iterations, tension);

            if (result.Count > 0 && result[result.Count - 1] == smoothedSeg[0])
                result.AddRange(smoothedSeg.Skip(1));
            else
                result.AddRange(smoothedSeg);
        }

        if (result.Count > 0 && result[0] == result[result.Count - 1]) result.RemoveAt(result.Count - 1);
        return result;
    }

    private List<Vector2> SmoothSegmentChaikin(List<Vector2> pts, int iterations, float tension)
    {
        if (pts == null || pts.Count < 2) return new List<Vector2>(pts);
        var work = new List<Vector2>(pts);
        for (int iter = 0; iter < iterations; iter++)
        {
            var outPts = new List<Vector2>();
            outPts.Add(work[0]);
            for (int i = 0; i < work.Count - 1; i++)
            {
                Vector2 p0 = work[i];
                Vector2 p1 = work[i + 1];
                float tnorm = Mathf.Clamp01(tension / 5f);
                float alpha = Mathf.Lerp(0.25f, 0.5f, tnorm);
                Vector2 q = Vector2.Lerp(p0, p1, alpha);
                Vector2 r = Vector2.Lerp(p0, p1, 1f - alpha);
                outPts.Add(q);
                outPts.Add(r);
            }
            outPts.Add(work[work.Count - 1]);
            work = outPts;
            if (work.Count > 10000)
            {
                break;
            }
        }
        return work;
    }

    private List<Vector2> SmoothClosedChaikin(List<Vector2> pts, int iterations, float tension)
    {
        if (pts == null || pts.Count < 2) return new List<Vector2>(pts);
        var work = new List<Vector2>(pts);
        for (int iter = 0; iter < iterations; iter++)
        {
            var outPts = new List<Vector2>();
            int n = work.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 p0 = work[i];
                Vector2 p1 = work[(i + 1) % n];
                float tnorm = Mathf.Clamp01(tension / 5f);
                float alpha = Mathf.Lerp(0.25f, 0.5f, tnorm);
                Vector2 q = Vector2.Lerp(p0, p1, alpha);
                Vector2 r = Vector2.Lerp(p0, p1, 1f - alpha);
                outPts.Add(q);
                outPts.Add(r);
            }
            work = outPts;
            if (work.Count > 10000)
            {
                break;
            }
        }
        return work;
    }

    private float GetSignedArea(List<Vector2> polygon)
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

    private bool IsPointInPolygon(Vector2 point, List<Vector2> polygon)
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

    private List<List<List<Vector2>>> GroupBoundariesByOuter(List<List<Vector2>> boundaries)
    {
        var groups = new List<List<List<Vector2>>>();
        if (boundaries == null || boundaries.Count == 0) return groups;

        int n = boundaries.Count;
        var areas = boundaries.Select(b => Mathf.Abs(GetSignedArea(b))).ToArray();
        var centroids = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            Vector2 c = Vector2.zero;
            foreach (var p in boundaries[i]) c += p;
            c /= boundaries[i].Count;
            centroids[i] = c;
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
                if (IsPointInPolygon(centroids[i], boundaries[j]))
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
            for (int j = 0; j < n; j++)
            {
                if (parent[j] == i) group.Add(boundaries[j]);
            }
            groups.Add(group);
        }
        return groups;
    }

    private void GenerateMeshFromBoundaries(List<List<Vector2>> boundaries, List<Vector3> allVertices,
        List<Vector3> allNormals, List<Vector2> allUVs, Dictionary<Material, List<int>> submeshData)
    {
        if (boundaries == null || boundaries.Count == 0) return;

        // Use curved extrusion if profile has keyframes
        bool useCurvedExtrusion = extrusionProfile != null && extrusionProfile.KeyframeCount > 0;

        if (useCurvedExtrusion)
        {
            GenerateCurvedMesh(boundaries, allVertices, allNormals, allUVs, submeshData);
        }
        else
        {
            GenerateStraightMesh(boundaries, allVertices, allNormals, allUVs, submeshData);
        }
    }

    private void GenerateStraightMesh(List<List<Vector2>> boundaries, List<Vector3> allVertices,
        List<Vector3> allNormals, List<Vector2> allUVs, Dictionary<Material, List<int>> submeshData)
    {
        if (boundaries == null || boundaries.Count == 0) return;

        try
        {
            var polygon = new TriangleNet.Geometry.Polygon();
            var outerVerts = boundaries[0].Select(p => new TriangleNet.Geometry.Vertex(p.x, p.y)).ToList();
            polygon.Add(new TriangleNet.Geometry.Contour(outerVerts));

            for (int h = 1; h < boundaries.Count; h++)
            {
                var holeVerts = boundaries[h].Select(p => new TriangleNet.Geometry.Vertex(p.x, p.y)).ToList();
                polygon.Add(new TriangleNet.Geometry.Contour(holeVerts), true);
            }

            var triMesh = (TriangleNet.Mesh)polygon.Triangulate();

            int vertexOffset = allVertices.Count;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var vertexMap = new Dictionary<long, int>();

            var sortedVertices = triMesh.Vertices.OrderBy(v => v.Y).ThenBy(v => v.X).ToList();

            // Only create front face vertices (no extrusion when no steps)
            foreach (var v in sortedVertices)
            {
                long id = GetDeterministicVertexId(v.X, v.Y);
                if (!vertexMap.ContainsKey(id))
                {
                    vertexMap[id] = vertices.Count;
                    vertices.Add(new Vector3((float)v.X * SCALE_FACTOR, (float)v.Y * SCALE_FACTOR, 0f));
                }
            }

            // Front face triangles only (no extrusion)
            foreach (var t in triMesh.Triangles)
            {
                var v0 = t.GetVertex(0);
                var v1 = t.GetVertex(1);
                var v2 = t.GetVertex(2);

                long id0 = GetDeterministicVertexId(v0.X, v0.Y);
                long id1 = GetDeterministicVertexId(v1.X, v1.Y);
                long id2 = GetDeterministicVertexId(v2.X, v2.Y);

                if (vertexMap.ContainsKey(id0) && vertexMap.ContainsKey(id1) && vertexMap.ContainsKey(id2))
                {
                    // Reversed winding for front face (normal points back toward camera)
                    triangles.Add(vertexMap[id2]);
                    triangles.Add(vertexMap[id1]);
                    triangles.Add(vertexMap[id0]);
                }
            }

            // All normals point backward for flat front face (toward camera)
            var meshNormals = new Vector3[vertices.Count];
            for (int i = 0; i < vertices.Count; i++)
            {
                meshNormals[i] = Vector3.back;
            }

            // Generate UVs for front face - project to XY plane and map to front face UV region
            var meshUVs = GenerateFaceUVs(vertices, true);

            // Add vertices, normals, and UVs to global lists
            allVertices.AddRange(vertices);
            allNormals.AddRange(meshNormals);
            allUVs.AddRange(meshUVs);

            // Add triangles to submesh with face material (slot 0)
            Material faceMat = meshRenderer.sharedMaterials[0];
            if (!submeshData.ContainsKey(faceMat))
                submeshData[faceMat] = new List<int>();

            for (int i = 0; i < triangles.Count; i++)
            {
                submeshData[faceMat].Add(triangles[i] + vertexOffset);
            }
        }
        catch (System.Exception)
        {
        }
    }

    private void GenerateCurvedMesh(List<List<Vector2>> boundaries, List<Vector3> allVertices,
        List<Vector3> allNormals, List<Vector2> allUVs, Dictionary<Material, List<int>> submeshData)
    {
        if (boundaries == null || boundaries.Count == 0) return;

        var boundaryIsHole = Enumerable.Range(0, boundaries.Count).Select(i => i > 0).ToList();
        var boundaryCentroids = new List<Vector2>();

        // Calculate centroid for each boundary
        foreach (var boundary in boundaries)
        {
            Vector2 centroid = Vector2.zero;
            foreach (var p in boundary) centroid += p;
            centroid /= boundary.Count;
            boundaryCentroids.Add(centroid);
        }

        // Calculate boundary normals for perpendicular offset
        var (boundaryNormals, holeVertices) = CalculateBoundaryNormals(boundaries);

        try
        {
            var polygon = new TriangleNet.Geometry.Polygon();
            var outerVerts = boundaries[0].Select(p => new TriangleNet.Geometry.Vertex(p.x, p.y)).ToList();
            polygon.Add(new TriangleNet.Geometry.Contour(outerVerts));

            for (int h = 1; h < boundaries.Count; h++)
            {
                var holeVerts = boundaries[h].Select(p => new TriangleNet.Geometry.Vertex(p.x, p.y)).ToList();
                polygon.Add(new TriangleNet.Geometry.Contour(holeVerts), true);
            }

            var triMesh = (TriangleNet.Mesh)polygon.Triangulate();

            int vertexOffset = allVertices.Count;
            var vertices = new List<Vector3>();
            var localSubmeshData = new Dictionary<Material, List<int>>();

            var sortedVertices = triMesh.Vertices.OrderBy(v => v.Y).ThenBy(v => v.X).ToList();

            // Build extrusion layers from keyframe profile
            var layers = new List<ExtrusionLayer>();

            if (extrusionProfile != null && extrusionProfile.KeyframeCount > 0)
            {
                int keyframeCount = extrusionProfile.KeyframeCount;

                for (int i = 0; i < keyframeCount; i++)
                {
                    // Use the keyframe's time (X-axis, 0-1) as the normalized depth position
                    float normalizedDepth = extrusionProfile.GetKeyframeTime(i);
                    float depth = normalizedDepth * extrusionDepth;
                    float curveValue = extrusionProfile.GetOffset(normalizedDepth);
                    // Apply extrusion width and triple the power
                    float curveOffset = curveValue * curveValue * curveValue * extrusionWidth;

                    layers.Add(new ExtrusionLayer { depth = depth, curveOffset = curveOffset });
                }
            }

            // Create vertex maps for each layer
            var layerVertexMaps = new List<Dictionary<long, int>>();
            for (int layerIdx = 0; layerIdx < layers.Count; layerIdx++)
            {
                var layer = layers[layerIdx];
                var vertexMap = new Dictionary<long, int>();

                foreach (var v in sortedVertices)
                {
                    long id = GetDeterministicVertexId(v.X, v.Y);
                    if (!vertexMap.ContainsKey(id))
                    {
                        Vector2 basePos = new Vector2((float)v.X, (float)v.Y);

                        // Apply perpendicular offset along boundary normal
                        Vector2 offsetPos = basePos;
                        if (boundaryNormals.TryGetValue(id, out Vector2 normal))
                        {
                            // For holes, invert the offset so they expand outward (opposite direction)
                            float offset = holeVertices.Contains(id) ? -layer.curveOffset : layer.curveOffset;
                            offsetPos = basePos + normal * offset;
                        }

                        vertexMap[id] = vertices.Count;
                        vertices.Add(new Vector3(offsetPos.x * SCALE_FACTOR, offsetPos.y * SCALE_FACTOR, layer.depth * SCALE_FACTOR));
                    }
                }

                layerVertexMaps.Add(vertexMap);
            }

            // Create side vertex duplicates for each layer
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

            // Build slot map to track material assignments
            var slotMap = new MaterialSlotMap(layers.Count);

            // Get materials from renderer slots
            var rendererMaterials = meshRenderer != null && meshRenderer.sharedMaterials != null
                ? meshRenderer.sharedMaterials
                : new Material[0];

            // Get face material from slot 0
            Material faceMat = slotMap.FaceSlot < rendererMaterials.Length ? rendererMaterials[slotMap.FaceSlot] : null;

            Material frontMat = faceMat;
            if (!localSubmeshData.ContainsKey(frontMat))
                localSubmeshData[frontMat] = new List<int>();

            foreach (var t in triMesh.Triangles)
            {
                var v0 = t.GetVertex(0);
                var v1 = t.GetVertex(1);
                var v2 = t.GetVertex(2);

                long id0 = GetDeterministicVertexId(v0.X, v0.Y);
                long id1 = GetDeterministicVertexId(v1.X, v1.Y);
                long id2 = GetDeterministicVertexId(v2.X, v2.Y);

                var frontMap = layerVertexMaps[0];
                if (frontMap.ContainsKey(id0) && frontMap.ContainsKey(id1) && frontMap.ContainsKey(id2))
                {
                    // Reversed winding for front face (normal points back toward camera)
                    localSubmeshData[frontMat].Add(frontMap[id2]);
                    localSubmeshData[frontMat].Add(frontMap[id1]);
                    localSubmeshData[frontMat].Add(frontMap[id0]);
                }
            }

            // Back face triangles - use front material (no back slot)
            Material backMat = frontMat;
            if (!localSubmeshData.ContainsKey(backMat))
                localSubmeshData[backMat] = new List<int>();

            foreach (var t in triMesh.Triangles)
            {
                var v0 = t.GetVertex(0);
                var v1 = t.GetVertex(1);
                var v2 = t.GetVertex(2);

                long id0 = GetDeterministicVertexId(v0.X, v0.Y);
                long id1 = GetDeterministicVertexId(v1.X, v1.Y);
                long id2 = GetDeterministicVertexId(v2.X, v2.Y);

                var backMap = layerVertexMaps[layerVertexMaps.Count - 1];
                if (backMap.ContainsKey(id0) && backMap.ContainsKey(id1) && backMap.ContainsKey(id2))
                {
                    // Normal winding for back face (normal points forward away from camera)
                    localSubmeshData[backMat].Add(backMap[id0]);
                    localSubmeshData[backMat].Add(backMap[id1]);
                    localSubmeshData[backMat].Add(backMap[id2]);
                }
            }

            // Initialize normals
            var meshNormals = new Vector3[vertices.Count];
            for (int i = 0; i < vertices.Count; i++)
            {
                if (Mathf.Approximately(vertices[i].z, 0f))
                    meshNormals[i] = Vector3.back;  // Front face points backward (toward camera)
                else if (Mathf.Approximately(vertices[i].z, extrusionDepth * SCALE_FACTOR))
                    meshNormals[i] = Vector3.forward;  // Back face points forward (away from camera)
                else
                    meshNormals[i] = Vector3.zero;
            }

            // Side faces connecting layers
            for (int b = 0; b < boundaries.Count; b++)
            {
                var boundary = boundaries[b];
                int n = boundary.Count;
                bool isHole = boundaryIsHole[b];

                for (int i = 0; i < n; i++)
                {
                    Vector2 p0 = boundary[i];
                    Vector2 p1 = boundary[(i + 1) % n];

                    // For each layer transition
                    for (int layerIdx = 0; layerIdx < layers.Count - 1; layerIdx++)
                    {
                        var currSideMap = layerSideVertexMaps[layerIdx];
                        var nextSideMap = layerSideVertexMaps[layerIdx + 1];

                        // Calculate offset positions for current layer
                        Vector2 offsetP0 = p0;
                        Vector2 offsetP1 = p1;

                        long baseId0 = GetDeterministicVertexId(p0.x, p0.y);
                        long baseId1 = GetDeterministicVertexId(p1.x, p1.y);

                        if (boundaryNormals.TryGetValue(baseId0, out Vector2 normal0))
                        {
                            // Apply hole inversion logic for offset calculation
                            float offset0 = holeVertices.Contains(baseId0) ? -layers[layerIdx].curveOffset : layers[layerIdx].curveOffset;
                            offsetP0 = p0 + normal0 * offset0;
                        }
                        if (boundaryNormals.TryGetValue(baseId1, out Vector2 normal1))
                        {
                            // Apply hole inversion logic for offset calculation
                            float offset1 = holeVertices.Contains(baseId1) ? -layers[layerIdx].curveOffset : layers[layerIdx].curveOffset;
                            offsetP1 = p1 + normal1 * offset1;
                        }

                        long triNetId0 = FindTriNetIdForPosition(layerVertexMaps[layerIdx], vertices, offsetP0 * SCALE_FACTOR, layers[layerIdx].depth * SCALE_FACTOR);
                        long triNetId1 = FindTriNetIdForPosition(layerVertexMaps[layerIdx], vertices, offsetP1 * SCALE_FACTOR, layers[layerIdx].depth * SCALE_FACTOR);

                        if (triNetId0 < 0 || triNetId1 < 0) continue;

                        int curr0 = currSideMap[triNetId0];
                        int curr1 = currSideMap[triNetId1];
                        int next0 = nextSideMap[triNetId0];
                        int next1 = nextSideMap[triNetId1];

                        // Get band material for this layer transition from renderer slot
                        int bandSlotIndex = slotMap.GetBandSlot(layerIdx);
                        Material layerMat = bandSlotIndex < rendererMaterials.Length ? rendererMaterials[bandSlotIndex] : frontMat;

                        if (!localSubmeshData.ContainsKey(layerMat))
                            localSubmeshData[layerMat] = new List<int>();

                        Vector2 edge = (p1 - p0).normalized;
                        Vector3 edgeNormal = new Vector3(edge.y, -edge.x, 0f).normalized;

                        // Reversed winding for positive Z extrusion direction
                        if (isHole)
                        {
                            localSubmeshData[layerMat].Add(curr0);
                            localSubmeshData[layerMat].Add(next1);
                            localSubmeshData[layerMat].Add(curr1);
                            localSubmeshData[layerMat].Add(curr0);
                            localSubmeshData[layerMat].Add(next0);
                            localSubmeshData[layerMat].Add(next1);
                        }
                        else
                        {
                            localSubmeshData[layerMat].Add(curr0);
                            localSubmeshData[layerMat].Add(curr1);
                            localSubmeshData[layerMat].Add(next1);
                            localSubmeshData[layerMat].Add(curr0);
                            localSubmeshData[layerMat].Add(next1);
                            localSubmeshData[layerMat].Add(next0);
                        }

                        Vector2 midpoint = (p0 + p1) / 2f;
                        Vector2 toCentroid = boundaryCentroids[b] - midpoint;
                        float dot = Vector2.Dot(edgeNormal, toCentroid.normalized);
                        bool pointsTowardCentroid = dot > 0f;
                        Vector3 useNormal = isHole ?
                            (pointsTowardCentroid ? edgeNormal : -edgeNormal) :
                            (pointsTowardCentroid ? -edgeNormal : edgeNormal);

                        meshNormals[curr0] = useNormal;
                        meshNormals[curr1] = useNormal;
                        meshNormals[next0] = useNormal;
                        meshNormals[next1] = useNormal;
                    }
                }
            }

            // Generate UVs for all vertices
            List<Vector2> meshUVs;

            // Try to use xatlas for UV generation first
            Vector3[] vertexArray = vertices.ToArray();
            Vector3[] normalArray = meshNormals;

            // Build triangle array from submesh data
            List<int> allTriangles = new List<int>();
            foreach (var kvp in localSubmeshData)
            {
                allTriangles.AddRange(kvp.Value);
            }

            // Only try xatlas if enabled in settings
            Vector2[] xatlasUVs = null;
            if (useXAtlasUVUnwrapping)
            {
                xatlasUVs = GenerateUVsWithXAtlas(vertexArray, allTriangles.ToArray(), normalArray, layers, layerVertexMaps);
            }

            if (xatlasUVs != null && xatlasUVs.Length == vertices.Count)
            {
                // Successfully generated UVs with xatlas
                meshUVs = new List<Vector2>(xatlasUVs);
                Debug.Log("GlyphText3D: Using xatlas-generated UVs");
            }
            else
            {
                // Fallback to original UV generation
                meshUVs = GenerateCurvedMeshUVs(vertices, layers, layerVertexMaps, layerSideVertexMaps, sortedVertices);
                Debug.Log("GlyphText3D: Using fallback UV generation");
            }

            // Add vertices, normals, and UVs to global lists
            allVertices.AddRange(vertices);
            allNormals.AddRange(meshNormals);
            allUVs.AddRange(meshUVs);

            // Add all local submesh triangles to global submeshData with vertex offset
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
        catch (System.Exception)
        {
        }
    }

    private class ExtrusionLayer
    {
        public float depth;
        public float curveOffset;
    }

    private (Dictionary<long, Vector2>, HashSet<long>) CalculateBoundaryNormals(List<List<Vector2>> boundaries)
    {
        var normalMap = new Dictionary<long, Vector2>();
        var holeVertices = new HashSet<long>();

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
                // For holes (CW), normal already points correctly (outward into hole)

                long id = GetDeterministicVertexId(p.x, p.y);
                normalMap[id] = avgNormal;

                // Track hole vertices
                if (isHole)
                {
                    holeVertices.Add(id);
                }
            }
        }

        return (normalMap, holeVertices);
    }

    private long GetDeterministicVertexId(double x, double y)
    {
        long xQuant = (long)System.Math.Round(x * 10000.0);
        long yQuant = (long)System.Math.Round(y * 10000.0);
        return (xQuant & 0xFFFFFFFFL) | ((yQuant & 0xFFFFFFFFL) << 32);
    }

    private long FindTriNetIdForPosition(Dictionary<long, int> vertexMap, List<Vector3> vertices, Vector2 pos, float zValue)
    {
        float tol = 1e-3f * SCALE_FACTOR;
        foreach (var kv in vertexMap)
        {
            Vector3 v = vertices[kv.Value];
            if (Mathf.Abs(v.z - zValue) > 1e-4f) continue;
            if (Vector2.Distance(new Vector2(v.x, v.y), pos) <= tol) return kv.Key;
        }
        return -1;
    }

    #region XAtlas UV Unwrapping

    /// <summary>
    /// Generate UVs using xatlas library for proper face projection mapping
    /// </summary>
    /// <param name="vertices">Mesh vertices</param>
    /// <param name="triangles">Mesh triangles</param>
    /// <param name="normals">Mesh normals</param>
    /// <param name="layers">Extrusion layers</param>
    /// <param name="layerVertexMaps">Vertex maps for each layer</param>
    /// <returns>UV coordinates array</returns>
    private Vector2[] GenerateUVsWithXAtlas(Vector3[] vertices, int[] triangles, Vector3[] normals,
        List<ExtrusionLayer> layers, List<Dictionary<long, int>> layerVertexMaps)
    {
        if (vertices == null || vertices.Length == 0 || triangles == null || triangles.Length == 0)
        {
            Debug.LogWarning("GlyphText3D: Cannot generate xatlas UVs - invalid mesh data");
            return null;
        }

        try
        {
            // Check if xatlas library is available
            if (!IsXAtlasAvailable())
            {
                Debug.LogWarning("GlyphText3D: xatlas library not available, using fallback UV generation");
                return null;
            }

            // Identify face groups (front, back, sides)
            var faceGroups = IdentifyFaceGroups(vertices, triangles, normals, layers, layerVertexMaps);

            // Initialize xatlas
            using (XAtlas atlas = new XAtlas())
            {
                // Configure pack options from serialized fields
                atlas.padding = uvPadding;
                atlas.texelsPerUnit = texelsPerUnit;
                atlas.resolution = uvResolution;
                atlas.maxChartSize = 0;
                atlas.packAttempts = 4096;
                atlas.bruteForce = false;

                // Add mesh to atlas
                if (!atlas.AddMesh(vertices, triangles, normals))
                {
                    Debug.LogError("GlyphText3D: Failed to add mesh to xatlas");
                    return null;
                }

                // Compute charts (UV islands / parametrization)
                if (!atlas.ComputeCharts())
                {
                    Debug.LogError("GlyphText3D: Failed to compute charts in xatlas");
                    return null;
                }

                // Pack charts into texture atlas
                if (!atlas.PackCharts())
                {
                    Debug.LogError("GlyphText3D: Failed to pack charts in xatlas");
                    return null;
                }

                // Normalize UV coordinates to 0-1 range
                atlas.Normalize();

                // Get UV coordinates
                Vector2[] uvs = atlas.GetUVs(0);

                if (uvs == null || uvs.Length == 0)
                {
                    Debug.LogError("GlyphText3D: Failed to retrieve UVs from xatlas");
                    return null;
                }

                // Check if xatlas split vertices
                int newVertexCount = uvs.Length;
                if (newVertexCount != vertices.Length)
                {
                    Debug.LogWarning($"GlyphText3D: xatlas split vertices (Original: {vertices.Length}, New: {newVertexCount}). Using fallback for gradient continuity.");
                    // Vertex splitting breaks gradient continuity on extrusion sides
                    // Fall back to custom UV generation that preserves topology
                    return null;
                }

                // Apply custom strip unwrapping for extrusion sides to ensure gradient continuity
                ApplyExtrusionStripUVs(ref uvs, vertices, triangles, normals, faceGroups, layers);

                // Validate UV coverage
                if (!ValidateUVCoverage(uvs))
                {
                    Debug.LogWarning("GlyphText3D: UV coverage validation found issues");
                }

                // Log UV generation statistics
                int atlasWidth = atlas.GetAtlasWidth();
                int atlasHeight = atlas.GetAtlasHeight();
                int atlasCount = atlas.GetAtlasCount();
                Debug.Log($"GlyphText3D: Generated UVs with xatlas - Atlas size: {atlasWidth}x{atlasHeight}, Atlas count: {atlasCount}, UV count: {uvs.Length}");

                return uvs;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"GlyphText3D: Exception during xatlas UV generation: {ex.Message}\n{ex.StackTrace}");
            return null;
        }
    }

    /// <summary>
    /// Check if xatlas library is available
    /// </summary>
    private bool IsXAtlasAvailable()
    {
        try
        {
            // Try to create an atlas instance to verify the library is loaded
            using (XAtlas atlas = new XAtlas())
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"GlyphText3D: xatlas library not available: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Face group information for UV mapping
    /// </summary>
    private class FaceGroup
    {
        public List<int> triangleIndices = new List<int>();
        public Vector3 normal;
        public string groupType;  // "front", "back", "side"
        public int layerIndex = -1;
    }

    /// <summary>
    /// Identify face groups (front, back, sides) from mesh data
    /// </summary>
    private Dictionary<string, FaceGroup> IdentifyFaceGroups(Vector3[] vertices, int[] triangles, Vector3[] normals,
        List<ExtrusionLayer> layers, List<Dictionary<long, int>> layerVertexMaps)
    {
        var groups = new Dictionary<string, FaceGroup>();
        groups["front"] = new FaceGroup { normal = Vector3.back, groupType = "front", layerIndex = 0 };
        groups["back"] = new FaceGroup { normal = Vector3.forward, groupType = "back", layerIndex = layers.Count - 1 };

        // Identify side faces for each extrusion band
        for (int i = 0; i < layers.Count - 1; i++)
        {
            groups[$"side_{i}"] = new FaceGroup { normal = Vector3.zero, groupType = "side", layerIndex = i };
        }

        // Classify triangles into groups based on their normals and Z position
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int i0 = triangles[i];
            int i1 = triangles[i + 1];
            int i2 = triangles[i + 2];

            Vector3 v0 = vertices[i0];
            Vector3 v1 = vertices[i1];
            Vector3 v2 = vertices[i2];

            // Calculate face center Z position
            float avgZ = (v0.z + v1.z + v2.z) / 3f;

            // Calculate face normal
            Vector3 edge1 = v1 - v0;
            Vector3 edge2 = v2 - v0;
            Vector3 faceNormal = Vector3.Cross(edge1, edge2).normalized;

            // Classify based on normal direction and Z position
            if (Mathf.Abs(faceNormal.z) > 0.9f)
            {
                // Front or back face
                if (Mathf.Approximately(avgZ, 0f))
                {
                    groups["front"].triangleIndices.Add(i);
                }
                else if (layers.Count > 0 && Mathf.Approximately(avgZ, layers[layers.Count - 1].depth * SCALE_FACTOR))
                {
                    groups["back"].triangleIndices.Add(i);
                }
            }
            else
            {
                // Side face - determine which layer band it belongs to
                for (int layerIdx = 0; layerIdx < layers.Count - 1; layerIdx++)
                {
                    float minZ = layers[layerIdx].depth * SCALE_FACTOR;
                    float maxZ = layers[layerIdx + 1].depth * SCALE_FACTOR;

                    if (avgZ >= minZ - 0.001f && avgZ <= maxZ + 0.001f)
                    {
                        groups[$"side_{layerIdx}"].triangleIndices.Add(i);
                        break;
                    }
                }
            }
        }

        return groups;
    }

    /// <summary>
    /// Apply proper strip unwrapping to extrusion sides for gradient continuity
    /// Keeps xatlas UVs for front/back faces, replaces side UVs with walking unfold
    /// </summary>
    private void ApplyExtrusionStripUVs(ref Vector2[] uvs, Vector3[] vertices, int[] triangles,
        Vector3[] normals, Dictionary<string, FaceGroup> faceGroups, List<ExtrusionLayer> layers)
    {
        if (layers == null || layers.Count < 2)
            return;

        // Identify which vertices belong to side faces
        HashSet<int> sideVertices = new HashSet<int>();
        foreach (var kvp in faceGroups)
        {
            if (kvp.Value.groupType == "side")
            {
                foreach (int triIdx in kvp.Value.triangleIndices)
                {
                    // triangleIndices stores the start index of each triangle
                    if (triIdx < triangles.Length - 2)
                    {
                        sideVertices.Add(triangles[triIdx]);
                        sideVertices.Add(triangles[triIdx + 1]);
                        sideVertices.Add(triangles[triIdx + 2]);
                    }
                }
            }
        }

        if (sideVertices.Count == 0)
            return;

        // For each side vertex, calculate UV based on:
        // U: angular position around the glyph perimeter (0-1 wrapping)
        // V: normalized depth (0 at front, 1 at back)

        // Calculate center point for angular calculation
        Vector2 center = Vector2.zero;
        int count = 0;
        foreach (int idx in sideVertices)
        {
            center += new Vector2(vertices[idx].x, vertices[idx].y);
            count++;
        }
        if (count > 0)
            center /= count;

        // Get depth range
        float minZ = layers[0].depth * SCALE_FACTOR;
        float maxZ = layers[layers.Count - 1].depth * SCALE_FACTOR;
        float depthRange = maxZ - minZ;
        if (depthRange < 0.0001f) depthRange = 1f;

        // Apply strip UVs: continuous angular unwrap + depth
        foreach (int idx in sideVertices)
        {
            Vector3 v = vertices[idx];

            // Calculate angular position (U coordinate)
            Vector2 dir = new Vector2(v.x, v.y) - center;
            float angle = Mathf.Atan2(dir.y, dir.x);
            float u = (angle + Mathf.PI) / (2f * Mathf.PI); // Normalize to 0-1

            // Calculate depth position (V coordinate)
            float depth = v.z;
            float vCoord = (depth - minZ) / depthRange;

            uvs[idx] = new Vector2(u, vCoord);
        }

        Debug.Log($"GlyphText3D: Applied strip unwrapping to {sideVertices.Count} side vertices for gradient continuity");
    }

    /// <summary>
    /// Setup planar projection for front/back faces
    /// </summary>
    private void SetupPlanarProjection(Vector3[] vertices, Vector3 normal, List<int> triangleIndices,
        ref Vector2[] uvs)
    {
        if (triangleIndices == null || triangleIndices.Count == 0)
            return;

        // Find bounds for normalization
        Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        HashSet<int> vertexSet = new HashSet<int>();
        foreach (int triIdx in triangleIndices)
        {
            vertexSet.Add(triIdx);
        }

        foreach (int idx in vertexSet)
        {
            if (idx < vertices.Length)
            {
                Vector3 v = vertices[idx];
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }
        }

        float width = max.x - min.x;
        float height = max.y - min.y;

        if (width < 0.0001f) width = 1f;
        if (height < 0.0001f) height = 1f;

        // Project vertices onto XY plane and normalize
        foreach (int idx in vertexSet)
        {
            if (idx < vertices.Length && idx < uvs.Length)
            {
                Vector3 v = vertices[idx];
                float u = (v.x - min.x) / width;
                float vCoord = (v.y - min.y) / height;
                uvs[idx] = new Vector2(u, vCoord);
            }
        }
    }

    /// <summary>
    /// Setup cylindrical projection for extrusion sides
    /// </summary>
    private void SetupCylindricalProjection(Vector3[] vertices, List<int> triangleIndices,
        float depthMin, float depthMax, ref Vector2[] uvs)
    {
        if (triangleIndices == null || triangleIndices.Count == 0)
            return;

        HashSet<int> vertexSet = new HashSet<int>();
        foreach (int triIdx in triangleIndices)
        {
            vertexSet.Add(triIdx);
        }

        // Calculate angle around center for each vertex
        Vector2 center = Vector2.zero;
        int count = 0;
        foreach (int idx in vertexSet)
        {
            if (idx < vertices.Length)
            {
                center += new Vector2(vertices[idx].x, vertices[idx].y);
                count++;
            }
        }
        if (count > 0)
            center /= count;

        // Unwrap as cylindrical coordinates
        foreach (int idx in vertexSet)
        {
            if (idx < vertices.Length && idx < uvs.Length)
            {
                Vector3 v = vertices[idx];
                Vector2 dir = new Vector2(v.x, v.y) - center;
                float angle = Mathf.Atan2(dir.y, dir.x);
                float u = (angle + Mathf.PI) / (2f * Mathf.PI);  // Normalize to 0-1

                float depth = v.z;
                float vCoord = depthMax > depthMin ? (depth - depthMin) / (depthMax - depthMin) : 0.5f;

                uvs[idx] = new Vector2(u, vCoord);
            }
        }
    }

    /// <summary>
    /// Validate that all UV coordinates are within valid range [0,1]
    /// </summary>
    private bool ValidateUVCoverage(Vector2[] uvs)
    {
        if (uvs == null || uvs.Length == 0)
            return false;

        bool allValid = true;
        int outOfRangeCount = 0;

        Vector2 minUV = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 maxUV = new Vector2(float.MinValue, float.MinValue);

        foreach (Vector2 uv in uvs)
        {
            minUV = Vector2.Min(minUV, uv);
            maxUV = Vector2.Max(maxUV, uv);

            if (uv.x < -0.001f || uv.x > 1.001f || uv.y < -0.001f || uv.y > 1.001f)
            {
                outOfRangeCount++;
                allValid = false;
            }
        }

        if (outOfRangeCount > 0)
        {
            Debug.LogWarning($"GlyphText3D: {outOfRangeCount} UVs out of range [0,1]. UV bounds: ({minUV.x}, {minUV.y}) to ({maxUV.x}, {maxUV.y})");
        }
        else
        {
            Debug.Log($"GlyphText3D: UV coverage valid. UV bounds: ({minUV.x}, {minUV.y}) to ({maxUV.x}, {maxUV.y})");
        }

        return allValid;
    }

    #endregion

    private void OnDestroy()
    {
        ClearMesh();
    }

#if UNITY_EDITOR
    /// <summary>
    /// Get the name of the current font asset for use in asset filenames.
    /// Called by the editor to generate default filenames.
    /// </summary>
    public string GetFontAssetName()
    {
        if (fontAsset == null) return null;
        return fontAsset.name;
    }

    /// <summary>
    /// Populate a GlyphText3DAsset with current generator settings.
    /// Called by the editor when generating an asset.
    /// </summary>
    public void PopulateAsset(GlyphText3DAsset asset)
    {
        if (asset == null) return;

        // Font reference
        asset.fontAsset = fontAsset;

        // Generation settings
        asset.simplifyArcLength = simplifyArcLength;
        asset.cornerAngleThreshold = cornerAngleThreshold;
        asset.postDpEpsilon = postDpEpsilon;
        asset.extrusionDepth = extrusionDepth;
        asset.extrusionWidth = extrusionWidth;

        // Copy extrusion profile curve
        if (extrusionProfile != null)
        {
            // Create a copy of the animation curve
            AnimationCurve curveCopy = new AnimationCurve();
            if (extrusionProfile.KeyframeCount > 0)
            {
                for (int i = 0; i < extrusionProfile.KeyframeCount; i++)
                {
                    float time = extrusionProfile.GetKeyframeTime(i);
                    float value = extrusionProfile.GetOffset(time);
                    curveCopy.AddKey(time, value);
                }
            }
            asset.extrusionProfile = curveCopy;
        }
        else
        {
            asset.extrusionProfile = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        }

        // TODO: Add mesh data generation here
        // - Generate meshes for all characters
        // - Store vertex/triangle data
        // - Store character metrics
    }
#endif

    /// <summary>
    /// Ensure the MeshRenderer.sharedMaterials array has exactly desiredSlots elements.
    /// This preserves existing materials up to desiredSlots and trims or expands as needed.
    /// This method is public to be callable from the editor for manual resizing.
    /// </summary>
    public void EnsureRendererMaterialSlotCount(int desiredSlots)
    {
        if (meshRenderer == null) InitializeComponents();
        if (meshRenderer == null) return;
        Material[] existing = meshRenderer.sharedMaterials ?? new Material[0];

        Material[] resized = new Material[desiredSlots];

        // Get default material based on render pipeline
        Material defaultMat = GetDefaultMaterial();

        for (int i = 0; i < desiredSlots; i++)
        {
            if (i < existing.Length && existing[i] != null)
            {
                resized[i] = existing[i];
            }
            else if (i > 0 && resized[i - 1] != null)
            {
                resized[i] = resized[i - 1];
            }
            else
            {
                resized[i] = defaultMat;
            }
        }
        meshRenderer.sharedMaterials = resized;
    }

    // Cache for default material to ensure we use the same instance
    private static Material cachedDefaultMaterial;

    /// <summary>
    /// Get appropriate default material based on the current render pipeline
    /// </summary>
    private Material GetDefaultMaterial()
    {
        // Return cached material if it exists and is still valid
        if (cachedDefaultMaterial != null)
            return cachedDefaultMaterial;

        Material defaultMat = null;

        // Check for URP/HDRP
        var renderPipelineAsset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
        if (renderPipelineAsset != null)
        {
            string pipelineName = renderPipelineAsset.GetType().Name;

            if (pipelineName.Contains("Universal"))
            {
                // URP
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Lightweight Render Pipeline/Lit"); // Older URP
                if (shader != null) defaultMat = new Material(shader);
            }
            else if (pipelineName.Contains("HDRenderPipeline"))
            {
                // HDRP  
                var shader = Shader.Find("HDRP/Lit");
                if (shader != null) defaultMat = new Material(shader);
            }
        }

        // Fall back to built-in pipeline
        if (defaultMat == null)
        {
            var shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Diffuse"); // Ultimate fallback
            if (shader != null) defaultMat = new Material(shader);
        }

        // If we still don't have a material, create one with error shader
        if (defaultMat == null)
        {
            defaultMat = new Material(Shader.Find("Hidden/InternalErrorShader"));
        }

        // Cache the material for reuse
        cachedDefaultMaterial = defaultMat;
        return defaultMat;
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(GlyphText3DGenerator))]
public class GlyphText3DGeneratorEditor : Editor
{
    private int lastKnownKeyframeCount = -1;

    private void OnEnable()
    {
        // Subscribe to editor updates to detect curve changes even while curve editor is open
        EditorApplication.update += PollCurveChanges;
    }

    private void OnDisable()
    {
        EditorApplication.update -= PollCurveChanges;
    }

    private void PollCurveChanges()
    {
        GlyphText3DGenerator glyphText = target as GlyphText3DGenerator;
        if (glyphText == null || glyphText.extrusionProfile == null) return;

        // Build slot map from current keyframe count
        var slotMap = new MaterialSlotMap(glyphText.extrusionProfile.KeyframeCount);
        int currentSlots = glyphText.meshRenderer != null && glyphText.meshRenderer.sharedMaterials != null
            ? glyphText.meshRenderer.sharedMaterials.Length
            : 0;

        // If slots don't match the map, we need to sync
        if (currentSlots != slotMap.TotalSlots)
        {
            glyphText.EnsureRendererMaterialSlotCount(slotMap.TotalSlots);
            glyphText.RequestMeshRegeneration();
            EditorUtility.SetDirty(glyphText);
        }
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GlyphText3DGenerator generator = target as GlyphText3DGenerator;
        if (generator == null) return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Asset Generation", EditorStyles.boldLabel);

        if (GUILayout.Button("Generate Glyph Asset", GUILayout.Height(30)))
        {
            GenerateGlyphAsset(generator);
        }
    }

    private void GenerateGlyphAsset(GlyphText3DGenerator generator)
    {
        // Get font asset name for default filename
        string fontName = generator.GetFontAssetName();
        string defaultFileName = string.IsNullOrEmpty(fontName)
            ? "GlyphText3DAsset"
            : $"{fontName}_GlyphAsset";

        // Open save file panel
        string path = EditorUtility.SaveFilePanelInProject(
            "Save Glyph Asset",
            defaultFileName,
            "asset",
            "Choose where to save the generated glyph asset",
            "Assets/Glyph3D/Generated"
        );

        if (string.IsNullOrEmpty(path))
        {
            // User cancelled
            return;
        }

        // Create new asset
        GlyphText3DAsset asset = ScriptableObject.CreateInstance<GlyphText3DAsset>();

        // Populate with current generator settings
        generator.PopulateAsset(asset);

        // Save asset to disk
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Highlight the newly created asset
        EditorGUIUtility.PingObject(asset);
        Selection.activeObject = asset;

        Debug.Log($"Generated GlyphText3DAsset at: {path}");
    }
}
#endif
