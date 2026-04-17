Shader "New_Tree/LeafBillboardIndirect"
{
    Properties
    {
        _BaseMap ("叶片贴图 (RGBA)", 2D) = "white" {}
        _RampTex ("颜色渐变", 2D) = "white" {}
        [HideInInspector] _BreakupNoiseTex ("Breakup Noise", 2D) = "gray" {}
        _Cutoff ("透明裁剪阈值", Range(0, 1)) = 0.33
        [HideInInspector] _AOStrength ("AO Strength", Range(0, 1)) = 0.35
        [HideInInspector] _AOStart ("AO Start", Range(0, 0.95)) = 0.55
        [HideInInspector] _AOExponent ("AO Exponent", Float) = 1.6
        [HideInInspector] _AORadiusScale ("AO Radius Scale", Float) = 1
        [HideInInspector] _TransColor ("Trans Color", Color) = (1, 0.92, 0.55, 1)
        [HideInInspector] _TransStrength ("Trans Strength", Float) = 0.18
        [HideInInspector] _TransScattering ("Trans Scattering", Float) = 18
        [HideInInspector] _TransNormalDistortion ("Trans Normal Distortion", Float) = 0.65
        [HideInInspector] _TransAmbient ("Trans Ambient", Float) = 0.04
        [HideInInspector] _WindStrength ("Wind Strength", Float) = 0.8
        [HideInInspector] _WindScale ("Wind Scale", Float) = 1
        [HideInInspector] _WindSpeed ("Wind Speed", Float) = 1
        [HideInInspector] _WindTime ("Wind Time", Float) = 0
        [HideInInspector] _BreakupScale ("Breakup Scale", Float) = 0.18
        [HideInInspector] _BreakupStrength ("Breakup Strength", Float) = 0.12
        [HideInInspector] _LocalLightingBlend ("Local Lighting Blend", Float) = 0.25
        [HideInInspector] _VariationStrength ("Variation Strength", Float) = 0.04
        [HideInInspector] _RampWrap ("Ramp Wrap", Float) = 0.35
        [HideInInspector] _LightContrast ("Light Contrast", Float) = 1.2
        [HideInInspector] _ShadowFloor ("Shadow Floor", Float) = 0.42
        [HideInInspector] _BaseMapBoost ("Base Map Boost", Float) = 1.35
        [HideInInspector] _TreeUniformScale ("Tree Uniform Scale", Float) = 1
        [HideInInspector] _TopTint ("Top Tint", Color) = (1.08, 1.02, 0.88, 1)
        [HideInInspector] _BottomTint ("Bottom Tint", Color) = (0.78, 0.9, 1.04, 1)
        [HideInInspector] _HeightTintStrength ("Height Tint Strength", Float) = 0.65
        [HideInInspector] _EdgeScatterStrength ("Edge Scatter Strength", Float) = 0.6
        [HideInInspector] _DebugView ("Debug View", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "AlphaTest"
            "RenderType" = "TransparentCutout"
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On
            AlphaToMask Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_RampTex);
            SAMPLER(sampler_RampTex);
            TEXTURE2D(_BreakupNoiseTex);
            SAMPLER(sampler_BreakupNoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float _Cutoff;
                float4x4 _TreeLocalToWorld;
                float4x4 _TreeWorldToLocal;
                float _TreeUniformScale;
                float4 _CrownCenterLS;
                float _CrownRadiusLS;
                float4 _CrownExtentsLS;
                float _TreeTopY;
                float _TreeBottomY;
                float _AOStrength;
                float _AOStart;
                float _AOExponent;
                float _AORadiusScale;
                float4 _TransColor;
                float _TransStrength;
                float _TransScattering;
                float _TransNormalDistortion;
                float _TransAmbient;
                float _WindStrength;
                float _WindScale;
                float _WindSpeed;
                float _WindTime;
                float _BreakupScale;
                float _BreakupStrength;
                float _LocalLightingBlend;
                float _VariationStrength;
                float _RampWrap;
                float _LightContrast;
                float _ShadowFloor;
                float _BaseMapBoost;
                float4 _TopTint;
                float4 _BottomTint;
                float _HeightTintStrength;
                float _EdgeScatterStrength;
                float _DebugView;
            CBUFFER_END

            struct LeafInstance
            {
                float3 positionLS;
                float size;
                float3 crownCenterLS;
                float crownRadiusLS;
                float3 crownExtentsLS;
                float rotation;
                float variation;
                float radial01;
                float localHeight01;
                float heightTintWeight;
                float shellWeight;
                uint treeId;
                float2 padding;
            };

            StructuredBuffer<LeafInstance> _LeafInstances;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 positionLS : TEXCOORD2;
                float3 controlPointCenterLS : TEXCOORD3;
                float3 crownExtentsLS : TEXCOORD4;
                float crownRadiusWS : TEXCOORD5;
                float2 breakupUV : TEXCOORD6;
                float4 packedData0 : TEXCOORD7;
                float2 packedData1 : TEXCOORD8;
            };

            float Hash31(float3 value)
            {
                value = frac(value * 0.1031);
                value += dot(value, value.yzx + 33.33);
                return frac((value.x + value.y) * value.z);
            }

            float ValueNoise(float3 value)
            {
                float3 baseValue = floor(value);
                float3 fraction = frac(value);
                float3 smoothFraction = fraction * fraction * (3.0 - 2.0 * fraction);

                float n000 = Hash31(baseValue + float3(0.0, 0.0, 0.0));
                float n100 = Hash31(baseValue + float3(1.0, 0.0, 0.0));
                float n010 = Hash31(baseValue + float3(0.0, 1.0, 0.0));
                float n110 = Hash31(baseValue + float3(1.0, 1.0, 0.0));
                float n001 = Hash31(baseValue + float3(0.0, 0.0, 1.0));
                float n101 = Hash31(baseValue + float3(1.0, 0.0, 1.0));
                float n011 = Hash31(baseValue + float3(0.0, 1.0, 1.0));
                float n111 = Hash31(baseValue + float3(1.0, 1.0, 1.0));

                float x00 = lerp(n000, n100, smoothFraction.x);
                float x10 = lerp(n010, n110, smoothFraction.x);
                float x01 = lerp(n001, n101, smoothFraction.x);
                float x11 = lerp(n011, n111, smoothFraction.x);
                float y0 = lerp(x00, x10, smoothFraction.y);
                float y1 = lerp(x01, x11, smoothFraction.y);
                return lerp(y0, y1, smoothFraction.z);
            }

            float SignedNoise(float3 value)
            {
                return ValueNoise(value) * 2.0 - 1.0;
            }

            float3 SignedVectorNoise(float3 value)
            {
                return float3(
                    SignedNoise(value + float3(17.17, 3.41, 11.73)),
                    SignedNoise(value + float3(5.93, 19.27, 7.11)),
                    SignedNoise(value + float3(13.57, 9.83, 23.41)));
            }

            float3 RotateAroundAxis(float3 value, float3 axis, float angle)
            {
                float axisLengthSq = dot(axis, axis);
                if (axisLengthSq < 1e-6)
                    return value;

                axis *= rsqrt(axisLengthSq);

                float s;
                float c;
                sincos(angle, s, c);
                return value * c + cross(axis, value) * s + axis * dot(axis, value) * (1.0 - c);
            }

            void GetQuadVertex(uint vertexID, out float2 quadOffset, out float2 uv)
            {
                if (vertexID == 0u) { quadOffset = float2(-0.5, -0.5); uv = float2(0.0, 0.0); return; }
                if (vertexID == 1u) { quadOffset = float2(-0.5, 0.5); uv = float2(0.0, 1.0); return; }
                if (vertexID == 2u) { quadOffset = float2(0.5, 0.5); uv = float2(1.0, 1.0); return; }
                if (vertexID == 3u) { quadOffset = float2(-0.5, -0.5); uv = float2(0.0, 0.0); return; }
                if (vertexID == 4u) { quadOffset = float2(0.5, 0.5); uv = float2(1.0, 1.0); return; }

                quadOffset = float2(0.5, -0.5);
                uv = float2(1.0, 0.0);
            }

            Varyings vert(Attributes input)
            {
                LeafInstance leaf = _LeafInstances[input.instanceID];

                float2 quadOffset;
                float2 uv;
                GetQuadVertex(input.vertexID, quadOffset, uv);

                // 大风负责整团 sway，采样在树局部空间里，树整体移动时风型会跟着树走。
                float windMotionStrength = _WindStrength * saturate(sign(_WindSpeed));
                float largeScale = max(_WindScale * 0.3, 1e-4);
                float largeSpeed = _WindSpeed;
                float smallScale = max(_WindScale * 5.0, 1e-4);
                float smallSpeed = _WindSpeed * 0.2;

                float3 largeNoiseInput = leaf.positionLS * largeScale + float3(0.0, 0.0, _WindTime * largeSpeed + leaf.variation * 5.0);
                float3 largeOffsetLS = SignedVectorNoise(largeNoiseInput) * (leaf.crownRadiusLS * windMotionStrength * 0.08);
                float3 animatedCenterLS = leaf.positionLS + largeOffsetLS;
                float3 centerWS = mul(_TreeLocalToWorld, float4(animatedCenterLS, 1.0)).xyz;
                float leafSize = leaf.size * max(_TreeUniformScale, 1e-4);

                float3 viewDirectionWS = SafeNormalize(GetCameraPositionWS() - centerWS);
                float3 cameraRightWS = normalize(float3(UNITY_MATRIX_I_V._m00, UNITY_MATRIX_I_V._m10, UNITY_MATRIX_I_V._m20));
                float3 cameraUpWS = normalize(float3(UNITY_MATRIX_I_V._m01, UNITY_MATRIX_I_V._m11, UNITY_MATRIX_I_V._m21));

                float3 rightWS = cross(cameraUpWS, viewDirectionWS);
                float rightLengthSq = dot(rightWS, rightWS);
                if (rightLengthSq < 1e-6)
                    rightWS = cameraRightWS;
                else
                    rightWS *= rsqrt(rightLengthSq);

                float3 upWS = normalize(cross(viewDirectionWS, rightWS));

                float s;
                float c;
                sincos(leaf.rotation, s, c);
                float2 rotated = float2(
                    quadOffset.x * c - quadOffset.y * s,
                    quadOffset.x * s + quadOffset.y * c);

                float3 controlPointCenterWS = mul(_TreeLocalToWorld, float4(leaf.crownCenterLS, 1.0)).xyz;
                float3 rotationAxisWS = centerWS - controlPointCenterWS;
                if (dot(rotationAxisWS, rotationAxisWS) < 1e-6)
                    rotationAxisWS = mul((float3x3)_TreeLocalToWorld, float3(0.0, 1.0, 0.0));

                // 小风负责局部 flutter：先生成 billboard，再绕每片叶簇自己的 pivot 做轻微旋转。
                float3 smallNoiseInput = leaf.positionLS * smallScale + float3(4.13, 9.71, _WindTime * smallSpeed + leaf.variation * 11.0);
                float smallNoise = SignedNoise(smallNoiseInput);
                float smallAngle = smallNoise * windMotionStrength * 0.22;

                float3 localOffsetWS = rightWS * (rotated.x * leafSize) + upWS * (rotated.y * leafSize);
                localOffsetWS = RotateAroundAxis(localOffsetWS, rotationAxisWS, smallAngle);

                float3 positionWS = centerWS + localOffsetWS;
                Varyings output;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = uv;
                output.positionWS = positionWS;
                output.positionLS = mul(_TreeWorldToLocal, float4(positionWS, 1.0)).xyz;
                output.controlPointCenterLS = leaf.crownCenterLS;
                output.crownExtentsLS = max(leaf.crownExtentsLS, 1e-4);
                output.crownRadiusWS = max(leaf.crownRadiusLS * max(_TreeUniformScale, 1e-4), 1e-4);
                output.breakupUV = animatedCenterLS.xz * _BreakupScale + animatedCenterLS.y * float2(0.173, 0.347) + leaf.variation * float2(1.37, 2.11);
                output.packedData0 = float4(leaf.variation, leaf.radial01, leaf.localHeight01, leaf.heightTintWeight);
                output.packedData1 = float2(leaf.shellWeight, 0.0);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half3 baseTextureColor = baseSample.rgb;
                half alphaMask = baseSample.a;
                clip(alphaMask - _Cutoff);

                float variation = input.packedData0.x;
                float radial01 = saturate(input.packedData0.y);
                float localHeight01 = saturate(input.packedData0.z);
                float heightTintWeight = saturate(input.packedData0.w);
                float shellWeight = saturate(input.packedData1.x);

                float3 macroNormalLS = (input.positionLS - _CrownCenterLS.xyz) / max(_CrownExtentsLS.xyz, 1e-4);
                float macroNormalLengthSq = dot(macroNormalLS, macroNormalLS);
                if (macroNormalLengthSq < 1e-6)
                    macroNormalLS = float3(0.0, 1.0, 0.0);
                else
                    macroNormalLS *= rsqrt(macroNormalLengthSq);

                float3 localNormalLS = (input.positionLS - input.controlPointCenterLS) / max(input.crownExtentsLS, 1e-4);
                float localNormalLengthSq = dot(localNormalLS, localNormalLS);
                if (localNormalLengthSq < 1e-6)
                    localNormalLS = macroNormalLS;
                else
                    localNormalLS *= rsqrt(localNormalLengthSq);

                float3 macroNormalWS = SafeNormalize(mul((float3x3)_TreeLocalToWorld, macroNormalLS));
                float3 localNormalWS = SafeNormalize(mul((float3x3)_TreeLocalToWorld, localNormalLS));

                Light mainLight = GetMainLight();
                float macroNdotL = dot(macroNormalWS, mainLight.direction);
                float localNdotL = dot(localNormalWS, mainLight.direction);
                float macroRamp = saturate((macroNdotL + _RampWrap) / (1.0 + _RampWrap));
                float localRamp = saturate((localNdotL + _RampWrap) / (1.0 + _RampWrap));
                float macroDiffuse = pow(saturate(macroNdotL), max(_LightContrast, 0.01));
                float localDiffuse = pow(saturate(localNdotL), max(_LightContrast, 0.01));

                float breakupNoise = SAMPLE_TEXTURE2D(_BreakupNoiseTex, sampler_BreakupNoiseTex, input.breakupUV).r * 2.0 - 1.0;
                float variationSigned = variation * 2.0 - 1.0;

                // 亮区保护：两者都亮时尽量保留亮面；只要有一边开始变暗，再让局部受光参与切割大色块。
                float rampBright = max(macroRamp, localRamp);
                float rampDark = min(macroRamp, localRamp);
                float diffuseBright = max(macroDiffuse, localDiffuse);
                float diffuseDark = min(macroDiffuse, localDiffuse);
                float bothBright = smoothstep(0.65, 0.9, min(macroRamp, localRamp));
                float adaptiveBlend = _LocalLightingBlend * (1.0 - bothBright);

                float rampU = saturate(
                    lerp(rampBright, rampDark, adaptiveBlend)
                    + breakupNoise * _BreakupStrength
                    + variationSigned * _VariationStrength);

                float lightMix = saturate(lerp(diffuseBright, diffuseDark, adaptiveBlend));
                float lightIntensity = lerp(_ShadowFloor, 1.0, lightMix);
                half3 rampColor = SAMPLE_TEXTURE2D(_RampTex, sampler_RampTex, float2(rampU, 0.5)).rgb;

                float treeHeight01 = saturate((input.positionLS.y - _TreeBottomY) / max(_TreeTopY - _TreeBottomY, 1e-4));
                float height01 = lerp(localHeight01, treeHeight01, heightTintWeight);
                half3 heightTint = lerp(half3(1.0, 1.0, 1.0), lerp(_BottomTint.rgb, _TopTint.rgb, height01), _HeightTintStrength);

                float3 globalCrownCenterWS = mul(_TreeLocalToWorld, float4(_CrownCenterLS.xyz, 1.0)).xyz;
                float3 centerToCameraWS = GetCameraPositionWS() - globalCrownCenterWS;
                float centerToCameraLengthSq = dot(centerToCameraWS, centerToCameraWS);
                if (centerToCameraLengthSq < 1e-6)
                    centerToCameraWS = float3(0.0, 0.0, -1.0);
                else
                    centerToCameraWS *= rsqrt(centerToCameraLengthSq);

                float signedDepth = dot(input.positionWS - globalCrownCenterWS, -centerToCameraWS);
                float aoRadiusWS = max(input.crownRadiusWS * max(_AORadiusScale, 0.1), 1e-4);
                float depth01 = saturate(signedDepth / aoRadiusWS * 0.5 + 0.5);
                float depthAoMask = pow(smoothstep(_AOStart, 1.0, depth01), max(_AOExponent, 0.01));
                float viewAoFactor = saturate(1.0 - depthAoMask * _AOStrength);

                half3 boostedBaseTextureColor = baseTextureColor * _BaseMapBoost;
                half3 baseLit = boostedBaseTextureColor * rampColor * lightIntensity;
                baseLit *= heightTint;
                baseLit *= viewAoFactor;

                float3 viewDirWS = SafeNormalize(GetCameraPositionWS() - input.positionWS);
                float3 distortedLightDir = SafeNormalize(mainLight.direction + macroNormalWS * _TransNormalDistortion);
                float backScatter = pow(saturate(dot(viewDirWS, -distortedLightDir)), max(_TransScattering, 0.01));
                float edgeMask = pow(1.0 - saturate(dot(viewDirWS, macroNormalWS)), 2.0) * radial01;
                float translucency = (backScatter + _TransAmbient) * _TransStrength * edgeMask * _EdgeScatterStrength;
                half3 transColor = boostedBaseTextureColor * _TransColor.rgb * translucency;

                int debugView = (int)round(_DebugView);
                if (debugView == 1)
                    return half4(shellWeight.xxx, alphaMask);
                if (debugView == 2)
                    return half4(radial01.xxx, alphaMask);
                if (debugView == 4)
                    return half4(viewAoFactor.xxx, alphaMask);
                if (debugView == 5)
                    return half4(macroRamp.xxx, alphaMask);

                half3 finalColor = baseLit + transColor;
                return half4(finalColor, alphaMask);
            }
            ENDHLSL
        }
    }
}
