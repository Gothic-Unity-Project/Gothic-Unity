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
            "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "RenderQueue" = "Transparent"
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                half4 color : COLOR;
                half3 normal : NORMAL;
                float4 uv : TEXCOORD0; // uv, array slice, max mip level
                float4 textureAnimation : TEXCOORD1; // linear anim x, linear anim y, frame count, fps

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                half3 normal : NORMAL;
                float4 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                half3 diffuse : COLOR;
                int frameIndex : TEXCOORD2;

                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D_ARRAY(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_TexelSize;
            CBUFFER_END

            #include "GothicIncludes.hlsl"

            half3 DiffuseLighting(v2f i, appdata v)
            {
                half3 diffuse = _SunColor + _AmbientColor;

                //for (int j = 0; j < min(MAX_VISIBLE_LIGHTS, unity_LightData.y); j++)
                //{
                //    int lightIndex = GetPerObjectLightIndex(j);
                //    Light light = CustomGetAdditionalPerObjectLight(lightIndex, i.worldPos);
                //    diffuse += AdditionalUnityLightDiffuse(light, i.normal);
                //}

                return diffuse;
            }

            v2f vert(appdata v)
            {
                v2f o;

                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.worldPos = TransformObjectToWorld(v.vertex);
                o.vertex = TransformObjectToHClip(v.vertex);
                float2 movingUv = v.uv.xy * REFERENCE_TEX_ARRAY_SIZE * _MainTex_TexelSize.xy + v.textureAnimation.xy * _Time.y * 1000;
                o.uv = float4(movingUv, v.uv.zw);
                o.normal = TransformObjectToWorldNormal(v.normal);
                o.diffuse = DiffuseLighting(o, v);
                o.frameIndex = (_Time.y * v.textureAnimation.w) % v.textureAnimation.z;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float mipLevel = CalcMipLevel(i.uv.xy * _MainTex_TexelSize.zw);
                half4 albedo = SAMPLE_TEXTURE2D_ARRAY_LOD(_MainTex, sampler_MainTex, i.uv.xy, i.uv.z + i.frameIndex, clamp(mipLevel, 0, i.uv.w));
                half3 diffuse = albedo * i.diffuse;

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
