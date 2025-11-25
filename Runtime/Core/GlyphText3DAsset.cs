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

        [Header("Character Metrics")]
        [Tooltip("Per-character advance widths in atlas pixels (for proper spacing)")]
        public System.Collections.Generic.Dictionary<char, float> characterAdvances = new System.Collections.Generic.Dictionary<char, float>();

        // TODO: Add per-glyph mesh data storage
        // - Character to mesh mapping
        // - Vertex/triangle data per glyph
        // - Baseline and bounds information

        // Placeholder for future implementation
        // public GlyphMeshData[] glyphMeshes;
    }
}
