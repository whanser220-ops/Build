[numthreads(64, 1, 1)]
void FinalizeInstances(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint instanceIndex;
    if (!TryResolveAliveDispatchInstanceIndex(dispatchThreadId.x, instanceIndex))
        return;

    InstanceSimulationState state = LoadSimulationState(instanceIndex);
    InstanceCombatStateData combatState = _CombatStateBuffer[instanceIndex];
    uint deathState = _DeathStateBuffer[instanceIndex];
    bool isDead = IsDeathStateDead(deathState);
    float resolvedHealth = isDead ? 0.0 : ComputeCombatHealth(deathState);
    float previousHealth = combatState.healthAndHitFeedback.x > 0.0
        ? combatState.healthAndHitFeedback.x
        : (float)GetCombatMaxHealthUnits();
    float currentFrameDamage = max(previousHealth - resolvedHealth, 0.0);
    float lastDamage = currentFrameDamage > 0.001 ? currentFrameDamage : combatState.healthAndHitFeedback.z;
    float hitFlash = isDead ? 0.0 : DecayCombatHitFlash(combatState.healthAndHitFeedback.y);
    if (!isDead && currentFrameDamage > 0.001)
        hitFlash = 1.0;
    combatState.healthAndHitFeedback = BuildCombatHealthFeedback(resolvedHealth, hitFlash, lastDamage);
    if (_EnableGpuInstanceCombat == 0)
    {
        combatState.flags = isDead ? CrowdVatInstanceCombatFlagDead : 0u;
        combatState.muzzleFlash = 0.0;
        combatState.healthAndHitFeedback = BuildCombatHealthFeedback((float)GetCombatMaxHealthUnits(), 0.0, 0.0);
        combatState.debugShotInfoPacked = PackAiDebugCombatShotInfo(-1, false, false, -1);
        combatState.debugShotTraceMeta = BuildAiDebugCombatShotTraceMeta(-1, 0.0, false, false);
    }

    float yawRadians = state.localPositionAndYaw.w;
    if (!isDead && (combatState.flags & CrowdVatInstanceCombatFlagHasTarget) != 0u)
    {
        float2 aimDirectionXZ = combatState.localTargetAndCooldown.xz - state.localPositionAndYaw.xz;
        if (dot(aimDirectionXZ, aimDirectionXZ) > 1e-5)
            yawRadians = atan2(aimDirectionXZ.x, aimDirectionXZ.y);
    }

    if (isDead)
    {
        float3 originLocal = BuildCombatTargetTopLocal(state);
        combatState.localOriginAndDistance = float4(originLocal, 1e20);
        combatState.localTargetAndCooldown = float4(originLocal, 0.0);
        combatState.localImpactNormalAndHit = float4(0.0, 1.0, 0.0, 0.0);
        combatState.healthAndHitFeedback = BuildCombatHealthFeedback(0.0, 0.0, lastDamage);
        combatState.muzzleFlash = 0.0;
        combatState.targetIndex = -1;
        combatState.flags = CrowdVatInstanceCombatFlagDead;
        combatState.debugShotInfoPacked = PackAiDebugCombatShotInfo(-1, false, false, -1);
        combatState.debugShotTraceMeta = BuildAiDebugCombatShotTraceMeta(-1, 0.0, false, false);
    }

    float muzzleFlash = isDead ? 0.0 : saturate(combatState.muzzleFlash);
    float combatVisualFlash = muzzleFlash;
    float sineYaw;
    float cosineYaw;
    sincos(yawRadians, sineYaw, cosineYaw);

    float3 axisXLocal = float3(cosineYaw, 0.0, -sineYaw);
    float3 axisYLocal = float3(0.0, 1.0, 0.0);
    float3 axisZLocal = float3(sineYaw, 0.0, cosineYaw);

    float3 centerWS = _RootPosition + TransformRootDirection(state.localPositionAndYaw.xyz);
    float renderScale = isDead ? 0.0 : state.scaleAndVelocity.x;
    float3 axisXWS = TransformRootDirection(axisXLocal) * renderScale;
    float3 axisYWS = TransformRootDirection(axisYLocal) * renderScale;
    float3 axisZWS = TransformRootDirection(axisZLocal) * renderScale;

    MatrixRows rows;
    rows.row0 = float4(axisXWS, centerWS.x);
    rows.row1 = float4(axisYWS, centerWS.y);
    rows.row2 = float4(axisZWS, centerWS.z);
    _InstanceTransforms[instanceIndex] = rows;
    _CombatStateBuffer[instanceIndex] = combatState;

    AnimationClipGpuData currentClip;
    TryGetAnimationClipData(0, currentClip);
    InstanceAnimationStateGpuData animationState;
    if (_AnimationClipCount > 0)
    {
        animationState = _InstanceAnimationStateBuffer[instanceIndex];
        TryGetAnimationClipData(animationState.currentClipIndex, currentClip);
    }
    else
    {
        animationState.currentClipIndex = 0;
        animationState.nextClipIndex = 0;
        animationState.isBlending = 0;
        animationState.padding0 = 0;
        animationState.currentClipTime = 0.0;
        animationState.nextClipTime = 0.0;
        animationState.transitionElapsed = 0.0;
        animationState.transitionDuration = 0.0;
    }

    float4 currentFrameData = BuildAnimationFrameData(currentClip, animationState.currentClipTime);
    float4 nextFrameData = currentFrameData;
    float clipBlend = 0.0;

    if (animationState.isBlending != 0)
    {
        AnimationClipGpuData nextClip;
        if (TryGetAnimationClipData(animationState.nextClipIndex, nextClip))
        {
            nextFrameData = BuildAnimationFrameData(nextClip, animationState.nextClipTime);
            clipBlend = animationState.transitionDuration <= 1e-5
                ? 1.0
                : saturate(animationState.transitionElapsed / animationState.transitionDuration);
        }
    }

    _InstanceFrameData[instanceIndex] = float4(
        currentFrameData.x,
        currentFrameData.y,
        currentFrameData.z,
        clipBlend);
    _InstanceFrameBlendData[instanceIndex] = float4(
        nextFrameData.x,
        nextFrameData.y,
        nextFrameData.z,
        combatVisualFlash);
}

