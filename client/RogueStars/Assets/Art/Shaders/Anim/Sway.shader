// Generic wind sway for standing props (trees, mushrooms, banners): a shear that grows with height above _Base, two
// layered sines (sway + gust), per-object phase. Fragment-only: works on a plain sprite with transparent margins or on a
// padded quad (_Pad, AnimProp.Quad) so the sway never clips at the rect's edge.
Shader "RogueStars/Anim/Sway"
{
    Properties
    {
        _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Amount ("Sway (uv at the top)", Range(0,0.1)) = 0.025
        _Speed ("Speed", Float) = 1.3
        _Gust ("Gust share", Range(0,1)) = 0.35
        _Base ("Rooted below (uv y)", Range(0,1)) = 0.2
        _Power ("Stiffness curve", Range(0.5,4)) = 1.8
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
            sampler2D _MainTex; fixed4 _Color;
            float _Amount, _Speed, _Gust, _Base, _Power, _Pad;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; float phase : TEXCOORD1; };
            v2f vert(appdata v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv * (1 + 2 * _Pad) - _Pad; o.color = v.color * _Color; o.phase = AnimObjectPhase(); return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y * _Speed + i.phase;
                float h = pow(saturate((i.uv.y - _Base) / (1 - _Base)), _Power);
                float wave = sin(t) * (1 - _Gust) + sin(t * 2.3 + 1.7) * _Gust * (0.6 + 0.4 * sin(t * 0.37));
                // the canopy also bobs a little (leaves dip as they swing)
                float2 uv = i.uv - float2(wave * _Amount * h, abs(wave) * _Amount * 0.15 * h);
                fixed4 c = tex2D(_MainTex, uv) * i.color * AnimInside(uv);
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
