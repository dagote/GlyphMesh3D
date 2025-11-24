using UnityEngine;
using UnityEditor;
using LanternPines.GlyphMesh3D.Core;

namespace LanternPines.GlyphMesh3D.Editor
{
    [CustomEditor(typeof(GlyphText3DGenerator))]
    public class GlyphText3DGeneratorEditor : UnityEditor.Editor
    {
        private SerializedProperty fontAsset;
        private SerializedProperty text;
        private SerializedProperty extrusionDepth;
        private SerializedProperty extrusionWidth;
        private SerializedProperty extrusionProfile;

        private SerializedProperty bandColorMode;
        private SerializedProperty gradientStart;
        private SerializedProperty gradientEnd;
        private SerializedProperty customBandColors;
        private SerializedProperty faceColor;
        private SerializedProperty lastBandColor;
        private SerializedProperty backColor;

        private SerializedProperty simplifyArcLength;
        private SerializedProperty cornerAngleThreshold;
        private SerializedProperty postDpEpsilon;

        private SerializedProperty useXAtlasUVUnwrapping;
        private SerializedProperty uvPadding;
        private SerializedProperty uvResolution;
        private SerializedProperty texelsPerUnit;

        private bool showAdvancedSimplification = false;
        private bool showUVSettings = false;

        private void OnEnable()
        {
            // Find all serialized properties
            fontAsset = serializedObject.FindProperty("fontAsset");
            text = serializedObject.FindProperty("text");
            extrusionDepth = serializedObject.FindProperty("extrusionDepth");
            extrusionWidth = serializedObject.FindProperty("extrusionWidth");
            extrusionProfile = serializedObject.FindProperty("extrusionProfile");

            bandColorMode = serializedObject.FindProperty("bandColorMode");
            gradientStart = serializedObject.FindProperty("gradientStart");
            gradientEnd = serializedObject.FindProperty("gradientEnd");
            customBandColors = serializedObject.FindProperty("customBandColors");
            faceColor = serializedObject.FindProperty("faceColor");
            lastBandColor = serializedObject.FindProperty("lastBandColor");
            backColor = serializedObject.FindProperty("backColor");

            simplifyArcLength = serializedObject.FindProperty("simplifyArcLength");
            cornerAngleThreshold = serializedObject.FindProperty("cornerAngleThreshold");
            postDpEpsilon = serializedObject.FindProperty("postDpEpsilon");

            useXAtlasUVUnwrapping = serializedObject.FindProperty("useXAtlasUVUnwrapping");
            uvPadding = serializedObject.FindProperty("uvPadding");
            uvResolution = serializedObject.FindProperty("uvResolution");
            texelsPerUnit = serializedObject.FindProperty("texelsPerUnit");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var generator = (GlyphText3DGenerator)target;

            // Text Settings
            EditorGUILayout.LabelField("Text Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(fontAsset);
            EditorGUILayout.PropertyField(text);

            EditorGUILayout.Space();

            // Extrusion Settings
            EditorGUILayout.LabelField("Extrusion Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(extrusionDepth);
            EditorGUILayout.PropertyField(extrusionWidth);
            EditorGUILayout.PropertyField(extrusionProfile);

            // Show band count info
            int bandCount = GetBandCount(generator);
            if (bandCount > 0)
            {
                EditorGUILayout.HelpBox($"Current band count: {bandCount} (based on {bandCount + 1} keyframes)", MessageType.Info);
            }

            EditorGUILayout.Space();

            // Band Colors Section
            EditorGUILayout.LabelField("Band Colors", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(bandColorMode);

            BandColorMode mode = (BandColorMode)bandColorMode.enumValueIndex;

            // Show relevant fields based on color mode
            switch (mode)
            {
                case BandColorMode.Gradient:
                    EditorGUILayout.PropertyField(gradientStart, new GUIContent("Start Color"));
                    EditorGUILayout.PropertyField(gradientEnd, new GUIContent("End Color"));
                    DrawColorPreview(generator, bandCount);
                    break;

                case BandColorMode.Rainbow:
                    EditorGUILayout.HelpBox("Rainbow mode generates colors automatically across the HSV spectrum.", MessageType.Info);
                    DrawColorPreview(generator, bandCount);
                    break;

                case BandColorMode.SingleColor:
                    EditorGUILayout.PropertyField(gradientStart, new GUIContent("Band Color"));
                    DrawColorPreview(generator, bandCount);
                    break;

                case BandColorMode.Custom:
                    EditorGUILayout.PropertyField(customBandColors, new GUIContent("Custom Band Colors"), true);

                    // Helper buttons
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Auto-Populate"))
                    {
                        AutoPopulateCustomColors(generator);
                    }
                    if (GUILayout.Button("Clear"))
                    {
                        customBandColors.arraySize = 0;
                        serializedObject.ApplyModifiedProperties();
                    }
                    EditorGUILayout.EndHorizontal();

                    // Warning if array is too short
                    if (customBandColors.arraySize < bandCount && bandCount > 0)
                    {
                        EditorGUILayout.HelpBox($"Custom array has {customBandColors.arraySize} colors but {bandCount} bands. Colors will repeat.", MessageType.Warning);
                    }

                    DrawColorPreview(generator, bandCount);
                    break;
            }

            EditorGUILayout.Space();

            // Cap colors
            EditorGUILayout.LabelField("Cap Colors", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(faceColor, new GUIContent("Front Cap Color"));
            EditorGUILayout.PropertyField(lastBandColor, new GUIContent("Last Band Color"));
            EditorGUILayout.PropertyField(backColor, new GUIContent("Back Cap Color"));

            EditorGUILayout.Space();

            // Advanced Simplification (collapsible)
            showAdvancedSimplification = EditorGUILayout.Foldout(showAdvancedSimplification, "Advanced Simplification", true);
            if (showAdvancedSimplification)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(simplifyArcLength);
                EditorGUILayout.PropertyField(cornerAngleThreshold);
                EditorGUILayout.PropertyField(postDpEpsilon);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();

            // UV Settings (collapsible)
            showUVSettings = EditorGUILayout.Foldout(showUVSettings, "UV Unwrapping", true);
            if (showUVSettings)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(useXAtlasUVUnwrapping);
                EditorGUILayout.PropertyField(uvPadding);
                EditorGUILayout.PropertyField(uvResolution);
                EditorGUILayout.PropertyField(texelsPerUnit);
                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();
        }

        private int GetBandCount(GlyphText3DGenerator generator)
        {
            // Access internal extrusionProfile via reflection or assume structure
            var profile = extrusionProfile.FindPropertyRelative("curve");
            if (profile != null && profile.animationCurveValue != null)
            {
                int keyframeCount = profile.animationCurveValue.keys.Length;
                return Mathf.Max(0, keyframeCount - 1);
            }
            return 0;
        }

        private void AutoPopulateCustomColors(GlyphText3DGenerator generator)
        {
            int bandCount = GetBandCount(generator);
            if (bandCount == 0)
            {
                EditorUtility.DisplayDialog("No Bands", "Add keyframes to the extrusion curve first.", "OK");
                return;
            }

            customBandColors.arraySize = bandCount;

            // Fill with gradient colors as starting point
            for (int i = 0; i < bandCount; i++)
            {
                float t = bandCount > 1 ? (float)i / (bandCount - 1) : 0.5f;
                Color color = Color.Lerp(gradientStart.colorValue, gradientEnd.colorValue, t);
                customBandColors.GetArrayElementAtIndex(i).colorValue = color;
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawColorPreview(GlyphText3DGenerator generator, int bandCount)
        {
            if (bandCount == 0) return;

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Preview:", EditorStyles.miniLabel);

            // Create preview colors based on current settings
            Color[] previewColors = GeneratePreviewColors(generator, bandCount);

            // Draw color boxes
            EditorGUILayout.BeginHorizontal();
            float boxWidth = Mathf.Min(EditorGUIUtility.currentViewWidth / bandCount - 5, 40);

            for (int i = 0; i < previewColors.Length; i++)
            {
                Rect rect = GUILayoutUtility.GetRect(boxWidth, 20, GUILayout.Width(boxWidth));
                EditorGUI.DrawRect(rect, previewColors[i]);
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(5);
        }

        private Color[] GeneratePreviewColors(GlyphText3DGenerator generator, int bandCount)
        {
            BandColorMode mode = (BandColorMode)bandColorMode.enumValueIndex;
            Color[] colors = new Color[bandCount];

            switch (mode)
            {
                case BandColorMode.Gradient:
                    for (int i = 0; i < bandCount; i++)
                    {
                        float t = bandCount > 1 ? (float)i / (bandCount - 1) : 0.5f;
                        colors[i] = Color.Lerp(gradientStart.colorValue, gradientEnd.colorValue, t);
                    }
                    break;

                case BandColorMode.Rainbow:
                    for (int i = 0; i < bandCount; i++)
                    {
                        float hue = (float)i / bandCount;
                        colors[i] = Color.HSVToRGB(hue, 1f, 1f);
                    }
                    break;

                case BandColorMode.SingleColor:
                    for (int i = 0; i < bandCount; i++)
                    {
                        colors[i] = gradientStart.colorValue;
                    }
                    break;

                case BandColorMode.Custom:
                    for (int i = 0; i < bandCount; i++)
                    {
                        if (customBandColors.arraySize > 0)
                        {
                            int idx = i % customBandColors.arraySize;
                            colors[i] = customBandColors.GetArrayElementAtIndex(idx).colorValue;
                        }
                        else
                        {
                            colors[i] = Color.white;
                        }
                    }
                    break;
            }

            return colors;
        }
    }
}
