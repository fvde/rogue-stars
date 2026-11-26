// Additive soft glow (fire light, portal light): procedural radial falloff, no texture; pulse + noisy flicker.
Shader "RogueStars/Anim/GlowPulse"
{
    Properties
    {
        _Color ("Glow colour", Color) = (1,0.6,0.2,1)
        _Intensity ("Intensity", Range(0,3)) = 0.8
        _Falloff ("Falloff", Range(0.5,6)) = 2.2
        _Pulse ("Pulse amount", Range(0,1)) = 0.25
        _PulseSpeed ("Pulse speed", Float) = 2.2
        _Flicker ("Flicker amount", Range(0,1)) = 0.2
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Cull Off Lighting Off ZWrite Off
        Blend One One
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "AnimCommon.cginc"
            fixed4 _Color; float _Intensity, _Falloff, _Pulse, _PulseSpeed, _Flicker;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; float phase : TEXCOORD1; };
            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.color = v.color; o.phase = AnimObjectPhase(); return o; }
            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y + i.phase;
                float d = saturate(1 - length(i.uv - 0.5) * 2);
                float k = 1 + _Pulse * sin(t * _PulseSpeed) + _Flicker * (AnimNoise(float2(t * 7, 0)) - 0.5) * 2;
                return _Color * i.color * pow(d, _Falloff) * _Intensity * k;
            }
            ENDCG
        }
    }
}
