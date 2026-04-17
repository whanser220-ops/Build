Shader "Unlit/ProceduralInstance"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
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
            float4 _MainTex_ST;
            
            // 顶点输入结构
            struct Attributes
            {
                float4 positionOS   : POSITION;
                float2 UV           : TEXCOORD0;
                uint instanceID     :SV_InstanceID;
            };
            
            // 顶点到片元结构
            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float2 UV           : TEXCOORD0;
                uint instanceID     :SV_InstanceID;
            };
            
            struct StarData
            {
                float3 position;
                float3 scale;
                float3 rotation;
                float seed;
            };

            StructuredBuffer<StarData> StarBuffer;

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
                    star = StarBuffer[instanceID];
                }
                else
                {
                    // 默认值，防止越界
                    star.position = float3(1, 1, 1);
                    
                }
                
                // 获取实例化的顶点位置
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz+star.position);
                output.positionCS = vertexInput.positionCS;
                output.UV = input.UV;
                return output;
            }
            
            // 片元着色器
            half4 frag(Varyings input) : SV_Target
            {
                // 获取当前实例的颜色属性
                half4 color = SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.UV);
                
                return color;
            }
            ENDHLSL
        }
    }
    
    // 回退到内置渲染管线（可选）
    Fallback "Universal Render Pipeline/Unlit"
}
