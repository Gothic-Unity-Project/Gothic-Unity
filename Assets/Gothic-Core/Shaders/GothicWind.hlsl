// Wind sway of VOBs with Gothic's visualAniMode WIND/WIND_ALT (trees, grass), like OpenGothic's
// VisualObjects::preFrameUpdateWind: every vertex moves along the wind by its height above the object's origin, a shear
// that swings with a cosine over the wind cycle. Objects farther along the wind swing later.
// Only compiled into materials with the _WIND keyword (WindService), which it sets only while the option is on - the
// other materials and the option turned off cost nothing.
// Needs _WindStrength in the shader's UnityPerMaterial buffer.
#ifndef GOTHIC_WIND_INCLUDED
#define GOTHIC_WIND_INCLUDED

// Seconds of one swing back and forth (Gothic.ini zWindCycleTime + zWindCycleTimeVar).
float _GothicWindPeriod;
// Wind direction on the ground (x, z).
float4 _GothicWindDirection;

float3 ApplyWind(float3 worldPos)
{
    float3 origin = TransformObjectToWorld(float3(0, 0, 0));
    float height = worldPos.y - origin.y;
    float cycle = frac(_Time.y / max(_GothicWindPeriod, 0.01)) * 2.0 - 1.0;
    // OpenGothic: position in cm times 0.0001 - in m times 0.01.
    float shift = dot(origin.xz, _GothicWindDirection.xy) * 0.01;
    float swing = _WindStrength * cos(cycle * PI + shift);
    worldPos.xz += _GothicWindDirection.xy * swing * height;
    return worldPos;
}

#endif
