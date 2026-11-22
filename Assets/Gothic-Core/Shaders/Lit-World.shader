Shader "Lit/World"
{
    Properties
    {
        _MainTex("Texture", 2DArray) = "" {}
        [HideInInspector]_StationaryLightCount("Stationary light count", Int) = 0
        _FocusBrightness("FocusBrightness", float) = 1
        [HideInInspector]_WindStrength("Wind strength", Float) = 0
        // VOBs in light mapped areas: the light of the world below them (StationaryLightsService.ApplyWorldLight),
        // a = 1 when set. World chunks keep a = 0.
        [HideInInspector]_VobLightTint("Vob light tint", Color) = (1, 1, 1, 0)
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
            #pragma multi_compile_local _ _WIND
            // Dynamic sun/moon shadows (GothicShadows.hlsl) - only where the URP asset has main light shadows.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float _FocusBrightness;

            struct appdata
            {
                float4 vertex : POSITION;
                half3 color : COLOR;
                half4 normal : NORMAL; // w = 1 on outdoor polygons (WorldMeshBuilder) - they follow the time of day
                float4 uv : TEXCOORD0; // uv, array slice, max mip level
                // Lightmap uv, lightmap array slice, w = -1 on light mapped polygons. A mesh without this channel
                // (VOBs) gets its
                // TEXCOORD0 here (w = max mip level >= 0) - a positive marker made every VOB sample the lightmaps.
                float4 lightmapUv : TEXCOORD1;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float4 lightmapUv : TEXCOORD2;
                // w = 1 where the sun reaches: outdoor (vertex lit) polygons and VOBs outside of light mapped areas.
                half4 normal : TEXCOORD3;
                half3 baseLight : COLOR;

                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D_ARRAY(_MainTex);
            SAMPLER(sampler_MainTex);

            // Gothic's lightmaps of light mapped polygons (caves, houses, mines) - WorldMeshBuilder, one slice per
            // lightmap page of the world. A 1x1 white array when the world has none.
            TEXTURE2D_ARRAY(_WorldLightmaps);
            SAMPLER(sampler_WorldLightmaps);
            float _WorldLightmapBrightness;

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_TexelSize;
                int _StationaryLightCount;
                float4x4 _StationaryLightIndices;
                float4x4 _StationaryLightIndices2;
                float _WindStrength;
                float4 _VobLightTint;
            CBUFFER_END

            #include "GothicIncludes.hlsl"
            #include "StationaryLighting.hlsl"
            #include "GothicWind.hlsl"
            #include "GothicShadows.hlsl"

            // Per pixel: per vertex a torch in the middle of a big cave wall lit nothing (its corners are far away).
            half3 StationaryLighting(half3 normal, float3 worldPos)
            {
                half3 diffuse = 0;
                for (uint k = 0; k < min(_StationaryLightCount, MAX_AFFECTING_STATIONARY_LIGHTS); k++)
                {
                    diffuse += AdditionalStationaryDiffuse(_StationaryLightIndices[k / 4][k % 4], worldPos, normal);
                }
                if (_StationaryLightCount >= MAX_AFFECTING_STATIONARY_LIGHTS)
                {
                    for (uint l = 0; l < min(_StationaryLightCount - MAX_AFFECTING_STATIONARY_LIGHTS,
                            MAX_AFFECTING_STATIONARY_LIGHTS); l++)
                    {
                        diffuse += AdditionalStationaryDiffuse(_StationaryLightIndices2[l / 4][l % 4], worldPos,
                                                                           normal);
                    }
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
                o.uv = float4(v.uv.xy * REFERENCE_TEX_ARRAY_SIZE * _MainTex_TexelSize.xy, v.uv.zw);
                o.lightmapUv = v.lightmapUv;
                o.normal = half4(TransformObjectToWorldNormal(v.normal.xyz), v.normal.w * (1 - _VobLightTint.a));
                // Gothic's baked vertex light; outdoor polygons follow the time of day.
                o.baseLight = v.color * lerp(half3(1, 1, 1), _DayLightColor, v.normal.w);
                // A VOB standing in a cave/house takes the light of the floor below it, like the engine's VOB lighting.
                o.baseLight = lerp(o.baseLight, _VobLightTint.rgb, _VobLightTint.a);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float mipLevel = CalcMipLevel(i.uv.xy * _MainTex_TexelSize.zw);
                half4 albedo = SAMPLE_TEXTURE2D_ARRAY_LOD(_MainTex, sampler_MainTex, i.uv.xy, i.uv.z,
                clamp(mipLevel, 0, i.uv.w));

                // Light mapped polygons: Gothic lit them with their lightmap, not the vertex light.
                half3 baseLight = i.baseLight;
                if (i.lightmapUv.w < -0.5)
                {
                    baseLight = SAMPLE_TEXTURE2D_ARRAY(_WorldLightmaps, sampler_WorldLightmaps, i.lightmapUv.xy,
                                                       i.lightmapUv.z).rgb * _WorldLightmapBrightness;
                }

                half3 normal = normalize(i.normal.xyz);
                // The sun's shadow darkens Gothic's baked (sun) light outdoors - not lightmaps, not the fires.
                if (i.normal.w > 0.5)
                {
                    baseLight = GothicApplySunShadow(baseLight, normal, i.worldPos);
                }

                GothicOcclusion occlusion = GothicGetOcclusion(i.vertex);
                half3 light = baseLight * occlusion.indirect +
                    StationaryLighting(normal, i.worldPos) * occlusion.direct;
                half3 diffuse = albedo.rgb * light * _FocusBrightness;

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
            // Gothic's polygons are one-sided: a wall facing away from the sun still casts its shadow.
            Cull Off

            HLSLPROGRAM
            #pragma vertex GothicShadowVert
            #pragma fragment GothicShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _WIND
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_TexelSize;
                int _StationaryLightCount;
                float4x4 _StationaryLightIndices;
                float4x4 _StationaryLightIndices2;
                float _WindStrength;
                float4 _VobLightTint;
            CBUFFER_END

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

            HLSLPROGRAM
            #pragma vertex GothicDepthVert
            #pragma fragment GothicDepthFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _WIND

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_TexelSize;
                int _StationaryLightCount;
                float4x4 _StationaryLightIndices;
                float4x4 _StationaryLightIndices2;
                float _WindStrength;
                float4 _VobLightTint;
            CBUFFER_END

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

            HLSLPROGRAM
            #pragma vertex GothicDepthVert
            #pragma fragment GothicDepthNormalsFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _WIND

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_TexelSize;
                int _StationaryLightCount;
                float4x4 _StationaryLightIndices;
                float4x4 _StationaryLightIndices2;
                float _WindStrength;
                float4 _VobLightTint;
            CBUFFER_END

            #include "GothicIncludes.hlsl"
            #include "GothicWind.hlsl"
            #include "GothicDepthPasses.hlsl"
            ENDHLSL
        }
    }
}
