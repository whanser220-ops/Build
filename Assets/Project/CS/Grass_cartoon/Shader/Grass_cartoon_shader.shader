Shader "Unlit/Grass_cartoon_shader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (0.4, 0.8, 0.4, 1)
        _NormalFactor("立体感",Float) = 1
        _Radio ("AO球形遮罩半径",Float) = 1
        
        [Header(Global Color Variation)]
        _MacroNoise ("Global Noise Map (R channel)", 2D) = "gray" {}
        [NoScaleOffset] _ColorGradient ("Color Gradient (1D)", 2D) = "white" {}
        _NoiseScale ("Noise Tiling Scale", Float) = 0.05
        
        [Header(Specular Settings)]
        _SpecularColor ("Specular Color", Color) = (1, 1, 0.8, 1)
        _SpecularStrength ("Specular Strength", Range(0, 1)) = 0.3
        _Smoothness ("Smoothness", Range(0.001, 1)) = 0.15
        _SpecularMask ("Specular Mask (高度渐变)", Range(0, 1)) = 0.6
        _AnisotropicShift ("Anisotropic Shift (各向异性偏移)", Range(-1, 1)) = 0.2
        _AnisotropicStrength ("Anisotropic Strength", Range(0, 1)) = 0.5

        [Header(Bending Control)]
        _P1Weight ("根部硬度", Range(0, 2)) = 0.1
        _P2Weight ("腰部弯曲", Range(0, 2)) = 0.6
        _P3Weight ("草尖摆动", Range(0, 2)) = 1.0
        _StiffnessCurve ("硬度曲线", Range(1, 4)) = 2.5
        _TipBias ("顶端偏移", Range(0, 1)) = 0.3
        _SwayFrequency ("摆动频率", Range(0, 5)) = 1.0

    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
    Pass
        {
        Name "ForwardLit"
        Tags { "LightMode" = "UniversalForward" }
        
        Cull Off
        ZTest LEqual
        ZWrite On
        
        HLSLPROGRAM
        #pragma vertex vert
        #pragma fragment frag
        #pragma instancing_options procedural:setup
        
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        
        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        float4 _MainTex_ST;
        float4 _BaseColor;
        float _NormalFactor;
        float _Radio;
        
        TEXTURE2D(_MacroNoise);
        SAMPLER(sampler_MacroNoise);
        TEXTURE2D(_ColorGradient);
        SAMPLER(sampler_ColorGradient);
        float _NoiseScale;
        
        // 高光参数
        float4 _SpecularColor;
        float _SpecularStrength;
        float _Smoothness;
        float _SpecularMask;
        float _AnisotropicShift;
        float _AnisotropicStrength;

        // 弯曲控制参数
        float _P1Weight;
        float _P2Weight;
        float _P3Weight;
        float _StiffnessCurve;
        float _TipBias;
        float _SwayFrequency;
        

        struct Attributes
        {
            float4 positionOS   : POSITION;
            float2 uv           : TEXCOORD0;
            uint vertexID       : SV_VertexID;
            uint instanceID     : SV_InstanceID;
        };
        
        struct Varyings
        {
            float4 positionCS   : SV_POSITION;
            float3 NormalWS     : TEXCOORD1;
            float3 NormalOS     : TEXCOORD3;
            float mask          : TEXCOORD4;
            float2 uv           : TEXCOORD0;
            float3 positionWS   : TEXCOORD2;
            float noiseValue    : TEXCOORD5;
            float3 tangentWS    : TEXCOORD6;  // 新增：切线方向（用于各向异性）
            float heightFactor  : TEXCOORD7;  // 新增：高度因子（用于高光渐变）
        };
        
        struct Grass
        {
            float3 VertexPosition[42];
            float2 uv[42];
        };
        
        struct GrassBlade
        {
            float hash;
            float3 position;
            float facing;
            float height;
            float width;
            float tilt;
            float bend;
            float sideBend;
            float windForce;
        };
        
        StructuredBuffer<GrassBlade> cullingOutputBuffer;
        StructuredBuffer<Grass> SingleGrassBuffer;

        void setup() {}
        
        // 贝塞尔数学库
        float3 CubicBezier(float t, float3 p0, float3 p1, float3 p2, float3 p3)
        {
            float u = 1.0 - t;
            float tt = t * t;
            float uu = u * u;
            float uuu = uu * u;
            float ttt = tt * t;
            return uuu * p0 + 3.0 * uu * t * p1 + 3.0 * u * tt * p2 + ttt * p3;
        }

        float3 CubicBezierDerivative(float t, float3 p0, float3 p1, float3 p2, float3 p3)
        {
            float u = 1.0 - t;
            return 3.0 * u * u * (p1 - p0) + 6.0 * u * t * (p2 - p1) + 3.0 * t * t * (p3 - p2);
        }

        float3 RotateAroundAxis(float3 pos, float3 axis, float angle)
        {
            float s = sin(angle);
            float c = cos(angle);
            return pos * c + axis * dot(pos, axis) * (1 - c) + cross(axis, pos) * s;
        }
        
        float SphereMask(float3 position, float3 center, float radius)
        {
            float dis = distance(center, position);
            float mask = smoothstep(0, radius, dis);
            return mask;
        }
        
        Varyings vert(Attributes input)
        {
            Varyings output = (Varyings)0;
            GrassBlade grass = cullingOutputBuffer[input.instanceID];
            Grass Singlegrass = SingleGrassBuffer[0];
            
            float3 localPos = Singlegrass.VertexPosition[input.vertexID];
            float2 localUV = Singlegrass.uv[input.vertexID];
            
            float t = saturate(localPos.y); 
            float mask = SphereMask(localPos.y, localPos, _Radio);
            
            float height = grass.height;
            float width = grass.width;
            float3 rootPos = float3(0, 0, 0);

            // 弯曲控制
            float bend = grass.bend;
            float windForce = grass.windForce;

            // 沿茎的弯曲分布 (根部硬、顶部软)
            float bendWeight = pow(t, _StiffnessCurve);

            // 风力动画 (随机相位避免同步)
            float sway = sin(_Time.y * _SwayFrequency + grass.hash * 6.28318530718) * windForce;

            // 贝塞尔控制点 —— 按高度分布弯曲
            float3 p0 = rootPos;

            // P1: 根部控制点 (高度33%，几乎不弯曲)
            float h1 = height * 0.33;
            float bend1 = (bend * _P1Weight + sway * 0.1) * bendWeight;
            float3 p1 = float3(bend1 * _TipBias * 0.3, h1, grass.tilt * h1 * 0.5);

            // P2: 腰部控制点 (高度60%，中等弯曲)
            float h2 = height * 0.6;
            float bend2 = (bend * _P2Weight + sway * 0.5) * pow(h2 / height, _StiffnessCurve);
            float3 p2 = float3(bend2, h2 + height * 0.1, grass.tilt * h2);

            // P3: 顶部控制点 (高度100%，最大弯曲)
            float h3 = height;
            float bend3 = (bend * _P3Weight + sway);
            float3 p3 = float3(bend3 + bend3 * _TipBias, h3, grass.tilt * h3);

            // 计算贝塞尔曲线位置和切线
            float3 bezierPos = CubicBezier(t, p0, p1, p2, p3);
            float3 tangent = normalize(CubicBezierDerivative(t, p0, p1, p2, p3));

            // 构建切线空间 - 在局部空间（未旋转前）
            // 草叶宽度方向是局部 X 轴 (1,0,0)
            float3 localRight = float3(1, 0, 0);

            // 法线垂直于切线和宽度方向（在局部空间）
            float3 localNormal = normalize(cross(localRight, tangent));

            // 应用草叶朝向旋转（grass.facing）
            float3 rotatedPos = RotateAroundAxis(bezierPos, float3(0, 1, 0), grass.facing);
            float3 rotatedTangent = RotateAroundAxis(tangent, float3(0, 1, 0), grass.facing);
            float3 rotatedNormal = RotateAroundAxis(localNormal, float3(0, 1, 0), grass.facing);
            float3 rotatedRight = RotateAroundAxis(localRight, float3(0, 1, 0), grass.facing);

            // 顶点构建：沿旋转后的宽度方向偏移
            float sideOffset = localPos.x * width;
            float3 finalLocalPos = rotatedPos + rotatedRight * sideOffset;

            // 立体感法线：根据 UV 在草叶宽度上偏移法线，模拟圆柱/椭球体
            float sideFactor = (localUV.x - 0.5) * 2.0;
            float3 stereoNormal = normalize(rotatedNormal + rotatedRight * sideFactor * _NormalFactor);
            
            // 世界空间位置
            float3 worldPos = grass.position + finalLocalPos;

            // 噪声采样
            float2 noiseUV = grass.position.xz * _NoiseScale;
            float noiseSample = SAMPLE_TEXTURE2D_LOD(_MacroNoise, sampler_MacroNoise, noiseUV, 0).r;

            // 输出
            output.positionWS = worldPos;
            output.positionCS = TransformWorldToHClip(worldPos);
            output.uv = localUV;
            output.mask = mask;
            output.NormalOS = stereoNormal;
            output.NormalWS = stereoNormal;
            output.tangentWS = rotatedTangent;  // 传递切线
            output.heightFactor = t;            // 传递高度因子
            output.noiseValue = noiseSample;

            return output;
        }
        
        // ===== 高光计算函数 =====
        
        // 1. 标准 Blinn-Phong 高光（适合草地顶部）
        float BlinnPhongSpecular(float3 normal, float3 lightDir, float3 viewDir, float smoothness)
        {
            float3 halfDir = normalize(lightDir + viewDir);
            float NdotH = saturate(dot(normal, halfDir));
            
            // 使用 smoothness 控制高光锐利度
            float specPower = exp2(10 * smoothness + 1);
            return pow(NdotH, specPower);
        }
        
        // 2. 各向异性高光（模拟草叶纤维方向）
        float AnisotropicSpecular(float3 normal, float3 tangent, float3 lightDir, 
                                  float3 viewDir, float smoothness, float shift)
        {
            // 计算副切线（bitangent）
            float3 bitangent = normalize(cross(normal, tangent));
            
            // 沿切线方向偏移法线（模拟草叶纤维）
            float3 shiftedTangent = normalize(tangent + normal * shift);
            
            // 计算半向量在副切线方向的投影
            float3 halfDir = normalize(lightDir + viewDir);
            float TdotH = dot(shiftedTangent, halfDir);
            float sinTH = sqrt(1.0 - TdotH * TdotH);
            
            // 各向异性指数
            float specPower = exp2(8 * smoothness + 1);
            return pow(sinTH, specPower);
        }
        
        // 3. GGX 高光（更真实的PBR高光 - 可选，目前未使用）
        float GGXSpecular(float3 normal, float3 lightDir, float3 viewDir, float roughness)
        {
            float3 halfDir = normalize(lightDir + viewDir);
            float NdotH = saturate(dot(normal, halfDir));
            float NdotL = saturate(dot(normal, lightDir));
            
            float alpha = roughness * roughness;
            float alpha2 = alpha * alpha;
            
            // GGX 分布
            float denom = NdotH * NdotH * (alpha2 - 1.0) + 1.0;
            float D = alpha2 / (3.14159 * denom * denom);
            
            return D * NdotL;
        }
        
        half4 frag(Varyings input) : SV_Target
        {
            // 全局颜色变化
            half4 macroColor = SAMPLE_TEXTURE2D(_ColorGradient, sampler_ColorGradient, 
                                                float2(input.noiseValue, 0.5));
            
            // 光照设置
            Light mainLight = GetMainLight();
            float3 lightDir = normalize(mainLight.direction);
            float3 normal = normalize(input.NormalWS);
            float3 viewDir = normalize(_WorldSpaceCameraPos - input.positionWS);
            
            // === 1. 漫反射（原有代码）===
            float NdotL = dot(normal, lightDir);
            float transmission = 0.4;
            float lightIntensity = saturate((NdotL + transmission) / (1.0 + transmission));
            
            // 阴影
            float shadowAttenuation = MainLightRealtimeShadow(TransformWorldToShadowCoord(input.positionWS));
            
            // 纹理
            half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
            
            // 基础颜色
            half3 diffuseColor = macroColor.rgb * mainLight.color * lightIntensity * shadowAttenuation;
            diffuseColor += macroColor.rgb * 0.2; // 环境光
            
            // B. 各向异性高光（草叶纤维感）
            float3 tangent = normalize(input.tangentWS);
            float anisoSpec = AnisotropicSpecular(normal, tangent, lightDir, viewDir, 
                                                  _Smoothness * 0.7, _AnisotropicShift);
            
            
            // 应用高光颜色和强度
            half3 specularColor = _SpecularColor.rgb * anisoSpec * _SpecularStrength;
            specularColor *= mainLight.color; // 受主光源颜色影响
            specularColor *= shadowAttenuation; // 阴影中无高光
            
            // 添加菲涅尔效果（边缘高光增强）
            float fresnelPower = 3.0;
            float fresnel = pow(1.0 - saturate(dot(normal, viewDir)), fresnelPower);
            specularColor *= (1.0 + fresnel * 0.5);
            
            // === 3. 最终颜色合成 ===
            half3 finalColor = diffuseColor + specularColor;
            
            return half4(finalColor * input.mask, 1.0);
        }
        ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Unlit"
}
