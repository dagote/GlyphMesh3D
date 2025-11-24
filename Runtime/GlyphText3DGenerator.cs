using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using LanternPines.GlyphMesh3D.Generation;
using LanternPines.GlyphMesh3D.UV;
using Mesh = UnityEngine.Mesh;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LanternPines.GlyphMesh3D.Core
{
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

        public AnimationCurve GetCurve() => curve;
    }

    /// <summary>
    /// Color mode for extrusion bands
    /// </summary>
    public enum BandColorMode
    {
        Gradient,       // Interpolate between two colors
        Rainbow,        // Full HSV rainbow spectrum
        Custom,         // Use custom color array
        SingleColor     // All bands same color
    }

    [ExecuteInEditMode]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class GlyphText3DGenerator : MonoBehaviour
    {
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

        [Header("Band Colors")]
        [Tooltip("Color scheme for extrusion bands")]
        [SerializeField] private BandColorMode bandColorMode = BandColorMode.Gradient;

        [Tooltip("Start color for gradient mode")]
        [SerializeField] private Color gradientStart = Color.cyan;

        [Tooltip("End color for gradient mode")]
        [SerializeField] private Color gradientEnd = Color.magenta;

        [Tooltip("Custom colors for each band (used in Custom mode)")]
        [SerializeField] private Color[] customBandColors = new Color[0];

        [Tooltip("Front cap color")]
        [SerializeField] private Color faceColor = Color.white;

        [Tooltip("Last band color (before back cap)")]
        [SerializeField] private Color lastBandColor = new Color(1f, 0.5f, 0f, 1f);

        [Tooltip("Back cap color")]
        [SerializeField] private Color backColor = Color.gray;

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

        // Internal state
        private MeshFilter meshFilter;
        internal MeshRenderer meshRenderer;
        private string previousText = "";
        private bool pendingMeshRegeneration = false;
        private Material[] previousMaterials = null;

        [MenuItem("GameObject/3D Object/Glyph Text 3D Generator")]
        private static void CreateGlyphText3D()
        {
            GameObject go = new GameObject("GlyphText3DGenerator");
            GlyphText3DGenerator glyphText = go.AddComponent<GlyphText3DGenerator>();
            if (Selection.activeTransform != null)
                go.transform.SetParent(Selection.activeTransform);
            Selection.activeGameObject = go;

#if UNITY_EDITOR
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

            // Set up default material from package
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                Material defaultMaterial = LoadPackageMaterial();
                if (defaultMaterial != null)
                {
                    renderer.sharedMaterial = defaultMaterial;
                }
            }
#endif

            // Set default values
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

                // Update tracked materials after regeneration
                if (meshRenderer != null)
                {
                    Material[] currentMaterials = meshRenderer.sharedMaterials;
                    previousMaterials = currentMaterials != null ? (Material[])currentMaterials.Clone() : null;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"GlyphText3DGenerator: Failed to generate mesh - {ex.Message}");
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
            // Detect material changes and trigger regeneration
            if (meshRenderer != null)
            {
                Material[] currentMaterials = meshRenderer.sharedMaterials;
                if (HasMaterialsChanged(currentMaterials))
                {
                    previousMaterials = currentMaterials != null ? (Material[])currentMaterials.Clone() : null;
                    pendingMeshRegeneration = true;
                }
            }

            // Process deferred mesh regeneration (happens after all slot changes are applied)
            if (pendingMeshRegeneration)
            {
                pendingMeshRegeneration = false;
                RegenerateMesh();
            }
        }

        /// <summary>
        /// Check if the materials array has changed compared to the previous frame.
        /// </summary>
        private bool HasMaterialsChanged(Material[] currentMaterials)
        {
            // First time check
            if (previousMaterials == null)
            {
                return currentMaterials != null && currentMaterials.Length > 0;
            }

            // Length changed
            if (currentMaterials == null || currentMaterials.Length != previousMaterials.Length)
            {
                return true;
            }

            // Check each material reference
            for (int i = 0; i < currentMaterials.Length; i++)
            {
                if (currentMaterials[i] != previousMaterials[i])
                {
                    return true;
                }
            }

            return false;
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

        private void GenerateMeshForText()
        {
            float xOffset = 0f;
            var allBoundaries = new List<List<Vector2>>();

            // Extract contours for each character using helper
            var extractionSettings = new GlyphContourExtractor.ContourExtractionSettings(
                simplifyArcLength, cornerAngleThreshold, postDpEpsilon);

            foreach (char c in text)
            {
                var boundaries = GlyphContourExtractor.ExtractContours(fontAsset, c, extractionSettings, xOffset);
                allBoundaries.AddRange(boundaries);

                // Calculate advance width for next character
                if (fontAsset.characterLookupTable.TryGetValue(c, out TMPro.TMP_Character glyphChar))
                {
                    var glyph = GetGlyph(glyphChar);
                    if (glyph != null)
                    {
                        xOffset += glyph.metrics.horizontalAdvance + 5f;
                    }
                }
            }

            if (allBoundaries.Count > 0)
            {
                // Group boundaries and generate combined mesh using helpers
                var groups = GlyphTriangulator.GroupBoundariesByOuter(allBoundaries);
                var combinedMesh = new Mesh();
                var allVertices = new List<Vector3>();
                var allNormals = new List<Vector3>();
                var allUVs = new List<Vector2>();
                var submeshData = new Dictionary<Material, List<int>>();

                // Build extrusion profile for mesh builder
                var profile = new GlyphExtrusionProcessor.ExtrusionProfile(
                    extrusionDepth, extrusionWidth, extrusionProfile.GetCurve());

                // Build mesh settings
                var meshSettings = new GlyphMeshBuilder.MeshBuildSettings
                {
                    ExtrusionProfile = profile,
                    Materials = meshRenderer.sharedMaterials,
                    UseXAtlasUV = useXAtlasUVUnwrapping,
                    UVPadding = uvPadding,
                    UVResolution = uvResolution,
                    TexelsPerUnit = texelsPerUnit
                };

                foreach (var group in groups)
                {
                    GenerateMeshFromBoundaries(group, meshSettings, allVertices, allNormals, allUVs, submeshData);
                }

                if (allVertices.Count > 0 && submeshData.Count > 0)
                {
                    combinedMesh.vertices = allVertices.ToArray();
                    combinedMesh.normals = allNormals.ToArray();
                    combinedMesh.uv = allUVs.ToArray();

                    // Use MaterialSlotMap as source of truth for structure
                    var slotMap = new MaterialSlotMap(extrusionProfile != null ? extrusionProfile.KeyframeCount : 1);
                    var materials = meshRenderer.sharedMaterials ?? new Material[0];

                    combinedMesh.subMeshCount = slotMap.TotalSlots;

                    for (int slotIdx = 0; slotIdx < slotMap.TotalSlots; slotIdx++)
                    {
                        Material slotMat = slotIdx < materials.Length ? materials[slotIdx] : null;

                        var tris = new int[0];
                        if (slotMat != null && submeshData.ContainsKey(slotMat))
                        {
                            tris = submeshData[slotMat].ToArray();
                        }

                        combinedMesh.SetTriangles(tris, slotIdx);
                    }

                    combinedMesh.RecalculateBounds();

                    // Clean up old mesh
                    ClearMesh();
                    meshFilter.sharedMesh = combinedMesh;

                    // Configure shader properties for multi-band colors
                    ConfigureShaderProperties();
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

        private void GenerateMeshFromBoundaries(List<List<Vector2>> boundaries,
            GlyphMeshBuilder.MeshBuildSettings settings,
            List<Vector3> allVertices, List<Vector3> allNormals, List<Vector2> allUVs,
            Dictionary<Material, List<int>> submeshData)
        {
            if (boundaries == null || boundaries.Count == 0) return;

            int vertexOffset = allVertices.Count;

            // Use GlyphMeshBuilder to generate the mesh
            var mesh = GlyphMeshBuilder.BuildMesh(boundaries, settings);

            if (mesh == null) return;

            // Extract data from generated mesh and append to combined mesh
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var uvs = mesh.uv;

            allVertices.AddRange(vertices);
            allNormals.AddRange(normals);
            allUVs.AddRange(uvs);

            // Extract triangles from each submesh
            for (int submeshIdx = 0; submeshIdx < mesh.subMeshCount; submeshIdx++)
            {
                var triangles = mesh.GetTriangles(submeshIdx);

                if (triangles.Length > 0)
                {
                    Material mat = submeshIdx < settings.Materials.Length ? settings.Materials[submeshIdx] : null;
                    if (mat != null)
                    {
                        if (!submeshData.ContainsKey(mat))
                            submeshData[mat] = new List<int>();

                        // Offset triangle indices by vertex offset
                        for (int i = 0; i < triangles.Length; i++)
                        {
                            submeshData[mat].Add(triangles[i] + vertexOffset);
                        }
                    }
                }
            }

            // Clean up temporary mesh
            if (Application.isPlaying)
                Destroy(mesh);
            else
                DestroyImmediate(mesh);
        }

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
        /// Load appropriate package material based on the current render pipeline
        /// </summary>
        private static Material LoadPackageMaterial()
        {
            // Return cached material if it exists and is still valid
            if (cachedDefaultMaterial != null)
                return cachedDefaultMaterial;

            Material defaultMat = null;

            // Check for URP/HDRP once for both editor and runtime
            var renderPipelineAsset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            bool isURP = false;

            if (renderPipelineAsset != null)
            {
                string pipelineName = renderPipelineAsset.GetType().Name;
                isURP = pipelineName.Contains("Universal");
            }

#if UNITY_EDITOR
            // Load package material based on pipeline
            string materialPath = isURP
                ? "Packages/com.lanternpines.glyphmesh3d/Runtime/Materials/GlyphTextMaterial_URP.mat"
                : "Packages/com.lanternpines.glyphmesh3d/Runtime/Materials/GlyphTextMaterial_Default.mat";

            defaultMat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

            // Fallback: search for materials by name if package path doesn't work
            if (defaultMat == null)
            {
                string materialName = isURP ? "GlyphTextMaterial_URP" : "GlyphTextMaterial_Default";
                string[] guids = AssetDatabase.FindAssets($"{materialName} t:Material");
                if (guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    defaultMat = AssetDatabase.LoadAssetAtPath<Material>(path);
                }
            }
#endif

            // Ultimate fallback: create material from shader if package materials not found
            if (defaultMat == null)
            {
                if (renderPipelineAsset != null)
                {
                    string pipelineName = renderPipelineAsset.GetType().Name;

                    if (pipelineName.Contains("Universal"))
                    {
                        var shader = Shader.Find("Universal Render Pipeline/Lit");
                        if (shader == null) shader = Shader.Find("Lightweight Render Pipeline/Lit");
                        if (shader != null) defaultMat = new Material(shader);
                    }
                    else if (pipelineName.Contains("HDRenderPipeline"))
                    {
                        var shader = Shader.Find("HDRP/Lit");
                        if (shader != null) defaultMat = new Material(shader);
                    }
                }

                if (defaultMat == null)
                {
                    var shader = Shader.Find("Standard");
                    if (shader == null) shader = Shader.Find("Diffuse");
                    if (shader != null) defaultMat = new Material(shader);
                }

                if (defaultMat == null)
                {
                    defaultMat = new Material(Shader.Find("Hidden/InternalErrorShader"));
                }
            }

            // Cache the material for reuse
            cachedDefaultMaterial = defaultMat;
            return defaultMat;
        }

        /// <summary>
        /// Get appropriate default material based on the current render pipeline
        /// </summary>
        private Material GetDefaultMaterial()
        {
            return LoadPackageMaterial();
        }

        /// <summary>
        /// Configure shader properties for multi-band extrusion colors
        /// </summary>
        private void ConfigureShaderProperties()
        {
            if (meshRenderer == null) return;

            var materials = meshRenderer.sharedMaterials;
            if (materials == null || materials.Length == 0) return;

            // Build slot map from current keyframe count
            var slotMap = new MaterialSlotMap(extrusionProfile != null ? extrusionProfile.KeyframeCount : 1);
            int bandCount = slotMap.BandCount;

            // Generate band colors
            Color[] bandColors = GenerateBandColors(bandCount);

            // Apply shader properties to all materials
            foreach (var mat in materials)
            {
                if (mat == null) continue;

                // Set basic quadrant colors (use inspector values)
                if (mat.HasProperty("_FaceColor"))
                    mat.SetColor("_FaceColor", faceColor);

                if (mat.HasProperty("_LastBandColor"))
                    mat.SetColor("_LastBandColor", lastBandColor);

                if (mat.HasProperty("_BackColor"))
                    mat.SetColor("_BackColor", backColor);

                // Set band count
                if (mat.HasProperty("_BandCount"))
                    mat.SetInt("_BandCount", bandCount);

                // Set band color array
                if (mat.HasProperty("_BandColors") && bandColors.Length > 0)
                {
                    mat.SetColorArray("_BandColors", bandColors);
                }
            }
        }

        /// <summary>
        /// Generate colors for each extrusion band based on the selected color mode
        /// </summary>
        private Color[] GenerateBandColors(int bandCount)
        {
            if (bandCount == 0)
                return new Color[0];

            Color[] colors = new Color[Mathf.Max(bandCount, 1)];

            switch (bandColorMode)
            {
                case BandColorMode.Gradient:
                    // Interpolate between start and end colors
                    for (int i = 0; i < colors.Length; i++)
                    {
                        float t = bandCount > 1 ? (float)i / (bandCount - 1) : 0.5f;
                        colors[i] = Color.Lerp(gradientStart, gradientEnd, t);
                    }
                    break;

                case BandColorMode.Rainbow:
                    // Full HSV rainbow spectrum
                    for (int i = 0; i < colors.Length; i++)
                    {
                        float hue = (float)i / bandCount;
                        colors[i] = Color.HSVToRGB(hue, 1f, 1f);
                    }
                    break;

                case BandColorMode.Custom:
                    // Use custom color array, repeat if necessary
                    for (int i = 0; i < colors.Length; i++)
                    {
                        if (customBandColors != null && customBandColors.Length > 0)
                        {
                            colors[i] = customBandColors[i % customBandColors.Length];
                        }
                        else
                        {
                            colors[i] = Color.white; // Fallback
                        }
                    }
                    break;

                case BandColorMode.SingleColor:
                    // All bands use the same color (gradientStart)
                    for (int i = 0; i < colors.Length; i++)
                    {
                        colors[i] = gradientStart;
                    }
                    break;
            }

            return colors;
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
}
