Shader "AIShader/SimplePBR"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (0.18, 0.35, 0.8, 1)
        _Metallic("Metallic", Range(0, 1)) = 0.0
        _Roughness("Roughness", Range(0.04, 1)) = 0.5
        [HDR] _EmissionColor("Emission Color", Color) = (0, 0, 0, 1)
        _EmissionIntensity("Emission Intensity", Range(0, 10)) = 0
        [Enum(Final,0,WorldNormal,1,ViewDirection,2,BaseColor,3,Metallic,4,Roughness,5,NoL,6,NoV,7,DirectDiffuse,8,DirectSpecular,9,IndirectDiffuse,10,Emission,11,Combine,12)]
        _AIShaderDebugChannel("Debug Channel", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 200

        Pass
        {
            Name "ForwardPBR"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Metallic;
                half _Roughness;
                half4 _EmissionColor;
                half _EmissionIntensity;
                half _AIShaderDebugChannel;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half3 FresnelSchlick(half cosTheta, half3 F0)
            {
                return F0 + (1.0h - F0) * pow(1.0h - saturate(cosTheta), 5.0h);
            }

            half DistributionGGX(half NoH, half roughness)
            {
                half a = roughness * roughness;
                half a2 = a * a;
                half denominator = NoH * NoH * (a2 - 1.0h) + 1.0h;
                return a2 / max(PI * denominator * denominator, 0.0001h);
            }

            half GeometrySchlickGGX(half NoX, half roughness)
            {
                half k = (roughness + 1.0h);
                k = (k * k) * 0.125h;
                return NoX / max(NoX * (1.0h - k) + k, 0.0001h);
            }

            half GeometrySmith(half NoV, half NoL, half roughness)
            {
                return GeometrySchlickGGX(NoV, roughness) * GeometrySchlickGGX(NoL, roughness);
            }

            half3 DebugOutput(half3 finalColor, half3 normalWS, half3 viewDirWS, half NoL, half NoV,
                half3 directDiffuse, half3 directSpecular, half3 indirectDiffuse, half3 emission)
            {
                int channel = (int)round(_AIShaderDebugChannel);
                if (channel == 1) return normalWS * 0.5h + 0.5h;
                if (channel == 2) return viewDirWS * 0.5h + 0.5h;
                if (channel == 3) return _BaseColor.rgb;
                if (channel == 4) return _Metallic.xxx;
                if (channel == 5) return _Roughness.xxx;
                if (channel == 6) return NoL.xxx;
                if (channel == 7) return NoV.xxx;
                if (channel == 8) return directDiffuse;
                if (channel == 9) return directSpecular;
                if (channel == 10) return indirectDiffuse;
                if (channel == 11) return emission;
                if (channel == 12) return directDiffuse + directSpecular + indirectDiffuse + emission;
                return finalColor;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 N = SafeNormalize(input.normalWS);
                half3 V = SafeNormalize(GetWorldSpaceNormalizeViewDir(input.positionWS));
                Light light = GetMainLight();
                half3 L = SafeNormalize(light.direction);
                half3 H = SafeNormalize(L + V);

                half NoL = saturate(dot(N, L));
                half NoV = saturate(dot(N, V));
                half NoH = saturate(dot(N, H));
                half VoH = saturate(dot(V, H));
                half3 F0 = lerp(0.04h.xxx, _BaseColor.rgb, _Metallic);
                half3 F = FresnelSchlick(VoH, F0);
                half D = DistributionGGX(NoH, _Roughness);
                half G = GeometrySmith(NoV, NoL, _Roughness);
                half3 specularBRDF = (D * G * F) / max(4.0h * NoV * NoL, 0.0001h);
                half3 diffuseBRDF = _BaseColor.rgb * (1.0h - _Metallic) / PI;
                half3 radiance = light.color * (light.distanceAttenuation * light.shadowAttenuation);
                half3 directDiffuse = diffuseBRDF * radiance * NoL;
                half3 directSpecular = specularBRDF * radiance * NoL;
                half3 indirectDiffuse = SampleSH(N) * diffuseBRDF;
                half3 emission = _EmissionColor.rgb * _EmissionIntensity;
                half3 finalColor = directDiffuse + directSpecular + indirectDiffuse + emission;
                return half4(DebugOutput(finalColor, N, V, NoL, NoV, directDiffuse, directSpecular, indirectDiffuse, emission), 1.0h);
            }
            ENDHLSL
        }
    }
}
