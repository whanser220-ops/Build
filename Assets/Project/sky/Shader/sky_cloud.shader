Shader "Unlit/sky_cloud"
{
    Properties
    {
        [Header (Sun)]
        _SunDirection ("Sun Direction", Vector) = (0, 0, 0, 0)
        _MoonDirection ("Moon Direction", Vector) = (0, 0, 0, 0)

        _MoonMap ("Moon Map", 2D) = "white" {}
        _StarTex ("Star Texture", 2D) = "white" {}
        _SkyGradientTex ("Sky Gradient", 2D) = "white" {}
        _HorizonGradientTex ("Horizon Gradient", 2D) = "white" {}
        _NoiseTex ("Noise Texture", 2D) = "white" {}
        _MilkyWayTex ("Milky Way Texture", 2D) = "white" {}

        _SunSize ("Sun Size", Float) = 0.01
        _SunCol ("Sun Color", Color) = (1, 1, 0, 1)
        _SunIntensity ("Sun Intensity", Float) = 2
        [HDR]_SunTopColor ("Sun Top Color", Color) = (1, 1, 0, 1)
        [HDR]_SunBottomColor ("Sun Bottom Color", Color) = (0, 1, 1, 1)

        [Header (Sky Scattering)]
        _RayleighColorDay ("Rayleigh Day Color", Color) = (0.24, 0.48, 1.0, 1)
        _RayleighColorSunset ("Rayleigh Sunset Color", Color) = (1.0, 0.46, 0.18, 1)
        _RayleighStrength ("Rayleigh Strength", Float) = 0.25
        _RayleighHorizonStrength ("Rayleigh Horizon Strength", Float) = 0.9

        [Header (Sun Halo)]
        _MieColor ("Mie Color", Color) = (1.0, 0.88, 0.72, 1)
        _MieStrength ("Mie Strength", Float) = 0.18
        _MieAnisotropy ("Mie Anisotropy", Range(0, 0.99)) = 0.76
        _SunsetTransitionStart ("Sunset Transition Start", Range(0, 1)) = 0.08
        _SunsetTransitionEnd ("Sunset Transition End", Range(0, 1)) = 0.35
        _SunsetCoreSuppression ("Sunset Core Suppression", Range(0, 1)) = 0.65

        _MoonCol ("Moon Color", Color) = (1, 1, 1, 1)
        _MoonIntensity ("Moon Intensity", Float) = 1

        _MilkyWayGradientColor_1 ("Milky Way Color A", Color) = (0, 1, 0, 1)
        _MilkyWayGradientColor_2 ("Milky Way Color B", Color) = (0, 1, 0, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "previewType" = "Skybox" }
        LOD 100
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            static const float SKY_PI = 3.14159265;

            float4x4 SunLToW;
            float4x4 MilkyLToW;

            struct appdata
            {
                float4 positionOS : POSITION;
                float4 uv : TEXCOORD0;
            };

            struct v2f
            {
                float3 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float4 positionCS : SV_POSITION;
                float4 UV : TEXCOORD2;
            };

            float3 _SunDirection;
            float3 _MoonDirection;

            SAMPLER(sampler_SkyGradientTex);
            TEXTURE2D(_SkyGradientTex);
            SAMPLER(sampler_StarTex);
            TEXTURE2D(_StarTex);
            SAMPLER(sampler_MoonMap);
            TEXTURE2D(_MoonMap);
            SAMPLER(sampler_HorizonGradientTex);
            TEXTURE2D(_HorizonGradientTex);
            SAMPLER(sampler_NoiseTex);
            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_MilkyWayTex);
            TEXTURE2D(_MilkyWayTex);

            float4 _MilkyWayTex_ST;
            float4 _MoonMap_ST;
            float4 _StarTex_ST;
            float4 _NoiseTex_ST;

            float _SunSize;
            float4 _SunCol;
            float _SunIntensity;
            float4 _SunTopColor;
            float4 _SunBottomColor;

            float4 _RayleighColorDay;
            float4 _RayleighColorSunset;
            float _RayleighStrength;
            float _RayleighHorizonStrength;

            float4 _MieColor;
            float _MieStrength;
            float _MieAnisotropy;
            float _SunsetTransitionStart;
            float _SunsetTransitionEnd;
            float _SunsetCoreSuppression;

            float4 _MoonCol;
            float _MoonIntensity;
            float4 _MilkyWayGradientColor_1;
            float4 _MilkyWayGradientColor_2;

            v2f vert(appdata v)
            {
                v2f o;
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.positionWS = TransformObjectToWorld(v.positionOS);
                o.uv = -normalize(GetWorldSpaceViewDir(o.positionWS));
                o.UV = v.uv;
                return o;
            }

            half getMiePhase(half eyeCos, half eyeCos2)
            {
                half g = saturate(_MieAnisotropy);
                half g2 = g * g;
                half temp = 1.0 + g2 - 2.0 * g * eyeCos;
                temp = pow(max(temp, 1.0e-4), pow(_SunSize, 0.65) * 10);
                temp = 1.5 * ((1.0 - g2) / (2.0 + g2)) * (1.0 + eyeCos2) / temp;
                return temp;
            }

            half calcSunAttenuation(half3 lightPos, half3 ray)
            {
                half focusedEyeCos = pow(saturate(dot(lightPos, ray)), 5);
                return getMiePhase(focusedEyeCos, focusedEyeCos * focusedEyeCos);
            }

            float getRayleighPhase(float cosTheta)
            {
                return 0.75 * (1.0 + cosTheta * cosTheta);
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 viewDir = normalize(i.uv);

                float3 gradientDir = viewDir * 0.5;
                float u = atan2(gradientDir.z, gradientDir.x) / (2.0 * SKY_PI) + 0.5;
                float v = asin(gradientDir.y) / SKY_PI + 0.5;
                float2 sphereUV = float2(u, v);

                float3 sunDir = normalize(-_SunDirection);
                float sunElevation = sunDir.y;
                float daylight = smoothstep(-0.05, 0.15, sunElevation);
                float twilight = smoothstep(-0.18, 0.08, sunElevation);
                float nightFactor = 1.0 - smoothstep(-0.05, 0.15, sunElevation);
                float sunsetBlend = smoothstep(_SunsetTransitionStart, _SunsetTransitionEnd, saturate(sunElevation));
                float sunsetFactor = 1.0 - sunsetBlend;
                float cosTheta = dot(viewDir, sunDir);

                float4 skyGradient = SAMPLE_TEXTURE2D(_SkyGradientTex, sampler_SkyGradientTex, float2(sphereUV.y, 0));
                float4 horizonGradient = SAMPLE_TEXTURE2D(_HorizonGradientTex, sampler_HorizonGradientTex, float2(i.uv.y, 0));
                float3 baseSky = skyGradient.rgb + horizonGradient.rgb;

                float horizonFactor = pow(saturate(1.0 - abs(viewDir.y)), 1.5);
                float awayFromSun = saturate(1.0 - (cosTheta * 0.5 + 0.5));
                float rayleighPhase = getRayleighPhase(cosTheta);
                float3 rayleighColor = lerp(_RayleighColorSunset.rgb, _RayleighColorDay.rgb, sunsetBlend);
                float rayleighStrength = _RayleighStrength * twilight;
                rayleighStrength *= lerp(0.35, 1.0, awayFromSun);
                rayleighStrength *= lerp(1.0, 1.0 + _RayleighHorizonStrength, horizonFactor);
                rayleighStrength *= lerp(1.0, 1.25, sunsetFactor * horizonFactor);
                float3 rayleighScatter = rayleighColor * rayleighPhase * rayleighStrength;

                float mieGlow = getMiePhase(saturate(cosTheta), saturate(cosTheta) * saturate(cosTheta));
                float horizonHaze = lerp(1.0, 1.35, horizonFactor * sunsetFactor);
                float3 haloColor = lerp(_MieColor.rgb, _RayleighColorSunset.rgb, sunsetFactor * 0.7);
                float3 mieScatter = haloColor * mieGlow * (_MieStrength * twilight * horizonHaze);

                float sunSize = max(_SunSize, 1.0e-4);
                float coreThreshold = 1.0 - lerp(sunSize * 0.28, sunSize * 0.42, sunsetFactor);
                float coreSoftness = lerp(sunSize * 0.08, sunSize * 0.18, sunsetFactor);
                float sunCoreMask = smoothstep(coreThreshold - coreSoftness, coreThreshold + coreSoftness, saturate(cosTheta));
                float coreIntensity = _SunIntensity * twilight * lerp(1.0, 1.0 - _SunsetCoreSuppression, sunsetFactor);
                coreIntensity *= lerp(1.0, 0.9, horizonFactor * sunsetFactor);
                float3 sunCoreColor = lerp(_SunCol.rgb, haloColor, sunsetFactor * 0.45);
                float3 sunCore = sunCoreMask * sunCoreColor * coreIntensity * lerp(1.0, 0.9, daylight);

                float3 finalColor = baseSky + rayleighScatter + mieScatter + sunCore;

                float3 moonUVDir = mul(-viewDir.xyz, SunLToW);
                float2 moonUV = moonUVDir.xy * _MoonMap_ST.xy + _MoonMap_ST.zw;
                float4 moonTex = SAMPLE_TEXTURE2D(_MoonMap, sampler_MoonMap, moonUV);
                float3 moonColor = moonTex.rgb * step(0, moonUVDir.z);
                half moonScattering = smoothstep(0.97, 1.3, dot(viewDir, _MoonDirection));
                moonColor = (moonColor * _MoonIntensity + moonScattering * 0.8) * _MoonCol.rgb * nightFactor;
                finalColor += moonColor;

                float4 starTex = SAMPLE_TEXTURE2D(_StarTex, sampler_StarTex, sphereUV);
                float4 noiseTex = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, viewDir.yz + _Time.x * 0.1);
                float starBright = smoothstep(0.3, 0.4, noiseTex.r);
                float starPos = smoothstep(0.21, 0.31, starTex.r);
                finalColor += saturate(starBright * starPos * 3.0) * nightFactor;

                float3 milkyUV = mul(viewDir.xyz, MilkyLToW);
                float4 milkyStarTex = SAMPLE_TEXTURE2D(_StarTex, sampler_StarTex, milkyUV.xz);
                float4 milkyTex = SAMPLE_TEXTURE2D(_MilkyWayTex, sampler_MilkyWayTex, milkyUV.xz * _MilkyWayTex_ST.xy + _MilkyWayTex_ST.zw);
                float4 milkyColor = lerp(_MilkyWayGradientColor_1, _MilkyWayGradientColor_2, smoothstep(0.01, 1, milkyTex.r)) * smoothstep(0.01, 1, milkyTex.r);
                float milkyStarBright = smoothstep(0.3, 0.4, noiseTex.r);
                float milkyStarPos = smoothstep(0, 0.1, milkyStarTex.r);
                float4 finalMilkyWayColor = milkyTex.g * milkyStarBright * milkyStarPos + milkyColor;
                finalColor += finalMilkyWayColor.rgb * nightFactor;

                if (viewDir.y < 0)
                {
                    finalColor = 0;
                }

                return float4(finalColor, 1);
            }
            ENDHLSL
        }
    }
}
