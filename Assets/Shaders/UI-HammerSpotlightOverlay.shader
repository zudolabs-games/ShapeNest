Shader "UI/HammerSpotlightOverlay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        _HoleFeather ("Hole Feather", Range(0, 0.03)) = 0.003
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float _HoleFeather;
            float _SpotlightHoleCount;
            float4 _SpotlightHoles[24];
            float4 _OverlayRect;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.worldPosition = IN.vertex;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color * _Color;
                return OUT;
            }

            float HoleMask(float2 uv)
            {
                int count = (int)_SpotlightHoleCount;
                float mask = 0;
                float feather = max(_HoleFeather, 0.0001);
                int i;
                for (i = 0; i < 24; i++)
                {
                    if (i >= count)
                    {
                        break;
                    }

                    float4 hole = _SpotlightHoles[i];
                    float2 bmin = hole.xy;
                    float2 bmax = hole.zw;
                    float2 d = max(bmin - uv, uv - bmax);
                    float dist = length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
                    float inside = 1.0 - smoothstep(-feather, feather, dist);
                    mask = max(mask, inside);
                }

                return mask;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 color = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                float2 overlaySize = max(_OverlayRect.zw - _OverlayRect.xy, float2(0.0001, 0.0001));
                float2 overlayUv = (IN.worldPosition.xy - _OverlayRect.xy) / overlaySize;
                color.a *= 1.0 - HoleMask(overlayUv);
                return color;
            }
            ENDCG
        }
    }

    FallBack "UI/Default"
}
