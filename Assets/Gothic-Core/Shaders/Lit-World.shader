Shader "Lit/World"
{
    Properties
    {
        _MainTex("Texture", 2DArray) = "" {}
        [HideInInspector]_StationaryLightCount("Stationary light count", Int) = 0
        _FocusBrightness("FocusBrightness", float) = 1
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float _FocusBrightness;

            struct appdata
            {
                float4 vertex : POSITION;
                half3 color : COLOR;
                half4 normal : NORMAL; // w = 1 on outdoor polygons (WorldMeshBuilder) - they follow the time of day
                float4 uv : TEXCOORD0; // uv, array slice, max mip level
                // Lightmap uv, lightmap array slice, w = -1 on light mapped polygons. A mesh without this channel (VOBs) gets its
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
                half3 normal : TEXCOORD3;
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
                float4 _VobLightTint;
            CBUFFER_END

            #include "GothicIncludes.hlsl"
            #include "StationaryLighting.hlsl"

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
                o.vertex = TransformObjectToHClip(v.vertex.xyz);
                o.uv = float4(v.uv.xy * REFERENCE_TEX_ARRAY_SIZE * _MainTex_TexelSize.xy, v.uv.zw);
                o.lightmapUv = v.lightmapUv;
                o.normal = TransformObjectToWorldNormal(v.normal.xyz);
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

                half3 light = baseLight + StationaryLighting(normalize(i.normal), i.worldPos);
                half3 diffuse = albedo.rgb * light * _FocusBrightness;

                diffuse = ApplyUnderWaterEffect(diffuse);

#if FOG_LINEAR || FOG_EXP || FOG_EXP2
                diffuse = ApplyFog(diffuse, i.worldPos);
#endif
                return half4(diffuse, 1);
            }
            ENDHLSL
        }
    }
}
