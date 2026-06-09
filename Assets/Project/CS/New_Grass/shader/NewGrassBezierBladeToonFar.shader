Shader "New_Grass/BezierBladeToonFar"
{
    Properties
    {
        [Header(Color)]
        _ColorNoiseMask ("Color Noise Mask", 2D) = "white" {}
        _ColorA ("Color A", Color) = (0.12, 0.50, 0.38, 1)
        _ColorB ("Color B", Color) = (0.66, 0.92, 0.22, 1)
        _ShadowTint ("Shadow Tint", Color) = (0.58, 0.70, 0.60, 1)
        _MidTint ("Mid Tint", Color) = (0.82, 0.92, 0.82, 1)
        _ToonThreshold1 ("Toon Threshold 1", Range(0, 1)) = 0.33
        _ToonThreshold2 ("Toon Threshold 2", Range(0, 1)) = 0.72
        _ToonBandSoftness ("Toon Band Softness", Range(0.001, 0.2)) = 0.03
        _ToonWrap ("Toon Wrap", Range(0, 1)) = 0.35
        _CurvedNormalAmount ("Curved Normal Amount", Range(0, 5)) = 1

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
            #pragma enable_d3d11_debug_symbols
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:ConfigureProcedural

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
                float3 curvedNormWS : TEXCOORD0;
                float2 rootXZ : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorA;
                half4 _ColorB;
                half4 _ShadowTint;
                half4 _MidTint;
                float4 _ColorNoiseMask_ST;

                float _ToonThreshold1;
                float _ToonThreshold2;
                float _ToonBandSoftness;
                float _ToonWrap;
                float _CurvedNormalAmount;

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
            CBUFFER_END

            TEXTURE2D(_ColorNoiseMask);
            SAMPLER(sampler_ColorNoiseMask);

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
                curvedNorm = mul(sideRot, curvedNorm);
                position = mul(sideRot, position);

                position += centerPos;
                curvedNorm = mul(rotMat, curvedNorm);
                position = mul(rotMat, position);

                float3 positionWS = position + bladePositionWS;
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.curvedNormWS = curvedNorm;
                OUT.rootXZ = bladePositionWS.xz;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {

                float3 n = normalize(IN.curvedNormWS);
                Light mainLight = GetMainLight();
                float3 l = normalize(mainLight.direction);

                float2 noiseUV = IN.rootXZ * _ColorNoiseMask_ST.xy + _ColorNoiseMask_ST.zw;
                float noiseMask = SAMPLE_TEXTURE2D(_ColorNoiseMask, sampler_ColorNoiseMask, noiseUV).r;
                float3 albedo = lerp(_ColorA.rgb, _ColorB.rgb, noiseMask);

                float wrappedNdotL = saturate((dot(n, l) + _ToonWrap) / (1.0 + _ToonWrap));
                float midBand = ToonBand(wrappedNdotL, _ToonThreshold1, _ToonBandSoftness);
                float highBand = ToonBand(wrappedNdotL, _ToonThreshold2, _ToonBandSoftness);

                float3 toonTint = lerp(_ShadowTint.rgb, _MidTint.rgb, midBand);
                toonTint = lerp(toonTint, float3(1.0, 1.0, 1.0), highBand);

                float3 ambient = SampleSH(n) * albedo * 0.35;
                float3 diffuse = albedo * toonTint * mainLight.color * mainLight.distanceAttenuation;
                return half4(ambient + diffuse, 1.0);
            }
            ENDHLSL
        }
    }
}
