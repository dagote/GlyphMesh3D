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

        [Tooltip("Character spacing used during generation")]
        public float characterSpacing;

        [Tooltip("Whether XAtlas UV unwrapping was used")]
        public bool useXAtlasUVUnwrapping;

        [Header("Materials")]
        [Tooltip("Materials used during generation (one per material slot)")]
        public Material[] materials;

        [Tooltip("Number of material slots used during generation")]
        public int materialSlotCount;

        [Header("Font Metrics")]
        [Tooltip("Pre-calculated line height in normalized font units")] 
        public float lineHeight;

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
    /// Stores mesh asset and metadata for a single character glyph.
    /// </summary>
    [Serializable]
    public class GlyphMeshData
    {
        [Header("Character Info")]
        public char character;

        [Header("Character Metrics")]
        [Tooltip("Horizontal advance width for this character")]
        public float advanceWidth;

        [Tooltip("Horizontal bearing X (offset from origin to place mesh)")]
        public float bearingX;

        [Tooltip("Baseline offset (vertical position relative to baseline)")]
        public float baselineOffset;

        [Tooltip("Bounding box of the glyph")]
        public Bounds bounds;

        [Header("Mesh Asset")]
        [Tooltip("The Unity Mesh asset for this glyph")]
        public Mesh mesh;
    }
}
