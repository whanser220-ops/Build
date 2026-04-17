Shader "caroon_Tree/StylizedTreeCrownIndirect"
{
    Properties
    {
        _BaseMap ("Leaf Card", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.45
        _TopTint ("Top Tint", Color) = (0.84, 1.0, 0.72, 1)
        _MidTint ("Mid Tint", Color) = (0.43, 0.78, 0.32, 1)
        _BottomTint ("Bottom Tint", Color) = (0.17, 0.38, 0.18, 1)
        _SkyAmbientColor ("Sky Ambient", Color) = (0.62, 0.74, 0.56, 1)
        _GroundAmbientColor ("Ground Ambient", Color) = (0.12, 0.18, 0.23, 1)
        _HuePositiveTint ("Hue Positive", Color) = (0.96, 1.02, 0.88, 1)
        _HueNegativeTint ("Hue Negative", Color) = (0.88, 0.93, 1.02, 1)
        _RimColor ("Rim Color", Color) = (1.0, 0.95, 0.78, 1)
        _WindSurfaceLock ("Wind Surface Lock", Range(0, 1)) = 1
        _FrontShellDepthBias ("Front Shell Depth Bias", Range(0, 0.03)) = 0.008
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
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct StylizedCrownClusterData
            {
                float3 centerWS;
                float radius;
                float3 axisXWS;
                float density;
                float3 axisYWS;
                float layer;
                float3 axisZWS;
                float depthInCrown;
                float3 extents;
                float ambientOccl;
                uint startCardIndex;
                uint cardCount;
                float directOcclBias;
                float directionalOcclusion;
                float directionalTransmittance;
                float upFacingBias;
                float shellBias;
                float leafSizeScale;
                float padding;
            };

            struct StylizedLeafCardData
            {
                float3 positionWS;
                float size;
                float3 localPosN;
                float rotation;
                float3 outwardNormalWS;
                float shell;
                float random01;
                float hueVariation;
                float ao;
                float windPhase;
                uint clusterId;
                float3 padding;
            };

            StructuredBuffer<StylizedLeafCardData> _VisibleLeafCards;
            StructuredBuffer<StylizedCrownClusterData> _Clusters;

            CBUFFER_START(UnityPerMaterial)
                float4 _CrownCenterWS;
                float4 _CrownExtentsWS;
                float _CrownMinY;
                float _CrownMaxY;
                float _BillboardBlend;
                float _NormalBend;
                float _Cutoff;
                float _ThicknessSigma;
                float _InnerDirectScale;
                float _InnerAmbientScale;
                float _TopLightBoost;
                float _TopLightExponent;
                float _BottomDarkness;
                float _CrownCoreDarkness;
                float _LayerDarkness;
                float _DiffuseWrap;
                float4 _SkyAmbientColor;
                float4 _GroundAmbientColor;
                float4 _TopTint;
                float4 _MidTint;
                float4 _BottomTint;
                float4 _HuePositiveTint;
                float4 _HueNegativeTint;
                float _HueVariationStrength;
                float4 _RimColor;
                float _RimStrength;
                float _RimPower;
                float _WindAmplitude;
                float _WindFlutter;
                float _WindSpeed;
                float _WindSurfaceLock;
                float _FrontShellDepthBias;
                float4 _WindSpatialFrequency;
                float4 _WindDirectionWS;
                float4 _LightDirectionWS;
                float _TimeValue;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 cardNormalWS : TEXCOORD2;
                float3 localPosN : TEXCOORD3;
                float4 packed0 : TEXCOORD4;
                nointerpolation uint clusterId : TEXCOORD5;
            };


            float2 Rotate2D(float2 value, float angle)
            {
                float s;
                float c;
                sincos(angle, s, c);
                return float2(value.x * c - value.y * s, value.x * s + value.y * c);
            }

            float3 ComputeShellNormalWS(StylizedCrownClusterData cluster, float3 localPosN)
            {
                // 用椭球外壳法线来表达“团块受光方向”，
                // 不让 quad 自身法线完全主导亮面。
                float3 gradient = cluster.axisXWS * (localPosN.x / max(cluster.extents.x, 1e-4))
                    + cluster.axisYWS * (localPosN.y / max(cluster.extents.y, 1e-4))
                    + cluster.axisZWS * (localPosN.z / max(cluster.extents.z, 1e-4));
                return SafeNormalize(gradient);
            }

            void BuildSemiBillboardBasis(
                StylizedCrownClusterData cluster,
                StylizedLeafCardData card,
                out float3 rightWS,
                out float3 upWS,
                out float3 facingWS)
            {
                // 不是纯 billboard：
                // 基础朝向来自叶团外壳方向，再向相机方向混合一部分。
                float3 baseFacingWS = SafeNormalize(card.outwardNormalWS);
                float3 viewDirWS = SafeNormalize(GetCameraPositionWS() - card.positionWS);
                facingWS = SafeNormalize(lerp(baseFacingWS, viewDirWS, _BillboardBlend));

                float3 anchorUpWS = cluster.axisYWS;
                if (abs(dot(anchorUpWS, facingWS)) > 0.97)
                    anchorUpWS = cluster.axisXWS;

                rightWS = SafeNormalize(cross(anchorUpWS, facingWS));
                upWS = SafeNormalize(cross(facingWS, rightWS));
            }

            float3 ComputeWindOffset(float3 centerWS, float2 uv, float3 rightWS, float3 upWS, StylizedLeafCardData card)
            {
                // 让叶片上半部分摆得更多，下半部分更稳定，
                // 保持“树冠轻微抖动”而不是“草一样甩动”。
                float bendMask = saturate(uv.y);
                bendMask *= bendMask;

                float phase = _TimeValue * _WindSpeed + card.windPhase;
                float primary = sin(phase + dot(centerWS, _WindSpatialFrequency.xyz));
                float secondary = sin(phase * 1.91 + dot(centerWS.zxy, _WindSpatialFrequency.yzx) * 2.17);

                float3 sway = _WindDirectionWS.xyz * (_WindAmplitude * primary * bendMask);
                float3 flutter = rightWS * (_WindFlutter * secondary * bendMask);
                flutter += upWS * (_WindFlutter * 0.35 * primary * bendMask);
                return sway + flutter;
            }

            float ComputeSelfThickness(StylizedCrownClusterData cluster, float3 localPosN, float3 lightDirWS)
            {
                // 解析近似“沿光方向离开椭球前还要穿过多少厚度”，
                // 用于伪遮挡，而不是替代法线受光。
                float3 lightDirLocal = float3(
                    dot(lightDirWS, cluster.axisXWS),
                    dot(lightDirWS, cluster.axisYWS),
                    dot(lightDirWS, cluster.axisZWS));

                float3 d = lightDirLocal / max(cluster.extents, 1e-4);
                float a = dot(d, d);
                float b = 2.0 * dot(localPosN, d);
                float c = dot(localPosN, localPosN) - 1.0;
                float discriminant = b * b - 4.0 * a * c;
                if (discriminant <= 0.0 || a < 1e-6)
                    return 0.0;

                float exitT = (-b + sqrt(discriminant)) / (2.0 * a);
                return max(exitT, 0.0);
            }

            float3 ComputeHeightTint(float top01)
            {
                // 上嫩、中鲜、下暗，做动画树常见的高度色阶。
                float3 tint = lerp(_BottomTint.rgb, _MidTint.rgb, smoothstep(0.0, 0.55, top01));
                tint = lerp(tint, _TopTint.rgb, smoothstep(0.55, 1.0, top01));
                return tint;
            }

            Varyings vert(Attributes input)
            {
                // 顶点阶段负责把共享 quad 扩展成世界空间中的一张叶片卡片。
                StylizedLeafCardData card = _VisibleLeafCards[input.instanceID];
                StylizedCrownClusterData cluster = _Clusters[card.clusterId];

                float3 rightWS;
                float3 upWS;
                float3 facingWS;
                BuildSemiBillboardBasis(cluster, card, rightWS, upWS, facingWS);
                float3 shellNormalWS = ComputeShellNormalWS(cluster, card.localPosN);

                // quad 在局部 2D 平面内先旋转，再按 basis 展开到世界空间。
                float2 rotated = Rotate2D(input.positionOS.xy, card.rotation);
                float3 positionWS = card.positionWS + rightWS * (rotated.x * card.size) + upWS * (rotated.y * card.size);
                float3 rawWindOffset = ComputeWindOffset(card.positionWS, input.uv, rightWS, upWS, card);
                float3 radialWind = shellNormalWS * dot(rawWindOffset, shellNormalWS);
                float3 tangentWind = rawWindOffset - radialWind;
                float3 windOffset = lerp(rawWindOffset, tangentWind, _WindSurfaceLock);
                positionWS += windOffset;

                float3 viewDirCenterWS = SafeNormalize(GetCameraPositionWS() - card.positionWS);
                float frontShell = saturate(dot(shellNormalWS, viewDirCenterWS));
                float shellPriority = saturate(card.shell) * frontShell;
                positionWS += viewDirCenterWS * (shellPriority * _FrontShellDepthBias);

                Varyings output;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                output.positionWS = positionWS;
                output.cardNormalWS = facingWS;
                output.localPosN = card.localPosN;
                output.packed0 = float4(card.shell, card.ao, card.hueVariation, card.random01);
                output.clusterId = card.clusterId;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 先做 alpha clip，尽量减少后续对透明区域的无效着色。
                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv)*3;
                clip(baseSample.a - _Cutoff);

                StylizedCrownClusterData cluster = _Clusters[input.clusterId];
                Light mainLight = GetMainLight();
                float3 lightDirWS = SafeNormalize(_LightDirectionWS.xyz);
                float3 shellNormalWS = ComputeShellNormalWS(cluster, input.localPosN);
                float3 bentNormalWS = SafeNormalize(lerp(input.cardNormalWS, shellNormalWS, _NormalBend));
                float3 viewDirWS = SafeNormalize(GetCameraPositionWS() - input.positionWS);

                float shellMask = pow(saturate(input.packed0.x), 1.15);
                float thickness = ComputeSelfThickness(cluster, input.localPosN, lightDirWS);
                float selfTransmittance = exp(-thickness * cluster.density * _ThicknessSigma);
                float clusterTransmittance = cluster.directionalTransmittance;

                // bentNormal 负责亮面方向，遮挡项只负责压亮度，两者职责分开。
                float ndotl = saturate((dot(bentNormalWS, lightDirWS) + _DiffuseWrap) / (1.0 + _DiffuseWrap));
                float top01 = saturate((input.positionWS.y - _CrownMinY) / max(_CrownMaxY - _CrownMinY, 1e-4));
                float bottom01 = 1.0 - top01;
                float crownCore01 = 1.0 - saturate(length((input.positionWS - _CrownCenterWS.xyz) / max(_CrownExtentsWS.xyz, 1e-4)));

                // 树冠整体层级压暗：底部更暗，核心更暗，深层 cluster 更暗。
                float crownTransmittance = 1.0;
                crownTransmittance *= lerp(1.0, 1.0 - _BottomDarkness, bottom01);
                crownTransmittance *= lerp(1.0, 1.0 - _CrownCoreDarkness, crownCore01 * max(cluster.depthInCrown, 0.15));
                crownTransmittance *= lerp(1.0, 1.0 - _LayerDarkness, cluster.layer);

                // 直射光 = 亮面方向 * 团内厚度 * 团间遮挡 * 树冠层级压暗。
                float directLight = ndotl * selfTransmittance * clusterTransmittance * crownTransmittance;
                directLight *= lerp(_InnerDirectScale, 1.0, shellMask);
                directLight *= lerp(1.0, _TopLightBoost, pow(top01, _TopLightExponent));

                // 环境光走另一套更艺术化的逻辑：上方偏亮，地面反射偏冷偏暗。
                float skyMask = saturate(bentNormalWS.y * 0.5 + 0.5);
                float groundMask = saturate(-bentNormalWS.y * 0.5 + 0.5);
                float3 ambient = _SkyAmbientColor.rgb * lerp(0.45, 1.15, skyMask) * lerp(0.8, 1.1, top01);
                ambient += _GroundAmbientColor.rgb * groundMask * 0.65;
                ambient *= crownTransmittance;
                ambient *= lerp(_InnerAmbientScale, 1.0, shellMask);
                ambient *= input.packed0.y;

                float rim = pow(saturate(1.0 - dot(viewDirWS, bentNormalWS)), _RimPower) * _RimStrength;
                rim *= shellMask * clusterTransmittance * lerp(0.7, 1.0, selfTransmittance);

                // 最终颜色以贴图为底，再叠加高度色阶、少量色相漂移和风格化光照。
                float3 heightTint = ComputeHeightTint(top01);
                float3 hueTint = lerp(_HueNegativeTint.rgb, _HuePositiveTint.rgb, saturate(input.packed0.z * 0.5 + 0.5));
                float3 albedo = baseSample.rgb * heightTint * lerp(1.0, hueTint, _HueVariationStrength);

                float3 directColor = albedo * directLight * mainLight.color;
                float3 ambientColor = albedo * ambient;
                float3 rimColor = albedo * _RimColor.rgb * rim;
                float3 finalColor = ambientColor + directColor + rimColor;
                return half4(finalColor, baseSample.a);
            }
            ENDHLSL
        }
    }
}






