Shader "WorldSim/NprDiorama"
{
    Properties
    {
        _RimColor ("Rim Color", Color) = (0.23, 0.16, 0.10, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.5
        _Saturation ("Saturation", Range(0, 1)) = 0.95
        _Brightness ("Brightness", Range(0.5, 1.5)) = 1.05
        _DetailMap ("Detail Probe", 2D) = "white" {}
        _DetailStrength ("Detail Strength", Range(0, 1)) = 0.35
        _DetailTiling ("Detail Tiling", Range(1, 32)) = 8
        _WaterColor ("Water Color", Color) = (0.37, 0.48, 0.55, 1)
        _WaterFresnel ("Water Fresnel", Range(0.5, 8)) = 3.0
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
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
                float4 color : COLOR;
                float2 uv : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
            };

            TEXTURE2D(_DetailMap);
            SAMPLER(sampler_DetailMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _RimColor;
                float _RimPower;
                float _Saturation;
                float _Brightness;
                float4 _DetailMap_ST;
                float _DetailStrength;
                float _DetailTiling;
                float4 _WaterColor;
                float _WaterFresnel;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.viewDirWS = GetWorldSpaceViewDir(positionWS);
                o.color = input.color;
                o.uv = input.uv;
                o.positionWS = positionWS;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(i.viewDirWS);
                float ndotv = saturate(dot(n, v));
                float rim = pow(saturate(1.0 - ndotv), _RimPower);

                // vertex.a：1=陆地，0=水面（ColorForTile 写入）
                float landMask = saturate(i.color.a);

                float3 land = i.color.rgb * _Brightness;
                float luma = dot(land, float3(0.299, 0.587, 0.114));
                land = lerp(luma.xxx, land, _Saturation);

                float2 duv = i.uv * _DetailTiling;
                float3 detail = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, duv).rgb;
                // 近距混入笔触：乘性压暗一点点 + 轻微叠加，避免塑料色块
                land = lerp(land, land * detail * 1.05, _DetailStrength * landMask);
                land = lerp(land, _RimColor.rgb, rim * 0.35 * landMask);

                // 水面：灰蓝 + Fresnel 亮边，弱波纹（无 SSR/焦散）
                float fresnel = pow(saturate(1.0 - ndotv), _WaterFresnel);
                float wave = sin(i.positionWS.x * 3.5 + i.positionWS.z * 2.7 + _Time.y * 0.6) * 0.015
                           + sin(i.positionWS.x * 7.0 - i.positionWS.z * 5.0 + _Time.y * 1.1) * 0.008;
                float3 water = _WaterColor.rgb * (0.92 + wave);
                water = lerp(water, water * 1.18, fresnel * 0.55);
                water = lerp(water, _RimColor.rgb, rim * 0.18);

                float3 c = lerp(water, land, landMask);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
