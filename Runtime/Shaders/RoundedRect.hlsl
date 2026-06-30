#ifndef UVS_ROUNDED_RECT_INCLUDED
#define UVS_ROUNDED_RECT_INCLUDED

// Signed distance to a rounded box with per-corner radius (iq).
// r order: x = top-right, y = bottom-right, z = top-left, w = bottom-left.
float UVS_sdRoundedBox(float2 p, float2 b, float4 r)
{
    r.xy = (p.x > 0.0) ? r.xy : r.zw;
    r.x  = (p.y > 0.0) ? r.x  : r.y;
    float2 q = abs(p) - b + r.x;
    return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r.x;
}

// Shader Graph Custom Function (File mode). Node name: "RoundedRect".
// Inputs:  UV (Vector2), Size (Vector2), Radius (Vector4), Softness (Float)
// Output:  Alpha (Float)
void RoundedRect_float(float2 UV, float2 Size, float4 Radius, float Softness, out float Alpha)
{
    float2 p = (UV - 0.5) * Size;
    float2 b = Size * 0.5;
    float d = UVS_sdRoundedBox(p, b, Radius);
    float aa = fwidth(d) + Softness;
    Alpha = saturate(1.0 - smoothstep(-aa, aa, d));
}

#endif
