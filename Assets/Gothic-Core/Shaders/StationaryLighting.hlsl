#ifndef STATIONARY_LIGHTING_INCLUDED
#define STATIONARY_LIGHTING_INCLUDED

// Unity caps a global array at 1023 elements. Mobile (Quest) keeps 512 to stay small in the constant buffer.
// Must match StationaryLightsService._maxTotalLights.
#if defined(SHADER_API_MOBILE)
#define MAX_TOTAL_STATIONARY_LIGHTS 512
#else
#define MAX_TOTAL_STATIONARY_LIGHTS 1023
#endif
#define MAX_AFFECTING_STATIONARY_LIGHTS 16

float4 _GlobalStationaryLightPositionsAndAttenuation[MAX_TOTAL_STATIONARY_LIGHTS];
// rgb: color, a: indoor + 2 * (cached shadow slot + 1) - indoor (0/1): under a light mapped ceiling
// (IsStationaryLightIndoor); the slot: its cube in _GothicLightShadowCache (StationaryLightCacheSlot, -1 = none).
real4 _GlobalStationaryLightColors[MAX_TOTAL_STATIONARY_LIGHTS];

// Cached cube shadows of the lamps and fires (LightShadowCacheDomain, PC only): 6 slices per light (+X, -X, +Y, -Y,
// +Z, -Z), each the distance to the nearest wall/VOB divided by the light's range. Drawn once per light - every lamp in
// reach gets its shadow (URP renders only ~40, every frame), a candle in a house no longer lights the street.
#if !defined(SHADER_API_MOBILE)
#define GOTHIC_LIGHT_SHADOW_CACHE
// Same as LightShadowCacheDomain.FaceSize.
#define GOTHIC_LIGHT_CACHE_FACE_SIZE 128
TEXTURE2D_ARRAY(_GothicLightShadowCache);

static const float3 _GothicCacheFaceForward[6] =
    { float3(1, 0, 0), float3(-1, 0, 0), float3(0, 1, 0), float3(0, -1, 0), float3(0, 0, 1), float3(0, 0, -1) };
static const float3 _GothicCacheFaceUp[6] =
    { float3(0, 1, 0), float3(0, 1, 0), float3(0, 0, 1), float3(0, 0, 1), float3(0, 1, 0), float3(0, 1, 0) };
// cross(up, forward) - the camera's right in Unity's left-handed space.
static const float3 _GothicCacheFaceRight[6] =
    { float3(0, 0, -1), float3(0, 0, 1), float3(-1, 0, 0), float3(1, 0, 0), float3(1, 0, 0), float3(-1, 0, 0) };
#endif

// A pooled light with w < 0 casts a shadow (StationaryLightsService): the shaders light it through its URP light with
// the cube shadow (GothicShadowedLights) and skip it here - URP reaches every object in its range, the pool only the
// renderers the light gathered.
// A candle in a house doesn't light the house's outside walls (no shadow for it from the walls).
bool IsStationaryLightIndoor(uint lightIndex)
{
    float a = _GlobalStationaryLightColors[lightIndex].a;
    return a - 2 * floor(a * 0.5) > 0.5;
}

// The light's slot in _GothicLightShadowCache, -1 without a cached shadow (not drawn yet, too far, Quest).
int StationaryLightCacheSlot(uint lightIndex)
{
#if defined(GOTHIC_LIGHT_SHADOW_CACHE)
    return (int)floor(_GlobalStationaryLightColors[lightIndex].a * 0.5) - 1;
#else
    return -1;
#endif
}

