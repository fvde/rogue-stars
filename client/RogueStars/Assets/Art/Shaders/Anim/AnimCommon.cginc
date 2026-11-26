// Shared helpers of the animation-pilot shaders (built-in pipeline, sprite-style premultiplied output).
#ifndef ROGUESTARS_ANIM_COMMON
#define ROGUESTARS_ANIM_COMMON

float AnimHash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

// Value noise in 0..1 (no texture fetch: one ALU-only function, cheap enough for a prop or two on screen).
float AnimNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    float2 u = f * f * (3 - 2 * f);
    return lerp(lerp(AnimHash(i), AnimHash(i + float2(1, 0)), u.x), lerp(AnimHash(i + float2(0, 1)), AnimHash(i + float2(1, 1)), u.x), u.y);
}

// Padded quads (AnimProp.Quad) carry uv -_Pad..1+_Pad so displaced pixels may leave the sprite's rect; outside 0..1 is empty.
float AnimInside(float2 uv) { return step(0, uv.x) * step(uv.x, 1) * step(0, uv.y) * step(uv.y, 1); }

// A per-object phase (_Phase, set by PropFx from the prop's world position; objects with the same material are batched, so the
// object matrix cannot tell them apart), so copies of one prop never move in lockstep.
float _Phase;
float AnimObjectPhase() { return _Phase; }

#endif
