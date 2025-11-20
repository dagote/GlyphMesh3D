using TMPro;
using UnityEngine;

[CreateAssetMenu(menuName = "Glyph3D/GlyphText3DSettings", fileName = "GlyphText3DSettings")]
public class GlyphText3DSettings : ScriptableObject
{
    [Header("Font")]
    public TMP_FontAsset fontAsset;

    [Header("Simplification")]
    [Range(0f, 10f)]
    public float simplifyArcLength = 1.5f;

    [Range(40f, 100f)]
    public float cornerAngleThreshold = 80f;

    [Range(0f, 10f)]
    public float postDpEpsilon = 1.2f;
}