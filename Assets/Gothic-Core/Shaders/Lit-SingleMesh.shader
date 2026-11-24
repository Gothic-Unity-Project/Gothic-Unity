Shader "Lit/SingleMesh"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
        // NPCs' Mdl_SetModelFatness: the body pushed out along its normals (m).
        [HideInInspector]_Fatness("Fatness", Float) = 0
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                half3 color : COLOR;
                half3 normal : NORMAL;
                half2 uv : TEXCOORD0;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                half2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                // The sun's part (shadowed per pixel); diffuse: lights.
                half3 sunDiffuse : TEXCOORD2;
                half3 normalWS : TEXCOORD3;
                half3 diffuse : COLOR;

                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Texture/sampler live outside the CBUFFER (a sampler inside UnityPerMaterial makes the shader
            // SRP-Batcher-incompatible). Only the _ST stays as the per-material constant.
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Fatness;
            CBUFFER_END

            #include "GothicIncludes.hlsl"
            #include "GothicShadows.hlsl"

            // Lights - the sun and the ambient come per pixel (SunAndAmbientShadowed).
            half3 DiffuseLighting(half3 normal, float3 worldPos)
            {
                half3 diffuse = 0;

                for (int j = 0; j < min(MAX_VISIBLE_LIGHTS, unity_LightData.y); j++)
                {
                    int lightIndex = GetPerObjectLightIndex(j);
                    // Shadow casting lights come per pixel (GothicShadowedLights).
                    if (GothicIsShadowedLight(lightIndex))
                        continue;
                    Light light = CustomGetAdditionalPerObjectLight(lightIndex, worldPos);
                    diffuse += AdditionalUnityLightDiffuse(light, normal);
                }

                return diffuse;
            }

            v2f vert(appdata v)
            {
                v2f o;

                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                // Fatter or thinner NPCs (0 for everything else).
                float3 position = v.vertex.xyz + v.normal * _Fatness;
                o.worldPos = TransformObjectToWorld(position);
                o.vertex = TransformObjectToHClip(position);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                half3 normalWS = TransformObjectToWorldNormal(v.normal);
                o.sunDiffuse = SunDiffuse(normalWS, v.color);
                o.normalWS = normalWS;
                o.diffuse = DiffuseLighting(normalWS, o.worldPos);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                GothicOcclusion occlusion = GothicGetOcclusion(i.vertex);
                half3 light = GothicSunAndAmbient(i.sunDiffuse, i.worldPos, occlusion) +
                    (i.diffuse + GothicShadowedLights(i.worldPos, normalize(i.normalWS), false)) * occlusion.direct;
                half3 diffuse = albedo.rgb * GothicClampLight(light);

                diffuse = ApplyUnderWaterEffect(diffuse);

#if FOG_LINEAR || FOG_EXP || FOG_EXP2
                diffuse = ApplyFog(diffuse, i.worldPos);
#endif
                return half4(diffuse, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            // Gothic's polygons are one-sided: a wall facing away from the light still casts its shadow.
            Cull Off

            HLSLPROGRAM
            #pragma vertex GothicShadowVert
            #pragma fragment GothicShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Fatness;
            CBUFFER_END

            #define GOTHIC_SHADOW_FATNESS
            #include "GothicIncludes.hlsl"
            #include "GothicShadowCaster.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ZTest LEqual
            ColorMask R

            HLSLPROGRAM
            #pragma vertex GothicDepthVert
            #pragma fragment GothicDepthFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Fatness;
            CBUFFER_END

            #define GOTHIC_SHADOW_FATNESS
            #include "GothicIncludes.hlsl"
            #include "GothicDepthPasses.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex GothicDepthVert
            #pragma fragment GothicDepthNormalsFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Fatness;
            CBUFFER_END

            #define GOTHIC_SHADOW_FATNESS
            #include "GothicIncludes.hlsl"
            #include "GothicDepthPasses.hlsl"
            ENDHLSL
        }
    }
}
