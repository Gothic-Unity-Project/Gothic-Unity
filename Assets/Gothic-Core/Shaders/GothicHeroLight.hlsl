#ifndef GOTHIC_HERO_LIGHT_INCLUDED
#define GOTHIC_HERO_LIGHT_INCLUDED

// The hero light (HeroLightService): one point light computed for every pixel - the burning torch in the player's
// hand. All other lights stay per vertex. The shaders add it only while the global keyword _HERO_LIGHT is on
// (#pragma dynamic_branch: one shader variant with a switch inside, so turning it on compiles nothing).
// Needs GothicIncludes.hlsl (_PointLightIntensity).

// xyz = world position (m), w = 1 / range^2 - like the stationary lights' attenuation.
float4 _HeroLightPositionRange;
// Linear color with the flicker and the fade-out already in it.
real3 _HeroLightColor;
// x = radius (m) of the bright part around the flame, y = its strength, z = the base level up to the range.
float4 _HeroLightShape;

// Bright close to the flame and fading with the square of the distance, like a real flame, on top of a small base
// level (_HeroLightShape); cut off smoothly at Gothic's range (15 m for the torch - alone that range kept the light
// nearly flat in the first meters, so no pool of light followed the torch). Surfaces facing the light get more of it,
// with a little wrap so the light reaches a bit around edges.

half3 HeroLightDiffuse(float3 worldPos, half3 normal)
{
    float3 lightVector = _HeroLightPositionRange.xyz - worldPos;
    float distanceSqr = max(dot(lightVector, lightVector), HALF_MIN);
    half window = saturate(1.0 - distanceSqr * _HeroLightPositionRange.w);
    window *= window;
    half nearFalloff = _HeroLightShape.y / (1.0 + distanceSqr / (_HeroLightShape.x * _HeroLightShape.x));
    half falloff = window * (_HeroLightShape.z + nearFalloff);

    half3 lightDirection = half3(lightVector * rsqrt(distanceSqr));
    half wrappedDot = saturate((dot(normal, lightDirection) + 0.3) / 1.3);

    return _HeroLightColor * (falloff * wrappedDot * _PointLightIntensity);
}

#endif
