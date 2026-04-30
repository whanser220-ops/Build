Shader "New_Grass/BezierBladeToon"
{
    Properties
    {
        [Header(Shading)]
        _TopColor ("Top Color", Color) = (0.25, 0.5, 0.5, 1)
        _BottomColor ("Bottom Color", Color) = (0.25, 0.5, 0.5, 1)
        _GrassAlbedo ("Grass albedo", 2D) = "white" {}
        _ColorNoiseMask ("Color Noise Mask", 2D) = "white" {}
        _ColorA ("Color A", Color) = (0.12, 0.50, 0.38, 1)
        _ColorB ("Color B", Color) = (0.66, 0.92, 0.22, 1)
        _SpecularStrength ("Specular Strength", Range(0, 4)) = 0.9
        _Smoothness ("Smoothness", Range(0, 1)) = 0.55
        _CurvedNormalAmount ("Curved Normal Amount", Range(0, 5)) = 1

        [Header(Volume)]
        _VolumeStrength ("Volume Strength", Range(0, 2)) = 0.8
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.12

        [Header(Toon Shading)]
        _ShadowTint ("Shadow Tint", Color) = (0.58, 0.70, 0.60, 1)
        _MidTint ("Mid Tint", Color) = (0.82, 0.92, 0.82, 1)
        _ToonThreshold1 ("Toon Threshold 1", Range(0, 1)) = 0.33
        _ToonThreshold2 ("Toon Threshold 2", Range(0, 1)) = 0.72
        _ToonBandSoftness ("Toon Band Softness", Range(0.001, 0.2)) = 0.03
        _ToonWrap ("Toon Wrap", Range(0, 1)) = 0.35
        _ToonSpecThreshold ("Toon Spec Threshold", Range(0, 1)) = 0.6
        _ToonSpecSoftness ("Toon Spec Softness", Range(0.001, 0.2)) = 0.05
        _ToonSpecColor ("Toon Spec Color", Color) = (1, 0.97, 0.88, 1)
        _ToonRimThreshold ("Toon Rim Threshold", Range(0, 1)) = 0.72

        [Header(Shape)]
        _Height ("Height", Float) = 1
        _Tilt ("Tilt", Float) = 0.9
        _BladeWidth ("BladeWidth", Float) = 0.1
        _TaperAmount ("Taper Amount", Float) = 0
        _p1Offset ("p1Offset", Float) = 1
        _p2Offset ("p2Offset", Float) = 1

        [Header(Wind Animation)]
        _WaveAmplitude ("Wave Amplitude", Float) = 1
        _WaveSpeed ("Wave Speed", Float) = 1
        _SinOffsetRange ("Phase Variation", Range(0, 10)) = 0.3
        _PushTipForward ("Push Tip Forward", Range(0, 2)) = 1

        [HideInInspector] _LocalWindTex ("Local Wind Tex", 2D) = "black" {}
        [HideInInspector] _LocalWindScale ("Local Wind Scale", Float) = 0.01
        [HideInInspector] _LocalWindSpeed ("Local Wind Speed", Float) = 0.1
        [HideInInspector] _LocalWindStrength ("Local Wind Strength", Float) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:ConfigureProcedural
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float t : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float3 curvedNormWS : TEXCOORD3;
                float3 originalNormWS : TEXCOORD4;
                float4 shadowCoord : TEXCOORD5;
                float side01 : TEXCOORD6;
                float3 tangentWS : TEXCOORD7;
                float2 rootXZ : TEXCOORD8;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor;
                half4 _BottomColor;
                half4 _ColorA;
                half4 _ColorB;
                half4 _ShadowTint;
                half4 _MidTint;
                half4 _ToonSpecColor;
                float4 _ColorNoiseMask_ST;

                float _SpecularStrength;
                float _Smoothness;
                float _CurvedNormalAmount;
                float _VolumeStrength;
                float _RimStrength;

                float _ToonThreshold1;
                float _ToonThreshold2;
                float _ToonBandSoftness;
                float _ToonWrap;
                float _ToonSpecThreshold;
                float _ToonSpecSoftness;
                float _ToonRimThreshold;

                float _Height;
                float _Tilt;
                float _BladeWidth;
                float _TaperAmount;
                float _p1Offset;
                float _p2Offset;
                float _WaveAmplitude;
                float _WaveSpeed;
                float _SinOffsetRange;
                float _PushTipForward;
                float _LocalWindScale;
                float _LocalWindSpeed;
                float _LocalWindStrength;
            CBUFFER_END

            TEXTURE2D(_GrassAlbedo);
            SAMPLER(sampler_GrassAlbedo);
            TEXTURE2D(_ColorNoiseMask);
            SAMPLER(sampler_ColorNoiseMask);
            TEXTURE2D(_LocalWindTex);
            SAMPLER(sampler_LocalWindTex);

#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
            struct GrassBlade
            {
                float3 positionWS;
                float rotAngle;
                float hash;
                float height;
                float width;
                float tilt;
                float bend;
            };

            struct VisibleGrassInstance
            {
                uint bladeIndex;
                float rotAngle;
                float windForce;
                float padding;
            };

            StructuredBuffer<VisibleGrassInstance> _GrassBlades;
            StructuredBuffer<GrassBlade> _GeneratedGrassBlades;
#endif

            void ConfigureProcedural()
            {
            }

            float3 CubicBezier(float3 p0, float3 p1, float3 p2, float3 p3, float t)
            {
                float3 a = lerp(p0, p1, t);
                float3 b = lerp(p2, p3, t);
                float3 c = lerp(p1, p2, t);
                float3 d = lerp(a, c, t);
                float3 e = lerp(c, b, t);
                return lerp(d, e, t);
            }

            float3 CubicBezierTangent(float3 p0, float3 p1, float3 p2, float3 p3, float t)
            {
                float omt = 1.0 - t;
                float omt2 = omt * omt;
                float t2 = t * t;

                float3 tangent =
                    3.0 * omt2 * (p1 - p0) +
                    6.0 * omt * t * (p2 - p1) +
                    3.0 * t2 * (p3 - p2);

                return normalize(tangent);
            }

            float KajiyaKay(float3 tangentDir, float3 lightDir, float3 viewDir, float roughness)
            {
                float3 tangent = normalize(tangentDir);
                float tDotL = clamp(dot(tangent, normalize(lightDir)), -1.0, 1.0);
                float tDotV = clamp(dot(tangent, normalize(viewDir)), -1.0, 1.0);

                float sinTL = sqrt(saturate(1.0 - tDotL * tDotL));
                float sinTV = sqrt(saturate(1.0 - tDotV * tDotV));

                float specular = sinTL * sinTV - tDotL * tDotV;
                return pow(saturate(specular), rcp(max(roughness, 0.02)));
            }

            float ToonBand(float value, float threshold, float softness)
            {
                return smoothstep(threshold - softness, threshold + softness, value);
            }

            float3 GetP0()
            {
                return float3(0, 0, 0);
            }

            float3 GetP3(float height, float tilt, float sideBend)
            {
                float safeHeight = max(height, 0.0001);
                float clampedTilt = clamp(tilt, -0.9999, 0.9999);
                float p3y = clampedTilt * safeHeight;
                float p3x = sqrt(max(0.0, safeHeight * safeHeight - p3y * p3y));
                return float3(-p3x, p3y, sideBend);
            }

            void GetP1P2P3(float3 p0, inout float3 p3, float bend, float hash, float windForce, out float3 p1, out float3 p2)
            {
                p1 = lerp(p0, p3, 0.33);
                p2 = lerp(p0, p3, 0.66);

                float3 bladeDir = normalize(p3 - p0);
                float3 bezCtrlOffsetDir = normalize(cross(bladeDir, float3(0, 0, 1)));
                p1 += bezCtrlOffsetDir * bend * _p1Offset;
                p2 += bezCtrlOffsetDir * bend * _p2Offset;

                float phase = (_Time.y + hash * 2.0 * 3.14) * _WaveSpeed;
                float p2WindEffect = sin(phase + 0.66 * 2.0 * 3.14 * _SinOffsetRange) * windForce;
                p2WindEffect *= 0.66 * _WaveAmplitude;

                float p3WindEffect = sin(phase + 1.0 * 2.0 * 3.14 * _SinOffsetRange) * windForce;
                p3WindEffect += _PushTipForward * windForce * (1.0 - saturate(bend));
                p3WindEffect *= _WaveAmplitude;

                p2 += bezCtrlOffsetDir * p2WindEffect;
                p3 += bezCtrlOffsetDir * p3WindEffect;
            }

            float3x3 RotAxis3x3(float angle, float3 axis)
            {
                axis = normalize(axis);

                float s;
                float c;
                sincos(angle, s, c);

                float t = 1.0 - c;

                float x = axis.x;
                float y = axis.y;
                float z = axis.z;

                float xy = x * y;
                float xz = x * z;
                float yz = y * z;
                float xs = x * s;
                float ys = y * s;
                float zs = z * s;

                float m00 = t * x * x + c;
                float m01 = t * xy - zs;
                float m02 = t * xz + ys;
                float m10 = t * xy + zs;
                float m11 = t * y * y + c;
                float m12 = t * yz - xs;
                float m20 = t * xz - ys;
                float m21 = t * yz + xs;
                float m22 = t * z * z + c;

                return float3x3(
                    m00, m01, m02,
                    m10, m11, m12,
                    m20, m21, m22
                );
            }

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;

                float height = _Height;
                float widthBase = _BladeWidth;
                float tilt = _Tilt;
                float bend = 1.0;
                float sideBend = 0.0;
                float rotationAngle = 0.0;
                float bladeHash = 0.0;
                float windForce = 0.0;
                float3 bladePositionWS = 0.0;

#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                VisibleGrassInstance visible = _GrassBlades[IN.instanceID];
                GrassBlade blade = _GeneratedGrassBlades[visible.bladeIndex];
                height = blade.height;
                widthBase = blade.width;
                tilt = blade.tilt;
                bend = blade.bend + visible.windForce;
                sideBend = 0.0;
                rotationAngle = visible.rotAngle;
                bladeHash = blade.hash;
                windForce = visible.windForce;
                bladePositionWS = blade.positionWS;
#endif

                float3 p0 = GetP0();
                float3 p3 = GetP3(height, tilt, sideBend);
                float3 p1 = 0.0;
                float3 p2 = 0.0;
                GetP1P2P3(p0, p3, bend, bladeHash, windForce, p1, p2);

                float t = saturate(IN.color.r);
                float3 centerPos = CubicBezier(p0, p1, p2, p3, t);
                float width = widthBase * (1.0 - _TaperAmount * t);
                float side = IN.color.g * 2.0 - 1.0;
                float3 position = centerPos + float3(0.0, 0.0, side * width);

                float3 tangent = CubicBezierTangent(p0, p1, p2, p3, t);
                float3 normal = normalize(cross(tangent, float3(0.0, 0.0, 1.0)));

                float3 curvedNorm = normal;
                curvedNorm.z += side * _CurvedNormalAmount;
                curvedNorm = normalize(curvedNorm);

                float3x3 rotMat = RotAxis3x3(-rotationAngle, float3(0.0, 1.0, 0.0));
                float3x3 sideRot = RotAxis3x3(sideBend, normalize(tangent));

                position -= centerPos;
                normal = mul(sideRot, normal);
                curvedNorm = mul(sideRot, curvedNorm);
                position = mul(sideRot, position);

                position += centerPos;
                normal = mul(rotMat, normal);
                curvedNorm = mul(rotMat, curvedNorm);
                tangent = normalize(mul(rotMat, tangent));
                position = mul(rotMat, position);

                float3 positionWS = position + bladePositionWS;

                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.uv = IN.uv;
                OUT.t = t;
                OUT.positionWS = positionWS;
                OUT.curvedNormWS = curvedNorm;
                OUT.originalNormWS = normal;
                OUT.shadowCoord = TransformWorldToShadowCoord(positionWS);
                OUT.side01 = side * 0.5 + 0.5;
                OUT.tangentWS = tangent;
                OUT.rootXZ = bladePositionWS.xz;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 curvedNorm = normalize(IN.curvedNormWS);
                float3 originalNorm = normalize(IN.originalNormWS);
                float3 tangentWS = normalize(IN.tangentWS);

                float bladeSide = IN.side01 * 2.0 - 1.0;
                float roundMask = sqrt(saturate(1.0 - bladeSide * bladeSide));

                float3 widthDirWS = normalize(cross(originalNorm, tangentWS));
                float3 cylindricalNorm = normalize(originalNorm + widthDirWS * bladeSide * _VolumeStrength);
                float3 baseN = normalize(lerp(curvedNorm, cylindricalNorm, 0.7));
                float3 n = baseN;

                Light mainLight = GetMainLight(IN.shadowCoord);
                float3 l = normalize(mainLight.direction);
                float3 v = normalize(GetCameraPositionWS() - IN.positionWS);
                float lightAtten = mainLight.shadowAttenuation * mainLight.distanceAttenuation;

                float2 noiseUV = IN.rootXZ * _ColorNoiseMask_ST.xy + _ColorNoiseMask_ST.zw;
                float noiseMask = SAMPLE_TEXTURE2D(_ColorNoiseMask, sampler_ColorNoiseMask, noiseUV).r;
                float4 grassCol = lerp(_ColorA, _ColorB, noiseMask);
                float3 albedo = grassCol.rgb;

                float wrappedNdotL = saturate((dot(n, l) + _ToonWrap) / (1.0 + _ToonWrap));
                float litInput = saturate(wrappedNdotL * lightAtten);
                float midBand = ToonBand(litInput, _ToonThreshold1, _ToonBandSoftness);
                float highBand = ToonBand(litInput, _ToonThreshold2, _ToonBandSoftness);

                float3 toonTint = lerp(_ShadowTint.rgb, _MidTint.rgb, midBand);
                toonTint = lerp(toonTint, 1.0, highBand);
                float volumeMask = lerp(0.92, 1.0, roundMask);
                float3 diffuse = albedo * mainLight.color * toonTint * volumeMask;

                float anisotropicRoughness = max(0.02, 1.0 - saturate(_Smoothness) * 0.96);
                float anisoSpec = KajiyaKay(tangentWS, l, v, anisotropicRoughness);
                float specInput = saturate(anisoSpec * wrappedNdotL * lightAtten);
                float specBand = ToonBand(specInput, _ToonSpecThreshold, _ToonSpecSoftness);
                float3 finalSpecular = specBand * _ToonSpecColor.rgb * mainLight.color * _SpecularStrength;

                float rimValue = 1.0 - saturate(dot(n, v));
                float rimBand = ToonBand(rimValue, _ToonRimThreshold, _ToonBandSoftness);
                float3 rimColor = albedo * rimBand * _RimStrength;

                float3 finalColor = diffuse + finalSpecular + rimColor;
                return half4(finalColor, grassCol.a);
            }
            ENDHLSL
        }
    }
}
