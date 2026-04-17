Shader "New_Grass/VolumeCloud/LearningRaymarch3DNoise"
{
    Properties
    {
        
        _NoiseTex3D ("三维噪声图", 3D) = "" {}
        _DensityMultiplier ("密度倍率", Range(0.0, 8.0)) = 2.2
        _DensityThreshold ("密度阈值", Range(0.0, 1.0)) = 0.45
        _Absorption ("吸收强度", Range(0.01, 8.0)) = 2.2

        _StepCount ("步进次数", Range(8, 128)) = 32
        _BoxFade ("边界淡化", Range(0.001, 0.5)) = 0.08
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            Cull Front
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define MAX_STEPS 128

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            TEXTURE3D(_NoiseTex3D);
            SAMPLER(sampler_NoiseTex3D);

            CBUFFER_START(UnityPerMaterial)
                float _DensityMultiplier;
                float _DensityThreshold;
                float _Absorption;
                float _StepCount;
                float _BoxFade;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                return output;
            }

            float3 GetSafeRayDirection(float3 rayDir)
            {
                float3 safeRayDir = rayDir;
                float3 nearZeroMask = 1.0 - step(float3(1.0e-5, 1.0e-5, 1.0e-5), abs(rayDir));
                float3 replacementDir = float3(
                    rayDir.x >= 0.0 ? 1.0e-5 : -1.0e-5,
                    rayDir.y >= 0.0 ? 1.0e-5 : -1.0e-5,
                    rayDir.z >= 0.0 ? 1.0e-5 : -1.0e-5);
                return lerp(safeRayDir, replacementDir, nearZeroMask);
            }

            bool RayBoxIntersection(
                float3 rayOriginOS,
                float3 rayDirOS,
                float3 boxMinOS,
                float3 boxMaxOS,
                out float tEnter,
                out float tExit)
            {
                float3 safeRayDir = GetSafeRayDirection(rayDirOS);
                float3 t0 = (boxMinOS - rayOriginOS) / safeRayDir;
                float3 t1 = (boxMaxOS - rayOriginOS) / safeRayDir;

                float3 tMin3 = min(t0, t1);
                float3 tMax3 = max(t0, t1);

                tEnter = max(max(tMin3.x, tMin3.y), tMin3.z);
                tExit = min(min(tMax3.x, tMax3.y), tMax3.z);
                return tExit > max(tEnter, 0.0);
            }

            float SampleEdgeFade(float3 uvw)
            {
                float3 edgeDistance = min(uvw, 1.0 - uvw);
                float minDistance = min(edgeDistance.x, min(edgeDistance.y, edgeDistance.z));
                return saturate(minDistance / max(_BoxFade, 1.0e-4));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 cameraWS = GetCameraPositionWS();
                float3 cameraOS = mul(GetWorldToObjectMatrix(), float4(cameraWS, 1.0)).xyz;
                float3 rayDirOS = normalize(input.positionOS - cameraOS);

                float tEnter;
                float tExit;
                if (!RayBoxIntersection(
                    cameraOS,
                    rayDirOS,
                    float3(-0.5, -0.5, -0.5),
                    float3(0.5, 0.5, 0.5),
                    tEnter,
                    tExit))
                {
                    discard;
                }

                float rayStart = max(tEnter, 0.0);
                float rayLength = tExit - rayStart;
                if (rayLength <= 1.0e-5)
                {
                    discard;
                }

                int stepCount = clamp((int)round(_StepCount), 1, MAX_STEPS);
                float stepSize = rayLength / stepCount;
                float3 samplePosOS = cameraOS + rayDirOS * (rayStart + stepSize * 0.5);

                float transmittance = 1.0;
                float accumulatedDensity = 0.0;

                [loop]
                for (int step = 0; step < MAX_STEPS; step++)
                {
                    if (step >= stepCount)
                    {
                        break;
                    }

                    float3 uvw = samplePosOS + float3(0.5, 0.5, 0.5);
                    float edgeMask = SampleEdgeFade(uvw);
                    float noiseValue = SAMPLE_TEXTURE3D(_NoiseTex3D, sampler_NoiseTex3D, uvw).r;
                    float density = saturate((noiseValue - _DensityThreshold) * _DensityMultiplier);
                    density *= edgeMask;

                    float stepAlpha = 1.0 - exp(-density * _Absorption * stepSize);
                    accumulatedDensity += density * stepSize * transmittance * 2.0;
                    transmittance *= 1.0 - stepAlpha;

                    if (transmittance < 0.01)
                    {
                        break;
                    }

                    samplePosOS += rayDirOS * stepSize;
                }

                float finalAlpha = 1.0 - transmittance;
                float finalGray = saturate(accumulatedDensity + finalAlpha * 0.35);
                if (finalAlpha <= 1.0e-4)
                {
                    discard;
                }

                return half4(finalGray.xxx, finalAlpha);
            }
            ENDHLSL
        }
    }
}
