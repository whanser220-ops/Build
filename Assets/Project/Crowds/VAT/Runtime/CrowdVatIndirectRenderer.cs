using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed partial class CrowdVatIndirectRenderer : MonoBehaviour
{
    private static readonly int BoneAnimationTextureId = Shader.PropertyToID("_BoneAnimationTex");
    private static readonly int InstanceTransformsId = Shader.PropertyToID("_InstanceTransforms");
    private static readonly int InstanceFrameDataId = Shader.PropertyToID("_InstanceFrameData");
    private static readonly int InstanceFrameBlendDataId = Shader.PropertyToID("_InstanceFrameBlendData");
    private static readonly int VisibleInstanceIndicesId = Shader.PropertyToID("_VisibleInstanceIndices");
    private static readonly int AnimationClipMetadataBufferId = Shader.PropertyToID("_AnimationClipMetadataBuffer");
    private static readonly int InstanceAnimationStateBufferId = Shader.PropertyToID("_InstanceAnimationStateBuffer");
    private static readonly int CrowdRootLocalRow0Id = Shader.PropertyToID("_CrowdRootLocalRow0");
    private static readonly int CrowdRootLocalRow1Id = Shader.PropertyToID("_CrowdRootLocalRow1");
    private static readonly int CrowdRootLocalRow2Id = Shader.PropertyToID("_CrowdRootLocalRow2");
    private static readonly int TracerColorCampAId = Shader.PropertyToID("_TracerColorCampA");
    private static readonly int TracerColorCampBId = Shader.PropertyToID("_TracerColorCampB");
    private static readonly int TracerWidthId = Shader.PropertyToID("_TracerWidth");
    private static readonly int TracerBrightnessId = Shader.PropertyToID("_TracerBrightness");
    private static readonly int TracerMinFlashId = Shader.PropertyToID("_TracerMinFlash");
    private static readonly int MuzzleFlashTexId = Shader.PropertyToID("_MuzzleFlashTex");
    private static readonly int TracerTexId = Shader.PropertyToID("_TracerTex");
    private static readonly int ImpactTexId = Shader.PropertyToID("_ImpactTex");
    private static readonly int MuzzleFlashSizeId = Shader.PropertyToID("_MuzzleFlashSize");
    private static readonly int ImpactFlashSizeId = Shader.PropertyToID("_ImpactFlashSize");
    private static readonly int ImpactPointOffsetId = Shader.PropertyToID("_ImpactPointOffset");
    private static readonly int MuzzleFlashBrightnessId = Shader.PropertyToID("_MuzzleFlashBrightness");
    private static readonly int ImpactFlashBrightnessId = Shader.PropertyToID("_ImpactFlashBrightness");
    private static readonly int MuzzleFlashFlipbookId = Shader.PropertyToID("_MuzzleFlashFlipbook");
    private static readonly int ImpactFlashFlipbookId = Shader.PropertyToID("_ImpactFlashFlipbook");

    private static readonly int InstanceCountId = Shader.PropertyToID("_InstanceCount");
    private static readonly int AnimationClipCountId = Shader.PropertyToID("_AnimationClipCount");
    private static readonly int PlaybackTimeId = Shader.PropertyToID("_PlaybackTime");
    private static readonly int BasePlaybackSpeedId = Shader.PropertyToID("_BasePlaybackSpeed");
    private static readonly int ClipStartFrameId = Shader.PropertyToID("_ClipStartFrame");
    private static readonly int ClipFrameCountId = Shader.PropertyToID("_ClipFrameCount");
    private static readonly int ClipLengthId = Shader.PropertyToID("_ClipLength");
    private static readonly int ClipLoopId = Shader.PropertyToID("_ClipLoop");
    private static readonly int RootPositionId = Shader.PropertyToID("_RootPosition");
    private static readonly int RootRightId = Shader.PropertyToID("_RootRight");
    private static readonly int RootUpId = Shader.PropertyToID("_RootUp");
    private static readonly int RootForwardId = Shader.PropertyToID("_RootForward");
    private static readonly int SpawnDataId = Shader.PropertyToID("_SpawnData");
    private static readonly int SimulationPositionYawReadBufferId = Shader.PropertyToID("_SimulationPositionYawReadBuffer");
    private static readonly int SimulationPositionYawWriteBufferId = Shader.PropertyToID("_SimulationPositionYawWriteBuffer");
    private static readonly int SimulationScaleReadBufferId = Shader.PropertyToID("_SimulationScaleReadBuffer");
    private static readonly int SimulationScaleWriteBufferId = Shader.PropertyToID("_SimulationScaleWriteBuffer");
    private static readonly int SimulationVelocityReadBufferId = Shader.PropertyToID("_SimulationVelocityReadBuffer");
    private static readonly int SimulationVelocityWriteBufferId = Shader.PropertyToID("_SimulationVelocityWriteBuffer");
    private static readonly int DeathStateBufferId = Shader.PropertyToID("_DeathStateBuffer");
    private static readonly int InteractionSphereBufferId = Shader.PropertyToID("_InteractionSphereBuffer");
    private static readonly int InteractionSphereCountId = Shader.PropertyToID("_InteractionSphereCount");
    private static readonly int GridCounterBufferId = Shader.PropertyToID("_GridCounterBuffer");
    private static readonly int GridOccupantBufferId = Shader.PropertyToID("_GridOccupantBuffer");
    private static readonly int GridPrevTouchedCellBufferId = Shader.PropertyToID("_GridPrevTouchedCellBuffer");
    private static readonly int GridPrevTouchedCounterBufferId = Shader.PropertyToID("_GridPrevTouchedCounterBuffer");
    private static readonly int GridCurrTouchedCellBufferId = Shader.PropertyToID("_GridCurrTouchedCellBuffer");
    private static readonly int GridCurrTouchedCounterBufferId = Shader.PropertyToID("_GridCurrTouchedCounterBuffer");
    private static readonly int GridClearDispatchArgsBufferId = Shader.PropertyToID("_GridClearDispatchArgsBuffer");
    private static readonly int SpatialCapsuleStartRadiusBufferId = Shader.PropertyToID("_SpatialCapsuleStartRadiusBuffer");
    private static readonly int SpatialCapsuleEndHeightBufferId = Shader.PropertyToID("_SpatialCapsuleEndHeightBuffer");
    private static readonly int SpatialOwnerIndexBufferId = Shader.PropertyToID("_SpatialOwnerIndexBuffer");
    private static readonly int SpatialTargetMaskBufferId = Shader.PropertyToID("_SpatialTargetMaskBuffer");
    private static readonly int SpatialFlagsBufferId = Shader.PropertyToID("_SpatialFlagsBuffer");
    private static readonly int SpatialFactionBufferId = Shader.PropertyToID("_SpatialFactionBuffer");
    private static readonly int SpatialQueryBufferId = Shader.PropertyToID("_SpatialQueryBuffer");
    private static readonly int SpatialQueryResultBufferId = Shader.PropertyToID("_SpatialQueryResultBuffer");
    private static readonly int SpatialQueryHitBufferId = Shader.PropertyToID("_SpatialQueryHitBuffer");
    private static readonly int SpatialQueryCountId = Shader.PropertyToID("_SpatialQueryCount");
    private static readonly int MaxSpatialQueryHitsId = Shader.PropertyToID("_MaxSpatialQueryHits");
    private static readonly int GridBuildModeId = Shader.PropertyToID("_GridBuildMode");
    private static readonly int DeltaTimeId = Shader.PropertyToID("_DeltaTime");
    private static readonly int EnableApproximateCollisionId = Shader.PropertyToID("_EnableApproximateCollision");
    private static readonly int GridDimId = Shader.PropertyToID("_GridDim");
    private static readonly int GridCellCountId = Shader.PropertyToID("_GridCellCount");
    private static readonly int CellSizeId = Shader.PropertyToID("_CellSize");
    private static readonly int InvCellSizeId = Shader.PropertyToID("_InvCellSize");
    private static readonly int GridMinXZId = Shader.PropertyToID("_GridMinXZ");
    private static readonly int GridMaxXZId = Shader.PropertyToID("_GridMaxXZ");
    private static readonly int MaxCellOccupancyId = Shader.PropertyToID("_MaxCellOccupancy");
    private static readonly int CollisionRadiusId = Shader.PropertyToID("_CollisionRadius");
    private static readonly int CapsuleHeightId = Shader.PropertyToID("_CapsuleHeight");
    private static readonly int MaxSpatialQueryElementRadiusId = Shader.PropertyToID("_MaxSpatialQueryElementRadius");
    private static readonly int AnchorStiffnessId = Shader.PropertyToID("_AnchorStiffness");
    private static readonly int VelocityDampingId = Shader.PropertyToID("_VelocityDamping");
    private static readonly int GoalStiffnessId = Shader.PropertyToID("_GoalStiffness");
    private static readonly int EnableNavigationPotentialFieldId = Shader.PropertyToID("_EnableNavigationPotentialField");
    private static readonly int NavigationPotentialProbeDistanceId = Shader.PropertyToID("_NavigationPotentialProbeDistance");
    private static readonly int NavigationDensityLateralStrengthId = Shader.PropertyToID("_NavigationDensityLateralStrength");
    private static readonly int NavigationDensitySlowdownStrengthId = Shader.PropertyToID("_NavigationDensitySlowdownStrength");
    private static readonly int NavigationDensityReferenceOccupancyId = Shader.PropertyToID("_NavigationDensityReferenceOccupancy");
    private static readonly int EnableLocalAvoidanceId = Shader.PropertyToID("_EnableLocalAvoidance");
    private static readonly int LocalAvoidanceRadiusId = Shader.PropertyToID("_LocalAvoidanceRadius");
    private static readonly int LocalAvoidanceStrengthId = Shader.PropertyToID("_LocalAvoidanceStrength");
    private static readonly int DensitySlowdownStrengthId = Shader.PropertyToID("_DensitySlowdownStrength");
    private static readonly int DensityReferenceNeighborCountId = Shader.PropertyToID("_DensityReferenceNeighborCount");
    private static readonly int LocalAvoidanceMaxNeighborsId = Shader.PropertyToID("_LocalAvoidanceMaxNeighbors");
    private static readonly int SelfCollisionStrengthId = Shader.PropertyToID("_SelfCollisionStrength");
    private static readonly int SelfCollisionSlopId = Shader.PropertyToID("_SelfCollisionSlop");
    private static readonly int SelfCollisionMaxPushPerStepId = Shader.PropertyToID("_SelfCollisionMaxPushPerStep");
    private static readonly int EnableDensityConstraintId = Shader.PropertyToID("_EnableDensityConstraint");
    private static readonly int DensityComfortDistanceId = Shader.PropertyToID("_DensityComfortDistance");
    private static readonly int DensityConstraintStiffnessId = Shader.PropertyToID("_DensityConstraintStiffness");
    private static readonly int MaxPushPerStepId = Shader.PropertyToID("_MaxPushPerStep");
    private static readonly int MaxDisplacementFromSpawnId = Shader.PropertyToID("_MaxDisplacementFromSpawn");
    private static readonly int InactiveReturnStrengthId = Shader.PropertyToID("_InactiveReturnStrength");
    private static readonly int EnableTerrainCollisionId = Shader.PropertyToID("_EnableTerrainCollision");
    private static readonly int TerrainPositionId = Shader.PropertyToID("_TerrainPosition");
    private static readonly int TerrainSizeId = Shader.PropertyToID("_TerrainSize");
    private static readonly int TerrainHeightOffsetId = Shader.PropertyToID("_TerrainHeightOffset");
    private static readonly int TerrainHeightmapId = Shader.PropertyToID("_TerrainHeightmap");
    private static readonly int EnableStaticSdfCollisionId = Shader.PropertyToID("_EnableStaticSdfCollision");
    private static readonly int StaticSdfTextureId = Shader.PropertyToID("_StaticSdfTexture");
    private static readonly int StaticSdfWorldCenterId = Shader.PropertyToID("_StaticSdfWorldCenter");
    private static readonly int StaticSdfWorldSizeId = Shader.PropertyToID("_StaticSdfWorldSize");
    private static readonly int StaticSdfDistanceScaleId = Shader.PropertyToID("_StaticSdfDistanceScale");
    private static readonly int StaticSdfDistanceBiasId = Shader.PropertyToID("_StaticSdfDistanceBias");
    private static readonly int EnableEnvironmentDistanceFieldId = Shader.PropertyToID("_EnableEnvironmentDistanceField");
    private static readonly int EnvironmentDistanceFieldTextureId = Shader.PropertyToID("_EnvironmentDistanceFieldTexture");
    private static readonly int EnvironmentDistanceWorldCenterId = Shader.PropertyToID("_EnvironmentDistanceWorldCenter");
    private static readonly int EnvironmentDistanceWorldSizeId = Shader.PropertyToID("_EnvironmentDistanceWorldSize");
    private static readonly int EnvironmentDistanceDistanceScaleId = Shader.PropertyToID("_EnvironmentDistanceDistanceScale");
    private static readonly int EnvironmentDistanceDistanceBiasId = Shader.PropertyToID("_EnvironmentDistanceDistanceBias");
    private static readonly int CapsulePbdSampleCountId = Shader.PropertyToID("_CapsulePbdSampleCount");
    private static readonly int WorldToLocalRow0Id = Shader.PropertyToID("_WorldToLocalRow0");
    private static readonly int WorldToLocalRow1Id = Shader.PropertyToID("_WorldToLocalRow1");
    private static readonly int WorldToLocalRow2Id = Shader.PropertyToID("_WorldToLocalRow2");
    private static readonly int SquadStateBufferId = Shader.PropertyToID("_SquadStateBuffer");
    private static readonly int SquadAliveCountBufferId = Shader.PropertyToID("_SquadAliveCountBuffer");
    private static readonly int AgentSquadDataBufferId = Shader.PropertyToID("_AgentSquadDataBuffer");
    private static readonly int FormationSlotBufferId = Shader.PropertyToID("_FormationSlotBuffer");
    private static readonly int EnableRuntimeSquadAnchorsId = Shader.PropertyToID("_EnableRuntimeSquadAnchors");
    private static readonly int UseRuntimeFormationSlotsId = Shader.PropertyToID("_UseRuntimeFormationSlots");
    private static readonly int SquadStateCountId = Shader.PropertyToID("_SquadStateCount");
    private static readonly int AgentSquadDataCountId = Shader.PropertyToID("_AgentSquadDataCount");
    private static readonly int FormationSlotCountId = Shader.PropertyToID("_FormationSlotCount");
    private static readonly int CombatStateBufferId = Shader.PropertyToID("_CombatStateBuffer");
    private static readonly int TargetAcquisitionStateBufferId = Shader.PropertyToID("_TargetAcquisitionStateBuffer");
    private static readonly int TargetAcquisitionCandidateBufferId = Shader.PropertyToID("_TargetAcquisitionCandidateBuffer");
    private static readonly int TargetAcquisitionLosDispatchArgsBufferId = Shader.PropertyToID("_TargetAcquisitionLosDispatchArgsBuffer");
    private static readonly int SquadAcquisitionDispatchArgsBufferId = Shader.PropertyToID("_SquadAcquisitionDispatchArgsBuffer");
    private static readonly int CombatActiveStateBufferId = Shader.PropertyToID("_CombatActiveStateBuffer");
    private static readonly int CombatActivationMetaBufferId = Shader.PropertyToID("_CombatActivationMetaBuffer");
    private static readonly int CombatActiveInstanceIndexBufferId = Shader.PropertyToID("_CombatActiveInstanceIndexBuffer");
    private static readonly int CombatActiveInstanceCounterBufferId = Shader.PropertyToID("_CombatActiveInstanceCounterBuffer");
    private static readonly int CombatActiveInstanceIndexReadBufferId = Shader.PropertyToID("_CombatActiveInstanceIndexReadBuffer");
    private static readonly int CombatActiveInstanceCounterReadBufferId = Shader.PropertyToID("_CombatActiveInstanceCounterReadBuffer");
    private static readonly int CombatActiveInstanceDispatchArgsBufferId = Shader.PropertyToID("_CombatActiveInstanceDispatchArgsBuffer");
    private static readonly int CombatSquadCandidateBufferId = Shader.PropertyToID("_CombatSquadCandidateBuffer");
    private static readonly int CombatSquadCandidateCounterBufferId = Shader.PropertyToID("_CombatSquadCandidateCounterBuffer");
    private static readonly int CombatCandidateClusterWorkItemBufferId = Shader.PropertyToID("_CombatCandidateClusterWorkItemBuffer");
    private static readonly int AiDebugTargetIndexBufferId = Shader.PropertyToID("_AiDebugTargetIndexBuffer");
    private static readonly int AiDebugStageRecordBufferId = Shader.PropertyToID("_AiDebugStageRecordBuffer");
    private static readonly int AiDebugTargetCountId = Shader.PropertyToID("_AiDebugTargetCount");
    private static readonly int AiDebugStageId = Shader.PropertyToID("_AiDebugStageId");
    private static readonly int AiDebugFrameIndexId = Shader.PropertyToID("_AiDebugFrameIndex");
    private static readonly int AiDebugSolverIterationId = Shader.PropertyToID("_AiDebugSolverIteration");
    private static readonly int AiDebugStageWriteOffsetId = Shader.PropertyToID("_AiDebugStageWriteOffset");
    private static readonly int AiDebugCandidateCounterBufferId = Shader.PropertyToID("_AiDebugCandidateCounterBuffer");
    private static readonly int AiDebugCandidateRecordBufferId = Shader.PropertyToID("_AiDebugCandidateRecordBuffer");
    private static readonly int AiDebugCandidateCapacityId = Shader.PropertyToID("_AiDebugCandidateCapacity");
    private static readonly int AiDebugDiscoveryFrameIndexId = Shader.PropertyToID("_AiDebugDiscoveryFrameIndex");
    private static readonly int AiDebugDiscoveryFlagsId = Shader.PropertyToID("_AiDebugDiscoveryFlags");
    private static readonly int AiDebugDiscoveryCenterRadiusId = Shader.PropertyToID("_AiDebugDiscoveryCenterRadius");
    private static readonly int AiDebugDiscoveryBoxCenterId = Shader.PropertyToID("_AiDebugDiscoveryBoxCenter");
    private static readonly int AiDebugDiscoveryBoxExtentsId = Shader.PropertyToID("_AiDebugDiscoveryBoxExtents");
    private static readonly int AiDebugDiscoveryScreenRectId = Shader.PropertyToID("_AiDebugDiscoveryScreenRect");
    private static readonly int AiDebugDiscoveryWorldToClipRow0Id = Shader.PropertyToID("_AiDebugDiscoveryWorldToClipRow0");
    private static readonly int AiDebugDiscoveryWorldToClipRow1Id = Shader.PropertyToID("_AiDebugDiscoveryWorldToClipRow1");
    private static readonly int AiDebugDiscoveryWorldToClipRow2Id = Shader.PropertyToID("_AiDebugDiscoveryWorldToClipRow2");
    private static readonly int AiDebugDiscoveryWorldToClipRow3Id = Shader.PropertyToID("_AiDebugDiscoveryWorldToClipRow3");
    private static readonly int AiDebugDiscoverySquadId = Shader.PropertyToID("_AiDebugDiscoverySquadId");
    private static readonly int EnableGpuInstanceCombatId = Shader.PropertyToID("_EnableGpuInstanceCombat");
    private static readonly int CombatRangeId = Shader.PropertyToID("_CombatRange");
    private static readonly int CombatShotsPerSecondId = Shader.PropertyToID("_CombatShotsPerSecond");
    private static readonly int CombatShotIntervalJitterId = Shader.PropertyToID("_CombatShotIntervalJitter");
    private static readonly int CombatShotSpreadDegreesId = Shader.PropertyToID("_CombatShotSpreadDegrees");
    private static readonly int CombatHitRadiusPaddingId = Shader.PropertyToID("_CombatHitRadiusPadding");
    private static readonly int CombatHitHeightPaddingId = Shader.PropertyToID("_CombatHitHeightPadding");
    private static readonly int EnableCombatTerrainOcclusionId = Shader.PropertyToID("_EnableCombatTerrainOcclusion");
    private static readonly int CombatTerrainOcclusionSampleSpacingId = Shader.PropertyToID("_CombatTerrainOcclusionSampleSpacing");
    private static readonly int CombatTerrainOcclusionClearanceId = Shader.PropertyToID("_CombatTerrainOcclusionClearance");
    private static readonly int CombatEnvironmentOcclusionMaxStepsId = Shader.PropertyToID("_CombatEnvironmentOcclusionMaxSteps");
    private static readonly int CombatOriginHeightId = Shader.PropertyToID("_CombatOriginHeight");
    private static readonly int CombatTargetHeightId = Shader.PropertyToID("_CombatTargetHeight");
    private static readonly int CombatMuzzleFlashDecayId = Shader.PropertyToID("_CombatMuzzleFlashDecay");
    private static readonly int CombatImpactFlashDurationId = Shader.PropertyToID("_CombatImpactFlashDuration");
    private static readonly int CombatMaxHealthId = Shader.PropertyToID("_CombatMaxHealth");
    private static readonly int CombatDamagePerHitId = Shader.PropertyToID("_CombatDamagePerHit");
    private static readonly int CombatHitFlashDecayId = Shader.PropertyToID("_CombatHitFlashDecay");
    private static readonly int CombatOccludedTargetRetryDelayId = Shader.PropertyToID("_CombatOccludedTargetRetryDelay");
    private static readonly int CombatNoTargetRetryDelayId = Shader.PropertyToID("_CombatNoTargetRetryDelay");
    private static readonly int TargetAcquisitionSearchIntervalMinId = Shader.PropertyToID("_TargetAcquisitionSearchIntervalMin");
    private static readonly int TargetAcquisitionSearchIntervalMaxId = Shader.PropertyToID("_TargetAcquisitionSearchIntervalMax");
    private static readonly int TargetAcquisitionFovCosineId = Shader.PropertyToID("_TargetAcquisitionFovCosine");
    private static readonly int TargetAcquisitionMaxCandidateChecksId = Shader.PropertyToID("_TargetAcquisitionMaxCandidateChecks");
    private static readonly int TargetAcquisitionCurrentTargetBonusId = Shader.PropertyToID("_TargetAcquisitionCurrentTargetBonus");
    private static readonly int TargetAcquisitionLastAttackerBonusId = Shader.PropertyToID("_TargetAcquisitionLastAttackerBonus");
    private static readonly int TargetAcquisitionLockDurationId = Shader.PropertyToID("_TargetAcquisitionLockDuration");
    private static readonly int TargetAcquisitionLostSightGraceId = Shader.PropertyToID("_TargetAcquisitionLostSightGrace");
    private static readonly int TargetAcquisitionLineOfSightRecheckIntervalId = Shader.PropertyToID("_TargetAcquisitionLineOfSightRecheckInterval");
    private static readonly int TargetAcquisitionDistanceScoreWeightId = Shader.PropertyToID("_TargetAcquisitionDistanceScoreWeight");
    private static readonly int TargetAcquisitionViewScoreWeightId = Shader.PropertyToID("_TargetAcquisitionViewScoreWeight");
    private static readonly int CombatCandidateCapacityPerSquadId = Shader.PropertyToID("_CombatCandidateCapacityPerSquad");
    private static readonly int CombatActiveHoldTimeId = Shader.PropertyToID("_CombatActiveHoldTime");
    private static readonly int CombatActiveProbeIntervalFramesId = Shader.PropertyToID("_CombatActiveProbeIntervalFrames");
    private static readonly int SimulationFrameIndexId = Shader.PropertyToID("_SimulationFrameIndex");
    private static readonly int AliveInstanceIndexBufferId = Shader.PropertyToID("_AliveInstanceIndexBuffer");
    private static readonly int AliveInstanceCounterBufferId = Shader.PropertyToID("_AliveInstanceCounterBuffer");
    private static readonly int AliveInstanceDispatchArgsBufferId = Shader.PropertyToID("_AliveInstanceDispatchArgsBuffer");
    private static readonly int AliveInstanceIndexReadBufferId = Shader.PropertyToID("_AliveInstanceIndexReadBuffer");
    private static readonly int AliveInstanceCounterReadBufferId = Shader.PropertyToID("_AliveInstanceCounterReadBuffer");
    private static readonly int PhysicsActiveStateBufferId = Shader.PropertyToID("_PhysicsActiveStateBuffer");
    private static readonly int PhysicsActivationMetaBufferId = Shader.PropertyToID("_PhysicsActivationMetaBuffer");
    private static readonly int PhysicsActiveInstanceIndexBufferId = Shader.PropertyToID("_PhysicsActiveInstanceIndexBuffer");
    private static readonly int PhysicsActiveInstanceCounterBufferId = Shader.PropertyToID("_PhysicsActiveInstanceCounterBuffer");
    private static readonly int PhysicsActiveInstanceIndexReadBufferId = Shader.PropertyToID("_PhysicsActiveInstanceIndexReadBuffer");
    private static readonly int PhysicsActiveInstanceCounterReadBufferId = Shader.PropertyToID("_PhysicsActiveInstanceCounterReadBuffer");
    private static readonly int PhysicsActiveInstanceDispatchArgsBufferId = Shader.PropertyToID("_PhysicsActiveInstanceDispatchArgsBuffer");
    private static readonly int WakeGridCounterBufferId = Shader.PropertyToID("_WakeGridCounterBuffer");
    private static readonly int WakeGridOccupantBufferId = Shader.PropertyToID("_WakeGridOccupantBuffer");
    private static readonly int WakeGridDimId = Shader.PropertyToID("_WakeGridDim");
    private static readonly int WakeGridCellCountId = Shader.PropertyToID("_WakeGridCellCount");
    private static readonly int WakeGridCellSizeId = Shader.PropertyToID("_WakeGridCellSize");
    private static readonly int WakeInvGridCellSizeId = Shader.PropertyToID("_WakeInvGridCellSize");
    private static readonly int PhysicsActivationPassId = Shader.PropertyToID("_PhysicsActivationPass");
    private static readonly int PhysicsWakeSpeedId = Shader.PropertyToID("_PhysicsWakeSpeed");
    private static readonly int PhysicsSleepSpeedId = Shader.PropertyToID("_PhysicsSleepSpeed");
    private static readonly int PhysicsWakeAnchorErrorId = Shader.PropertyToID("_PhysicsWakeAnchorError");
    private static readonly int PhysicsSleepAnchorErrorId = Shader.PropertyToID("_PhysicsSleepAnchorError");
    private static readonly int PhysicsWakeNeighborRadiusId = Shader.PropertyToID("_PhysicsWakeNeighborRadius");
    private static readonly int PhysicsActiveHoldTimeId = Shader.PropertyToID("_PhysicsActiveHoldTime");
    private static readonly int PhysicsSleepDelayId = Shader.PropertyToID("_PhysicsSleepDelay");
    private static readonly int VisibleRuntimeSquadMaskBufferId = Shader.PropertyToID("_VisibleRuntimeSquadMaskBuffer");
    private static readonly int RuntimeSquadBoundsBufferId = Shader.PropertyToID("_RuntimeSquadBoundsBuffer");
    private static readonly int VisibleRuntimeSquadCountId = Shader.PropertyToID("_VisibleRuntimeSquadCount");
    private static readonly int VisibleUnassignedInstancesId = Shader.PropertyToID("_VisibleUnassignedInstances");
    private static readonly int VisibleInstanceCounterBufferId = Shader.PropertyToID("_VisibleInstanceCounterBuffer");
    private static readonly int VisibleLod1InstanceIndicesId = Shader.PropertyToID("_VisibleLod1InstanceIndices");
    private static readonly int VisibleLod1InstanceCounterBufferId = Shader.PropertyToID("_VisibleLod1InstanceCounterBuffer");
    private static readonly int VisibleLod2InstanceIndicesId = Shader.PropertyToID("_VisibleLod2InstanceIndices");
    private static readonly int VisibleLod2InstanceCounterBufferId = Shader.PropertyToID("_VisibleLod2InstanceCounterBuffer");
    private static readonly int VisibleRenderArgsBufferId = Shader.PropertyToID("_VisibleRenderArgsBuffer");
    private static readonly int VisibleRenderArgsIndexCountId = Shader.PropertyToID("_VisibleRenderArgsIndexCount");
    private static readonly int VisibleRenderArgsStartIndexId = Shader.PropertyToID("_VisibleRenderArgsStartIndex");
    private static readonly int VisibleRenderArgsBaseVertexId = Shader.PropertyToID("_VisibleRenderArgsBaseVertex");
    private static readonly int HasVisibleFrustumPlanesId = Shader.PropertyToID("_HasVisibleFrustumPlanes");
    private static readonly int VisibleBoundsCenterId = Shader.PropertyToID("_VisibleBoundsCenter");
    private static readonly int VisibleBoundsExtentsId = Shader.PropertyToID("_VisibleBoundsExtents");
    private static readonly int VisibleInstanceBoundsRadiusId = Shader.PropertyToID("_VisibleInstanceBoundsRadius");
    private static readonly int VisibleFrustumPlane0Id = Shader.PropertyToID("_VisibleFrustumPlane0");
    private static readonly int VisibleFrustumPlane1Id = Shader.PropertyToID("_VisibleFrustumPlane1");
    private static readonly int VisibleFrustumPlane2Id = Shader.PropertyToID("_VisibleFrustumPlane2");
    private static readonly int VisibleFrustumPlane3Id = Shader.PropertyToID("_VisibleFrustumPlane3");
    private static readonly int VisibleFrustumPlane4Id = Shader.PropertyToID("_VisibleFrustumPlane4");
    private static readonly int VisibleFrustumPlane5Id = Shader.PropertyToID("_VisibleFrustumPlane5");
    private static readonly int VisibleCameraPositionId = Shader.PropertyToID("_VisibleCameraPosition");
    private static readonly int VisibleLodTierCountId = Shader.PropertyToID("_VisibleLodTierCount");
    private static readonly int VisibleLod1StartDistanceId = Shader.PropertyToID("_VisibleLod1StartDistance");
    private static readonly int VisibleLod2StartDistanceId = Shader.PropertyToID("_VisibleLod2StartDistance");
    private const string DefaultShaderName = "Project/Crowd/VATIndirectLit";
    private const string DefaultTertiaryLodShaderName = "Project/Crowd/VATIndirectSimple";
    private const string DefaultCombatTracerShaderName = "Project/Crowd/VATCombatTracer";
#if UNITY_EDITOR
    private const string DefaultCombatFxTextureFolder = "Assets/Project/Textures/Crowds/CombatFx";
    private const string DefaultCombatMuzzleFlashTexturePath = "Assets/Project/Textures/Crowds/CombatFx/qiangkou.png";
    private const string DefaultCombatTracerTexturePath = "Assets/Project/Textures/Crowds/CombatFx/子弹曳光.png";
    private const string DefaultCombatImpactTexturePath = "Assets/Project/Textures/Crowds/CombatFx/子弹命中——雾.png";
    private const string DefaultCombatMuzzleFlashTextureSearchFilter = "qiangkou";
    private const string DefaultCombatTracerTextureSearchFilter = "子弹曳光";
    private const string DefaultCombatImpactTextureSearchFilter = "子弹命中";
#endif
    private const string BuildAliveInstanceListKernelName = "BuildAliveInstanceList";
    private const string BuildAliveDispatchArgsKernelName = "BuildAliveDispatchArgs";
    private const string ClearWakeGridKernelName = "ClearWakeGrid";
    private const string BuildWakeGridKernelName = "BuildWakeGrid";
    private const string EvaluatePhysicsActiveKernelName = "EvaluatePhysicsActive";
    private const string BuildPhysicsActiveInstanceListKernelName = "BuildPhysicsActiveInstanceList";
    private const string BuildPhysicsActiveDispatchArgsKernelName = "BuildPhysicsActiveDispatchArgs";
    private const string EvaluateCombatActiveKernelName = "EvaluateCombatActive";
    private const string BuildCombatActiveInstanceListKernelName = "BuildCombatActiveInstanceList";
    private const string CompactCombatActiveInstanceListKernelName = "CompactCombatActiveInstanceList";
    private const string BuildCombatActiveDispatchArgsKernelName = "BuildCombatActiveDispatchArgs";
    private const string ClearCombatSquadCandidateCountersKernelName = "ClearCombatSquadCandidateCounters";
    private const string ClearVisibleRuntimeSquadBoundsKernelName = "ClearVisibleRuntimeSquadBounds";
    private const string BuildVisibleRuntimeSquadBoundsKernelName = "BuildVisibleRuntimeSquadBounds";
    private const string CullVisibleRuntimeSquadsKernelName = "CullVisibleRuntimeSquads";
    private const string BuildVisibleRuntimeInstanceListKernelName = "BuildVisibleRuntimeInstanceList";
    private const string BuildVisibleIndirectArgsKernelName = "BuildVisibleIndirectArgs";
    private const string PredictKernelName = "PredictInstances";
    private const string BuildGridClearDispatchArgsKernelName = "BuildGridClearDispatchArgs";
    private const string ClearGridKernelName = "ClearGrid";
    private const string BuildGridKernelName = "BuildGrid";
    private const string BuildSpatialElementsKernelName = "BuildSpatialElements";
    private const string SolveCrowdKernelName = "SolveCrowdCollisions";
    private const string BuildCombatCandidateClustersKernelName = "BuildCombatCandidateClusters";
    private const string ResolveTargetAcquisitionKernelName = "ResolveTargetAcquisition";
    private const string BuildTargetAcquisitionLosDispatchArgsKernelName = "BuildTargetAcquisitionLosDispatchArgs";
    private const string ResolveTargetAcquisitionLineOfSightKernelName = "ResolveTargetAcquisitionLineOfSight";
    private const string BuildSquadAcquisitionDispatchArgsKernelName = "BuildSquadAcquisitionDispatchArgs";
    private const string EvaluateSquadAcquisitionCandidatesKernelName = "EvaluateSquadAcquisitionCandidates";
    private const string FinalizeTargetAcquisitionKernelName = "FinalizeTargetAcquisition";
    private const string ResolveInstanceCombatKernelName = "ResolveInstanceCombat";
    private const string ClearSpatialQueriesKernelName = "ClearSpatialQueryResults";
    private const string ResolveSpatialQueriesKernelName = "ResolveSpatialQueries";
    private const string FinalizeKernelName = "FinalizeInstances";
    private const string CaptureAiDebugGpuStageKernelName = "CaptureAiDebugGpuStage";
    private const string DiscoverAiDebugGpuCandidatesKernelName = "DiscoverAiDebugGpuCandidates";
    private const int ThreadGroupSize = 64;
    private const int TargetAcquisitionTopK = 3;
    private const int TargetAcquisitionCandidateSlots = TargetAcquisitionTopK + 1;
    private const int CombatCandidateClusterCellsPerTile = 8;
    private const int CombatCandidateCapacityPerSquadMax = 512;
    private const int SpatialQueryThreadGroupSize = 8;
    private const int GridBuildModePhysicsActiveOnly = 0;
    private const int GridBuildModeAllQueryables = 1;
    private const int DebugCharacterInteractionQueryId = -10001;
    private const int DebugCustomInteractionQueryIdBase = -11000;
    private const int SquadAliveCountReadbackIntervalFrames = 6;
    private const int InvalidClipIndex = -1;
    private const float DefaultInstanceAnimationTransitionDuration = 0.2f;
    private static readonly uint[] AliveInstanceCounterResetData = { 0u };
    private static readonly uint[] CombatActiveInstanceCounterResetData = { 0u, 0u };
    private static readonly uint[] AliveInstanceDispatchArgsResetData = { 1u, 1u, 1u };
    private static readonly uint[] EmptyDispatchArgsResetData = { 0u, 1u, 1u };
    private static readonly uint[] VisibleInstanceCounterResetData = { 0u };
    private const uint InstanceCombatFlagHasTarget = 1u << 0;
    private const uint InstanceCombatFlagHasLineOfSight = 1u << 1;
    private const uint InstanceCombatFlagFiredThisFrame = 1u << 2;
    private const uint InstanceCombatFlagDead = 1u << 4;
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    private struct InstanceSpawnData
    {
        public Vector3 localPosition;
        public float yawRadians;
        public float uniformScale;
        public float normalizedTimeOffset;
        public float playbackSpeedMultiplier;
        public uint faction;
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    private struct InstanceSimulationState
    {
        public Vector4 localPositionAndYaw;
        public Vector4 scaleAndVelocity;
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    private struct InteractionSphereData
    {
        public Vector4 localCenterAndRadius;
        public Vector4 parameters;
    }

    [Serializable]
    private struct InteractionSphere
    {
        [InspectorName("目标")]
        [Tooltip("作为驱散球中心的 Transform。运行时会读取它的位置并转换到 renderer 本地空间。")]
        public Transform target;

        [InspectorName("半径")]
        [Tooltip("驱散球半径，单位为世界空间米。只影响 XZ 平面上的近场推动。")]
        public float radius;

        [InspectorName("强度")]
        [Tooltip("驱散球推开人群的强度。值越大，进入半径内的实例每帧被推开的距离越明显。")]
        public float strength;
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    private struct MatrixRows
    {
        public Vector4 row0;
        public Vector4 row1;
        public Vector4 row2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AnimationClipGpuData
    {
        public int startFrame;
        public int frameCount;
        public float lengthSeconds;
        public int loop;
    }

    private struct InstanceAnimationStateCpuData
    {
        public int currentClipIndex;
        public int nextClipIndex;
        public float currentClipTime;
        public float nextClipTime;
        public float transitionElapsed;
        public float transitionDuration;
        public float playbackSpeedMultiplier;
        public bool isBlending;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InstanceAnimationStateGpuData
    {
        public int currentClipIndex;
        public int nextClipIndex;
        public int isBlending;
        public int padding0;
        public float currentClipTime;
        public float nextClipTime;
        public float transitionElapsed;
        public float transitionDuration;
    }

    private struct SpawnAreaContext
    {
        public Vector2 size;
        public Vector2 centerXZ;
    }

    private struct RuntimeRenderResource
    {
        public CrowdVatAnimationAsset animationAsset;
        public CrowdVatPlayer templatePrefab;
        public Mesh mesh;
        public Material[] runtimeMaterials;
        public Vector4 crowdRootRow0;
        public Vector4 crowdRootRow1;
        public Vector4 crowdRootRow2;
        public Bounds agentLocalBounds;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpatialElementData
    {
        public Vector4 localCapsuleStartAndRadius;
        public Vector4 localCapsuleEndAndHeight;
        public uint ownerIndex;
        public uint targetMask;
        public uint flags;
        public uint faction;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpatialQueryData
    {
        public Vector4 localStartAndRadius;
        public Vector4 localEndAndPadding;
        public uint queryId;
        public uint targetMask;
        public uint factionMask;
        public uint shape;
        public uint flags;
        public uint maxHits;
        public uint padding0;
        public uint padding1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpatialQueryResultData
    {
        public uint queryId;
        public uint totalHits;
        public uint writtenHits;
        public uint overflow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpatialQueryHitData
    {
        public uint queryId;
        public uint ownerIndex;
        public uint targetMask;
        public uint flags;
        public uint faction;
        public float normalizedDistance;
        public uint padding0;
        public uint padding1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RuntimeSquadStateGpuData
    {
        public Vector4 localCenterAndAnchorBlend;
        public Vector4 localForwardAndMoveSpeed;
        public Vector4 localTargetAndSpacingX;
        public Vector4 metadata0;
        public Vector4 metadata1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RuntimeAgentSquadDataGpuData
    {
        public Vector4 squadAndSlotAndRoleAndFlags;
        public Vector4 slotOffsetAndWeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RuntimeFormationSlotGpuData
    {
        public Vector4 localOffsetAndRoleAndRank;
        public Vector4 preferredDistanceAndPadding;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RuntimeSquadBoundsGpuData
    {
        public uint minX;
        public uint minY;
        public uint minZ;
        public uint maxX;
        public uint maxY;
        public uint maxZ;
        public uint validCount;
        public uint padding0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InstanceCombatStateData
    {
        public Vector4 localOriginAndDistance;
        public Vector4 localTargetAndCooldown;
        public Vector4 localImpactNormalAndHit;
        public Vector4 healthAndHitFeedback;
        public float muzzleFlash;
        public int targetIndex;
        public uint flags;
        public uint debugShotInfoPacked;
        public Vector4 debugShotTraceMeta;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TargetAcquisitionStateData
    {
        public Vector4 lastKnownTargetAndScore;
        public Vector4 timers;
        public Vector4 debugRejectCounts0;
        public Vector4 debugRejectCounts1;
        public int currentTargetIndex;
        public int lastAttackerIndex;
        public uint flags;
        public int debugVisibleSampleIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TargetAcquisitionCandidateData
    {
        public Vector4 targetLocalAndScore;
        public float targetDistance;
        public int targetIndex;
        public uint rejectReason;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CombatSquadCandidateData
    {
        public uint instanceIndex;
        public uint faction;
        public Vector4 targetLocalAndScale;
        public float distanceToSourceBounds;
        public uint padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CombatCandidateClusterWorkItemData
    {
        public uint sourceSquadIndex;
        public uint tileBegin;
        public uint cellWidth;
        public uint cellCount;
        public int minCoordX;
        public int minCoordY;
        public uint padding0;
        public uint padding1;
    }

    [Header("资源引用")]
    [Tooltip("推荐直接拖入已生成的 Qianxia crowd VAT prefab 根节点，用来复用动画资产、材质来源和渲染根节点偏移。")]
    [SerializeField] private CrowdVatPlayer _templatePrefab;
    [Tooltip("也可以直接指定动画资产；如果同时指定了模板 prefab，会优先使用模板上的资源。")]
    [SerializeField] private CrowdVatAnimationAsset _animationAsset;
    [Tooltip("第二档距离 LOD 使用的 VAT prefab。未指定时不启用第二档。")]
    [SerializeField] private CrowdVatPlayer _secondaryLodTemplatePrefab;
    [Tooltip("也可以直接指定第二档动画资产；如果同时指定了模板 prefab，会优先使用模板上的资源。")]
    [SerializeField] private CrowdVatAnimationAsset _secondaryLodAnimationAsset;
    [Tooltip("第三档距离 LOD 使用的 VAT prefab。未指定时不启用第三档。")]
    [SerializeField] private CrowdVatPlayer _tertiaryLodTemplatePrefab;
    [Tooltip("也可以直接指定第三档动画资产；如果同时指定了模板 prefab，会优先使用模板上的资源。")]
    [SerializeField] private CrowdVatAnimationAsset _tertiaryLodAnimationAsset;
    [Tooltip("负责写入实例矩阵、动画帧和近场求解状态的 Compute Shader。")]
    [SerializeField] private ComputeShader _updateCompute;
    [Tooltip("Indirect 版本的人群 Shader。留空时会尝试按默认名字查找。")]
    [SerializeField] private Shader _indirectShader;
    [Tooltip("第三档距离 LOD 专用的人群 Shader。留空时默认查找 VATIndirectSimple；未找到时回退到主 indirect shader。")]
    [SerializeField] private Shader _tertiaryLodIndirectShader;
    [Tooltip("可选的 indirect 材质模板；留空时运行时会复制一份默认材质。")]
    [SerializeField] private Material _indirectMaterialTemplate;
    [Tooltip("默认播放的 clip 名称。留空时回退到动画资产里的第一个 clip。")]
    [SerializeField] private string _clipName;

    [Header("实例布局")]
    [InspectorName("实例数量")]
    [Tooltip("人群实例总数。双阵营模式下会自动平均拆到 A / B 两边。")]
    [SerializeField] [Min(1)] private int _instanceCount = 1024;
    [InspectorName("铺满整个地形")]
    [Tooltip("启用后，如果存在可用 Terrain，则出生点会分布到整个地形范围，而不是只落在局部矩形区域。")]
    [SerializeField] private bool _distributeAcrossWholeTerrain = true;
    [InspectorName("出生区域尺寸")]
    [Tooltip("单阵营模式下的整体出生区域尺寸；双阵营模式下会在此基础上继续拆分给两边。X 对应宽度，Y 对应深度。")]
    [SerializeField] private Vector2 _areaSize = new Vector2(48.0f, 48.0f);
    [InspectorName("出生扰动")]
    [Tooltip("出生点在各自网格单元内的随机扰动强度。0 表示整齐排布，1 表示尽量打散。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _cellJitter = 0.7f;
    [InspectorName("基础高度偏移")]
    [Tooltip("出生点基础高度偏移。若开启地形贴地，会在生成后再根据地形重新修正。")]
    [SerializeField] private float _heightOffset;
    [InspectorName("随机种子")]
    [Tooltip("出生布局随机种子。固定后可以稳定复现同一套人群分布。")]
    [SerializeField] private int _randomSeed = 12345;

    [Header("Scene Query 静态世界")]
    [Tooltip("可选的 Scene Query 字段资产。可统一提供 Ground / Walkable / Static SDF 数据；未提供的部分会回退到本组件上的旧字段。")]
    [SerializeField] private CrowdVatSceneQueryFieldAsset _sceneQueryFieldAsset;
    [Tooltip("参与 Environment SDF 烘焙的场景 Collider 列表。战斗遮挡现在只使用 Environment SDF，不再使用 static ray。")]
    [SerializeField] private Collider[] _environmentSdfObstacleColliders = Array.Empty<Collider>();

    [Header("Terrain 引用 / 诊断")]
    [Tooltip("旧 Terrain 解析开关。人物运行时不再通过 GPU Terrain heightmap 贴地；Terrain 仅保留给初始化、诊断和战斗遮挡等非 SDF 主链用途。")]
    [SerializeField] private bool _enableTerrainCollision = true;
    [Tooltip("未手动指定 Terrain 时，自动使用场景里的 Terrain 作为参考来源。")]
    [SerializeField] private bool _autoResolveTerrain = true;
    [Tooltip("参考 Terrain。人物运行时高度校正以 baked/environment SDF 或 static SDF 为准。")]
    [SerializeField] private Terrain _terrain;
    [Tooltip("Terrain 参考高度偏移，仅用于兼容路径与诊断；SDF 主链不依赖它。")]
    [SerializeField] private float _terrainHeightOffset = 0.0f;
    [Tooltip("启用后，额外使用 3D static SDF 体积参与 capsule PBD 碰撞投影。")]
    [SerializeField] private bool _enableStaticSdfCollision;
    [Tooltip("静态场景的 3D SDF 贴图。当前按轴对齐世界体积采样。")]
    [SerializeField] private Texture3D _staticSdfTexture;
    [Tooltip("static SDF 体积的世界空间中心。")]
    [SerializeField] private Vector3 _staticSdfWorldCenter = new Vector3(0.0f, 1.0f, 0.0f);
    [Tooltip("static SDF 体积的世界空间尺寸。")]
    [SerializeField] private Vector3 _staticSdfWorldSize = new Vector3(8.0f, 4.0f, 8.0f);
    [Tooltip("SDF 采样值到世界距离的缩放。默认假设贴图里直接存世界单位距离。")]
    [SerializeField] private float _staticSdfDistanceScale = 1.0f;
    [Tooltip("SDF 采样值到世界距离的偏移。可用于适配非零等值面的距离场。")]
    [SerializeField] private float _staticSdfDistanceBias = 0.0f;

    [Header("阵营布局")]
    [InspectorName("阵营布局模式")]
    [Tooltip("双阵营模式会把人群自动切成 A / B 两边，作为后续射击、索敌和阵营判定的基础。")]
    [SerializeField] private CrowdVatFactionLayoutMode _factionLayout = CrowdVatFactionLayoutMode.TwoOpposingFactions;
    [InspectorName("阵营分割轴")]
    [Tooltip("Depth 表示沿 Z 轴前后对阵，Width 表示沿 X 轴左右对阵。")]
    [SerializeField] private CrowdVatFactionSplitAxis _factionSplitAxis = CrowdVatFactionSplitAxis.Depth;
    [InspectorName("阵营中线间隔")]
    [Tooltip("两个阵营中心之间的中线宽度，后续可以直接拿来放掩体或留出交战走廊。")]
    [SerializeField] [Min(0.0f)] private float _factionCenterGap = 6.0f;
    [InspectorName("朝向敌方阵营")]
    [Tooltip("开启后，两边默认朝向对方阵营，便于后续直接接射击弹道与目标判定。")]
    [SerializeField] private bool _orientTowardEnemyFaction = true;
    [InspectorName("手动指定双方中心")]
    [Tooltip("启用后，CampA / CampB 的位置直接由下面两个中心点控制，不再由 Split Axis 和 Center Gap 自动推导。")]
    [SerializeField] private bool _useExplicitFactionCenters;
    [InspectorName("CampA 中心 XZ")]
    [Tooltip("CampA 的中心点，使用 CrowdVatIndirectRenderer 本地空间的 XZ 坐标。X 表示左右，Y 表示前后(Z)。")]
    [SerializeField] private Vector2 _campACenterXZ = new Vector2(0.0f, -6.0f);
    [InspectorName("CampB 中心 XZ")]
    [Tooltip("CampB 的中心点，使用 CrowdVatIndirectRenderer 本地空间的 XZ 坐标。X 表示左右，Y 表示前后(Z)。")]
    [SerializeField] private Vector2 _campBCenterXZ = new Vector2(0.0f, 6.0f);
    [InspectorName("使用空物体控制阵营")]
    [Tooltip("启用后，CampA / CampB 的中心和初始生成范围优先由控制空物体与四个角点空物体驱动；这只是出生分布，不限制后续运行时移动。")]
    [SerializeField] private bool _useFactionControlTransforms = true;
    [InspectorName("CampA 控制空物体")]
    [Tooltip("推荐使用 CrowdVatIndirectRenderer 的子物体。直接移动它即可调整 CampA 中心，展开后拖四个角点即可修改 CampA 的初始生成范围，不限制后续运行时移动。")]
    [SerializeField] private Transform _campAControlTransform;
    [InspectorName("CampB 控制空物体")]
    [Tooltip("推荐使用 CrowdVatIndirectRenderer 的子物体。直接移动它即可调整 CampB 中心，展开后拖四个角点即可修改 CampB 的初始生成范围，不限制后续运行时移动。")]
    [SerializeField] private Transform _campBControlTransform;
    [SerializeField] [HideInInspector] private bool _useExplicitFactionAreaSizes;
    [SerializeField] [HideInInspector] private Vector2 _campAAreaSize = new Vector2(48.0f, 21.0f);
    [SerializeField] [HideInInspector] private Vector2 _campBAreaSize = new Vector2(48.0f, 21.0f);
    [SerializeField] [HideInInspector] private Transform _campANorthWestCornerTransform;
    [SerializeField] [HideInInspector] private Transform _campANorthEastCornerTransform;
    [SerializeField] [HideInInspector] private Transform _campASouthEastCornerTransform;
    [SerializeField] [HideInInspector] private Transform _campASouthWestCornerTransform;
    [SerializeField] [HideInInspector] private Transform _campBNorthWestCornerTransform;
    [SerializeField] [HideInInspector] private Transform _campBNorthEastCornerTransform;
    [SerializeField] [HideInInspector] private Transform _campBSouthEastCornerTransform;
    [SerializeField] [HideInInspector] private Transform _campBSouthWestCornerTransform;
    [Header("角色尺度")]
    [Tooltip("所有角色的基础统一缩放。")]
    [SerializeField] [Min(0.01f)] private float _baseScale = 1.0f;
    [Tooltip("单个角色在基础缩放上的随机波动范围，用来打散整齐感。")]
    [SerializeField] private Vector2 _scaleMultiplierRange = new Vector2(0.95f, 1.05f);

    [Header("动画随机化")]
    [Tooltip("组件启用时自动开始播放默认动画。")]
    [SerializeField] private bool _playOnEnable = true;
    [Tooltip("是否强制把当前 clip 当作循环动画播放。")]
    [SerializeField] private bool _loopOverride = true;
    [Tooltip("整个人群的基础播放速度。")]
    [SerializeField] [Min(0.0f)] private float _playbackSpeed = 1.0f;
    [Tooltip("单个角色播放速度的随机范围。")]
    [SerializeField] private Vector2 _playbackSpeedMultiplierRange = new Vector2(0.9f, 1.1f);
    [Tooltip("起始帧时间偏移的随机强度。1 表示尽量打散不同角色的动画相位。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _normalizedStartOffsetRandom = 1.0f;

    [Header("空间查询 / PBD 近似")]
    [InspectorName("启用近似碰撞")]
    [Tooltip("启用 GPU 侧近似碰撞、空间网格、PBD 分离和空间查询。关闭后人群实例不会做人和人、环境近似分离。")]
    [SerializeField] private bool _enableApproximateCollision = true;
    [InspectorName("碰撞半径")]
    [Tooltip("人和人自碰撞使用的 XZ 平面圆盘半径，单位为本地空间米。数值越大，人与人之间间隔越宽。")]
    [SerializeField] [Min(0.01f)] private float _collisionRadius = 0.35f;
    [InspectorName("碰撞高度")]
    [Tooltip("角色胶囊高度，主要用于环境约束、空间查询和战斗命中代理，不直接参与人和人的 XZ 圆盘分离。")]
    [SerializeField] [Min(0.02f)] private float _collisionHeight = 1.65f;
    [InspectorName("网格尺寸")]
    [Tooltip("GPU broadphase 网格单元尺寸。应大于等于角色直径附近；太小会增加网格开销，太大可能让单格候选过多。")]
    [SerializeField] [Min(0.05f)] private float _queryCellSize = 0.8f;
    [InspectorName("模拟边界额外留白")]
    [Tooltip("GPU PBD / 空间网格在出生区域外额外保留的本地空间距离。小队自由半径较大或点击移动到边缘时，留白不足会把人压到边界上形成重叠。")]
    [SerializeField] [Min(0.0f)] private float _simulationBoundsPadding = 16.0f;
    [InspectorName("单格最大容量")]
    [Tooltip("每个网格单元最多记录多少个实例。密集人群下如果容量太小，会漏掉部分邻居、目标或空间查询候选。")]
    [SerializeField] [Min(4)] private int _maxCellOccupancy = 256;
    [InspectorName("最大空间查询数")]
    [Tooltip("每帧最多处理多少个外部空间查询请求。用于调试、选择、命中或其他系统查询人群代理。")]
    [SerializeField] [Min(1)] private int _maxSpatialQueries = 32;
    [InspectorName("每查询最大命中数")]
    [Tooltip("每个空间查询最多写回多少个命中结果。数值越大，结果越完整，但 GPU buffer 占用越高。")]
    [SerializeField] [Min(1)] private int _maxHitsPerSpatialQuery = 32;
    [InspectorName("PBD 迭代次数")]
    [Tooltip("人和人自碰撞 PBD 约束的求解轮数。每轮读取预测位置、投影圆盘约束，再进入下一轮；更高更稳定、更少互穿，但 GPU 成本更高。")]
    [SerializeField] [Min(1)] private int _solverIterations = 8;
    [InspectorName("胶囊采样数")]
    [Tooltip("环境 PBD 胶囊约束沿高度方向采样的点数。数值越高，对墙面、地面和障碍更稳定，但成本更高。")]
    [SerializeField] [Range(2, 8)] private int _capsulePbdSampleCount = 4;
    [InspectorName("自碰撞强度")]
    [Tooltip("人和人自碰撞位置投影强度。0 表示不推开，1 表示按本轮 PBD 逆质量约束结果完全分离。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _selfCollisionStrength = 1.0f;
    [InspectorName("自碰撞容差")]
    [Tooltip("人和人自碰撞的微小容差，单位为本地空间米。用于避免边缘接触时每帧来回纠正造成抖动。")]
    [SerializeField] [Min(0.0f)] private float _selfCollisionSlop = 0.015f;
    [InspectorName("自碰撞单步最大推开")]
    [Tooltip("人和人自碰撞每次 PBD 迭代最多能修正的距离，单位为本地空间米。密集重叠时调大，抖动时调小。")]
    [SerializeField] [Min(0.01f)] private float _selfCollisionMaxPushPerStep = 1.2f;
    [InspectorName("目标约束强度")]
    [Tooltip("预测阶段把行人位置拉向小队导航目标的软约束强度。值越大越坚定朝目标走，值越小越容易被人群和障碍挤开。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _goalStiffness = 0.2f;
    [InspectorName("启用局部避障")]
    [Tooltip("启用后，实例会在预测移动阶段读取附近人群，提前做轻量侧向调整；最终不穿透仍由 PBD 自碰撞负责。")]
    [SerializeField] private bool _enableLocalAvoidance = true;
    [InspectorName("避障感知半径")]
    [Tooltip("局部避障读取附近邻居的半径，单位为本地空间米。建议略大于角色直径，过大会增加 GPU 成本并让人群过早绕开。")]
    [SerializeField] [Min(0.05f)] private float _localAvoidanceRadius = 2.6f;
    [InspectorName("侧向避让强度")]
    [Tooltip("前方有人时产生侧向预避让速度的强度。0 表示不做侧向预避让。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _localAvoidanceStrength = 0.45f;
    [InspectorName("避障最大检查邻居")]
    [Tooltip("单个实例每帧局部避障最多处理的邻居数量上限。用于限制高密度区域的 GPU 成本。")]
    [SerializeField] [Min(1)] private int _localAvoidanceMaxNeighbors = 32;
    [InspectorName("速度阻尼")]
    [Tooltip("预测阶段对上一帧 PBD 反推速度的保留比例。PBD 求解结束后会用最终位置减去本帧旧位置重新反推速度，供下一帧惯性、朝向和局部避障使用。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _velocityDamping = 0.82f;
    [InspectorName("单步最大推开")]
    [Tooltip("交互球每帧最多能把实例推开多远，单位为本地空间米。用于限制玩家/交互源造成的瞬时位移。")]
    [SerializeField] [Min(0.01f)] private float _maxPushPerStep = 0.18f;
    [InspectorName("最大出生点偏移")]
    [Tooltip("静态模式下实例允许偏离出生点的最大距离。运行时小队移动不再使用编队槽位或保持距离投影。")]
    [SerializeField] [Min(0.01f)] private float _maxDisplacementFromSpawn = 1.25f;
    [InspectorName("非活跃回锚强度")]
    [Tooltip("实例处于 physics inactive 且没有显式小队移动时，回到静态出生锚点的强度。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _inactiveReturnStrength = 0.22f;
    [InspectorName("自动查找玩家控制器")]
    [Tooltip("自动查找场景中的 CharacterController，用于玩家交互球和调试引用。")]
    [SerializeField] private bool _autoResolveCharacterController = true;
    [InspectorName("玩家控制器")]
    [Tooltip("可选的玩家 CharacterController。主要用于玩家近场驱散球。")]
    [SerializeField] private CharacterController _characterController;
    [InspectorName("优先玩家名称")]
    [Tooltip("自动查找玩家控制器时优先匹配的对象名称。找不到时再回退到场景中的 CharacterController。")]
    [SerializeField] private string _preferredCharacterName = "QianxiaThirdPerson";
    [InspectorName("玩家参与驱散")]
    [Tooltip("启用后，会把玩家 CharacterController 转成一个 interaction sphere，用于近场推开人群；关闭时玩家不参与人群碰撞/驱散。")]
    [SerializeField] private bool _useCharacterAsInteractionSphere = false;
    [InspectorName("玩家驱散半径倍率")]
    [Tooltip("玩家驱散球半径相对角色碰撞半径的倍率。越大，人群越早被玩家推开。")]
    [SerializeField] [Min(0.1f)] private float _characterInteractionRadiusMultiplier = 2.2f;
    [InspectorName("玩家驱散强度")]
    [Tooltip("玩家驱散球推开人群的强度。0 表示玩家不推开人群。")]
    [SerializeField] [Min(0.0f)] private float _characterInteractionStrength = 0.14f;
    [InspectorName("额外交互球")]
    [Tooltip("可选的近场驱散源。第一版先用球体近似，只影响人群的 XZ 平面位置。")]
    [SerializeField] private InteractionSphere[] _interactionSpheres = Array.Empty<InteractionSphere>();

    [Header("Physics Active")]
    [Tooltip("物理 active 唤醒速度阈值。用于控制 physicsActive 进入近场碰撞/局部避让。")]
    [SerializeField] [Min(0.0f)] private float _physicsWakeSpeed = 0.22f;
    [Tooltip("物理 active 休眠速度阈值。低于该值并满足其他稳定条件后才允许回睡。")]
    [SerializeField] [Min(0.0f)] private float _physicsSleepSpeed = 0.08f;
    [Tooltip("偏离静态锚点或 runtime 编队槽位超过该距离时进入 physics active。")]
    [SerializeField] [Min(0.0f)] private float _physicsWakeAnchorError = 0.35f;
    [Tooltip("偏离锚点低于该距离才允许累计休眠时间。")]
    [SerializeField] [Min(0.0f)] private float _physicsSleepAnchorError = 0.16f;
    [Tooltip("局部传播唤醒半径。运行时会与 collisionRadius * 3 和 1.1 取最大值。")]
    [SerializeField] [Min(0.0f)] private float _physicsWakeNeighborRadius = 1.1f;
    [Tooltip("实例被命令、速度、偏移或碰撞修正唤醒后的最短保活时间。")]
    [SerializeField] [Min(0.0f)] private float _physicsActiveHoldTime = 0.20f;
    [Tooltip("稳定条件持续超过该时间后，physics active 才会回到 inactive。")]
    [SerializeField] [Min(0.0f)] private float _physicsSleepDelay = 0.18f;
    [Tooltip("WakeGrid 单元尺寸。运行时会与 queryCellSize 和 collisionRadius * 4 取最大值。")]
    [SerializeField] [Min(0.0f)] private float _physicsWakeGridCellSize = 0.0f;

    [Header("GPU Combat / 开枪")]
    [Tooltip("启用后，每个 GPU 实例都会独立索敌并维护自己的开火状态。")]
    [SerializeField] private bool _enableGpuInstanceCombat = true;
    [Tooltip("CombatActive 保活时间。已有目标、枪口火光或受击反馈会在这段时间内持续进入战斗工作集。")]
    [SerializeField] [Min(0.0f)] private float _combatActiveHoldTime = 0.35f;
    [Tooltip("无目标单位低频进入 CombatActive 做索敌探测的帧间隔。1 表示每帧全量探测，数值越大越省但初始发现目标越慢。")]
    [SerializeField] [Min(1)] private int _combatActiveProbeIntervalFrames = 8;
    [Tooltip("每个实例搜索敌对目标的最大距离。")]
    [SerializeField] [Min(0.1f)] private float _combatRange = 36.0f;
    [Tooltip("每个实例每秒最多开火多少次。0 表示只索敌不触发开火闪光。")]
    [SerializeField] [Min(0.0f)] private float _combatShotsPerSecond = 3.0f;
    [Tooltip("每个实例射击间隔的随机波动幅度。0 表示所有人严格同频，0.2 表示各自间隔会在基础值上下 20% 内浮动。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _combatShotIntervalJitter = 0.2f;
    [Tooltip("每次开枪的基础角度散布，单位为度。散布会改变真实子弹射线，因此可能打偏、打到前方其他人或先打到掩体。")]
    [SerializeField] [Min(0.0f)] private float _combatShotSpreadDegrees = 2.0f;
    [Tooltip("射击命中胶囊相对人群碰撞胶囊额外放大的半径。用于贴近角色可见轮廓，避免视觉 tracer 擦到角色但逻辑 miss。")]
    [SerializeField] [Min(0.0f)] private float _combatHitRadiusPadding = 0.18f;
    [Tooltip("射击命中胶囊相对人群碰撞胶囊额外增加的高度。用于覆盖头发、动作和 VAT 网格边界的视觉高度。")]
    [SerializeField] [Min(0.0f)] private float _combatHitHeightPadding = 0.2f;
    #pragma warning disable 0414
    [Tooltip("已废弃：Combat 遮挡不再使用 Terrain 高度图，保留字段仅用于兼容旧场景序列化。")]
    [SerializeField, HideInInspector] private bool _enableCombatTerrainOcclusion = true;
    [Tooltip("已废弃：Combat 遮挡不再使用 Terrain 高度图固定间距采样。")]
    [SerializeField, HideInInspector] [Min(0.1f)] private float _combatTerrainOcclusionSampleSpacing = 0.75f;
    #pragma warning restore 0414
    [Tooltip("Combat baked Environment SDF 遮挡的距离阈值。枪线距离环境表面低于该值时，视为被遮挡。")]
    [SerializeField] [Min(0.0f)] private float _combatTerrainOcclusionClearance = 0.25f;
    [Tooltip("Combat Environment SDF 遮挡射线最多 march 多少步。降低可显著压低索敌/弹道遮挡成本，但过低会漏掉细薄遮挡。")]
    [SerializeField] [Range(1, 96)] private int _combatEnvironmentOcclusionMaxSteps = 48;
    [Tooltip("枪线起点相对角色脚底抬升的高度。")]
    [SerializeField] [Min(0.0f)] private float _combatOriginHeight = 1.35f;
    [Tooltip("没有 VAT 渲染资源时的瞄准高度回退值。有 VAT 模型时，目标采样点使用 VAT MeshBounds 的本地顶部高度。")]
    [SerializeField] [Min(0.0f)] private float _combatTargetHeight = 1.35f;
    [Tooltip("开火闪光的衰减速度。")]
    [SerializeField] [Min(0.0f)] private float _combatMuzzleFlashDecay = 10.0f;
    [Tooltip("每个人群实例的最大血量。血量在 GPU combat 中维护，归零后才会死亡。")]
    [SerializeField] [Min(1.0f)] private float _combatMaxHealth = 100.0f;
    [Tooltip("每次真实命中人群实例时扣除的血量。")]
    [SerializeField] [Min(0.0f)] private float _combatDamagePerHit = 34.0f;
    [Tooltip("受击反馈缓存的衰减速度，仅用于状态读取和调试；人物本体不再闪红，命中表现由子弹命中贴图负责。")]
    [SerializeField] [Min(0.0f)] private float _combatHitFlashDecay = 8.0f;
    [Tooltip("缓存目标因遮挡失效后，等待多久再允许重新做一次完整索敌。用于压住掩体后的高频重扫。")]
    [SerializeField] [Min(0.0f)] private float _combatOccludedTargetRetryDelay = 0.08f;
    [Tooltip("完整索敌后仍没找到可见目标时，等待多久再重试下一轮索敌。")]
    [SerializeField] [Min(0.0f)] private float _combatNoTargetRetryDelay = 0.22f;
    [Tooltip("近处 AI 完整搜索目标的最短间隔。目标锁定期间只做轻量校验，不做完整重扫。")]
    [SerializeField] [Min(0.02f)] private float _targetAcquisitionSearchIntervalMin = 0.2f;
    [Tooltip("近处 AI 完整搜索目标的最长间隔。每个实例会用稳定随机数落在最短/最长之间，避免同帧扎堆。")]
    [SerializeField] [Min(0.02f)] private float _targetAcquisitionSearchIntervalMax = 0.5f;
    [Tooltip("目标必须落在该水平视野角内才会被选中。")]
    [SerializeField] [Range(1.0f, 360.0f)] private float _targetAcquisitionFovDegrees = 140.0f;
    [Tooltip("单次完整索敌最多检查多少个 grid 候选。数值越大越稳，但密集人群下 GPU 成本越高。")]
    [SerializeField] [Min(1)] private int _targetAcquisitionMaxCandidateChecks = 48;
    [Tooltip("每个 squad broadphase 候选池容量。该池是保守超集，最终目标仍由每个 agent 按视野、距离、遮挡和评分决定。")]
    [SerializeField] [Range(1, CombatCandidateCapacityPerSquadMax)] private int _combatCandidateCapacityPerSquad = 512;
    [Tooltip("当前目标参与评分时的额外加分，用于减少来回切目标。")]
    [SerializeField] [Min(0.0f)] private float _targetAcquisitionCurrentTargetBonus = 0.35f;
    [Tooltip("最近攻击者参与评分时的额外加分；当前版本先保留字段，后续接入伤害事件后生效。")]
    [SerializeField] [Min(0.0f)] private float _targetAcquisitionLastAttackerBonus = 0.6f;
    [Tooltip("选中一个新目标后，至少保持多久不做目标切换。目标死亡、越界或严重遮挡时仍会提前失效。")]
    [SerializeField] [Min(0.0f)] private float _targetAcquisitionLockDuration = 1.25f;
    [Tooltip("当前目标短暂被挡住后保留多久。宽限期内不开火，但不会立刻抖动切目标。")]
    [SerializeField] [Min(0.0f)] private float _targetAcquisitionLostSightGrace = 0.25f;
    [Tooltip("已有目标的 Environment SDF 视线复查间隔。目标锁定期间不会每帧做重型 SDF ray march，0 表示每帧复查。")]
    [SerializeField] [Min(0.0f)] private float _targetAcquisitionLineOfSightRecheckInterval = 0.12f;
    [Tooltip("目标评分里的距离权重。")]
    [SerializeField] [Min(0.0f)] private float _targetAcquisitionDistanceScoreWeight = 0.55f;
    [Tooltip("目标评分里的视角权重，目标越靠近正前方得分越高。")]
    [SerializeField] [Min(0.0f)] private float _targetAcquisitionViewScoreWeight = 0.45f;
    [Header("GPU Combat / 弹道")]
    [Tooltip("启用后，会为当前可见且正在开火的实例补一层 GPU tracer。")]
    [SerializeField] private bool _enableCombatTracer = true;
    [Tooltip("弹道层使用的 Shader。留空时会按默认名字查找。")]
    [SerializeField] private Shader _combatTracerShader;
    [Tooltip("可选的 tracer 材质模板；留空时会在运行时创建默认材质。")]
    [SerializeField] private Material _combatTracerMaterialTemplate;
    [Tooltip("枪口火光序列帧贴图，默认使用 Assets/Project/Textures/Crowds/CombatFx/qiangkou.png。")]
    [SerializeField] private Texture2D _combatMuzzleFlashTexture;
    [Tooltip("子弹曳光贴图，默认使用 Assets/Project/Textures/Crowds/CombatFx/子弹曳光.png。")]
    [SerializeField] private Texture2D _combatTracerTexture;
    [Tooltip("命中火花序列帧贴图，默认使用 Assets/Project/Textures/Crowds/CombatFx/子弹命中——雾.png。")]
    [SerializeField] private Texture2D _combatImpactTexture;
    [Tooltip("弹道宽度，单位为世界空间米。")]
    [SerializeField] [Min(0.001f)] private float _combatTracerWidth = 0.09f;
    [Tooltip("弹道亮度倍率。")]
    [SerializeField] [Min(0.0f)] private float _combatTracerBrightness = 1.8f;
    [Tooltip("低于这个 muzzle flash 强度后，不再继续画 tracer。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _combatTracerMinFlash = 0.04f;
    [Tooltip("枪口火光 billboard 尺寸，单位为世界空间米。")]
    [SerializeField] [Min(0.001f)] private float _combatMuzzleFlashSize = 0.42f;
    [Tooltip("命中火花 billboard 尺寸，单位为世界空间米。")]
    [SerializeField] [Min(0.001f)] private float _combatImpactFlashSize = 0.55f;
    [Tooltip("命中火花贴图从出现到消失的持续时间，单位为秒。")]
    [SerializeField] [Min(0.01f)] private float _combatImpactFlashDuration = 0.28f;
    [Tooltip("命中火花沿射线反方向抬出的距离，用于减少贴地或贴墙闪烁。")]
    [SerializeField] [Min(0.0f)] private float _combatImpactPointOffset = 0.04f;
    [Tooltip("枪口火光亮度倍率。")]
    [SerializeField] [Min(0.0f)] private float _combatMuzzleFlashBrightness = 2.2f;
    [Tooltip("命中火花亮度倍率。")]
    [SerializeField] [Min(0.0f)] private float _combatImpactFlashBrightness = 1.8f;
    [Tooltip("枪口火光贴图的序列帧列数。")]
    [SerializeField] [Min(1)] private int _combatMuzzleFlashFlipbookColumns = 4;
    [Tooltip("枪口火光贴图的序列帧行数。")]
    [SerializeField] [Min(1)] private int _combatMuzzleFlashFlipbookRows = 4;
    [Tooltip("命中火花贴图的序列帧列数。")]
    [SerializeField] [Min(1)] private int _combatImpactFlashFlipbookColumns = 4;
    [Tooltip("命中火花贴图的序列帧行数。")]
    [SerializeField] [Min(1)] private int _combatImpactFlashFlipbookRows = 4;
    [Tooltip("CampA 的 tracer 颜色。")]
    [SerializeField] private Color _combatTracerColorCampA = new Color(1.0f, 0.42f, 0.18f, 1.0f);
    [Tooltip("CampB 的 tracer 颜色。")]
    [SerializeField] private Color _combatTracerColorCampB = new Color(0.22f, 0.74f, 1.0f, 1.0f);

    [Header("模板渲染根节点")]
    [InspectorName("网格根节点本地位置")]
    [Tooltip("模板角色网格根节点相对 renderer 的本地位置偏移。用于适配 prefab 中模型根节点不在原点的情况。")]
    [SerializeField] private Vector3 _meshRootLocalPosition = Vector3.zero;
    [InspectorName("网格根节点本地旋转")]
    [Tooltip("模板角色网格根节点相对 renderer 的本地欧拉角偏移。用于修正模型朝向。")]
    [SerializeField] private Vector3 _meshRootLocalEulerAngles = Vector3.zero;
    [InspectorName("网格根节点本地缩放")]
    [Tooltip("模板角色网格根节点相对 renderer 的本地缩放。通常保持为 1，除非源模型比例需要整体修正。")]
    [SerializeField] private Vector3 _meshRootLocalScale = Vector3.one;

    [Header("渲染")]
    [InspectorName("阴影投射")]
    [Tooltip("人群 indirect draw 的阴影投射模式。")]
    [SerializeField] private ShadowCastingMode _shadowCastingMode = ShadowCastingMode.On;
    [InspectorName("接收阴影")]
    [Tooltip("人群材质是否接收场景阴影。")]
    [SerializeField] private bool _receiveShadows = true;
    [InspectorName("包围盒扩展")]
    [Tooltip("渲染 bounds 额外扩展距离，避免动画、弹道或命中特效在边缘被裁剪。")]
    [SerializeField] [Min(0.0f)] private float _boundsPadding = 2.0f;
    [InspectorName("启用视锥剔除")]
    [Tooltip("启用后，CPU/GPU 会按相机视锥裁剪可见人群实例。关闭后更保守但成本更高。")]
    [SerializeField] private bool _enableFrustumCulling = true;
    [InspectorName("第二档起始距离")]
    [Tooltip("距离相机超过该距离后，转入第二档 crowd LOD。0 表示不启用第二档。")]
    [SerializeField] [Min(0.0f)] private float _secondaryLodStartDistance = 12.0f;
    [InspectorName("第三档起始距离")]
    [Tooltip("距离相机超过该距离后，转入第三档 crowd LOD。0 表示不启用第三档。")]
    [SerializeField] [Min(0.0f)] private float _tertiaryLodStartDistance = 24.0f;

    private ComputeBuffer _spawnDataBuffer;
    private ComputeBuffer _simulationPositionYawBufferA;
    private ComputeBuffer _simulationPositionYawBufferB;
    private ComputeBuffer _simulationPositionYawReadBuffer;
    private ComputeBuffer _simulationPositionYawWriteBuffer;
    private ComputeBuffer _simulationScaleBufferA;
    private ComputeBuffer _simulationScaleBufferB;
    private ComputeBuffer _simulationScaleReadBuffer;
    private ComputeBuffer _simulationScaleWriteBuffer;
    private ComputeBuffer _simulationVelocityBufferA;
    private ComputeBuffer _simulationVelocityBufferB;
    private ComputeBuffer _simulationVelocityReadBuffer;
    private ComputeBuffer _simulationVelocityWriteBuffer;
    private ComputeBuffer _deathStateBuffer;
    private ComputeBuffer _aliveInstanceIndexBuffer;
    private ComputeBuffer _aliveInstanceCounterBuffer;
    private ComputeBuffer _aliveInstanceDispatchArgsBuffer;
    private ComputeBuffer _physicsActiveStateBuffer;
    private ComputeBuffer _physicsActivationMetaBuffer;
    private ComputeBuffer _physicsActiveInstanceIndexBuffer;
    private ComputeBuffer _physicsActiveInstanceCounterBuffer;
    private ComputeBuffer _physicsActiveInstanceDispatchArgsBuffer;
    private ComputeBuffer _wakeGridCounterBuffer;
    private ComputeBuffer _wakeGridOccupantBuffer;
    private ComputeBuffer _gridCounterBuffer;
    private ComputeBuffer _gridOccupantBuffer;
    private ComputeBuffer _gridTouchedCellBufferA;
    private ComputeBuffer _gridTouchedCellBufferB;
    private ComputeBuffer _gridPrevTouchedCellBuffer;
    private ComputeBuffer _gridCurrTouchedCellBuffer;
    private ComputeBuffer _gridTouchedCounterBufferA;
    private ComputeBuffer _gridTouchedCounterBufferB;
    private ComputeBuffer _gridPrevTouchedCounterBuffer;
    private ComputeBuffer _gridCurrTouchedCounterBuffer;
    private ComputeBuffer _gridClearDispatchArgsBuffer;
    private ComputeBuffer _spatialCapsuleStartRadiusBuffer;
    private ComputeBuffer _spatialCapsuleEndHeightBuffer;
    private ComputeBuffer _spatialOwnerIndexBuffer;
    private ComputeBuffer _spatialTargetMaskBuffer;
    private ComputeBuffer _spatialFlagsBuffer;
    private ComputeBuffer _spatialFactionBuffer;
    private ComputeBuffer _spatialQueryBuffer;
    private ComputeBuffer _spatialQueryResultBuffer;
    private ComputeBuffer _spatialQueryHitBuffer;
    private ComputeBuffer _interactionSphereBuffer;
    private ComputeBuffer _combatStateBuffer;
    private ComputeBuffer _targetAcquisitionStateBuffer;
    private ComputeBuffer _targetAcquisitionCandidateBuffer;
    private ComputeBuffer _targetAcquisitionLosDispatchArgsBuffer;
    private ComputeBuffer _squadAcquisitionDispatchArgsBuffer;
    private ComputeBuffer _combatActiveStateBuffer;
    private ComputeBuffer _combatActivationMetaBuffer;
    private ComputeBuffer _combatActiveInstanceIndexBuffer;
    private ComputeBuffer _combatActiveInstanceCounterBuffer;
    private ComputeBuffer _combatActiveInstanceDispatchArgsBuffer;
    private ComputeBuffer _combatSquadCandidateBuffer;
    private ComputeBuffer _combatSquadCandidateCounterBuffer;
    private ComputeBuffer _combatCandidateClusterWorkItemBuffer;
    private ComputeBuffer _combatCandidateClusterDispatchArgsBuffer;
    private ComputeBuffer _squadStateBuffer;
    private ComputeBuffer _squadAliveCountBuffer;
    private ComputeBuffer _agentSquadDataBuffer;
    private ComputeBuffer _formationSlotBuffer;
    private ComputeBuffer _agentCoreBuffer;
    private ComputeBuffer _agentPhysicsExtBuffer;
    private ComputeBuffer _animationClipMetadataBuffer;
    private ComputeBuffer _instanceAnimationStateBuffer;
    private ComputeBuffer _instanceTransformBuffer;
    private ComputeBuffer _instanceFrameDataBuffer;
    private ComputeBuffer _instanceFrameBlendDataBuffer;
    private ComputeBuffer _visibleInstanceIndexBuffer;
    private ComputeBuffer _visibleInstanceCounterBuffer;
    private ComputeBuffer _visibleLod1InstanceIndexBuffer;
    private ComputeBuffer _visibleLod1InstanceCounterBuffer;
    private ComputeBuffer _visibleLod2InstanceIndexBuffer;
    private ComputeBuffer _visibleLod2InstanceCounterBuffer;
    private ComputeBuffer _visibleRuntimeSquadMaskBuffer;
    private ComputeBuffer _runtimeSquadBoundsBuffer;
    private GraphicsBuffer[] _indirectArgsBuffers = Array.Empty<GraphicsBuffer>();
    private GraphicsBuffer[] _visibleLod1IndirectArgsBuffers = Array.Empty<GraphicsBuffer>();
    private GraphicsBuffer[] _visibleLod2IndirectArgsBuffers = Array.Empty<GraphicsBuffer>();
    private GraphicsBuffer[] _combatTracerArgsBuffers = Array.Empty<GraphicsBuffer>();
    private RuntimeRenderResource _runtimeRenderResource;
    private RuntimeRenderResource _secondaryRuntimeRenderResource;
    private RuntimeRenderResource _tertiaryRuntimeRenderResource;
    private bool _hasRuntimeRenderResource;
    private bool _hasSecondaryRuntimeRenderResource;
    private bool _hasTertiaryRuntimeRenderResource;
    private RuntimeRenderResource[] _activeVisibleLodRenderResources = Array.Empty<RuntimeRenderResource>();
    private GraphicsBuffer[][] _activeVisibleLodArgsBuffers = Array.Empty<GraphicsBuffer[]>();
    private Mesh _combatTracerMesh;
    private Material[] _combatTracerMaterials = Array.Empty<Material>();
    private Bounds _localCrowdBounds = new Bounds(Vector3.zero, Vector3.one);
    private GraphicsBuffer.IndirectDrawIndexedArgs[] _indirectArgsCache = Array.Empty<GraphicsBuffer.IndirectDrawIndexedArgs>();
    private GraphicsBuffer.IndirectDrawIndexedArgs[] _visibleLod1IndirectArgsCache = Array.Empty<GraphicsBuffer.IndirectDrawIndexedArgs>();
    private GraphicsBuffer.IndirectDrawIndexedArgs[] _visibleLod2IndirectArgsCache = Array.Empty<GraphicsBuffer.IndirectDrawIndexedArgs>();
    private GraphicsBuffer.IndirectDrawIndexedArgs[] _combatTracerArgsCache = Array.Empty<GraphicsBuffer.IndirectDrawIndexedArgs>();
    private AnimationClipGpuData[] _animationClipGpuCache = Array.Empty<AnimationClipGpuData>();
    private InstanceAnimationStateCpuData[] _instanceAnimationStateCpuCache = Array.Empty<InstanceAnimationStateCpuData>();
    private InstanceAnimationStateGpuData[] _instanceAnimationStateGpuCache = Array.Empty<InstanceAnimationStateGpuData>();
    private CrowdVatAnimationAsset.ClipInfo _currentClip;
    private int _currentClipIndex = InvalidClipIndex;
    private InteractionSphereData[] _interactionSphereUploadCache = Array.Empty<InteractionSphereData>();
    private CrowdVatSpatialQueryRequest[] _runtimeSpatialQueries = Array.Empty<CrowdVatSpatialQueryRequest>();
    private SpatialQueryData[] _spatialQueryUploadCache = Array.Empty<SpatialQueryData>();
    private SpatialQueryResultData[] _spatialQueryResultCache = Array.Empty<SpatialQueryResultData>();
    private SpatialQueryHitData[] _spatialQueryHitCache = Array.Empty<SpatialQueryHitData>();
    private InstanceCombatStateData[] _combatStateReadbackCache = Array.Empty<InstanceCombatStateData>();
    private CrowdVatSquadState[] _runtimeSquadStates = Array.Empty<CrowdVatSquadState>();
    private CrowdVatAgentSquadAssignment[] _runtimeAgentSquadData = Array.Empty<CrowdVatAgentSquadAssignment>();
    private CrowdVatFormationSlot[] _runtimeFormationSlots = Array.Empty<CrowdVatFormationSlot>();
    private readonly CrowdVatGpuBridgeState _gpuBridgeState = new CrowdVatGpuBridgeState();
    private readonly CrowdVatFramePacket _legacyFramePacketScratch = new CrowdVatFramePacket();
    private RuntimeSquadStateGpuData[] _squadStateUploadCache = Array.Empty<RuntimeSquadStateGpuData>();
    private uint[] _runtimeSquadAliveCountUploadCache = Array.Empty<uint>();
    private uint[] _runtimeSquadAliveCountReadbackCache = Array.Empty<uint>();
    private RuntimeAgentSquadDataGpuData[] _agentSquadUploadCache = Array.Empty<RuntimeAgentSquadDataGpuData>();
    private RuntimeFormationSlotGpuData[] _formationSlotUploadCache = Array.Empty<RuntimeFormationSlotGpuData>();
    private CrowdVatAgentCore[] _agentCoreUploadCache = Array.Empty<CrowdVatAgentCore>();
    private CrowdVatAgentPhysicsExt[] _agentPhysicsExtUploadCache = Array.Empty<CrowdVatAgentPhysicsExt>();
    private InstanceSpawnData[] _spawnDataCache = Array.Empty<InstanceSpawnData>();
    private uint[] _visibleInstanceIndexCache = Array.Empty<uint>();
    private uint[] _visibleLod1InstanceIndexCache = Array.Empty<uint>();
    private uint[] _visibleLod2InstanceIndexCache = Array.Empty<uint>();
    private uint[] _visibleRuntimeSquadMaskUploadCache = Array.Empty<uint>();
    private CombatCandidateClusterWorkItemData[] _combatCandidateClusterWorkItemUploadCache = Array.Empty<CombatCandidateClusterWorkItemData>();
    private readonly uint[] _combatCandidateClusterDispatchArgsUploadCache = { 0u, 1u, 1u };
    private uint[] _allInstanceIndicesCache = Array.Empty<uint>();
    private Texture _resolvedTerrainHeightmap;
    private Texture3D _resolvedStaticSdfTexture;
    private Texture3D _resolvedEnvironmentDistanceFieldTexture;
    private Texture3D _fallbackStaticSdfTexture;
    private Vector2 _gridMinXZ;
    private Vector2 _gridMaxXZ;
    private Vector2Int _gridDimensions;
    private Vector2Int _wakeGridDimensions;
    private float _resolvedWakeGridCellSize = 1.4f;
    private bool _hasClip;
    private bool _isPlaying;
    private bool _resourcesDirty = true;
    private float _playbackTime;
    private float _minimumPlaybackSpeedMultiplier = 1.0f;
    private float _maximumQueryAgentRadius = 0.35f;
    private bool _hasResolvedEnvironmentDistanceField;
    private bool _hasResolvedTerrainCollision;
    private bool _hasResolvedStaticSdfCollision;
    private int _gridCellCount;
    private Bounds _visibleWorldBounds;
    private bool _hasVisibleBounds;
    private int _visibleInstanceCount;
    private int _visibleLod1InstanceCount;
    private int _visibleLod2InstanceCount;
    private int _activeVisibleLodTierCountThisFrame;
    private float _visibleLod1StartDistanceThisFrame = float.MaxValue;
    private float _visibleLod2StartDistanceThisFrame = float.MaxValue;
    private Vector3 _visibleCameraPositionThisFrame;
    private readonly Plane[] _frustumPlanes = new Plane[6];
    private int _buildAliveInstanceListKernel = -1;
    private int _buildAliveDispatchArgsKernel = -1;
    private int _clearWakeGridKernel = -1;
    private int _buildWakeGridKernel = -1;
    private int _evaluatePhysicsActiveKernel = -1;
    private int _buildPhysicsActiveInstanceListKernel = -1;
    private int _buildPhysicsActiveDispatchArgsKernel = -1;
    private int _evaluateCombatActiveKernel = -1;
    private int _buildCombatActiveInstanceListKernel = -1;
    private int _compactCombatActiveInstanceListKernel = -1;
    private int _buildCombatActiveDispatchArgsKernel = -1;
    private int _clearCombatSquadCandidateCountersKernel = -1;
    private int _clearVisibleRuntimeSquadBoundsKernel = -1;
    private int _buildVisibleRuntimeSquadBoundsKernel = -1;
    private int _cullVisibleRuntimeSquadsKernel = -1;
    private int _buildVisibleRuntimeInstanceListKernel = -1;
    private int _buildVisibleIndirectArgsKernel = -1;
    private int _predictKernel = -1;
    private int _buildGridClearDispatchArgsKernel = -1;
    private int _clearGridKernel = -1;
    private int _buildGridKernel = -1;
    private int _buildSpatialElementsKernel = -1;
    private int _solveCrowdKernel = -1;
    private int _buildCombatCandidateClustersKernel = -1;
    private int _resolveTargetAcquisitionKernel = -1;
    private int _buildTargetAcquisitionLosDispatchArgsKernel = -1;
    private int _resolveTargetAcquisitionLineOfSightKernel = -1;
    private int _buildSquadAcquisitionDispatchArgsKernel = -1;
    private int _evaluateSquadAcquisitionCandidatesKernel = -1;
    private int _finalizeTargetAcquisitionKernel = -1;
    private int _resolveInstanceCombatKernel = -1;
    private int _clearSpatialQueriesKernel = -1;
    private int _resolveSpatialQueriesKernel = -1;
    private int _finalizeKernel = -1;
    private int _captureAiDebugGpuStageKernel = -1;
    private int _discoverAiDebugGpuCandidatesKernel = -1;
    private int _activeSpatialQueryCount;
    private int _activeSquadStateCount;
    private int _activeAgentSquadDataCount;
    private int _activeFormationSlotCount;
    private int _lastCombatStateReadbackFrame = -1;
    private int _lastPhysicsActiveStateReadbackFrame = -1;
    private int _lastSquadAliveCountReadbackFrame = -1;
    private int _wakeGridCellCount;
    private bool _hasPendingSquadAliveCountReadback;
    private int _squadAliveCountReadbackVersion;
    private bool _runtimeSquadAliveCountDirty = true;
    private bool _runtimeAgentSquadDataDirty = true;
    private bool _runtimeFormationSlotsDirty = true;
    private bool _agentDataLayoutDirty = true;
    private bool _hasUnassignedRuntimeInstances;
    private uint[] _physicsActiveStateReadbackCache;
    private bool _usesGpuVisibleInstanceCompactionThisFrame;
    private bool _visibleUnassignedInstancesThisFrame;
}
