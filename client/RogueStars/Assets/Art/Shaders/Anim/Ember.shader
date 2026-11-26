// Additive round particle (embers, sparks): soft dot from the quad's uv, colour from the particle's vertex colour.
Shader "RogueStars/Anim/Ember"
{
    Properties { _Intensity ("Intensity", Range(0,4)) = 1.6 }
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
            float _Intensity;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.color = v.color; return o; }
            fixed4 frag(v2f i) : SV_Target
            {
                float d = saturate(1 - length(i.uv - 0.5) * 2);
                float core = d * d;
                return fixed4(i.color.rgb * core * _Intensity * i.color.a + core * core * i.color.a * 0.6, 0);
            }
            ENDCG
        }
    }
}
