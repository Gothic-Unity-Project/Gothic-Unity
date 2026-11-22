// Dynamic sun/moon shadows on Gothic's light (GD3D11-like): the shadow only darkens the light that comes from the sun -
// Gothic's baked vertex light on outdoor polygons, the sun part of objects and NPCs. Ambient light, lightmaps and
// fires/torches stay. Surfaces facing away from the sun get no extra shadow: Gothic's vertex light already has it.
// The shadow map is URP's main light (the "Sun" light of SkyService); its shadowStrength follows the time of day.
// Without the shadow keywords (Quest, shadows off) GothicSunShadow is 1 and compiles away.
#ifndef GOTHIC_SHADOWS_INCLUDED
#define GOTHIC_SHADOWS_INCLUDED

#if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE) || defined(_MAIN_LIGHT_SHADOWS_SCREEN)
    #define GOTHIC_RECEIVE_SUN_SHADOWS
#endif

// 1 = lit, down to 1 - shadowStrength in the sun's shadow. Faded out towards the shadow distance.
half GothicSunShadow(float3 worldPos)
{
#if defined(GOTHIC_RECEIVE_SUN_SHADOWS)
    float4 shadowCoord = TransformWorldToShadowCoord(worldPos);
    half shadow = MainLightRealtimeShadow(shadowCoord);
    return lerp(shadow, 1, GetMainLightShadowFade(worldPos));
#else
    return 1;
#endif
}

// How much of a surface's light comes from the sun's direction: 0 facing away, 1 facing it (a soft edge, so the
// terminator doesn't show the shadow map's bias). _SunDirection points from the sun to the ground.
half GothicSunFacing(half3 normalWS)
{
    return saturate(dot(normalWS, -_SunDirection) * 4);
}

// Screen space ambient occlusion (URP's SSAO feature, PC): indirect darkens Gothic's baked light and the ambient,
// direct (weaker, the feature's "direct lighting strength") the sun and the fires. 1 without SSAO.
struct GothicOcclusion
{
    half indirect;
    half direct;
};

GothicOcclusion GothicGetOcclusion(float4 positionCS)
{
    GothicOcclusion occlusion;
#if defined(_SCREEN_SPACE_OCCLUSION)
    AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(positionCS));
    occlusion.indirect = ao.indirectAmbientOcclusion;
    occlusion.direct = ao.directAmbientOcclusion;
#else
    occlusion.indirect = 1;
    occlusion.direct = 1;
#endif
    return occlusion;
}

// Objects and plants: sun (shadowed) + ambient, both occluded - SunAndAmbientDiffuse per pixel.
half3 GothicSunAndAmbient(half3 sunDiffuse, float3 worldPos, GothicOcclusion occlusion)
{
    return saturate(sunDiffuse * GothicSunShadow(worldPos) * occlusion.direct + _AmbientColor * occlusion.indirect);
}

// The baked light of a sun-facing surface, darkened by the shadow at worldPos.
half3 GothicApplySunShadow(half3 bakedLight, half3 normalWS, float3 worldPos)
{
#if defined(GOTHIC_RECEIVE_SUN_SHADOWS)
    half facing = GothicSunFacing(normalWS);
    return bakedLight * lerp(1, GothicSunShadow(worldPos), facing);
#else
    return bakedLight;
#endif
}

#endif
