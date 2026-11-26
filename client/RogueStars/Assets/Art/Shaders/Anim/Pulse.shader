// Glow pulse on the sprite itself: the bright, saturated pixels (crystals, lamps, runes, windows) breathe and flicker; _Whole
// mixes in the whole sprite. Colour-based mask, no extra texture. Additive lift in _Tint on top of a brightness swell.
Shader "RogueStars/Anim/Pulse"
{
    Properties
    {
        _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Phase ("Phase", Float) = 0
        _Glow ("Glow colour", Color) = (0.5,0.9,1,1)
        _Pulse ("Pulse amount", Range(0,1.5)) = 0.5
        _Speed ("Pulse speed", Float) = 1.8
        _Flicker ("Flicker", Range(0,1)) = 0.1
        _MaskLo ("Mask from (saturation x value)", Range(0,1)) = 0.35
        _MaskHi ("Mask full at", Range(0,1)) = 0.75
        _Whole ("Whole sprite share", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off Lighting Off ZWrite Off
        Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "AnimCommon.cginc"
            sampler2D _MainTex; fixed4 _Color, _Glow;
            float _Pulse, _Speed, _Flicker, _MaskLo, _MaskHi, _Whole;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.color = v.color * _Color; return o; }
            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;
                float mx = max(c.r, max(c.g, c.b)), mn = min(c.r, min(c.g, c.b));
                float sat = (mx - mn) / (mx + 0.001);
                float mask = lerp(smoothstep(_MaskLo, _MaskHi, sat * mx + mx * mx * 0.25), 1, _Whole);
                float t = _Time.y + AnimObjectPhase();
                float k = 0.5 + 0.5 * sin(t * _Speed) + _Flicker * (AnimNoise(float2(t * 6, 3)) - 0.5) * 2;
                float g = mask * k * _Pulse;
                c.rgb = c.rgb * (1 + g * 0.5) + _Glow.rgb * g * 0.35 * c.a;
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
