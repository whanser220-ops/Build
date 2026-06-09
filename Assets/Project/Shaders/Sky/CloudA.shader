Shader "Unlit/CloudA"
{
    Properties
    {
        _CloudTex ("Cloud Texture", 2D) = "white" {}
        _NoiseTex ("Noise Texture", 2D) = "white" {}
        _SunDirection ("Sun Direction", Vector) = (0, 1, 0, 0)

        [Header (Cloud Palette)]
        _CloudBaseColor ("Cloud Base Color", Color) = (1, 1, 1, 1)
        _CloudDarkColorFar ("Dark Color Far", Color) = (0.58, 0.66, 0.78, 1)
        _CloudDarkColorNearSun ("Dark Color Near Sun", Color) = (0.88, 0.72, 0.62, 1)
        _CloudBrightColorFar ("Bright Color Far", Color) = (0.95, 0.98, 1, 1)
        _CloudBrightColorNearSun ("Bright Color Near Sun", Color) = (1, 0.94, 0.84, 1)

        [Header (Cloud Shape)]
        _Cloud_SDF_TSb ("SDF Threshold", Range(0.003, 1.5)) = 0.5
        _SdfSoftness ("SDF Softness", Range(0.001, 0.2)) = 0.02
        _EdgeIntensity ("Edge Intensity", Range(0, 2)) = 0.65
        _DensityStrength ("Layer Contrast", Range(0, 2)) = 1
        _NoiseDisturbance ("Noise Disturbance", Range(0, 0.1)) = 0.03
        _RotationSpeed ("Rotation Speed", Range(0, 1)) = 0.1

        [Header (Sun Circle)]
        _SunColorRadius ("Sun Color Radius", Range(0.01, 0.35)) = 0.12
        _SunEdgeRadius ("Sun Edge Radius", Range(0.005, 0.25)) = 0.055
        _AreaClampSoftness ("Area Clamp Softness", Range(0.001, 1)) = 0.05
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma enable_d3d11_debug_symbols
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct appdata
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD1;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float2 noiseUV : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float4 positionCS : SV_POSITION;
            };

            TEXTURE2D(_CloudTex);
            SAMPLER(sampler_CloudTex);
            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            float3 _SunDirection;
            float4 _NoiseTex_ST;
            float4 _CloudBaseColor;
            float4 _CloudDarkColorFar;
            float4 _CloudDarkColorNearSun;
            float4 _CloudBrightColorFar;
            float4 _CloudBrightColorNearSun;
            float _Cloud_SDF_TSb;
            float _SdfSoftness;
            float _EdgeIntensity;
            float _DensityStrength;
            float _NoiseDisturbance;
            float _RotationSpeed;
            float _SunColorRadius;
            float _SunEdgeRadius;
            float _AreaClampSoftness;

            float3x3 RotateY(float theta)
            {
                float c = cos(theta);
                float s = sin(theta);
                return float3x3(
                    c, 0, s,
                    0, 1, 0,
                    -s, 0, c
                );
            }

            float3 SafeNormalize(float3 value, float3 fallback)
            {
                float lengthSq = dot(value, value);
                if (lengthSq < 1e-6)
                {
                    return fallback;
                }

                return value * rsqrt(lengthSq);
            }

            float SampleCloudMaskFromSdf(float sdf)
            {
                float edge0 = max(0.0, _Cloud_SDF_TSb - _SdfSoftness);
                float edge1 = _Cloud_SDF_TSb + _SdfSoftness;
                return smoothstep(edge0, edge1, sdf);
            }

            float ComputeSdfEdge(float sdf)
            {
                float outerEdge = 1.0 - smoothstep(_Cloud_SDF_TSb, _Cloud_SDF_TSb + _SdfSoftness * 4.0, sdf);
                float innerEdge = smoothstep(_Cloud_SDF_TSb - _SdfSoftness * 3.0, _Cloud_SDF_TSb + _SdfSoftness, sdf);
                return saturate(outerEdge * innerEdge * 3.0);
            }

            v2f vert(appdata v)
            {
                v2f o;
                v.positionOS.xyz = mul(RotateY(_Time.x * _RotationSpeed), v.positionOS.xyz);
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.positionWS = TransformObjectToWorld(v.positionOS);
                o.uv = v.uv;
                o.noiseUV = TRANSFORM_TEX(v.uv, _NoiseTex) + _Time.x * 0.1;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float2 noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, i.noiseUV).rg * 2.0 - 1.0;
                float2 disturbedUV = i.uv + noise * _NoiseDisturbance;
                float4 cloudSample = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, disturbedUV);

                // Texture layout:
                // r = body dark/bright layering
                // g = authored edge outline
                // b = sdf
                // a = cloud area clamp
                float layerSource = saturate(cloudSample.r);
                float edgeAuthoring = saturate(cloudSample.g);
                float sdf = saturate(cloudSample.b);
                float areaClamp = saturate(cloudSample.a);

                float cloudMask = SampleCloudMaskFromSdf(sdf);
                float clampMask = smoothstep(0.0, _AreaClampSoftness, areaClamp);
                float bodyMask = cloudMask * clampMask;

                float layerContrast = max(_DensityStrength, 1.0e-3);
                float bodyLayer = saturate((layerSource - 0.5) * layerContrast + 0.5);
                bodyLayer = smoothstep(0.08, 0.92, bodyLayer);

                float3 lightDirWS = SafeNormalize(-_SunDirection, float3(0, 1, 0));
                float3 cloudDirWS = SafeNormalize(i.positionWS, float3(0, 1, 0));
                float sunCircle = saturate(dot(cloudDirWS, lightDirWS));
                sunCircle = sunCircle * sunCircle;

                float sunColorThreshold = saturate(1.0 - _SunColorRadius);
                float sunEdgeThreshold = saturate(1.0 - _SunEdgeRadius);
                float sunColorInfluence = smoothstep(sunColorThreshold, 1.0, sunCircle) * clampMask;
                float sunEdgeInfluence = smoothstep(sunEdgeThreshold, 1.0, sunCircle) * clampMask;

                float3 baseTint = _CloudBaseColor.rgb;
                float3 CloudColorAB = lerp(_CloudDarkColorFar.rgb, _CloudDarkColorNearSun.rgb, sunColorInfluence);
                float3 CloudColorCD = lerp(_CloudBrightColorFar.rgb, _CloudBrightColorNearSun.rgb, sunColorInfluence);
                float3 color = lerp(CloudColorAB, CloudColorCD, bodyLayer) * baseTint;

                float sdfEdge = ComputeSdfEdge(sdf);
                float edgeMask = saturate(edgeAuthoring + sdfEdge * 0.55);
                edgeMask *= sunEdgeInfluence;
                edgeMask *= bodyMask;
                float3 edgeColor = CloudColorCD * baseTint;
                color += edgeColor * edgeMask * _EdgeIntensity;

                float alpha = saturate(max(bodyMask, sdfEdge * clampMask * 0.035)) * _CloudBaseColor.a;
                return float4(color, alpha);
            }
            ENDHLSL
        }
    }
}
