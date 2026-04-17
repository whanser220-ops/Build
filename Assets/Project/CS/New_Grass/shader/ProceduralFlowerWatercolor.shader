Shader "New_Grass/ProceduralFlowerWatercolor"
{
    Properties
    {
        [Enum(Stem,0,Petal,1,Center,2)] _PartMode ("Part Mode", Float) = 1

        [Header(Color)]
        _BaseColor ("Base Color", Color) = (0.98, 0.97, 0.92, 1)
        _ShadowColor ("Shadow Color", Color) = (0.77, 0.79, 0.88, 1)
        _HighlightColor ("Highlight Color", Color) = (1.0, 0.99, 0.96, 1)
        _EdgeTintColor ("Edge Tint Color", Color) = (0.73, 0.71, 0.84, 1)

        [Header(Lighting)]
        _LightWrap ("Light Wrap", Range(0, 1)) = 0.45
        _ShadowSoftness ("Shadow Softness", Range(0.01, 0.5)) = 0.16

        [Header(Watercolor)]
        _WashScale ("Wash Scale", Range(0.1, 32)) = 4.5
        _WashContrast ("Wash Contrast", Range(0, 1.5)) = 0.55
        _BrushScale ("Brush Scale", Range(0.1, 32)) = 13
        _BrushStrength ("Brush Strength", Range(0, 1.5)) = 0.65
        _BrushWarp ("Brush Warp", Range(0, 2)) = 0.8
        _PaperGrainScale ("Paper Grain Scale", Range(0.1, 64)) = 22
        _PaperGrainStrength ("Paper Grain Strength", Range(0, 1)) = 0.18
        _PigmentPooling ("Pigment Pooling", Range(0, 1.5)) = 0.55

        [Header(Edge Breakup)]
        _EdgeBreakup ("Edge Breakup", Range(0, 1.5)) = 0.5
        _EdgeClipStrength ("Edge Clip Strength", Range(0, 1)) = 0.3
        _EdgeClipThreshold ("Edge Clip Threshold", Range(0, 1)) = 0.45
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

    // Shadow caster light parameters are populated by URP when rendering shadow maps.
    float3 _LightDirection;
    float3 _LightPosition;

    CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor;
        half4 _ShadowColor;
        half4 _HighlightColor;
        half4 _EdgeTintColor;

        float _PartMode;
        float _LightWrap;
        float _ShadowSoftness;
        float _WashScale;
        float _WashContrast;
        float _BrushScale;
        float _BrushStrength;
        float _BrushWarp;
        float _PaperGrainScale;
        float _PaperGrainStrength;
        float _PigmentPooling;
        float _EdgeBreakup;
        float _EdgeClipStrength;
        float _EdgeClipThreshold;
    CBUFFER_END

    struct Attributes
    {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
        float2 uv : TEXCOORD0;
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float3 positionWS : TEXCOORD0;
        float3 normalWS : TEXCOORD1;
        float2 uv : TEXCOORD2;
        float3 positionOS : TEXCOORD3;
        float fogFactor : TEXCOORD4;
    };

    struct ShadowVaryings
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
        float3 positionOS : TEXCOORD1;
    };

    int GetPartMode()
    {
        if (_PartMode < 0.5)
            return 0;
        if (_PartMode < 1.5)
            return 1;
        return 2;
    }

    float Hash12(float2 value)
    {
        float3 p3 = frac(float3(value.xyx) * 0.1031);
        p3 += dot(p3, p3.yzx + 33.33);
        return frac((p3.x + p3.y) * p3.z);
    }

    float Noise(float2 value)
    {
        float2 cell = floor(value);
        float2 local = frac(value);
        float a = Hash12(cell);
        float b = Hash12(cell + float2(1.0, 0.0));
        float c = Hash12(cell + float2(0.0, 1.0));
        float d = Hash12(cell + float2(1.0, 1.0));
        float2 smoothLocal = local * local * (3.0 - 2.0 * local);
        return lerp(lerp(a, b, smoothLocal.x), lerp(c, d, smoothLocal.x), smoothLocal.y);
    }

    float2 Rotate2D(float2 value, float angle)
    {
        float sine;
        float cosine;
        sincos(angle, sine, cosine);
        return float2(
            value.x * cosine - value.y * sine,
            value.x * sine + value.y * cosine);
    }

    float Fbm(float2 value)
    {
        float amplitude = 0.5;
        float sum = 0.0;
        float2 currentValue = value;

        [unroll]
        for (int octave = 0; octave < 4; octave++)
        {
            sum += Noise(currentValue) * amplitude;
            currentValue = Rotate2D(currentValue * 2.02 + 19.1, 0.57);
            amplitude *= 0.5;
        }

        return sum;
    }

    float2 GetWashCoords(float2 uv, float3 positionOS)
    {
        int partMode = GetPartMode();
        if (partMode == 0)
            return float2(uv.y * 1.35 + positionOS.x * 0.85, uv.x * 0.55 + positionOS.z * 1.05) * _WashScale;

        if (partMode == 1)
            return float2(uv.y * 1.75 + positionOS.x * 0.65, uv.x * 1.25 + positionOS.z * 0.65) * _WashScale;

        float2 centered = uv * 2.0 - 1.0;
        float radial = length(centered);
        float angle = atan2(centered.y, centered.x) * 0.15915494 + 0.5;
        return float2(radial * 2.2 + positionOS.x * 0.45, angle * 1.55 + positionOS.z * 0.45) * _WashScale;
    }

    float EvaluateWash(float2 uv, float3 positionOS)
    {
        return Fbm(GetWashCoords(uv, positionOS) + 11.13);
    }

    float EvaluateBrush(float2 uv, float3 positionOS)
    {
        int partMode = GetPartMode();
        float warp = (Fbm(GetWashCoords(uv, positionOS) * 0.7 + 23.7) - 0.5) * _BrushWarp;

        if (partMode == 0)
        {
            float2 brushCoord = float2(uv.y * _BrushScale * 1.65 + warp, uv.x * _BrushScale * 0.22 + positionOS.x * 2.4);
            float sampleValue = Noise(brushCoord);
            return saturate(pow(1.0 - abs(sampleValue * 2.0 - 1.0), 1.6));
        }

        if (partMode == 1)
        {
            float2 brushCoord = float2(uv.y * _BrushScale * 1.9 + warp, uv.x * _BrushScale * 0.42 + positionOS.x * 1.15);
            return saturate(pow(Fbm(brushCoord + 41.7), 1.08));
        }

        float2 centered = uv * 2.0 - 1.0;
        float radial = length(centered);
        float angle = atan2(centered.y, centered.x) * 0.15915494;
        float2 brushCoord = float2(radial * _BrushScale * 2.8 + warp, angle * _BrushScale * 1.15);
        return Fbm(brushCoord + 65.2);
    }

    float EvaluatePaperGrain(float2 uv, float3 positionOS)
    {
        float2 grainCoord = (uv * 3.0 + positionOS.xy * 0.55) * _PaperGrainScale;
        return Noise(grainCoord + 97.1);
    }

    float EvaluateEdgeProximity(float2 uv)
    {
        int partMode = GetPartMode();
        if (partMode == 1)
        {
            float side = abs(uv.x * 2.0 - 1.0);
            float tip = smoothstep(0.72, 1.0, uv.y);
            return saturate(max(side, tip));
        }

        if (partMode == 2)
            return saturate(length(uv * 2.0 - 1.0));

        return 0.0;
    }

    float EvaluateAlphaMask(float2 uv, float3 positionOS)
    {
        int partMode = GetPartMode();
        if (partMode == 0)
            return 1.0;

        float edgeStrength = partMode == 1 ? _EdgeClipStrength : _EdgeClipStrength * 0.18;
        if (edgeStrength <= 0.0001)
            return 1.0;

        float edgeProximity = EvaluateEdgeProximity(uv);
        float breakupScale = partMode == 1 ? 18.0 : 10.0;
        float breakupNoise = (Fbm(float2(uv.y * breakupScale, uv.x * breakupScale * 0.8) + positionOS.xz * 4.0 + 13.4) - 0.5) * 2.0;
        float edgeLimit = 1.0 - edgeStrength + breakupNoise * _EdgeBreakup * edgeStrength * 0.5;
        float alphaMask = 1.0 - smoothstep(edgeLimit, 1.0, edgeProximity);

        if (partMode == 1)
        {
            float baseKeep = 1.0 - smoothstep(0.0, 0.16, uv.y);
            alphaMask = max(alphaMask, baseKeep * 0.95);
        }

        return saturate(alphaMask);
    }

    float3 EvaluateWatercolorLighting(float2 uv, float3 positionOS, float3 positionWS, float3 normalWS, float alphaMask)
    {
        float wash = EvaluateWash(uv, positionOS);
        float brush = EvaluateBrush(uv, positionOS);
        float grain = EvaluatePaperGrain(uv, positionOS);
        float edgeProximity = EvaluateEdgeProximity(uv);

        float3 normalizedNormal = NormalizeNormalPerPixel(normalWS);
        Light mainLight = GetMainLight(TransformWorldToShadowCoord(positionWS));
        float wrappedNdotL = saturate((dot(normalizedNormal, mainLight.direction) + _LightWrap) / (1.0 + _LightWrap));
        float3 ambientColor = SampleSH(normalizedNormal);
        float ambientValue = saturate(dot(ambientColor, float3(0.2126, 0.7152, 0.0722)));

        float lightResponse = saturate(max(ambientValue * 0.6, wrappedNdotL * mainLight.shadowAttenuation));
        lightResponse = saturate(lightResponse + (wash - 0.5) * _WashContrast * 0.35);

        float shadowMask = 1.0 - smoothstep(0.34 - _ShadowSoftness, 0.72 + _ShadowSoftness, lightResponse);
        float highlightMask = smoothstep(0.56, 0.96, lightResponse + (brush - 0.5) * 0.12);

        float washTone = lerp(1.0 - _WashContrast * 0.25, 1.0 + _WashContrast * 0.12, wash);
        float brushTone = lerp(1.0 - _BrushStrength * 0.22, 1.0 + _BrushStrength * 0.07, brush);
        float grainTone = lerp(1.0 - _PaperGrainStrength * 0.18, 1.0 + _PaperGrainStrength * 0.05, grain);

        float pigment = (1.0 - lightResponse) * 0.65 + edgeProximity * 0.4 + (1.0 - brush) * 0.2;
        if (GetPartMode() == 1)
            pigment += (1.0 - smoothstep(0.0, 0.24, uv.y)) * 0.4;

        pigment = saturate(pigment * _PigmentPooling);
        float edgeTintMask = saturate(pow(edgeProximity, 1.35) * 0.55 + pigment * 0.5 + (1.0 - alphaMask) * 1.4);

        float3 color = lerp(_BaseColor.rgb, _ShadowColor.rgb, shadowMask);
        color = lerp(color, _HighlightColor.rgb, highlightMask * 0.85);
        color *= washTone * brushTone * grainTone;
        color = lerp(color, _EdgeTintColor.rgb, edgeTintMask);

        float3 sceneTint = lerp(float3(1.0, 1.0, 1.0), saturate(mainLight.color.rgb + ambientColor * 0.35), 0.25);
        return color * sceneTint;
    }

    Varyings Vert(Attributes input)
    {
        Varyings output;
        output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
        output.normalWS = TransformObjectToWorldNormal(input.normalOS);
        output.positionCS = TransformWorldToHClip(output.positionWS);
        output.uv = input.uv;
        output.positionOS = input.positionOS.xyz;
        output.fogFactor = ComputeFogFactor(output.positionCS.z);
        return output;
    }

    half4 Frag(Varyings input) : SV_TARGET
    {
        float alphaMask = EvaluateAlphaMask(input.uv, input.positionOS);
        clip(alphaMask - _EdgeClipThreshold);

        float3 color = EvaluateWatercolorLighting(input.uv, input.positionOS, input.positionWS, input.normalWS, alphaMask);
        color = MixFog(color, input.fogFactor);
        return half4(color, 1.0);
    }

    ShadowVaryings ShadowVert(Attributes input)
    {
        ShadowVaryings output;
        float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
        float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

    #if _CASTING_PUNCTUAL_LIGHT_SHADOW
        float3 lightDirectionWS = normalize(_LightPosition - positionWS);
    #else
        float3 lightDirectionWS = _LightDirection;
    #endif

        float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
        output.positionCS = ApplyShadowClamping(positionCS);

        output.uv = input.uv;
        output.positionOS = input.positionOS.xyz;
        return output;
    }

    half4 ShadowFrag(ShadowVaryings input) : SV_TARGET
    {
        float alphaMask = EvaluateAlphaMask(input.uv, input.positionOS);
        clip(alphaMask - _EdgeClipThreshold);
        return 0;
    }
    ENDHLSL

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
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            ENDHLSL
        }
    }
}
