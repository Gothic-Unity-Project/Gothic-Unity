// DepthOnly and DepthNormals passes of Gothic's lit shaders: the world, VOBs, NPCs and plants draw into URP's depth
// and normals textures - screen space ambient occlusion (the renderer's SSAO feature) needs them, without them it saw
// an empty world. Both eyes in one draw (single-pass instanced).
// Included inside the pass after the shader's own UnityPerMaterial buffer and textures. Options like
// GothicShadowCaster.hlsl: GOTHIC_SHADOW_ALPHA_CLIP (texture array alpha cut at _Cutoff), GOTHIC_SHADOW_FATNESS,
// the _WIND keyword.
#ifndef GOTHIC_DEPTH_PASSES_INCLUDED
#define GOTHIC_DEPTH_PASSES_INCLUDED

struct GothicDepthAttributes
{
    float4 vertex : POSITION;
    float3 normal : NORMAL;
    float4 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct GothicDepthVaryings
{
    float4 positionCS : SV_POSITION;
    half3 normalWS : TEXCOORD0;
#if defined(GOTHIC_SHADOW_ALPHA_CLIP)
    float4 uv : TEXCOORD1;
#endif
    UNITY_VERTEX_OUTPUT_STEREO
};

GothicDepthVaryings GothicDepthVert(GothicDepthAttributes v)
{
    GothicDepthVaryings o = (GothicDepthVaryings)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

    float3 position = v.vertex.xyz;
#if defined(GOTHIC_SHADOW_FATNESS)
    position += v.normal * _Fatness;
#endif
    float3 positionWS = TransformObjectToWorld(position);
#if defined(_WIND)
    positionWS = ApplyWind(positionWS);
#endif
    o.positionCS = TransformWorldToHClip(positionWS);
    o.normalWS = TransformObjectToWorldNormal(v.normal);
#if defined(GOTHIC_SHADOW_ALPHA_CLIP)
    o.uv = float4(v.uv.xy * REFERENCE_TEX_ARRAY_SIZE * _MainTex_TexelSize.xy, v.uv.zw);
#endif
    return o;
}

void GothicDepthClip(GothicDepthVaryings i)
{
#if defined(GOTHIC_SHADOW_ALPHA_CLIP)
    float mipLevel = CalcMipLevel(i.uv.xy * _MainTex_TexelSize.zw);
    half alpha = SAMPLE_TEXTURE2D_ARRAY_LOD(_MainTex, sampler_MainTex, i.uv.xy, i.uv.z, clamp(mipLevel, 0, i.uv.w)).a;
    clip(alpha - _Cutoff);
#endif
}

half GothicDepthFrag(GothicDepthVaryings i) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    GothicDepthClip(i);
    return i.positionCS.z;
}

half4 GothicDepthNormalsFrag(GothicDepthVaryings i) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    GothicDepthClip(i);

    float3 normalWS = normalize(i.normalWS);
#if defined(_GBUFFER_NORMALS_OCT)
    float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
    float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
    return half4(PackFloat2To888(remappedOctNormalWS), 0.0);
#else
    return half4(normalWS, 0.0);
#endif
}

#endif
