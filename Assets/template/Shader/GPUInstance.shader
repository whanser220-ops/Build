Shader "Unlit/GPUInstance"
{
    Properties
    {
        _Color("Color", Color) = (1, 1, 1, 1)
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
            #pragma multi_compile_instancing // 启用GPU实例化
            
            // Unity 6 核心库引用
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            
            // 顶点输入结构
            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID // GPU实例化ID
            };
            
            // 顶点到片元结构
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID // 传递实例化ID到片元着色器
            };
            
            //定义了一个实例化属性缓冲区，允许每个GPU实例拥有不同的材质属性值
            //创建一个名为UnityPerMaterial的结构化缓冲区
            //这个缓冲区在GPU内存中存储所有实例的属性数据
            UNITY_INSTANCING_BUFFER_START(UnityPerMaterial)
                //定义一个可以每个实例不同的属性
                //float4是数据类型，_Color是属性名
                //实际上创建了一个数组：float4 _ColorArray[实例数量]
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
            UNITY_INSTANCING_BUFFER_END(UnityPerMaterial)
            
            // 顶点着色器
            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                
                // 设置实例化ID
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                
                // 获取实例化的顶点位置
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                
                return output;
            }
            
            // 片元着色器
            half4 frag(Varyings input) : SV_Target
            {
                // 设置实例化ID
                UNITY_SETUP_INSTANCE_ID(input);
                
                // 获取当前实例的颜色属性
                half4 color = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Color);
                
                return color;
            }
            
            ENDHLSL
        }
    }
    
    // 回退到内置渲染管线（可选）
    Fallback "Universal Render Pipeline/Unlit"
}
