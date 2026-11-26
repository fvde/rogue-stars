// Sprite-sheet loop on the GPU: the frame comes from _Time (no script, no per-frame CPU work); rows run top to bottom.
Shader "RogueStars/Anim/Flipbook"
{
    Properties
    {
        _MainTex ("Sheet", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Cols ("Columns", Float) = 6
        _Rows ("Rows", Float) = 4
        _Frames ("Frames", Float) = 24
        _Fps ("Frames per second", Float) = 12
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
            sampler2D _MainTex; fixed4 _Color; float _Cols, _Rows, _Frames, _Fps;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; float phase : TEXCOORD1; };
            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.color = v.color * _Color; o.phase = AnimObjectPhase() * 5; return o; }
            fixed4 frag(v2f i) : SV_Target
            {
                float f = floor(fmod(_Time.y * _Fps + i.phase, _Frames));
                float col = fmod(f, _Cols), row = floor(f / _Cols);
                float2 uv = (float2(col, _Rows - 1 - row) + saturate(i.uv)) / float2(_Cols, _Rows);
                fixed4 c = tex2D(_MainTex, uv) * i.color;
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
