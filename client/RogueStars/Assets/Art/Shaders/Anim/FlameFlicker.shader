// Flames of a prop sprite: masked noise displacement (licking flames) + brightness flicker. _FlameMask.r marks the flames
// (a blurred, dilated mask so the edges move too); everything outside the mask stays still.
Shader "RogueStars/Anim/FlameFlicker"
{
    Properties
    {
        _MainTex ("Sprite", 2D) = "white" {}
        _FlameMask ("Flame mask (R)", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Distort ("Displacement (uv)", Range(0,0.06)) = 0.018
        _NoiseScale ("Noise scale", Float) = 7
        _Speed ("Rise speed", Float) = 1.4
        _Flicker ("Brightness flicker", Range(0,1)) = 0.22
        _FlickerSpeed ("Flicker speed", Float) = 9
        _BaseY ("Flame base (uv y)", Range(0,1)) = 0.3
        _Pad ("Quad padding (uv)", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Cull Off Lighting Off ZWrite Off
        Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "AnimCommon.cginc"
            sampler2D _MainTex, _FlameMask; fixed4 _Color;
            float _Distort, _NoiseScale, _Speed, _Flicker, _FlickerSpeed, _BaseY, _Pad;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; float phase : TEXCOORD1; };
            v2f vert(appdata v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv * (1 + 2 * _Pad) - _Pad; o.color = v.color * _Color; o.phase = AnimObjectPhase(); return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y + i.phase;
                float m = tex2D(_FlameMask, saturate(i.uv)).r * AnimInside(i.uv);
                // stronger toward the tips: the base of the flames sits on the logs
                float rise = saturate((i.uv.y - _BaseY) / (1 - _BaseY));
                float2 q = float2(i.uv.x * _NoiseScale, i.uv.y * _NoiseScale - t * _Speed * 2);
                float2 off = float2(AnimNoise(q) - 0.5, (AnimNoise(q + 17.3) - 0.5) * 0.6) * _Distort * m * (0.35 + rise * 1.3);
                float2 uv = i.uv + off;
                fixed4 c = tex2D(_MainTex, uv) * i.color * AnimInside(uv);
                float m2 = tex2D(_FlameMask, saturate(uv)).r;
                float f = (AnimNoise(float2(t * _FlickerSpeed, uv.y * 3)) - 0.5) * 2;
                c.rgb *= 1 + _Flicker * m2 * f;
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
