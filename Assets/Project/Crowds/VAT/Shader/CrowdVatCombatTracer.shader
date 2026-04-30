Shader "Project/Crowd/VATCombatTracer"
{
    Properties
    {
        _MuzzleFlashTex ("Muzzle Flash Texture", 2D) = "black" {}
        _TracerTex ("Tracer Texture", 2D) = "white" {}
        _ImpactTex ("Impact Texture", 2D) = "black" {}
        _TracerColorCampA ("Tracer Color CampA", Color) = (1, 0.42, 0.18, 1)
        _TracerColorCampB ("Tracer Color CampB", Color) = (0.22, 0.74, 1, 1)
        _TracerWidth ("Tracer Width", Float) = 0.09
        _TracerBrightness ("Tracer Brightness", Float) = 1.8
        _TracerMinFlash ("Tracer Min Flash", Range(0, 1)) = 0.04
        _MuzzleFlashSize ("Muzzle Flash Size", Float) = 0.42
        _ImpactFlashSize ("Impact Flash Size", Float) = 0.55
        _ImpactPointOffset ("Impact Point Offset", Float) = 0.04
        _MuzzleFlashBrightness ("Muzzle Flash Brightness", Float) = 2.2
        _ImpactFlashBrightness ("Impact Flash Brightness", Float) = 1.8
        _MuzzleFlashFlipbook ("Muzzle Flash Flipbook", Vector) = (4, 4, 0, 0)
        _ImpactFlashFlipbook ("Impact Flash Flipbook", Vector) = (4, 4, 0, 0)
    }

    HLSLINCLUDE
    #pragma target 4.5

    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
    #include "UnityIndirect.cginc"

    struct InstanceSpawnData
    {
        float3 localPosition;
        float yawRadians;
        float uniformScale;
        float normalizedTimeOffset;
        float playbackSpeedMultiplier;
        uint faction;
    };

    struct InstanceCombatStateData
    {
        float4 localOriginAndDistance;
        float4 localTargetAndCooldown;
        float4 localImpactNormalAndHit;
        float4 healthAndHitFeedback;
        float muzzleFlash;
        int targetIndex;
        uint flags;
        uint debugShotInfoPacked;
    };

    StructuredBuffer<InstanceSpawnData> _SpawnData;
    StructuredBuffer<InstanceCombatStateData> _CombatStateBuffer;
    StructuredBuffer<uint> _VisibleInstanceIndices;

    TEXTURE2D(_MuzzleFlashTex);
    SAMPLER(sampler_MuzzleFlashTex);
    TEXTURE2D(_TracerTex);
    SAMPLER(sampler_TracerTex);
    TEXTURE2D(_ImpactTex);
    SAMPLER(sampler_ImpactTex);

    CBUFFER_START(UnityPerMaterial)
        float4 _TracerColorCampA;
        float4 _TracerColorCampB;
        float4 _RootPosition;
        float4 _RootRight;
        float4 _RootUp;
        float4 _RootForward;
        float4 _MuzzleFlashFlipbook;
        float4 _ImpactFlashFlipbook;
        float _CombatRange;
        float _TracerWidth;
        float _TracerBrightness;
        float _TracerMinFlash;
        float _MuzzleFlashSize;
        float _ImpactFlashSize;
        float _ImpactPointOffset;
        float _MuzzleFlashBrightness;
        float _ImpactFlashBrightness;
    CBUFFER_END

    static const uint CrowdVatFactionCampB = 2u;
    static const uint CrowdVatInstanceCombatFlagHasTarget = 1u << 0;
    static const uint CrowdVatInstanceCombatFlagDead = 1u << 4;
    static const uint CrowdVatCombatFxTracer = 0u;
    static const uint CrowdVatCombatFxMuzzleFlash = 1u;
    static const uint CrowdVatCombatFxImpact = 2u;

    struct Attributes
    {
        float3 positionOS : POSITION;
        float2 uv : TEXCOORD0;
        float2 effectId : TEXCOORD1;
        uint instanceID : SV_InstanceID;
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
        float4 color : TEXCOORD1;
        float flash : TEXCOORD2;
        float active : TEXCOORD3;
        float effectId : TEXCOORD4;
    };

    float3 SafeNormalize3(float3 value, float3 fallbackValue)
    {
        float lengthSquared = dot(value, value);
        if (lengthSquared <= 1e-6)
            return fallbackValue;

        return value * rsqrt(lengthSquared);
    }

    float3 TransformCrowdLocalPointToWorld(float3 localPoint)
    {
        return _RootPosition.xyz
            + _RootRight.xyz * localPoint.x
            + _RootUp.xyz * localPoint.y
            + _RootForward.xyz * localPoint.z;
    }

    float3 TransformCrowdLocalDirectionToWorld(float3 localDirection)
    {
        return _RootRight.xyz * localDirection.x
            + _RootUp.xyz * localDirection.y
            + _RootForward.xyz * localDirection.z;
    }

    float3 BuildTracerRight(float3 lineDirectionWS, float3 viewDirectionWS)
    {
        float3 rightWS = cross(viewDirectionWS, lineDirectionWS);
        if (dot(rightWS, rightWS) <= 1e-6)
            rightWS = cross(float3(0.0, 1.0, 0.0), lineDirectionWS);

        if (dot(rightWS, rightWS) <= 1e-6)
            rightWS = cross(float3(1.0, 0.0, 0.0), lineDirectionWS);

        return SafeNormalize3(rightWS, float3(1.0, 0.0, 0.0));
    }

    void BuildBillboardAxes(float3 centerWS, out float3 rightWS, out float3 upWS)
    {
        float3 viewDirectionWS = SafeNormalize3(GetCameraPositionWS() - centerWS, float3(0.0, 0.0, 1.0));
        float3 referenceUpWS = abs(viewDirectionWS.y) > 0.96 ? float3(1.0, 0.0, 0.0) : float3(0.0, 1.0, 0.0);
        rightWS = SafeNormalize3(cross(referenceUpWS, viewDirectionWS), float3(1.0, 0.0, 0.0));
        upWS = SafeNormalize3(cross(viewDirectionWS, rightWS), float3(0.0, 1.0, 0.0));
    }

    void BuildImpactAxes(float3 impactNormalWS, out float3 rightWS, out float3 upWS)
    {
        float3 normalWS = SafeNormalize3(impactNormalWS, float3(0.0, 1.0, 0.0));
        float3 referenceAxisWS = abs(normalWS.y) > 0.96 ? float3(1.0, 0.0, 0.0) : float3(0.0, 1.0, 0.0);
        rightWS = SafeNormalize3(cross(referenceAxisWS, normalWS), float3(1.0, 0.0, 0.0));
        upWS = SafeNormalize3(cross(normalWS, rightWS), float3(0.0, 1.0, 0.0));
    }

    float2 ComputeFlipbookUv(float2 uv, float4 flipbook, float flash)
    {
        float columns = max(1.0, floor(flipbook.x + 0.5));
        float rows = max(1.0, floor(flipbook.y + 0.5));
        float frameCount = max(1.0, columns * rows);
        float progress = saturate(1.0 - flash);
        float frameIndex = min(floor(progress * frameCount), frameCount - 1.0);
        float column = fmod(frameIndex, columns);
        float rowFromTop = floor(frameIndex / columns);
        return float2(
            (column + uv.x) / columns,
            ((rows - 1.0 - rowFromTop) + uv.y) / rows);
    }

    Varyings vert(Attributes input)
    {
        InitIndirectDrawArgs(0);

        Varyings output;
        output.positionCS = float4(-2.0, -2.0, 1.0, 1.0);
        output.uv = input.uv;
        output.color = 1.0;
        output.flash = 0.0;
        output.active = 0.0;
        output.effectId = input.effectId.x;

        uint sourceInstanceID = GetIndirectInstanceID(input.instanceID);
        InstanceCombatStateData combatState = _CombatStateBuffer[sourceInstanceID];
        uint flags = combatState.flags;
        uint effectId = (uint)round(input.effectId.x);
        float muzzleFlash = saturate(combatState.muzzleFlash);
        float impactFlash = saturate(combatState.localImpactNormalAndHit.w);
        float flash = effectId == CrowdVatCombatFxImpact ? impactFlash : muzzleFlash;
        bool isActive = (flags & CrowdVatInstanceCombatFlagHasTarget) != 0u &&
            (flags & CrowdVatInstanceCombatFlagDead) == 0u &&
            (effectId == CrowdVatCombatFxImpact ? impactFlash > 0.001 : muzzleFlash > _TracerMinFlash);
        if (!isActive)
            return output;

        float3 originWS = TransformCrowdLocalPointToWorld(combatState.localOriginAndDistance.xyz);
        float3 targetWS = TransformCrowdLocalPointToWorld(combatState.localTargetAndCooldown.xyz);
        float3 lineVectorWS = targetWS - originWS;
        float lineLength = length(lineVectorWS);
        if (lineLength <= 1e-4)
            return output;

        float3 lineDirectionWS = lineVectorWS / lineLength;
        float3 midpointWS = 0.5 * (originWS + targetWS);
        float3 viewDirectionWS = SafeNormalize3(GetCameraPositionWS() - midpointWS, float3(0.0, 0.0, 1.0));

        InstanceSpawnData spawnData = _SpawnData[sourceInstanceID];
        float4 tracerColor = spawnData.faction == CrowdVatFactionCampB ? _TracerColorCampB : _TracerColorCampA;

        if (effectId == CrowdVatCombatFxTracer)
        {
            float3 rightWS = BuildTracerRight(lineDirectionWS, viewDirectionWS);
            float along = saturate(input.positionOS.y);
            float halfWidth = max(_TracerWidth, 1e-4) * 0.5;
            float side = input.positionOS.x;
            float3 positionWS = lerp(originWS, targetWS, along) + rightWS * side * halfWidth;

            output.positionCS = TransformWorldToHClip(positionWS);
            output.uv = float2(along, input.uv.x);
            output.color = tracerColor;
            output.flash = flash;
            output.active = 1.0;
            output.effectId = effectId;
            return output;
        }

        if (effectId == CrowdVatCombatFxImpact &&
            impactFlash <= 0.001)
        {
            return output;
        }

        float2 corner = input.uv * 2.0 - 1.0;
        float3 impactNormalWS = SafeNormalize3(
            TransformCrowdLocalDirectionToWorld(combatState.localImpactNormalAndHit.xyz),
            -lineDirectionWS);
        float3 centerWS = effectId == CrowdVatCombatFxMuzzleFlash
            ? originWS + lineDirectionWS * min(max(_MuzzleFlashSize, 0.001) * 0.35, lineLength * 0.25)
            : targetWS + impactNormalWS * max(_ImpactPointOffset, 0.0);
        float size = effectId == CrowdVatCombatFxMuzzleFlash
            ? max(_MuzzleFlashSize, 0.001)
            : max(_ImpactFlashSize, 0.001);
        size *= lerp(0.78, 1.12, flash);

        float3 rightBillboardWS;
        float3 upBillboardWS;
        if (effectId == CrowdVatCombatFxMuzzleFlash)
            BuildBillboardAxes(centerWS, rightBillboardWS, upBillboardWS);
        else
            BuildImpactAxes(impactNormalWS, rightBillboardWS, upBillboardWS);

        float3 billboardPositionWS = centerWS +
            (rightBillboardWS * corner.x + upBillboardWS * corner.y) * (size * 0.5);

        output.positionCS = TransformWorldToHClip(billboardPositionWS);
        output.uv = input.uv;
        output.color = 1.0;
        output.flash = flash;
        output.active = 1.0;
        output.effectId = effectId;
        return output;
    }

    half ExtractAdditiveAlpha(half3 color)
    {
        return saturate(max(color.r, max(color.g, color.b)));
    }

    half4 frag(Varyings input) : SV_Target
    {
        half active = saturate(input.active);
        if (active <= 0.0h)
            return 0.0h;

        uint effectId = (uint)round(input.effectId);
        half flash = saturate(input.flash);
        half4 sampleValue = 0.0h;
        half brightness = 1.0h;
        half3 tint = input.color.rgb;

        if (effectId == CrowdVatCombatFxTracer)
        {
            sampleValue = SAMPLE_TEXTURE2D(_TracerTex, sampler_TracerTex, input.uv);
            brightness = (half)_TracerBrightness;
        }
        else if (effectId == CrowdVatCombatFxMuzzleFlash)
        {
            float2 flipbookUv = ComputeFlipbookUv(input.uv, _MuzzleFlashFlipbook, input.flash);
            sampleValue = SAMPLE_TEXTURE2D(_MuzzleFlashTex, sampler_MuzzleFlashTex, flipbookUv);
            brightness = (half)_MuzzleFlashBrightness;
            tint = 1.0h;
        }
        else
        {
            float2 flipbookUv = ComputeFlipbookUv(input.uv, _ImpactFlashFlipbook, input.flash);
            sampleValue = SAMPLE_TEXTURE2D(_ImpactTex, sampler_ImpactTex, flipbookUv);
            brightness = (half)_ImpactFlashBrightness;
            tint = 1.0h;
        }

        half alpha = active * flash * ExtractAdditiveAlpha(sampleValue.rgb);
        half3 color = sampleValue.rgb * tint * brightness;
        return half4(color, alpha);
    }
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha One
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDHLSL
        }
    }
}
