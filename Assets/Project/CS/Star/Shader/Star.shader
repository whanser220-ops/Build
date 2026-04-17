Shader "Unlit/Star"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _RoughnessMap ("Roughness Map", 2D) = "white" {}
    }
    
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma instancing_options procedural:setup
            
            // Unity 6 核心库引用
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float _StarCount;
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);
            TEXTURE2D(_RoughnessMap);
            SAMPLER(sampler_RoughnessMap);
            float4 _MainTex_ST;
            
            // 顶点输入结构
            struct Attributes
            {
                float4 positionOS   : POSITION;
                float2 UV           : TEXCOORD0;
                float3 normalOS     : NORMAL;
                float4 tangentOS    : TANGENT;
                uint instanceID     : SV_InstanceID;
            };
            
            // 顶点到片元结构
            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float2 UV           : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
                float4 tangentWS    : TEXCOORD2;
                float3 bitangentWS  : TEXCOORD3;
                float3 positionWS   : TEXCOORD4;
                uint instanceID     : SV_InstanceID;
            };
            
            struct StarData
            {
                float3 position;
                float3 scale;
                float3 rotation;
                float seed;
            };

            StructuredBuffer<StarData> CullingOutputBuffer;

            float4x4 axis_angle_to_matrix(float3 axis, float angle)
            {
                float c = cos(angle);
                float s = sin(angle);
                float t = 1.0 - c;
                float3 n = normalize(axis);
                return float4x4(
                    t * n.x * n.x + c,      t * n.x * n.y - s * n.z, t * n.x * n.z + s * n.y, 0,
                    t * n.x * n.y + s * n.z, t * n.y * n.y + c,      t * n.y * n.z - s * n.x, 0,
                    t * n.x * n.z - s * n.y, t * n.y * n.z + s * n.x, t * n.z * n.z + c,      0,
                    0, 0, 0, 1
                );
            }
            
            float4x4 scale_matrix(float3 scale)
            {
                return float4x4(scale.x, 0, 0, 0, 0, scale.y, 0, 0, 0, 0, scale.z, 0, 0, 0, 0, 1);
            }
            
            float4x4 translation_matrix(float3 pos)
            {
                return float4x4(1, 0, 0, pos.x, 0, 1, 0, pos.y, 0, 0, 1, pos.z, 0, 0, 0, 1);
            }
            
            float3 ApplyNormalMap(float3 normalWS, float4 tangentWS, float3 bitangentWS, float3 normalMap)
            {
                // 解包法线贴图 (0-1 -> -1-1)
                float3 tangentNormal = normalMap * 2.0 - 1.0;
                
                // 创建TBN矩阵
                float3x3 tangentToWorld = float3x3(
                    tangentWS.xyz,
                    bitangentWS,
                    normalWS
                );
                
                // 转换到世界空间
                return normalize(mul(tangentNormal, tangentToWorld));
            }

            void setup()
            {
                
            }
            
            // 顶点着色器
            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                uint instanceID = input.instanceID;
                StarData star;
                if (instanceID < (uint)_StarCount)
                {
                    star = CullingOutputBuffer[instanceID];
                    // 构造变换矩阵
                    float4x4 scaleMat = scale_matrix(star.scale);
                    float4x4 rotMat = axis_angle_to_matrix(
                        normalize(star.rotation), 
                        length(star.rotation)
                    );
                    float4x4 transMat = translation_matrix(star.position);
                    // 组合变换矩阵: 先缩放 -> 旋转 -> 平移
                    float4x4 objectToWorld = mul(transMat, mul(rotMat, scaleMat));
                    // 应用变换
                    float4 worldPos = mul(objectToWorld, input.positionOS);
                    output.positionCS = mul(UNITY_MATRIX_VP, worldPos);
                }
                else
                {
                    // 默认值，防止越界
                    star.position = float3(1, 1, 1);
                    star.scale = float3(1, 1, 1);
                    star.rotation = float3(0, 0, 0);
                    star.seed = 0;
                }
                

                // 获取实例化的顶点位置
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);

                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.normalWS = normalInput.normalWS;
                output.tangentWS = float4(normalInput.tangentWS.xyz, input.tangentOS.w);
                output.bitangentWS = normalInput.bitangentWS;
                output.positionWS = vertexInput.positionWS;
                output.UV = input.UV;
                return output;
            }
            
            // 片元着色器
            half4 frag(Varyings input) : SV_Target
            {
                // 获取当前实例的颜色属性
                half4 color = SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.UV);
                half3 normalMap = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.UV).rgb;
                half roughness = SAMPLE_TEXTURE2D(_RoughnessMap, sampler_RoughnessMap, input.UV).r;

                half3 normalWS = ApplyNormalMap(
                    input.normalWS,
                    input.tangentWS,
                    input.bitangentWS,
                    normalMap
                );

                //向量准备
                Light MainLight = GetMainLight();
                float3 LightDir = MainLight.direction;
                float3 ViewDir = GetWorldSpaceViewDir(input.positionWS);
                //漫反射
                half NdotL = saturate((dot(normalWS,LightDir)+0.5)/2);
                half3 diffuse = MainLight.color * NdotL * color;
                
                return float4(diffuse,1);
            }
            ENDHLSL
        }
    }
    
    // 回退到内置渲染管线（可选）
    Fallback "Universal Render Pipeline/Unlit"
}
