// Soft smoke puff for particles: alpha-blended disc with a little noise, colour and fade from the particle's vertex colour.
Shader "RogueStars/Anim/Puff"
{
    Properties { _Soft ("Softness", Range(0.5,4)) = 1.6 }
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
            float _Soft;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.color = v.color; return o; }
            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = i.uv - 0.5;
                float d = saturate(1 - length(p) * 2);
                float n = 0.75 + 0.5 * (AnimNoise(i.uv * 5 + i.color.r * 9) - 0.5);
                float a = pow(d, _Soft) * n * i.color.a;
                return fixed4(i.color.rgb * a, a);
            }
            ENDCG
        }
    }
}
