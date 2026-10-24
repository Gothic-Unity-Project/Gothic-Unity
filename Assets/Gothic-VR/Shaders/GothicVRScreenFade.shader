// Screen fade of the VR camera (ScreenFade, XRI backend): a single color drawn on the inner faces of a small cube
// around the head. ZTest Always, so walls the head is inside of are covered as well. The render queue comes from the
// material (VRScreenFade-XRI): after the world's transparents, before the on-top UI (Gothic/UI OnTop, 3899+), so
// dialogs, subtitles and menus stay readable on black.
Shader "Gothic/VR Screen Fade"
{
    Properties
    {
        _Color ("Color", Color) = (0,0,0,0)
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
        }

        // Inner faces only: one layer per pixel from inside the cube.
        Cull Front
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                return _Color;
            }
            ENDCG
        }
    }
}
