Shader "Lit/Water"
{
    Properties
    {
        _MainTex("Texture", 2DArray) = "" {}
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent"
        }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 uv : TEXCOORD0; // uv, array slice, max mip level
                float4 textureAnimation : TEXCOORD1; // linear anim x, linear anim y, frame count, fps

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                // Animated textures occupy consecutive array slices; this is the current frame offset.
                // nointerpolation: a frame index must never be blended between vertices.
                nointerpolation float frameIndex : TEXCOORD2;
                half3 diffuse : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D_ARRAY(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_TexelSize;
            CBUFFER_END

            #include "GothicIncludes.hlsl"

            v2f vert(appdata v)
            {
                v2f o;

                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.worldPos = TransformObjectToWorld(v.vertex.xyz);
                o.vertex = TransformObjectToHClip(v.vertex.xyz);
                float2 movingUv = v.uv.xy * REFERENCE_TEX_ARRAY_SIZE * _MainTex_TexelSize.xy + v.textureAnimation.xy * _Time.y * 1000;
                o.uv = float4(movingUv, v.uv.zw);
                // textureAnimation.z is frameCount+1 (1 for non-animated textures -> frameIndex stays 0).
                o.frameIndex = floor(fmod(_Time.y * v.textureAnimation.w, max(v.textureAnimation.z, 1.0)));
                o.diffuse = _SunColor + _AmbientColor;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float mipLevel = CalcMipLevel(i.uv.xy * _MainTex_TexelSize.zw);
                half4 albedo = SAMPLE_TEXTURE2D_ARRAY_LOD(_MainTex, sampler_MainTex, i.uv.xy, i.uv.z + i.frameIndex,
                    clamp(mipLevel, 0, i.uv.w));
                half3 diffuse = albedo.rgb * i.diffuse;

                diffuse = ApplyUnderWaterEffect(diffuse);

#if FOG_LINEAR || FOG_EXP || FOG_EXP2
                diffuse = ApplyFog(diffuse, i.worldPos);
#endif
                return half4(diffuse, 0.5 * albedo.a);
            }
            ENDHLSL
        }
    }
}
