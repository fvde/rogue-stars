// Portal vortex on the sprite: inside a disc (_Center, _Radius in uv) the picture turns around the centre, faster toward the
// middle, with a breathing glow; outside it stays still. Seamless loop (the angle only grows), no video.
Shader "RogueStars/Anim/Swirl"
{
    Properties
    {
        _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Phase ("Phase", Float) = 0
        _Center ("Centre (uv)", Vector) = (0.5,0.5,0,0)
        _Radius ("Radius (uv)", Float) = 0.3
        _Squash ("Disc height / width", Float) = 0.6
        _Spin ("Turns per second at the centre", Float) = 0.35
        _Twist ("Extra twist toward the centre (radians)", Float) = 2.2
        _Glow ("Glow colour", Color) = (0.7,0.4,1,1)
        _GlowAmount ("Glow", Range(0,1)) = 0.3
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
            sampler2D _MainTex; fixed4 _Color, _Glow; float4 _Center;
            float _Radius, _Squash, _Spin, _Twist, _GlowAmount;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.color = v.color * _Color; return o; }
            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y + AnimObjectPhase();
                // work in the disc's own (squashed) space so the rotation stays round on screen
                float2 d = (i.uv - _Center.xy) / float2(1, _Squash);
                float r = length(d) / _Radius;
                float w = saturate(1 - r);
                w = w * w * (3 - 2 * w);
                float a = (t * _Spin * 6.2832 + _Twist * w * (0.6 + 0.4 * sin(t * 0.9))) * w;
                float cs = cos(a), sn = sin(a);
                float2 q = float2(d.x * cs - d.y * sn, d.x * sn + d.y * cs);
                float2 uv = _Center.xy + q * float2(1, _Squash);
                fixed4 c = tex2D(_MainTex, lerp(i.uv, uv, step(0.0001, w))) * i.color;
                float pulse = 0.5 + 0.5 * sin(t * 1.7);
                c.rgb += _Glow.rgb * w * w * (0.4 + 0.6 * pulse) * _GlowAmount * c.a;
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