// 1 = lit, 0 = a wall or VOB between the light and the position (bilinear 2x2 of the cached distances).
half StationaryLightCachedShadow(uint lightIndex, int slot, float3 worldPos, half3 normal)
{
#if defined(GOTHIC_LIGHT_SHADOW_CACHE)
    float4 lightPosAndAttenuation = _GlobalStationaryLightPositionsAndAttenuation[lightIndex];
    float invRange = sqrt(abs(lightPosAndAttenuation.w));
    float3 toLight = lightPosAndAttenuation.xyz - worldPos;
    // One texel at this distance (90 degrees per face); pushed towards the light by 1.5 of it - the surface itself is
    // in the map (Gothic's polygons are drawn both sides), else it shadows itself in stripes.
    float texel = length(toLight) * (2.0 / GOTHIC_LIGHT_CACHE_FACE_SIZE);
    float3 offsetNormal = dot(normal, toLight) >= 0 ? normal : -normal;
    float3 d = worldPos + offsetNormal * (texel * 1.5) - lightPosAndAttenuation.xyz;

    float3 a = abs(d);
    int face = a.x >= a.y && a.x >= a.z ? (d.x > 0 ? 0 : 1) : (a.y >= a.z ? (d.y > 0 ? 2 : 3) : (d.z > 0 ? 4 : 5));
    float major = dot(d, _GothicCacheFaceForward[face]);
    float2 uv = float2(dot(d, _GothicCacheFaceRight[face]), dot(d, _GothicCacheFaceUp[face])) / major * 0.5 + 0.5;

    // 5 cm against acne on flat walls.
    float current = (length(d) - 0.05) * invRange;
    float2 coord = uv * GOTHIC_LIGHT_CACHE_FACE_SIZE - 0.5;
    float2 f = frac(coord);
    int2 c = clamp((int2)floor(coord), 0, GOTHIC_LIGHT_CACHE_FACE_SIZE - 2);
    int sliceIndex = slot * 6 + face;
    half s00 = step(current, LOAD_TEXTURE2D_ARRAY(_GothicLightShadowCache, c, sliceIndex).r);
    half s10 = step(current, LOAD_TEXTURE2D_ARRAY(_GothicLightShadowCache, c + int2(1, 0), sliceIndex).r);
    half s01 = step(current, LOAD_TEXTURE2D_ARRAY(_GothicLightShadowCache, c + int2(0, 1), sliceIndex).r);
    half s11 = step(current, LOAD_TEXTURE2D_ARRAY(_GothicLightShadowCache, c + int2(1, 1), sliceIndex).r);
    return lerp(lerp(s00, s10, f.x), lerp(s01, s11, f.x), f.y);
#else
    return 1;
#endif
}

bool IsStationaryLightShadowed(uint lightIndex)
{
    return _GlobalStationaryLightPositionsAndAttenuation[lightIndex].w < 0;
}

half3 AdditionalStationaryDiffuse(uint lightIndex, real3 worldPos, real3 normal)
{
    float4 lightPosAndAttenuation = _GlobalStationaryLightPositionsAndAttenuation[lightIndex];
    float3 lightVector = lightPosAndAttenuation.xyz - worldPos;
    float distanceSqr = max(dot(lightVector, lightVector), HALF_MIN);

    // w = 1/R², negative for a shadow casting light.
    float attenuation = StationaryLightFalloff(distanceSqr, abs(lightPosAndAttenuation.w));

    return _GlobalStationaryLightColors[lightIndex].rgb * attenuation * _PointLightIntensity;
}

// A pooled light without a URP shadow (those come through GothicShadowedLights): with its cached shadow, else without -
// then an indoor light skips outdoor surfaces (the house's walls would have shadowed it).
half3 StationaryLightContribution(uint lightIndex, float3 worldPos, half3 normal, bool isOutdoorSurface)
{
    if (IsStationaryLightShadowed(lightIndex))
        return 0;
    int slot = StationaryLightCacheSlot(lightIndex);
    if (slot < 0 && isOutdoorSurface && IsStationaryLightIndoor(lightIndex))
        return 0;
    half3 diffuse = AdditionalStationaryDiffuse(lightIndex, worldPos, normal);
    if (slot >= 0 && any(diffuse > 0))
        diffuse *= StationaryLightCachedShadow(lightIndex, slot, worldPos, normal);
    return diffuse;
}

#endif
