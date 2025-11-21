using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LanternPines.GlyphMesh3D.Core
{
    /// <summary>
    /// Runtime component that instantiates and positions 3D glyph meshes from a GlyphText3DAsset.
    /// This is a stub implementation - full mesh instantiation logic will be added later.
    /// </summary>
    [ExecuteInEditMode]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class GlyphText3D : MonoBehaviour
    {
        [Header("Asset Reference")]
        [Tooltip("The generated glyph asset containing pre-baked meshes and settings")]
        public GlyphText3DAsset asset;

        [Header("Text Content")]
        [SerializeField]
        [Tooltip("The text to display using the asset's glyphs")]
        private string text = "Sample";

        // TODO: Implement runtime mesh instantiation from asset
        // Features to add:
        // - Load glyph meshes from asset
        // - Position glyphs based on advance widths and baselines from asset metadata
        // - Build combined mesh for text string
        // - Support dynamic text updates
        // - Handle material assignment from asset

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

        private void OnValidate()
        {
            // TODO: Regenerate mesh when text or asset changes
            if (asset != null)
            {
                // Placeholder for future implementation
                // RegenerateMeshFromAsset();
            }
        }

        private void Awake()
        {
            // TODO: Initialize components and generate initial mesh
        }

        // private void RegenerateMeshFromAsset()
        // {
        //     // TODO: Implementation
        //     // 1. Read glyph data from asset for each character in 'text'
        //     // 2. Calculate glyph positions using advance widths
        //     // 3. Combine meshes into single mesh
        //     // 4. Apply to MeshFilter
        // }
    }
}
