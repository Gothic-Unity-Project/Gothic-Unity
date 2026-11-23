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
// rgb: color, a: 1 = indoor light (under a light mapped ceiling, IsStationaryLightIndoor). Same 16 byte stride as
// the former real3.
real4 _GlobalStationaryLightColors[MAX_TOTAL_STATIONARY_LIGHTS];

// A pooled light with w < 0 casts a shadow (StationaryLightsService): the shaders light it through its URP light with
// the cube shadow (GothicShadowedLights) and skip it here - URP reaches every object in its range, the pool only the
// renderers the light gathered.
// A candle in a house doesn't light the house's outside walls (no shadow for it from the walls).
bool IsStationaryLightIndoor(uint lightIndex)
{
    return _GlobalStationaryLightColors[lightIndex].a > 0.5;
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

#endif
