Shader "New_Grass/VoronoiClumpMap"
{
    Properties
    {
        [Header(Clump Layout)]
        _NumClumpTypes ("Clump Types", Range(1, 40)) = 40
        _NumClumps ("Clump Count", Range(1, 100)) = 24
        _VoronoiScale ("Voronoi Scale", Float) = 1
        _Seed ("Seed", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "VoronoiClumpMap"
            ZWrite On
            ZTest Always
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float _NumClumpTypes;
                float _NumClumps;
                float _VoronoiScale;
                float _Seed;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float2 Hash22(float2 p)
            {
                float3 a = frac(float3(p.x, p.y, p.x) * float3(123.34, 234.34, 345.65));
                a += dot(a, a + 34.45);
                return frac(float2(a.x * a.y, a.y * a.z));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = saturate(input.uv);
                float minDist = 1e10;
                float clumpTypeId = 0.0;
                float2 clumpCenter = float2(0.0, 0.0);

                int clumpLimit = min(100, max(1, (int)round(_NumClumps)));
                int clumpTypeCount = max(1, (int)round(_NumClumpTypes));

                [loop]
                for (int j = 0; j < clumpLimit; j++)
                {
                    float index = (float)j + _Seed;
                    float2 center = Hash22(float2(index, index * 1.731));
                    float dist = distance(center, uv);

                    if (dist < minDist)
                    {
                        minDist = dist;
                        clumpTypeId = fmod((float)j, (float)clumpTypeCount);
                        clumpCenter = center;
                    }
                }
                return half4(clumpTypeId, clumpCenter.x, clumpCenter.y, 1.0);
            }
            ENDHLSL
        }
    }
}
