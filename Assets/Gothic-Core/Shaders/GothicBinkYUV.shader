// Displays a decoded Bink frame: 3 single-channel planes (Y full size, U/V half size) -> RGB.
// The planes are 16-aligned (stride), _ScaleY/_ScaleUV crop to the visible area. Rows are uploaded top-down.
// Conversion like OpenGothic shader/bink/bink.frag (BT.601, limited range).
Shader "Gothic/Bink YUV"
{
    Properties
    {
        _TexY ("Y", 2D) = "black" {}
        _TexU ("U", 2D) = "gray" {}
        _TexV ("V", 2D) = "gray" {}
        _ScaleY ("Y visible area", Vector) = (1, 1, 0, 0)
        _ScaleUV ("UV visible area", Vector) = (1, 1, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _TexY;
            sampler2D _TexU;
            sampler2D _TexV;
            float4 _ScaleY;
            float4 _ScaleUV;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                // Bink rows start at the top, texture rows at the bottom.
                o.uv = float2(v.uv.x, 1 - v.uv.y);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float y = tex2D(_TexY, i.uv * _ScaleY.xy).r * 255;
                float u = tex2D(_TexU, i.uv * _ScaleUV.xy).r * 255;
                float v = tex2D(_TexV, i.uv * _ScaleUV.xy).r * 255;

                float r = 1.164 * (y - 16) + 1.596 * (v - 128);
                float g = 1.164 * (y - 16) - 0.813 * (v - 128) - 0.391 * (u - 128);
                float b = 1.164 * (y - 16) + 2.018 * (u - 128);

                float3 color = saturate(float3(r, g, b) / 255);
            #if !UNITY_COLORSPACE_GAMMA
                color = GammaToLinearSpace(color);
            #endif
                return fixed4(color, 1);
            }
            ENDCG
        }
    }
}
