Shader "Unlit/geometryShader"
{
    Properties
    {
        
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
            #pragma geometry Geometry

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
            

            appdata vert (appdata v)
            {
                appdata o;
                o.positionOS = v.positionOS;
                o.uv = v.uv;
                return o;
            }
            
            [maxvertexcount(4)]
            void Geometry(point appdata input[1],inout TriangleStream<v2f> stream)
            {
                
            }

            half4 frag (v2f i) : SV_Target
            {
                return float4(0,0,0,1);
            }
            ENDHLSL
        }
    }
}
