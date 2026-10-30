// Comfort vignette of the VR camera (VrMoveVignette): a disc in front of the eyes that is clear in the middle and
// darkens towards its edge. The clear part (_Aperture, 0..1 of the disc radius) and its soft edge (_Feather) are set
// every frame while the player is moved. ZTest Always, so near walls don't cut it. The render queue comes from the
// material (VRMoveVignette): after the world's transparents, before the screen fade and the on-top UI.
Shader "Gothic/VR Move Vignette"
{
    Properties
    {
        _Color ("Color", Color) = (0,0,0,1)
        _Aperture ("Aperture", Range(0, 1)) = 1
        _Feather ("Feather", Range(0.01, 1)) = 0.2
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        // Keep the target's alpha: the color is a pure overlay.
        ColorMask RGB

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                // x: distance from the disc's center, 0..1 of its radius.
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float radius : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            float _Aperture;
            float _Feather;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.radius = v.uv.x;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                float alpha = smoothstep(_Aperture, _Aperture + _Feather, IN.radius);
                return fixed4(_Color.rgb, _Color.a * alpha);
            }
            ENDCG
        }
    }
}
