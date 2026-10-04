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
real3 _GlobalStationaryLightColors[MAX_TOTAL_STATIONARY_LIGHTS];

half3 AdditionalStationaryDiffuse(uint lightIndex, real3 worldPos, real3 normal)
{
    float4 lightPosAndAttenuation = _GlobalStationaryLightPositionsAndAttenuation[lightIndex];
    float3 lightVector = lightPosAndAttenuation.xyz - worldPos;
    float distanceSqr = max(dot(lightVector, lightVector), HALF_MIN);

    // Linear distance falloff (1 - d/R)
    float attenuation = saturate(1.0 - sqrt(distanceSqr * lightPosAndAttenuation.w));

    return _GlobalStationaryLightColors[lightIndex] * attenuation * _PointLightIntensity;
}

#endif
