Shader "Universal Render Pipeline/Custom/IceCrystalShell"
{
    Properties
    {
        _IceTex ("Ice Texture (2D)", 2D) = "white" {}
        _IceColor ("Ice Tint Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _CenterColor ("Center Translucent Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _BaseColor ("Milky Body Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _FrostColor ("Frosted Rim Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _GlossColor ("Gloss / Specular Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _CrackColor ("Crack Fracture Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _Smoothness ("Smoothness", Range(0.0, 1.0)) = 0.50
        _CrackAmount ("Crack Amount", Range(0.0, 1.0)) = 0.0
        _FreezeProgress ("Freeze Progress", Range(0.0, 1.0)) = 1.0
        _Shimmer ("Shimmer", Range(0.0, 1.0)) = 0.0
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Transparent" 
            "Queue" = "Transparent+20" 
            "RenderPipeline" = "UniversalPipeline" 
            "IgnoreProjector" = "True"
        }

        LOD 300
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float4 tangentOS    : TANGENT;
                float2 uv           : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                float3 positionOS   : TEXCOORD1;
                float2 uv           : TEXCOORD2;
                float3 normalWS     : TEXCOORD3;
            };

            TEXTURE2D(_IceTex);
            SAMPLER(sampler_IceTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _IceColor;
                float4 _CenterColor;
                float4 _BaseColor;
                float4 _FrostColor;
                float4 _GlossColor;
                float4 _CrackColor;
                float4 _IceTex_ST;
                float _Smoothness;
                float _CrackAmount;
                float _FreezeProgress;
                float _Shimmer;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.positionOS = input.positionOS.xyz;
                output.uv = input.uv;
                output.normalWS = normalInput.normalWS;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Top & side mapping derived from normalized position
                float3 geomNormalWS = normalize(input.normalWS);
                float isTopSurface = saturate((geomNormalWS.y - 0.20) * 3.0);

                float2 topUV = input.positionOS.xz * _IceTex_ST.xy * 0.90 + _IceTex_ST.zw + float2(0.5, 0.5);
                float2 sideUV = float2((input.positionOS.x + input.positionOS.z) * 0.70, input.positionOS.y * 1.10) * _IceTex_ST.xy + _IceTex_ST.zw + float2(0.5, 0.5);
                float2 texUV = lerp(sideUV, topUV, isTopSurface);

                // Sample raw ice.jpeg texture
                half4 iceSample = SAMPLE_TEXTURE2D(_IceTex, sampler_IceTex, texUV);
                half3 iceTexRGB = iceSample.rgb;
                float iceLuma = dot(iceTexRGB, half3(0.299, 0.587, 0.114));

                // Object-space radial distance
                float2 p = input.positionOS.xz * 2.0;
                float normX = abs(p.x);
                float normZ = abs(p.y);
                float boxDist = max(normX, normZ);
                float radDist = length(p) * 0.7071;
                float dist = lerp(boxDist, radDist, 0.45);

                // Use JPEG's natural texture details to deform the outer silhouette
                float organicEdgeDist = dist - (iceLuma - 0.5) * 0.12;
                float edgeMask = 1.0 - smoothstep(0.75, 0.96, organicEdgeDist);

                // Transparency hierarchy:
                // Center (dist < 0.40): transparent window (alpha ~0.30) so blue block is clear
                // Rim    (dist > 0.70): dense frosted border (alpha ~0.88-0.95) using JPEG colors
                float rimFactor = smoothstep(0.35, 0.85, dist);

                // Color composition: DOMINATED BY REAL ICE.JPEG PIXELS
                half3 frostBoost = float3(0.98, 1.0, 1.0) * smoothstep(0.50, 0.85, iceLuma) * 0.35;
                half3 finalRGB = iceTexRGB + frostBoost * rimFactor;

                // Alpha: translucent in center so block shows through, opaque at frosted rim
                float centerAlpha = 0.30;
                float rimAlpha = 0.88 + 0.10 * smoothstep(0.40, 0.80, iceLuma);
                float finalAlpha = lerp(centerAlpha, rimAlpha, rimFactor);

                // Apply organic edge mask from JPEG texture
                finalAlpha *= edgeMask;

                // Cracks for durability = 1
                if (_CrackAmount > 0.01)
                {
                    float darkLine = (1.0 - iceLuma) * _CrackAmount * isTopSurface;
                    float crackMask = smoothstep(0.35, 0.70, darkLine);
                    finalRGB = lerp(finalRGB, float3(1.0, 1.0, 1.0), crackMask * 0.80);
                    finalAlpha = max(finalAlpha, crackMask * 0.85 * edgeMask);
                }

                finalAlpha *= _FreezeProgress;

                // Phase 7: Calculate URP Directional Studio Light & Specular Sheen for 3D depth
                Light mainLight = GetMainLight();
                float3 lightDir = normalize(mainLight.direction);
                float3 viewDir = normalize(GetWorldSpaceAuthorizeViewDir(input.positionWS));
                float3 halfDir = normalize(lightDir + viewDir);

                float NdotL = saturate(dot(geomNormalWS, lightDir));
                float NdotH = saturate(dot(geomNormalWS, halfDir));
                float spec = pow(NdotH, 24.0 * _Smoothness + 8.0) * mainLight.distanceAttenuation;

                half3 litRGB = finalRGB * (0.65 + 0.35 * NdotL * mainLight.color) + mainLight.color * spec * 0.40;

                return half4(litRGB, saturate(finalAlpha));
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
