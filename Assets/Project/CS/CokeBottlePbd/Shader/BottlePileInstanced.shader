Shader "Project/CokeBottlePbd/InstancedLit"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.15
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma enable_d3d11_debug_symbols
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
            #include "UnityIndirect.cginc"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct MatrixRows
            {
                float4 row0;
                float4 row1;
                float4 row2;
            };

            StructuredBuffer<MatrixRows> _InstanceTransforms;

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _Smoothness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            float3 TransformPosition(MatrixRows rows, float3 positionOS)
            {
                return rows.row0.xyz * positionOS.x
                    + rows.row1.xyz * positionOS.y
                    + rows.row2.xyz * positionOS.z
                    + float3(rows.row0.w, rows.row1.w, rows.row2.w);
            }

            float3 TransformDirection(MatrixRows rows, float3 directionOS)
            {
                return rows.row0.xyz * directionOS.x
                    + rows.row1.xyz * directionOS.y
                    + rows.row2.xyz * directionOS.z;
            }

            Varyings vert(Attributes input)
            {
                InitIndirectDrawArgs(0);
                uint instanceID = GetIndirectInstanceID_Base(input.instanceID);
                MatrixRows rows = _InstanceTransforms[instanceID];
                float3 positionWS = TransformPosition(rows, input.positionOS.xyz);
                float3 normalWS = normalize(TransformDirection(rows, input.normalOS));

                Varyings output;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.normalWS = normalWS;
                output.positionWS = positionWS;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                Light mainLight = GetMainLight();
                float3 normalWS = normalize(input.normalWS);
                float3 lightDirection = normalize(mainLight.direction);
                float3 viewDirection = normalize(GetWorldSpaceViewDir(input.positionWS));
                float3 halfVector = normalize(lightDirection + viewDirection);

                float lambert = saturate(dot(normalWS, lightDirection));
                float specular = pow(saturate(dot(normalWS, halfVector)), lerp(4.0, 64.0, _Smoothness));

                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                float3 ambient = SampleSH(normalWS) * baseSample.rgb;
                float3 direct = baseSample.rgb * lambert * mainLight.color;
                float3 highlight = specular * mainLight.color * 0.2;

                return half4(ambient + direct + highlight, baseSample.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma enable_d3d11_debug_symbols
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
            #include "UnityIndirect.cginc"

            struct MatrixRows
            {
                float4 row0;
                float4 row1;
                float4 row2;
            };

            StructuredBuffer<MatrixRows> _InstanceTransforms;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            float3 TransformPosition(MatrixRows rows, float3 positionOS)
            {
                return rows.row0.xyz * positionOS.x
                    + rows.row1.xyz * positionOS.y
                    + rows.row2.xyz * positionOS.z
                    + float3(rows.row0.w, rows.row1.w, rows.row2.w);
            }

            float3 TransformDirection(MatrixRows rows, float3 directionOS)
            {
                return rows.row0.xyz * directionOS.x
                    + rows.row1.xyz * directionOS.y
                    + rows.row2.xyz * directionOS.z;
            }

            Varyings vert(Attributes input)
            {
                InitIndirectDrawArgs(0);
                uint instanceID = GetIndirectInstanceID_Base(input.instanceID);
                MatrixRows rows = _InstanceTransforms[instanceID];
                float3 positionWS = TransformPosition(rows, input.positionOS.xyz);
                float3 normalWS = normalize(TransformDirection(rows, input.normalOS));

                Varyings output;
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _MainLightPosition.xyz));
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
