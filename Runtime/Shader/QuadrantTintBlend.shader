Shader "Custom/GlyphText3dShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}

        // Front Face
        _FrontFaceColor ("Front Face Color", Color) = (1,1,1,1)

        [Space(10)]
        // Extrusion Colors 1-6
        _Extrusion1Color ("Extrusion 1 Color", Color) = (1,1,1,1)
        _Extrusion2Color ("Extrusion 2 Color", Color) = (1,1,1,1)
        _Extrusion3Color ("Extrusion 3 Color", Color) = (1,1,1,1)
        _Extrusion4Color ("Extrusion 4 Color", Color) = (1,1,1,1)
        _Extrusion5Color ("Extrusion 5 Color", Color) = (1,1,1,1)
        _Extrusion6Color ("Extrusion 6 Color", Color) = (1,1,1,1)

        [Space(10)]
        // Final Extrusion and Back Face
        _FinalExtrusionColor ("Final Extrusion Color", Color) = (1,1,1,1)
        _BackFaceColor ("Back Face Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _FrontFaceColor;
            float4 _Extrusion1Color, _Extrusion2Color, _Extrusion3Color;
            float4 _Extrusion4Color, _Extrusion5Color, _Extrusion6Color;
            float4 _FinalExtrusionColor, _BackFaceColor;

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float4 col = tex2D(_MainTex, uv);

                // 3x3 Grid Layout:
                // Row 2 (V: 0.66-1):   [Front Face]  [Ext 1]      [Ext 2]
                // Row 1 (V: 0.33-0.66): [Ext 3]       [Ext 4]      [Ext 5]
                // Row 0 (V: 0-0.33):   [Ext 6]       [Final Ext]  [Back Face]

                float4 tint;

                // Determine column (0, 1, or 2)
                int col_idx = 0;
                if (uv.x >= 0.66666) col_idx = 2;
                else if (uv.x >= 0.33333) col_idx = 1;

                // Determine row (0, 1, or 2)
                int row_idx = 0;
                if (uv.y >= 0.66666) row_idx = 2;
                else if (uv.y >= 0.33333) row_idx = 1;

                // Map grid position to color
                if (row_idx == 2) // Top row
                {
                    if (col_idx == 0) tint = _FrontFaceColor;
                    else if (col_idx == 1) tint = _Extrusion1Color;
                    else tint = _Extrusion2Color;
                }
                else if (row_idx == 1) // Middle row
                {
                    if (col_idx == 0) tint = _Extrusion3Color;
                    else if (col_idx == 1) tint = _Extrusion4Color;
                    else tint = _Extrusion5Color;
                }
                else // Bottom row (row_idx == 0)
                {
                    if (col_idx == 0) tint = _Extrusion6Color;
                    else if (col_idx == 1) tint = _FinalExtrusionColor;
                    else tint = _BackFaceColor;
                }

                float4 result = col * tint;
                result.a = col.a * tint.a;
                return result;
            }
            ENDCG
        }
    }
}