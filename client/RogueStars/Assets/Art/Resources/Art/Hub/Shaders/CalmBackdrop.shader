// The home backdrop's calm grade (HubBackdrop): lower saturation, highlights pulled toward a low pivot (darks stay dark,
// no grey wash), a little darker, a slight navy tint; graded in gamma space (the project renders linear). One texture read
// and a few ALU ops per pixel; supports RectMask2D clipping and the UI stencil like UI/Default.
Shader "RogueStars/UI/CalmBackdrop"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Saturation ("Saturation (1 = unchanged)", Range(0,1.5)) = 0.75
        _Contrast ("Contrast (1 = unchanged)", Range(0,1.5)) = 0.85
        _Pivot ("Contrast pivot (gamma)", Range(0,1)) = 0.25
        _Brightness ("Brightness multiply", Range(0,1.5)) = 0.85
        _Grade ("Grade colour", Color) = (0.078,0.063,0.188,1)
        _GradeAmount ("Grade colour amount", Range(0,1)) = 0.08

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT

            sampler2D _MainTex;
            fixed4 _Color, _Grade, _TextureSampleAdd;
            float4 _ClipRect, _MainTex_ST;
            half _Saturation, _Contrast, _Pivot, _Brightness, _GradeAmount;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; float4 world : TEXCOORD1; };

            v2f vert(appdata v)
            {
                v2f o;
                o.world = v.vertex;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half4 c = (tex2D(_MainTex, i.uv) + _TextureSampleAdd) * i.color;
                half3 grade = _Grade.rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                c.rgb = LinearToGammaSpace(c.rgb); // grade as authored (the constants were tuned on the sRGB art)
                grade = LinearToGammaSpace(grade);
                #endif
                half lum = dot(c.rgb, half3(0.299, 0.587, 0.114));
                c.rgb = lerp(lum.xxx, c.rgb, _Saturation);
                c.rgb = (c.rgb - _Pivot) * _Contrast + _Pivot;
                c.rgb = saturate(c.rgb * _Brightness);
                c.rgb = lerp(c.rgb, grade, _GradeAmount);
                #ifndef UNITY_COLORSPACE_GAMMA
                c.rgb = GammaToLinearSpace(c.rgb);
                #endif
                #ifdef UNITY_UI_CLIP_RECT
                c.a *= UnityGet2DClipping(i.world.xy, _ClipRect);
                #endif
                return c;
            }
            ENDCG
        }
    }
}
