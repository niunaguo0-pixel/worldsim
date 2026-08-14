Shader "WorldSim/NprWater"
{
    Properties
    {
        _BaseColor ("Water Color", Color) = (0.37, 0.48, 0.55, 0.85)
        _RimColor ("Rim Color", Color) = (0.23, 0.16, 0.10, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.8
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 3.0
        _WaveStrength ("Wave Strength", Range(0, 0.05)) = 0.015
    }
    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
            "RenderPipeline"="UniversalPipeline"
            "IgnoreProjector"="True"
        }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            Name "NprWater"
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
                float3 positionWS : TEXCOORD2;
                float4 color : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _RimColor;
                float _RimPower;
                float _FresnelPower;
                float _WaveStrength;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.viewDirWS = GetWorldSpaceViewDir(positionWS);
                o.positionWS = positionWS;
                o.color = input.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(i.viewDirWS);
                float ndotv = saturate(dot(n, v));
                float rim = pow(saturate(1.0 - ndotv), _RimPower);
                float fresnel = pow(saturate(1.0 - ndotv), _FresnelPower);

                float wave = sin(i.positionWS.x * 4.0 + i.positionWS.z * 3.0 + _Time.y * 0.7) * _WaveStrength
                           + sin(i.positionWS.x * 9.0 - i.positionWS.z * 6.0 + _Time.y * 1.3) * (_WaveStrength * 0.5);

                float3 c = _BaseColor.rgb * i.color.rgb;
                c *= (1.0 + wave * 8.0);
                c = lerp(c, c * 1.2, fresnel * 0.5);
                c = lerp(c, _RimColor.rgb, rim * 0.2);
                float a = saturate(_BaseColor.a * (0.75 + fresnel * 0.2));
                return half4(c, a);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
