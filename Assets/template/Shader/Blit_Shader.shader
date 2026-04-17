Shader "Unlit/Blit_Shader"
{
    Properties
    {
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 100
        Cull Off
        ZWrite Off
        ZTest Always
        
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            

            float4 frag (Varyings input) : SV_Target
            {
                // 使用Blit.hlsl提供的正确采样方式
                half4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp,input.texcoord.xy);
                return col;
            }
            ENDHLSL
        }
    }
}
