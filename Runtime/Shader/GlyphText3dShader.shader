Shader "Custom/GlyphText3dShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}

        // Front Face
        _FrontFaceColor ("Front Face Color", Color) = (1,1,1,1)

        [Space(10)]
        [Header(Extrusion Colors)]

        // Extrusion Colors 1-6
        _Extrusion1Color ("Extrusion 1 Color", Color) = (0.9,0.9,0.9,1)
        _Extrusion2Color ("Extrusion 2 Color", Color) = (0.8,0.8,0.8,1)
        _Extrusion3Color ("Extrusion 3 Color", Color) = (0.7,0.7,0.7,1)
        _Extrusion4Color ("Extrusion 4 Color", Color) = (0.6,0.6,0.6,1)
        _Extrusion5Color ("Extrusion 5 Color", Color) = (0.5,0.5,0.5,1)
        _Extrusion6Color ("Extrusion 6 Color", Color) = (0.4,0.4,0.4,1)

        [Space(10)]

        // Final Extrusion and Back Face
        _FinalExtrusionColor ("Final Extrusion Color", Color) = (0.3,0.3,0.3,1)
        _BackFaceColor ("Back Face Color", Color) = (0.2,0.2,0.2,1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        Pass
        {
            Tags { "LightMode"="ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;

            // Front Face
            float4 _FrontFaceColor;

            // Extrusion Colors
            float4 _Extrusion1Color;
            float4 _Extrusion2Color;
            float4 _Extrusion3Color;
            float4 _Extrusion4Color;
            float4 _Extrusion5Color;
            float4 _Extrusion6Color;

            // Final and Back
            float4 _FinalExtrusionColor;
            float4 _BackFaceColor;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR; // Vertex color stores band/face index
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float4 vertexColor : COLOR;
                LIGHTING_COORDS(2,3)
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.vertexColor = v.color;
                TRANSFER_VERTEX_TO_FRAGMENT(o);
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                // Sample texture
                float4 texColor = tex2D(_MainTex, i.uv);

                // Determine which color to use based on vertex color
                // The red channel stores the face/band index:
                // 0 = front face
                // 1-6 = extrusion bands 1-6
                // 7 = final extrusion
                // 8 = back face
                float index = i.vertexColor.r * 255.0; // Convert from 0-1 to 0-255

                float4 baseColor;

                if (index < 0.5)
                {
                    // Front face
                    baseColor = _FrontFaceColor;
                }
                else if (index < 1.5)
                {
                    // Extrusion 1
                    baseColor = _Extrusion1Color;
                }
                else if (index < 2.5)
                {
                    // Extrusion 2
                    baseColor = _Extrusion2Color;
                }
                else if (index < 3.5)
                {
                    // Extrusion 3
                    baseColor = _Extrusion3Color;
                }
                else if (index < 4.5)
                {
                    // Extrusion 4
                    baseColor = _Extrusion4Color;
                }
                else if (index < 5.5)
                {
                    // Extrusion 5
                    baseColor = _Extrusion5Color;
                }
                else if (index < 6.5)
                {
                    // Extrusion 6
                    baseColor = _Extrusion6Color;
                }
                else if (index < 7.5)
                {
                    // Final extrusion
                    baseColor = _FinalExtrusionColor;
                }
                else
                {
                    // Back face
                    baseColor = _BackFaceColor;
                }

                // Apply texture
                float4 color = texColor * baseColor;

                // Simple lighting
                float3 worldNormal = normalize(i.worldNormal);
                float nl = max(0, dot(worldNormal, _WorldSpaceLightPos0.xyz));
                float3 lighting = nl * _LightColor0.rgb + UNITY_LIGHTMODEL_AMBIENT.rgb;

                color.rgb *= lighting;

                // Apply shadow attenuation
                float atten = LIGHT_ATTENUATION(i);
                color.rgb *= atten;

                return color;
            }
            ENDCG
        }

        // Shadow casting pass
        Pass
        {
            Tags { "LightMode"="ShadowCaster" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                V2F_SHADOW_CASTER;
            };

            v2f vert(appdata v)
            {
                v2f o;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }

    FallBack "Diffuse"
}
