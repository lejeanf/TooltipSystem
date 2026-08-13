#ifndef JEANF_NAVIGATION_MARKER_INCLUDED
#define JEANF_NAVIGATION_MARKER_INCLUDED

// Shared SDF + animation math for the navigation path markers.
// Shapes: 0 = dot, 1 = chevron (points +V), 2 = line ribbon (solid, LineRenderer supplies the strip),
//         3 = target ring (ring + center dot).

float NavSegmentDistance(float2 p, float2 a, float2 b)
{
    float2 ab = b - a;
    float t = saturate(dot(p - a, ab) / dot(ab, ab));
    return length(p - a - ab * t);
}

float NavMarkerAlpha(float2 uv, float shape, float weight)
{
    float2 p = uv - 0.5;
    float d;
    if (shape < 0.5)
    {
        d = length(p) - 0.32;
    }
    else if (shape < 1.5)
    {
        float2 tip = float2(0.0, 0.18);
        float2 w1 = float2(-0.30, -0.20);
        float2 w2 = float2(0.30, -0.20);
        d = min(NavSegmentDistance(p, w1, tip), NavSegmentDistance(p, w2, tip)) - weight * 0.5;
    }
    else if (shape < 2.5)
    {
        return 1.0;
    }
    else
    {
        d = min(abs(length(p) - 0.36) - 0.05, length(p) - 0.14);
    }
    float aa = max(fwidth(d) * 1.2, 1e-4);
    return 1.0 - smoothstep(-aa, aa, d);
}

// Traveling brightness: a head moves player->target, each point lights as it passes and fades
// back to base over 'trail' meters. The head position (meters) is advanced on the CPU so the
// phase stays continuous when the path length changes between repaths (walking would otherwise
// scramble a _Time-based phase every resample). pulseMode: 0 = single pulse, 1 = repeating train.
float NavPulseGlow(float distMeters, float headMeters, float trail, float intervalMeters, float pulseMode)
{
    trail = max(trail, 0.01);
    float behind = headMeters - distMeters;
    if (pulseMode >= 0.5)
    {
        behind = behind - intervalMeters * floor(behind / intervalMeters);
    }
    else if (behind < 0.0)
    {
        return saturate(1.0 + behind / 0.4); // short attack ramp so the head doesn't pop in
    }
    return exp(-behind / trail);
}

// Markers behind the player fade out and are fully gone ~2.5m behind — the walked-past part
// of the path is consumed smoothly instead of the old hard sprite removal.
float NavConsumeFade(float distMeters, float playerDistMeters)
{
    return smoothstep(-2.5, -0.5, distMeters - playerDistMeters);
}

// Arrival wipe: everything below hideDist (meters, animated start->target) disappears with a soft edge.
float NavHideWipe(float distMeters, float hideDistMeters)
{
    return smoothstep(-0.1, 0.5, distMeters - hideDistMeters);
}

#endif
