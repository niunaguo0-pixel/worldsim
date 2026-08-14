Shader "WorldSim/NprDiorama"
{
    Properties
    {
        _RimColor ("Rim Color", Color) = (0.23, 0.16, 0.10, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.5
        _Saturation ("Saturation", Range(0, 1)) = 0.72
        _Brightness ("Brightness", Range(0.5, 1.5)) = 1.05
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        LOD 100
        Pass
        {
            Name "NprUnlit"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
                float4 color : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _RimColor;
                float _RimPower;
                float _Saturation;
                float _Brightness;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.viewDirWS = GetWorldSpaceViewDir(positionWS);
                o.color = input.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(i.viewDirWS);
                float rim = pow(saturate(1.0 - saturate(dot(n, v))), _RimPower);

                float3 c = i.color.rgb * _Brightness;
                float luma = dot(c, float3(0.299, 0.587, 0.114));
                c = lerp(luma.xxx, c, _Saturation);
                // 深褐细描边感：边缘叠一点轮廓色（美术圣经 A1）
                c = lerp(c, _RimColor.rgb, rim * 0.35);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
