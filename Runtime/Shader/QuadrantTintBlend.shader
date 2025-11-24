Shader "Custom/QuadrantTintBlend"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _FaceColor ("Face Color (Q1)", Color) = (1,1,1,1)
        _LastBandColor ("Last Band Color (Q3)", Color) = (1,1,1,1)
        _BackColor ("Back Color (Q4)", Color) = (1,1,1,1)
        _BandCount ("Band Count", Int) = 1
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
            float4 _FaceColor;
            float4 _BandColors[256];  // Expandable array up to 256 bands
            float4 _LastBandColor;
            float4 _BackColor;
            int _BandCount;

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;  // Band index stored in UV2
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float bandIndex : TEXCOORD1;  // Band index passed to fragment shader
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.bandIndex = v.uv2.x;  // Extract band index from UV2.x
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float4 col = tex2D(_MainTex, uv);

                float4 tint;
                // Quadrant mapping:
                // Q1 (top-left): Front cap - _FaceColor
                // Q2 (top-right): Looping bands - _BandColors[bandIndex]
                // Q3 (bottom-left): Final band - _LastBandColor
                // Q4 (bottom-right): Back cap - _BackColor

                if (uv.x < 0.5 && uv.y < 0.5)
                {
                    // Q3 - Final band
                    tint = _LastBandColor;
                }
                else if (uv.x >= 0.5 && uv.y < 0.5)
                {
                    // Q4 - Back cap
                    tint = _BackColor;
                }
                else if (uv.x < 0.5 && uv.y >= 0.5)
                {
                    // Q1 - Front cap
                    tint = _FaceColor;
                }
                else
                {
                    // Q2 - Looping bands (use array)
                    int bandIdx = clamp((int)i.bandIndex, 0, _BandCount - 1);
                    tint = _BandColors[bandIdx];
                }

                float4 result = col * tint;
                result.a = col.a * tint.a;
                return result;
            }
            ENDCG
        }
    }
}