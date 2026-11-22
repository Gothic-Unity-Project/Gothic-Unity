Shader "Lit/AlphaToCoverage"
{
    Properties
    {
        _MainTex("Texture", 2DArray) = "" {}
        _Cutoff ("Cutoff", Range(0,1)) = 0.7
        _MipScale ("Mip scale", Range(0,1)) = 0.25
        _DistanceFade("Distance to wide coverage", Float) = 10
        [HideInInspector]_StationaryLightCount("Stationary light count", Int) = 0
        [HideInInspector]_WindStrength("Wind strength", Float) = 0
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest"
        }
        AlphaToMask On

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _WIND
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 uv : TEXCOORD0;
                half3 color : COLOR;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 uv : TEXCOORD0;
                float distance : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
                // The sun's part (shadowed per pixel); diffuse: fires/torches.
                half3 sunDiffuse : TEXCOORD3;
                half3 diffuse : COLOR;

                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D_ARRAY(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_TexelSize;
                half _Cutoff;
                half _MipScale;
                float _DistanceFade;
                int _StationaryLightCount;
                float4x4 _StationaryLightIndices;
                float _WindStrength;
            CBUFFER_END

            #include "GothicIncludes.hlsl"
            #include "StationaryLighting.hlsl"
            #include "GothicWind.hlsl"
            #include "GothicShadows.hlsl"

            // Fires/torches - the sun and the ambient come per pixel (SunAndAmbientShadowed).
            half3 DiffuseLighting(half3 normal, float3 worldPos)
            {
                half3 diffuse = 0;

                //for (int j = 0; j < min(MAX_VISIBLE_LIGHTS, unity_LightData.y); j++)
                //{
                //    int lightIndex = GetPerObjectLightIndex(j);
                //    Light light = CustomGetAdditionalPerObjectLight(lightIndex, i.worldPos);
                //    diffuse += AdditionalUnityLightDiffuse(light, i.normal);
                //}

                for (uint k = 0; k < min(_StationaryLightCount, MAX_AFFECTING_STATIONARY_LIGHTS); k++)
                {
                    diffuse += AdditionalStationaryDiffuse(_StationaryLightIndices[k / 4][k % 4], worldPos, normal);
                }

                return diffuse;
            }

            v2f vert(appdata v)
            {
                v2f o;

                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.worldPos = TransformObjectToWorld(v.vertex.xyz);
#if defined(_WIND)
                o.worldPos = ApplyWind(o.worldPos);
                o.vertex = TransformWorldToHClip(o.worldPos);
#else
                o.vertex = TransformObjectToHClip(v.vertex.xyz);
#endif
                float3 toCamera = o.worldPos - _WorldSpaceCameraPos;
                o.distance = length(toCamera);
                o.uv = float4(v.uv.xy * REFERENCE_TEX_ARRAY_SIZE * _MainTex_TexelSize.xy, v.uv.zw);
                half3 normalWS = TransformObjectToWorldNormal(v.normal);
                o.sunDiffuse = SunDiffuse(normalWS, v.color);
                o.diffuse = DiffuseLighting(normalWS, o.worldPos);

                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float mipLevel = CalcMipLevel(i.uv.xy * _MainTex_TexelSize.zw);
                half4 albedo = SAMPLE_TEXTURE2D_ARRAY_LOD(_MainTex, sampler_MainTex, i.uv.xy, i.uv.z,
                                        clamp(mipLevel, 0, i.uv.w));
                // Rescale alpha by mip level since preserved coverage mip maps can't be generated at runtime.
                // Reuse mipLevel from above (CalcMipLevel already returns >= 0) instead of recomputing ddx/ddy + log2.
                albedo.a *= 1 + mipLevel * _MipScale;
                // Rescale alpha by partial derivative, faded by distance. This way, at a distance, the wide coverage is kept to reduce aliasing further.
                albedo.a = lerp((albedo.a - _Cutoff) / max(fwidth(albedo.a), 0.0001) + 0.5, albedo.a,
                                                      saturate(max(i.distance, 0.0001) / _DistanceFade));

                GothicOcclusion occlusion = GothicGetOcclusion(i.vertex);
                half3 light = GothicSunAndAmbient(i.sunDiffuse, i.worldPos, occlusion) + i.diffuse * occlusion.direct;
                half3 diffuse = albedo.rgb * light;

                diffuse = ApplyUnderWaterEffect(diffuse);
                
#if FOG_LINEAR || FOG_EXP || FOG_EXP2
                diffuse = ApplyFog(diffuse, i.worldPos);
#endif
                return half4(diffuse, albedo.a);
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
            AlphaToMask Off
            // Gothic's polygons are one-sided: a wall facing away from the light still casts its shadow.
            Cull Off

            HLSLPROGRAM
            #pragma vertex GothicShadowVert
            #pragma fragment GothicShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _WIND
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D_ARRAY(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_TexelSize;
                half _Cutoff;
                half _MipScale;
                float _DistanceFade;
                int _StationaryLightCount;
                float4x4 _StationaryLightIndices;
                float _WindStrength;
            CBUFFER_END

            #define GOTHIC_SHADOW_ALPHA_CLIP
            #include "GothicIncludes.hlsl"
            #include "GothicWind.hlsl"
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
            AlphaToMask Off

            HLSLPROGRAM
            #pragma vertex GothicDepthVert
            #pragma fragment GothicDepthFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _WIND

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D_ARRAY(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_TexelSize;
                half _Cutoff;
                half _MipScale;
                float _DistanceFade;
                int _StationaryLightCount;
                float4x4 _StationaryLightIndices;
                float _WindStrength;
            CBUFFER_END

            #define GOTHIC_SHADOW_ALPHA_CLIP
            #include "GothicIncludes.hlsl"
            #include "GothicWind.hlsl"
            #include "GothicDepthPasses.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            ZTest LEqual
            AlphaToMask Off

            HLSLPROGRAM
            #pragma vertex GothicDepthVert
            #pragma fragment GothicDepthNormalsFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _WIND

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D_ARRAY(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_TexelSize;
                half _Cutoff;
                half _MipScale;
                float _DistanceFade;
                int _StationaryLightCount;
                float4x4 _StationaryLightIndices;
                float _WindStrength;
            CBUFFER_END

            #define GOTHIC_SHADOW_ALPHA_CLIP
            #include "GothicIncludes.hlsl"
            #include "GothicWind.hlsl"
            #include "GothicDepthPasses.hlsl"
            ENDHLSL
        }
    }
}
