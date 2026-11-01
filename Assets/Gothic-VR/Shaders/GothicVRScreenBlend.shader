// Screen blend of the VR camera (VrWorldEffectView): a script effect's tint (DEMENTOR_FX, a spell's flash) drawn on the
// inner faces of a small cube around the head, like Gothic/VR Screen Fade. A texture (the Dementors' animated
// ScreenFX_Dem) is laid over each eye's view by its screen position. ZTest Always, so walls the head is inside of are
// covered as well. The render queue comes from the material, just below the screen fade.
Shader "Gothic/VR Screen Blend"
{
    Properties
    {
        _Color ("Color", Color) = (0,0,0,0)
        _MainTex ("Texture", 2D) = "white" {}
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
                float4 screenPos : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            sampler2D _MainTex;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.screenPos = ComputeScreenPos(OUT.vertex);
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                float2 uv = IN.screenPos.xy / IN.screenPos.w;
                return tex2D(_MainTex, uv) * _Color;
            }
            ENDCG
        }
    }
}
