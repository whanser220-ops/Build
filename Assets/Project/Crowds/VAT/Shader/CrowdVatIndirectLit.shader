Shader "Project/Crowd/VATIndirectLit"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Range(0, 2)) = 1
        _SpecGlossMap ("Spec Gloss Map", 2D) = "white" {}
        _SpecColor ("Spec Color", Color) = (1, 1, 1, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 1
        _SpecularStrength ("Specular Strength", Range(0, 1)) = 0.12
        [Toggle] _UseNormalMap ("Use Normal Map", Float) = 0
        [Toggle] _UseSpecGlossMap ("Use Spec Gloss Map", Float) = 0
        [Toggle] _AlphaClip ("Alpha Clip", Float) = 0
        _Cutoff ("Cutoff", Range(0, 1)) = 0.5
    }

    HLSLINCLUDE
    #pragma target 4.5

    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
    #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
    #include "UnityIndirect.cginc"

    TEXTURE2D(_SpecGlossMap);
    SAMPLER(sampler_SpecGlossMap);
    Texture2D<float4> _BoneAnimationTex;

    struct MatrixRows
    {
        float4 row0;
        float4 row1;
        float4 row2;
    };

    StructuredBuffer<MatrixRows> _InstanceTransforms;
    StructuredBuffer<float4> _InstanceFrameData;
    StructuredBuffer<uint> _VisibleInstanceIndices;

    CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        float4 _BaseColor;
        float4 _SpecColor;
        float4 _CrowdRootLocalRow0;
        float4 _CrowdRootLocalRow1;
        float4 _CrowdRootLocalRow2;
        float _BumpScale;
        float _Smoothness;
        float _SpecularStrength;
        float _UseNormalMap;
        float _UseSpecGlossMap;
        float _AlphaClip;
        float _Cutoff;
    CBUFFER_END

    struct Attributes
    {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
        float4 tangentOS : TANGENT;
        float2 uv : TEXCOORD0;
        float4 bonePixelOffsets : TEXCOORD2;
        float4 boneWeights : TEXCOORD3;
        uint instanceID : SV_InstanceID;
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
        float3 normalWS : TEXCOORD1;
        float3 positionWS : TEXCOORD2;
        float4 shadowCoord : TEXCOORD3;
        float4 tangentWS : TEXCOORD4;
    };

    struct ShadowVaryings
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    float4 LoadBoneAnimationRow(uint frameIndex, uint basePixelOffset, uint matrixRowId)
    {
        return _BoneAnimationTex.Load(int3(basePixelOffset + matrixRowId, frameIndex, 0));
    }

    float3 TransformVatPosition(float4 row0, float4 row1, float4 row2, float3 positionOS)
    {
        float4 position = float4(positionOS, 1.0);
        return float3(
            dot(row0, position),
            dot(row1, position),
            dot(row2, position));
    }

    float3 TransformVatDirection(float3 row0, float3 row1, float3 row2, float3 directionOS)
    {
        return float3(
            dot(row0, directionOS),
            dot(row1, directionOS),
            dot(row2, directionOS));
    }

    float3 TransformPositionColumns(float4 row0, float4 row1, float4 row2, float3 positionOS)
    {
        return row0.xyz * positionOS.x
            + row1.xyz * positionOS.y
            + row2.xyz * positionOS.z
            + float3(row0.w, row1.w, row2.w);
    }

    float3 TransformDirectionColumns(float3 row0, float3 row1, float3 row2, float3 directionOS)
    {
        return row0 * directionOS.x
            + row1 * directionOS.y
            + row2 * directionOS.z;
    }

    void ExtractRotationRows(inout float3 row0, inout float3 row1, inout float3 row2)
    {
        float sx = max(length(row0), 1e-5);
        float sy = max(length(row1), 1e-5);
        float sz = max(length(row2), 1e-5);

        float determinantSign = dot(cross(row0, row1), row2);
        if (determinantSign < 0.0)
            sx = -sx;

        row0 /= sx;
        row1 /= sy;
        row2 /= sz;
    }

    void ComputeFramePose(
        uint frameIndex,
        float3 positionInput,
        float3 normalInput,
        float4 tangentInput,
        float4 bonePixelOffsets,
        float4 boneWeights,
        out float3 positionOutput,
        out float3 normalOutput,
        out float4 tangentOutput)
    {
        float4 normalizedWeights = boneWeights;
        float weightSum = normalizedWeights.x + normalizedWeights.y + normalizedWeights.z + normalizedWeights.w;
        if (weightSum <= 1e-5)
        {
            positionOutput = positionInput;
            normalOutput = normalize(normalInput);
            tangentOutput = tangentInput;
            return;
        }

        normalizedWeights /= weightSum;
        positionOutput = float3(0.0, 0.0, 0.0);
        normalOutput = float3(0.0, 0.0, 0.0);
        float3 tangentAccum = float3(0.0, 0.0, 0.0);

        [unroll]
        for (int influenceIndex = 0; influenceIndex < 4; influenceIndex++)
        {
            float weight = normalizedWeights[influenceIndex];
            if (weight <= 1e-5)
                continue;

            uint basePixelOffset = (uint)round(bonePixelOffsets[influenceIndex]);
            float4 row0 = LoadBoneAnimationRow(frameIndex, basePixelOffset, 0);
            float4 row1 = LoadBoneAnimationRow(frameIndex, basePixelOffset, 1);
            float4 row2 = LoadBoneAnimationRow(frameIndex, basePixelOffset, 2);

            positionOutput += weight * TransformVatPosition(row0, row1, row2, positionInput);

            float3 rotationRow0 = row0.xyz;
            float3 rotationRow1 = row1.xyz;
            float3 rotationRow2 = row2.xyz;
            ExtractRotationRows(rotationRow0, rotationRow1, rotationRow2);
            normalOutput += weight * TransformVatDirection(rotationRow0, rotationRow1, rotationRow2, normalInput);
            tangentAccum += weight * TransformVatDirection(rotationRow0, rotationRow1, rotationRow2, tangentInput.xyz);
        }

        normalOutput = normalize(normalOutput);
        tangentOutput = float4(normalize(tangentAccum), tangentInput.w);
    }

    void ComputeVatPose(
        float4 frameData,
        float3 positionInput,
        float3 normalInput,
        float4 tangentInput,
        float4 bonePixelOffsets,
        float4 boneWeights,
        out float3 positionOutput,
        out float3 normalOutput,
        out float4 tangentOutput)
    {
        uint frameIndex = (uint)round(frameData.x);
        uint nextFrameIndex = (uint)round(frameData.y);
        float frameBlend = saturate(frameData.z);

        float3 currentPosition;
        float3 currentNormal;
        float4 currentTangent;
        ComputeFramePose(
            frameIndex,
            positionInput,
            normalInput,
            tangentInput,
            bonePixelOffsets,
            boneWeights,
            currentPosition,
            currentNormal,
            currentTangent);

        if (frameBlend <= 1e-5 || frameIndex == nextFrameIndex)
        {
            positionOutput = currentPosition;
            normalOutput = currentNormal;
            tangentOutput = currentTangent;
            return;
        }

        float3 nextPosition;
        float3 nextNormal;
        float4 nextTangent;
        ComputeFramePose(
            nextFrameIndex,
            positionInput,
            normalInput,
            tangentInput,
            bonePixelOffsets,
            boneWeights,
            nextPosition,
            nextNormal,
            nextTangent);

        positionOutput = lerp(currentPosition, nextPosition, frameBlend);
        normalOutput = normalize(lerp(currentNormal, nextNormal, frameBlend));
        tangentOutput = float4(normalize(lerp(currentTangent.xyz, nextTangent.xyz, frameBlend)), tangentInput.w);
    }

    float3 TransformCrowdRootPosition(float3 positionOS)
    {
        return TransformPositionColumns(_CrowdRootLocalRow0, _CrowdRootLocalRow1, _CrowdRootLocalRow2, positionOS);
    }

    float3 TransformCrowdRootDirection(float3 directionOS)
    {
        float3 row0 = _CrowdRootLocalRow0.xyz;
        float3 row1 = _CrowdRootLocalRow1.xyz;
        float3 row2 = _CrowdRootLocalRow2.xyz;
        ExtractRotationRows(row0, row1, row2);
        return TransformDirectionColumns(row0, row1, row2, directionOS);
    }

    float3 TransformInstancePosition(MatrixRows rows, float3 positionOS)
    {
        return TransformPositionColumns(rows.row0, rows.row1, rows.row2, positionOS);
    }

    float3 TransformInstanceDirection(MatrixRows rows, float3 directionOS)
    {
        float3 row0 = rows.row0.xyz;
        float3 row1 = rows.row1.xyz;
        float3 row2 = rows.row2.xyz;
        ExtractRotationRows(row0, row1, row2);
        return TransformDirectionColumns(row0, row1, row2, directionOS);
    }

    float4 SampleBase(float2 uv)
    {
        return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
    }

    float3 SampleVatNormalTS(float2 uv)
    {
        if (_UseNormalMap <= 0.5)
            return float3(0.0, 0.0, 1.0);

        half4 normalSample = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv);
        #if BUMP_SCALE_NOT_SUPPORTED
            return UnpackNormal(normalSample);
        #else
            return UnpackNormalScale(normalSample, _BumpScale);
        #endif
    }

    float4 SampleSpecGloss(float2 uv)
    {
        if (_UseSpecGlossMap <= 0.5)
            return float4(_SpecColor.rgb, saturate(_Smoothness));

        float4 specGlossSample = SAMPLE_TEXTURE2D(_SpecGlossMap, sampler_SpecGlossMap, uv);
        specGlossSample.rgb *= _SpecColor.rgb;
        specGlossSample.a *= saturate(_Smoothness);
        return specGlossSample;
    }

    float3 ApplyVatNormalMap(float2 uv, float3 normalWS, float4 tangentWS)
    {
        float3 normalTS = SampleVatNormalTS(uv);
        float3 tangentDirectionWS = normalize(tangentWS.xyz);
        float3 bitangentWS = normalize(cross(normalWS, tangentDirectionWS) * tangentWS.w);
        return normalize(
            tangentDirectionWS * normalTS.x
            + bitangentWS * normalTS.y
            + normalWS * normalTS.z);
    }

    void ApplyAlphaClip(float alpha)
    {
        if (_AlphaClip > 0.5)
            clip(alpha - _Cutoff);
    }

    Varyings ForwardVert(Attributes input)
    {
        InitIndirectDrawArgs(0);
        uint visibleInstanceID = GetIndirectInstanceID(input.instanceID);
        uint sourceInstanceID = _VisibleInstanceIndices[visibleInstanceID];
        MatrixRows instanceRows = _InstanceTransforms[sourceInstanceID];
        float4 frameData = _InstanceFrameData[sourceInstanceID];

        float3 skinnedPositionMS;
        float3 skinnedNormalMS;
        float4 skinnedTangentMS;
        ComputeVatPose(
            frameData,
            input.positionOS.xyz,
            input.normalOS,
            input.tangentOS,
            input.bonePixelOffsets,
            input.boneWeights,
            skinnedPositionMS,
            skinnedNormalMS,
            skinnedTangentMS);

        float3 agentPositionOS = TransformCrowdRootPosition(skinnedPositionMS);
        float3 agentNormalOS = normalize(TransformCrowdRootDirection(skinnedNormalMS));
        float3 agentTangentOS = normalize(TransformCrowdRootDirection(skinnedTangentMS.xyz));
        float3 positionWS = TransformInstancePosition(instanceRows, agentPositionOS);
        float3 normalWS = normalize(TransformInstanceDirection(instanceRows, agentNormalOS));
        float3 tangentWS = normalize(TransformInstanceDirection(instanceRows, agentTangentOS));

        Varyings output;
        output.positionCS = TransformWorldToHClip(positionWS);
        output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
        output.normalWS = normalWS;
        output.positionWS = positionWS;
        output.shadowCoord = TransformWorldToShadowCoord(positionWS);
        output.tangentWS = float4(tangentWS, skinnedTangentMS.w);
        return output;
    }

    half4 ForwardFrag(Varyings input) : SV_Target
    {
        half4 baseSample = SampleBase(input.uv);
        ApplyAlphaClip(baseSample.a);

        float3 normalWS = normalize(input.normalWS);
        normalWS = ApplyVatNormalMap(input.uv, normalWS, input.tangentWS);
        float3 viewDirectionWS = normalize(GetWorldSpaceViewDir(input.positionWS));
        Light mainLight = GetMainLight(input.shadowCoord);
        float3 lightDirectionWS = normalize(mainLight.direction);
        float3 halfVector = normalize(lightDirectionWS + viewDirectionWS);
        float4 specGloss = SampleSpecGloss(input.uv);

        float lambert = saturate(dot(normalWS, lightDirectionWS));
        float specularPower = lerp(8.0, 128.0, specGloss.a);
        float specular = pow(saturate(dot(normalWS, halfVector)), specularPower);

        float3 ambient = SampleSH(normalWS) * baseSample.rgb;
        float3 direct = baseSample.rgb
            * lambert
            * mainLight.color
            * mainLight.distanceAttenuation
            * mainLight.shadowAttenuation;
        float3 highlight = specular
            * specGloss.rgb
            * _SpecularStrength
            * mainLight.color
            * mainLight.distanceAttenuation
            * mainLight.shadowAttenuation;

        return half4(ambient + direct + highlight, baseSample.a);
    }

    ShadowVaryings ShadowVert(Attributes input)
    {
        InitIndirectDrawArgs(0);
        uint visibleInstanceID = GetIndirectInstanceID(input.instanceID);
        uint sourceInstanceID = _VisibleInstanceIndices[visibleInstanceID];
        MatrixRows instanceRows = _InstanceTransforms[sourceInstanceID];
        float4 frameData = _InstanceFrameData[sourceInstanceID];

        float3 skinnedPositionMS;
        float3 skinnedNormalMS;
        float4 skinnedTangentMS;
        ComputeVatPose(
            frameData,
            input.positionOS.xyz,
            input.normalOS,
            input.tangentOS,
            input.bonePixelOffsets,
            input.boneWeights,
            skinnedPositionMS,
            skinnedNormalMS,
            skinnedTangentMS);

        float3 agentPositionOS = TransformCrowdRootPosition(skinnedPositionMS);
        float3 agentNormalOS = normalize(TransformCrowdRootDirection(skinnedNormalMS));
        float3 positionWS = TransformInstancePosition(instanceRows, agentPositionOS);
        float3 normalWS = normalize(TransformInstanceDirection(instanceRows, agentNormalOS));

        ShadowVaryings output;
        output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _MainLightPosition.xyz));
        output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
        return output;
    }

    half4 ShadowFrag(ShadowVaryings input) : SV_Target
    {
        half4 baseSample = SampleBase(input.uv);
        ApplyAlphaClip(baseSample.a);
        return 0;
    }
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex ForwardVert
            #pragma fragment ForwardFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            Cull Back
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            ENDHLSL
        }
    }
}
