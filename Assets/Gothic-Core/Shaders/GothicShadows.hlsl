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

// Gothic's light is 0...1 per channel (8 bit vertex light): a lamp's pool next to another's and the baked light
// burnt the colors out (bright orange) - the sum stops at 1, like in the engine.
half3 GothicClampLight(half3 light)
{
    return min(light, 1.0);
}

// Objects and plants: sun (shadowed) + ambient, both occluded - SunAndAmbientDiffuse per pixel.
half3 GothicSunAndAmbient(half3 sunDiffuse, float3 worldPos, GothicOcclusion occlusion)
{
    return saturate(sunDiffuse * GothicSunShadow(worldPos) * occlusion.direct + _AmbientColor * occlusion.indirect);
}

// The few closest lamps, fires and torches are shadow casting URP lights (StationaryLightsService). True for such a
// light of the object's per-object lights.
bool GothicIsShadowedLight(int lightIndex)
{
#if defined(_ADDITIONAL_LIGHT_SHADOWS)
    return GetAdditionalLightShadowParams(lightIndex).w >= 0;
#else
    return false;
#endif
}

// This frame's shadow casting lights: their index among URP's additional lights (GothicShadowLightsFeature) - not
// the object's per-object list (8 lights, a big world chunk near candles didn't get the lamp).
float4 _GothicShadowLightIndices[2];
float _GothicShadowLightCount;

// The shadow casting lights, per pixel with their cube shadow. World and plants (isStationaryFalloff): like a
// stationary light (StationaryLightFalloff, no N·L, StationaryLighting.hlsl - which skips them). Objects and NPCs: like their
// other lights (AdditionalUnityLightDiffuse).
half3 GothicShadowedLights(float3 worldPos, half3 normalWS, bool isStationaryFalloff)
{
    half3 diffuse = 0;
#if defined(_ADDITIONAL_LIGHT_SHADOWS)
    int count = min(8, (int)_GothicShadowLightCount);
    for (int k = 0; k < count; k++)
    {
        int lightIndex = (int)_GothicShadowLightIndices[k / 4][k % 4];

    #if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
        float4 lightPositionWS = _AdditionalLightsBuffer[lightIndex].position;
        half3 color = _AdditionalLightsBuffer[lightIndex].color.rgb;
        half4 distanceAndSpotAttenuation = _AdditionalLightsBuffer[lightIndex].attenuation;
    #else
        float4 lightPositionWS = _AdditionalLightsPosition[lightIndex];
        half3 color = _AdditionalLightsColor[lightIndex].rgb;
        half4 distanceAndSpotAttenuation = _AdditionalLightsAttenuation[lightIndex];
    #endif
        float3 lightVector = lightPositionWS.xyz - worldPos * lightPositionWS.w;
        float distanceSqr = max(dot(lightVector, lightVector), HALF_MIN);
        half3 lightDirection = half3(lightVector * rsqrt(distanceSqr));
        half attenuation = isStationaryFalloff
            ? StationaryLightFalloff(distanceSqr, distanceAndSpotAttenuation.x)
            : CustomDistanceAttenuation(distanceSqr, distanceAndSpotAttenuation.x) *
              saturate(dot(lightDirection, normalWS));
        if (attenuation <= 0)
            continue;

        half shadow = AdditionalLightRealtimeShadow(lightIndex, worldPos, lightDirection);
        diffuse += color * attenuation * shadow * _PointLightIntensity;
    }
#endif
    return diffuse;
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
