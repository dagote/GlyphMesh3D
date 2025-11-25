Shader "Custom/QuadrantTintBlend"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _ColorFront ("Front Color", Color) = (1,1,1,1)
        _ColorBack ("Back Color", Color) = (1,1,1,1)
        _ColorExtrusion1 ("Extrusion Color 1", Color) = (1,1,1,1)
        _ColorExtrusion2 ("Extrusion Color 2", Color) = (1,1,1,1)
        _ColorExtrusion3 ("Extrusion Color 3", Color) = (1,1,1,1)
        _ColorExtrusion4 ("Extrusion Color 4", Color) = (1,1,1,1)
        _ColorExtrusion5 ("Extrusion Color 5", Color) = (1,1,1,1)
        _ColorExtrusion6 ("Extrusion Color 6", Color) = (1,1,1,1)
        _ColorFinal ("Final Extrusion Color", Color) = (1,1,1,1)
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
            float4 _ColorFront, _ColorBack;
            float4 _ColorExtrusion1, _ColorExtrusion2, _ColorExtrusion3;
            float4 _ColorExtrusion4, _ColorExtrusion5, _ColorExtrusion6;
            float4 _ColorFinal;

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float4 col = tex2D(_MainTex, i.uv);

                // Color index is encoded in the vertex color's R channel (0-8)
                // 0 = Front, 1 = Back, 2-7 = Extrusion 1-6, 8 = Final
                int colorIndex = (int)(i.color.r * 8.0 + 0.5);

                float4 tint;
                if (colorIndex == 0)
                    tint = _ColorFront;
                else if (colorIndex == 1)
                    tint = _ColorBack;
                else if (colorIndex == 2)
                    tint = _ColorExtrusion1;
                else if (colorIndex == 3)
                    tint = _ColorExtrusion2;
                else if (colorIndex == 4)
                    tint = _ColorExtrusion3;
                else if (colorIndex == 5)
                    tint = _ColorExtrusion4;
                else if (colorIndex == 6)
                    tint = _ColorExtrusion5;
                else if (colorIndex == 7)
                    tint = _ColorExtrusion6;
                else // colorIndex == 8
                    tint = _ColorFinal;

                float4 result = col * tint;
                result.a = col.a * tint.a;
                return result;
            }
            ENDCG
        }
    }
}