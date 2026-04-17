Shader "Unlit/Boid"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma instancing_options procedural:setup


            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct appdata
            {
                float4 positionOS   : POSITION;
                
                float2 uv           : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv           : TEXCOORD0;
                float4 positionCS   : SV_POSITION;
            };
            struct Boids
            {
                float3 position;
                float3 Dirction;
                float noise_offset;
            };

            StructuredBuffer<Boids> BoidsBuffer;

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4x4 _Matrix;
            float3 _BoidPosition;
            float4 _MainTex_ST;


            float4x4 create_matrix(float3 pos, float3 dir, float3 up) {
            float3 zaxis = normalize(dir);
            float3 xaxis = normalize(cross(up, zaxis));
            float3 yaxis = cross(zaxis, xaxis);
            return float4x4(
                xaxis.x, yaxis.x, zaxis.x, pos.x,
                xaxis.y, yaxis.y, zaxis.y, pos.y,
                xaxis.z, yaxis.z, zaxis.z, pos.z,
                0, 0, 0, 1
                );
            }

            void setup()
            {
                #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                 _BoidPosition = BoidsBuffer[unity_InstanceID].position;
                _Matrix = create_matrix(BoidsBuffer[unity_InstanceID].position, BoidsBuffer[unity_InstanceID].Dirction, float3(0.0, 1.0, 0.0));
                #endif
            }

            v2f vert (appdata v)
            {
                v2f o;
                v.positionOS = mul(_Matrix, v.positionOS);
                o.positionCS =TransformObjectToHClip(v.positionOS);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                // sample the texture
                half4 col = SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv);
                return col;
            }
            ENDHLSL
        }
    }
}
