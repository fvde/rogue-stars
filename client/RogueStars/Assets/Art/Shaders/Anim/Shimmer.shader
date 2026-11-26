// A highlight band sweeping across the sprite (crystal facets, polished metal, shrine stone), scrolling in uv space; only on
// bright / saturated pixels unless _Whole raises the share. One ALU-only pass.
Shader "RogueStars/Anim/Shimmer"
{
    Properties
    {
        _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Phase ("Phase", Float) = 0
        _Glow ("Highlight colour", Color) = (1,1,0.9,1)
        _Strength ("Strength", Range(0,2)) = 0.7
        _Speed ("Sweeps per second", Float) = 0.22
        _Scale ("Bands across the sprite", Float) = 1
        _Width ("Band width", Range(0.02,0.5)) = 0.12
        _Slant ("Slant (uv y share)", Range(-1,1)) = 0.5
        _Whole ("Whole sprite share", Range(0,1)) = 0.25
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
            float _Strength, _Speed, _Scale, _Width, _Slant, _Whole;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.color = v.color * _Color; return o; }
            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;
                float mx = max(c.r, max(c.g, c.b)), mn = min(c.r, min(c.g, c.b));
                float mask = lerp(smoothstep(0.35, 0.8, mx + (mx - mn) * 0.5), 1, _Whole);
                float t = _Time.y * _Speed + AnimObjectPhase() * 0.13;
                // the band travels diagonally and rests off-sprite for most of the cycle (a sweep, then a pause)
                float x = (i.uv.x + i.uv.y * _Slant) * _Scale * 0.5 - frac(t) * 2.6 + 1.3;
                float band = saturate(1 - abs(x) / _Width);
                band = band * band * (3 - 2 * band);
                c.rgb += _Glow.rgb * band * mask * _Strength * c.a;
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
