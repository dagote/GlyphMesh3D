using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace LanternPines.GlyphMesh3D.Core
{
    /// <summary>
    /// Stores generated 3D glyph mesh data and generation settings.
    /// Created by GlyphText3DGenerator and consumed by GlyphText3D runtime component.
    /// </summary>
    [CreateAssetMenu(menuName = "Glyph3D/GlyphText3DAsset", fileName = "GlyphText3DAsset")]
    public class GlyphText3DAsset : ScriptableObject
    {
        [Header("Font Reference")]
        public TMP_FontAsset fontAsset;

        [Header("Baked Generation Settings")]
        [Tooltip("Simplification parameter used during generation")]
        public float simplifyArcLength;

        [Tooltip("Corner angle threshold used during generation")]
        public float cornerAngleThreshold;

        [Tooltip("Post Douglas-Peucker epsilon used during generation")]
        public float postDpEpsilon;

        [Tooltip("Extrusion depth used during generation")]
        public float extrusionDepth;

        [Tooltip("Extrusion width used during generation")]
        public float extrusionWidth;

        [Tooltip("Extrusion profile curve used during generation")]
        public AnimationCurve extrusionProfile;

        [Tooltip("Text size used during generation")]
        public float textSize;

        [Tooltip("Character spacing used during generation")]
        public float characterSpacing;

        [Tooltip("Whether XAtlas UV unwrapping was used")]
        public bool useXAtlasUVUnwrapping;

        [Header("Materials")]
        [Tooltip("Materials used during generation (one per material slot)")]
        public Material[] materials;

        [Tooltip("Number of material slots used during generation")]
        public int materialSlotCount;

        [Header("Pre-Generated Glyph Data")]
        [Tooltip("All generated glyphs from the source text")]
        public GlyphMeshData[] glyphMeshes;

        /// <summary>
        /// Get glyph mesh data for a specific character.
        /// </summary>
        public GlyphMeshData GetGlyphData(char character)
        {
            if (glyphMeshes == null) return null;

            foreach (var glyph in glyphMeshes)
            {
                if (glyph.character == character)
                    return glyph;
            }

            return null;
        }
    }

    /// <summary>
    /// Stores all mesh data and metadata for a single character glyph.
    /// </summary>
    [Serializable]
    public class GlyphMeshData
    {
        [Header("Character Info")]
        public char character;

        [Header("Character Metrics")]
        [Tooltip("Horizontal advance width for this character")]
        public float advanceWidth;

        [Tooltip("Baseline offset")]
        public float baselineOffset;

        [Tooltip("Bounding box of the glyph")]
        public Bounds bounds;

        [Tooltip("X offset used during generation (for positioning)")]
        public float xOffset;

        [Header("Mesh Data")]
        [Tooltip("Vertex positions")]
        public Vector3[] vertices;

        [Tooltip("Vertex normals")]
        public Vector3[] normals;

        [Tooltip("Vertex UVs")]
        public Vector2[] uvs;

        [Tooltip("Vertex colors (if any)")]
        public Color[] colors;

        [Tooltip("Submesh triangle indices - one array per material slot")]
        public int[][] submeshTriangles;

        [Tooltip("Number of material slots (submeshes)")]
        public int submeshCount;
    }
}
