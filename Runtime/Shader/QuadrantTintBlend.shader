Shader "Custom/QuadrantTintBlend"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _TintQ1 ("Tint Q1", Color) = (1,1,1,1)
        _TintQ2 ("Tint Q2", Color) = (1,1,1,1)
        _TintQ3 ("Tint Q3", Color) = (1,1,1,1)
        _TintQ4 ("Tint Q4", Color) = (1,1,1,1)
        // Removed BlendMode property for regular Unity blending
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
            float4 _TintQ1, _TintQ2, _TintQ3, _TintQ4;

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

                float4 tint;
                // Reverse quadrant order:
                // Q1: bottom-left, Q2: bottom-right, Q3: top-left, Q4: top-right
                if (uv.x < 0.5 && uv.y < 0.5)
                    tint = _TintQ3;
                else if (uv.x >= 0.5 && uv.y < 0.5)
                    tint = _TintQ4;
                else if (uv.x < 0.5 && uv.y >= 0.5)
                    tint = _TintQ1;
                else
                    tint = _TintQ2;

                float4 result = col * tint;
                result.a = col.a * tint.a;
                return result;
            }
            ENDCG
        }
    }
}