// ShadowCaster pass of Gothic's lit shaders: the world, VOBs, NPCs and plants draw into URP's shadow maps (the sun's
// cascades), so they cast dynamic shadows (GothicShadows.hlsl receives them).
// Included inside the pass after the shader's own UnityPerMaterial buffer and textures (same layout in every pass, else
// the SRP batcher breaks). Options, defined before the include:
// - GOTHIC_SHADOW_ALPHA_CLIP: _MainTex is a texture array with alpha (plants, fences), cut at _Cutoff like the main pass.
// - GOTHIC_SHADOW_FATNESS: the vertex is pushed out by _Fatness (NPCs' Mdl_SetModelFatness).
// - _WIND (material keyword): the same sway as the main pass (GothicWind.hlsl), or the shadow stays behind the tree.
#ifndef GOTHIC_SHADOW_CASTER_INCLUDED
#define GOTHIC_SHADOW_CASTER_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

// Set by URP for the light whose shadow map is drawn.
float3 _LightDirection;
float3 _LightPosition;

struct GothicShadowAttributes
{
    float4 vertex : POSITION;
    float3 normal : NORMAL;
    float4 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct GothicShadowVaryings
{
    float4 positionCS : SV_POSITION;
#if defined(GOTHIC_SHADOW_ALPHA_CLIP)
    float4 uv : TEXCOORD0;
#endif
};

GothicShadowVaryings GothicShadowVert(GothicShadowAttributes v)
{
    GothicShadowVaryings o = (GothicShadowVaryings)0;
    UNITY_SETUP_INSTANCE_ID(v);

    float3 position = v.vertex.xyz;
#if defined(GOTHIC_SHADOW_FATNESS)
    position += v.normal * _Fatness;
#endif
    float3 positionWS = TransformObjectToWorld(position);
#if defined(_WIND)
    positionWS = ApplyWind(positionWS);
#endif
    float3 normalWS = TransformObjectToWorldNormal(v.normal);

#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
    float3 lightDirectionWS = _LightDirection;
#endif
    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
#if UNITY_REVERSED_Z
    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#else
    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#endif
    o.positionCS = positionCS;

#if defined(GOTHIC_SHADOW_ALPHA_CLIP)
    o.uv = float4(v.uv.xy * REFERENCE_TEX_ARRAY_SIZE * _MainTex_TexelSize.xy, v.uv.zw);
#endif
    return o;
}

half4 GothicShadowFrag(GothicShadowVaryings i) : SV_Target
{
#if defined(GOTHIC_SHADOW_ALPHA_CLIP)
    // A low mip: the shadow map is coarse anyway, and leaves keep their holes instead of turning into blobs.
    half alpha = SAMPLE_TEXTURE2D_ARRAY_LOD(_MainTex, sampler_MainTex, i.uv.xy, i.uv.z, min(1, i.uv.w)).a;
    clip(alpha - _Cutoff);
#endif
    return 0;
}

#endif
