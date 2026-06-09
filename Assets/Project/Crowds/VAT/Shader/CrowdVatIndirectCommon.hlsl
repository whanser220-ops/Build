struct InstanceSpawnData
{
    float3 localPosition;
    float yawRadians;
    float uniformScale;
    float normalizedTimeOffset;
    float playbackSpeedMultiplier;
    uint faction;
};

struct InstanceSimulationState
{
    float4 localPositionAndYaw;
    float4 scaleAndVelocity;
};

struct InteractionSphereData
{
    float4 localCenterAndRadius;
    float4 parameters;
};

struct MatrixRows
{
    float4 row0;
    float4 row1;
    float4 row2;
};

struct AnimationClipGpuData
{
    int startFrame;
    int frameCount;
    float lengthSeconds;
    int loop;
};

struct InstanceAnimationStateGpuData
{
    int currentClipIndex;
    int nextClipIndex;
    int isBlending;
    int padding0;
    float currentClipTime;
    float nextClipTime;
    float transitionElapsed;
    float transitionDuration;
};

struct SpatialElementData
{
    float4 localCapsuleStartAndRadius;
    float4 localCapsuleEndAndHeight;
    uint ownerIndex;
    uint targetMask;
    uint flags;
    uint faction;
};

struct SpatialQueryData
{
    float4 localStartAndRadius;
    float4 localEndAndPadding;
    uint queryId;
    uint targetMask;
    uint factionMask;
    uint shape;
    uint flags;
    uint maxHits;
    uint padding0;
    uint padding1;
};

struct SpatialQueryResultData
{
    uint queryId;
    uint totalHits;
    uint writtenHits;
    uint overflow;
};

struct SpatialQueryHitData
{
    uint queryId;
    uint ownerIndex;
    uint targetMask;
    uint flags;
    uint faction;
    float normalizedDistance;
    uint padding0;
    uint padding1;
};

struct SquadStateData
{
    float4 localCenterAndAnchorBlend;
    float4 localForwardAndMoveSpeed;
    float4 localTargetAndSpacingX;
    float4 metadata0;
    float4 metadata1;
};

struct AgentSquadData
{
    float4 squadAndSlotAndRoleAndFlags;
    float4 slotOffsetAndWeight;
};

struct FormationSlotData
{
    float4 localOffsetAndRoleAndRank;
    float4 preferredDistanceAndPadding;
};

struct RuntimeSquadBoundsData
{
    uint minX;
    uint minY;
    uint minZ;
    uint maxX;
    uint maxY;
    uint maxZ;
    uint validCount;
    uint padding0;
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
    float4 debugShotTraceMeta;
};

struct TargetAcquisitionStateData
{
    float4 lastKnownTargetAndScore;
    float4 timers;
    float4 debugRejectCounts0;
    float4 debugRejectCounts1;
    int currentTargetIndex;
    int lastAttackerIndex;
    uint flags;
    int debugVisibleSampleIndex;
};

struct TargetAcquisitionCandidateData
{
    float4 targetLocalAndScore;
    float targetDistance;
    int targetIndex;
    uint rejectReason;
    uint flags;
};

struct CombatSquadCandidateData
{
    uint instanceIndex;
    uint faction;
    float4 targetLocalAndScale;
    float distanceToSourceBounds;
    uint padding;
};

struct CombatCandidateClusterWorkItemData
{
    uint sourceSquadIndex;
    uint tileBegin;
    uint cellWidth;
    uint cellCount;
    int2 minCoord;
    uint2 padding;
};

struct CrowdVatAiDebugGpuStageRecord
{
    uint frameIndex;
    uint stageId;
    int solverIteration;
    uint targetSlot;
    uint instanceIndex;
    uint physicsActive;
    uint deathState;
    uint gridCellKey;
    uint gridOccupantCount;
    uint gridOverflow;
    uint combatFlags;
    uint acquisitionFlags;
    int combatTargetIndex;
    int acquisitionTargetIndex;
    int acquisitionLastAttackerIndex;
    uint valid;
    float4 localPositionYaw;
    float4 velocityScaleHealth;
    float4 combatDistanceCooldownMuzzleHit;
    float4 combatImpactDamageNormalizedHealth;
    float4 acquisitionLastKnownScore;
    float4 acquisitionTimers;
    float4 acquisitionRejectCounts0;
    float4 acquisitionRejectCounts1;
    float4 combatDebugShotInfo;
    float4 combatDebugShotTrace;
};

struct CrowdVatAiDebugGpuCandidateRecord
{
    uint frameIndex;
    uint candidateSlot;
    uint instanceIndex;
    uint reasonMask;
    float reservedFloat0;
    uint physicsActive;
    uint deathState;
    uint gridCellKey;
    uint gridOccupantCount;
    uint gridOverflow;
    uint combatFlags;
    uint acquisitionFlags;
    int combatTargetIndex;
    int acquisitionTargetIndex;
    uint valid;
    uint reserved0;
    float4 localPositionYaw;
    float4 velocityScaleHealth;
    float4 acquisitionRejectCounts0;
    float4 acquisitionRejectCounts1;
    float4 combatDistanceCooldownMuzzleHit;
};

struct IndirectDrawIndexedArgsData
{
    uint indexCountPerInstance;
    uint instanceCount;
    uint startIndex;
    uint baseVertexIndex;
    uint startInstance;
};

StructuredBuffer<InstanceSpawnData> _SpawnData;
StructuredBuffer<float4> _SimulationPositionYawReadBuffer;
RWStructuredBuffer<float4> _SimulationPositionYawWriteBuffer;
StructuredBuffer<float> _SimulationScaleReadBuffer;
RWStructuredBuffer<float> _SimulationScaleWriteBuffer;
StructuredBuffer<float2> _SimulationVelocityReadBuffer;
RWStructuredBuffer<float2> _SimulationVelocityWriteBuffer;
RWStructuredBuffer<uint> _DeathStateBuffer;
RWStructuredBuffer<uint> _AliveInstanceIndexBuffer;
RWStructuredBuffer<uint> _AliveInstanceCounterBuffer;
RWStructuredBuffer<uint> _AliveInstanceDispatchArgsBuffer;
StructuredBuffer<uint> _AliveInstanceIndexReadBuffer;
StructuredBuffer<uint> _AliveInstanceCounterReadBuffer;
RWStructuredBuffer<uint> _PhysicsActiveStateBuffer;
RWStructuredBuffer<float4> _PhysicsActivationMetaBuffer;
RWStructuredBuffer<uint> _PhysicsActiveInstanceIndexBuffer;
RWStructuredBuffer<uint> _PhysicsActiveInstanceCounterBuffer;
RWStructuredBuffer<uint> _PhysicsActiveInstanceDispatchArgsBuffer;
StructuredBuffer<uint> _PhysicsActiveInstanceIndexReadBuffer;
StructuredBuffer<uint> _PhysicsActiveInstanceCounterReadBuffer;
RWStructuredBuffer<uint> _CombatActiveStateBuffer;
RWStructuredBuffer<float4> _CombatActivationMetaBuffer;
RWStructuredBuffer<uint> _CombatActiveInstanceIndexBuffer;
RWStructuredBuffer<uint> _CombatActiveInstanceCounterBuffer;
RWStructuredBuffer<uint> _CombatActiveInstanceDispatchArgsBuffer;
StructuredBuffer<uint> _CombatActiveInstanceIndexReadBuffer;
StructuredBuffer<uint> _CombatActiveInstanceCounterReadBuffer;
RWStructuredBuffer<uint> _WakeGridCounterBuffer;
RWStructuredBuffer<uint> _WakeGridOccupantBuffer;
StructuredBuffer<InteractionSphereData> _InteractionSphereBuffer;
StructuredBuffer<SquadStateData> _SquadStateBuffer;
StructuredBuffer<AgentSquadData> _AgentSquadDataBuffer;
StructuredBuffer<FormationSlotData> _FormationSlotBuffer;
StructuredBuffer<AnimationClipGpuData> _AnimationClipMetadataBuffer;
StructuredBuffer<InstanceAnimationStateGpuData> _InstanceAnimationStateBuffer;
RWStructuredBuffer<MatrixRows> _InstanceTransforms;
RWStructuredBuffer<float4> _InstanceFrameData;
RWStructuredBuffer<float4> _InstanceFrameBlendData;
RWStructuredBuffer<uint> _VisibleInstanceIndices;
RWStructuredBuffer<uint> _VisibleInstanceCounterBuffer;
RWStructuredBuffer<uint> _VisibleLod1InstanceIndices;
RWStructuredBuffer<uint> _VisibleLod1InstanceCounterBuffer;
RWStructuredBuffer<uint> _VisibleLod2InstanceIndices;
RWStructuredBuffer<uint> _VisibleLod2InstanceCounterBuffer;
RWStructuredBuffer<uint> _GridCounterBuffer;
RWStructuredBuffer<uint> _GridOccupantBuffer;
RWStructuredBuffer<uint> _GridPrevTouchedCellBuffer;
RWStructuredBuffer<uint> _GridPrevTouchedCounterBuffer;
RWStructuredBuffer<uint> _GridCurrTouchedCellBuffer;
RWStructuredBuffer<uint> _GridCurrTouchedCounterBuffer;
RWStructuredBuffer<uint> _GridClearDispatchArgsBuffer;
RWStructuredBuffer<float4> _SpatialCapsuleStartRadiusBuffer;
RWStructuredBuffer<float4> _SpatialCapsuleEndHeightBuffer;
RWStructuredBuffer<uint> _SpatialOwnerIndexBuffer;
RWStructuredBuffer<uint> _SpatialTargetMaskBuffer;
RWStructuredBuffer<uint> _SpatialFlagsBuffer;
RWStructuredBuffer<uint> _SpatialFactionBuffer;
RWStructuredBuffer<uint> _SquadAliveCountBuffer;
StructuredBuffer<SpatialQueryData> _SpatialQueryBuffer;
RWStructuredBuffer<SpatialQueryResultData> _SpatialQueryResultBuffer;
RWStructuredBuffer<SpatialQueryHitData> _SpatialQueryHitBuffer;
RWStructuredBuffer<InstanceCombatStateData> _CombatStateBuffer;
RWStructuredBuffer<TargetAcquisitionStateData> _TargetAcquisitionStateBuffer;
RWStructuredBuffer<TargetAcquisitionCandidateData> _TargetAcquisitionCandidateBuffer;
RWStructuredBuffer<uint> _TargetAcquisitionLosDispatchArgsBuffer;
RWStructuredBuffer<uint> _SquadAcquisitionDispatchArgsBuffer;
RWStructuredBuffer<CombatSquadCandidateData> _CombatSquadCandidateBuffer;
RWStructuredBuffer<uint> _CombatSquadCandidateCounterBuffer;
StructuredBuffer<CombatCandidateClusterWorkItemData> _CombatCandidateClusterWorkItemBuffer;
StructuredBuffer<uint> _AiDebugTargetIndexBuffer;
RWStructuredBuffer<CrowdVatAiDebugGpuStageRecord> _AiDebugStageRecordBuffer;
RWStructuredBuffer<uint> _AiDebugCandidateCounterBuffer;
RWStructuredBuffer<CrowdVatAiDebugGpuCandidateRecord> _AiDebugCandidateRecordBuffer;
RWStructuredBuffer<uint> _VisibleRuntimeSquadMaskBuffer;
RWStructuredBuffer<RuntimeSquadBoundsData> _RuntimeSquadBoundsBuffer;
RWStructuredBuffer<IndirectDrawIndexedArgsData> _VisibleRenderArgsBuffer;
Texture2D<float4> _TerrainHeightmap;
SamplerState sampler_TerrainHeightmap;
Texture3D<float4> _StaticSdfTexture;
SamplerState sampler_StaticSdfTexture;
Texture3D<float4> _EnvironmentDistanceFieldTexture;
SamplerState sampler_EnvironmentDistanceFieldTexture;

int _InstanceCount;
int _AnimationClipCount;
float _PlaybackTime;
float _BasePlaybackSpeed;
int _ClipStartFrame;
int _ClipFrameCount;
float _ClipLength;
int _ClipLoop;
float3 _RootPosition;
float3 _RootRight;
float3 _RootUp;
float3 _RootForward;
float _DeltaTime;
int _EnableApproximateCollision;
int _InteractionSphereCount;
int2 _GridDim;
int _GridCellCount;
int _MaxCellOccupancy;
int _GridBuildMode;
int2 _WakeGridDim;
int _WakeGridCellCount;
int _PhysicsActivationPass;
int _SpatialQueryCount;
int _MaxSpatialQueryHits;
float _CellSize;
float _InvCellSize;
float _WakeGridCellSize;
float _WakeInvGridCellSize;
float2 _GridMinXZ;
float2 _GridMaxXZ;
float _CollisionRadius;
float _CapsuleHeight;
float _MaxSpatialQueryElementRadius;
float _AnchorStiffness;
float _VelocityDamping;
float _GoalStiffness;
int _EnableNavigationPotentialField;
float _NavigationPotentialProbeDistance;
float _NavigationDensityLateralStrength;
float _NavigationDensitySlowdownStrength;
int _NavigationDensityReferenceOccupancy;
int _EnableLocalAvoidance;
float _LocalAvoidanceRadius;
float _LocalAvoidanceStrength;
float _DensitySlowdownStrength;
int _DensityReferenceNeighborCount;
int _LocalAvoidanceMaxNeighbors;
float _SelfCollisionStrength;
float _SelfCollisionSlop;
float _SelfCollisionMaxPushPerStep;
int _EnableDensityConstraint;
float _DensityComfortDistance;
float _DensityConstraintStiffness;
float _MaxPushPerStep;
float _MaxDisplacementFromSpawn;
float _InactiveReturnStrength;
float _PhysicsWakeSpeed;
float _PhysicsSleepSpeed;
float _PhysicsWakeAnchorError;
float _PhysicsSleepAnchorError;
float _PhysicsWakeNeighborRadius;
float _PhysicsActiveHoldTime;
float _PhysicsSleepDelay;
float _CombatActiveHoldTime;
int _CombatActiveProbeIntervalFrames;
int _SimulationFrameIndex;
int _EnableTerrainCollision;
float3 _TerrainPosition;
float3 _TerrainSize;
float _TerrainHeightOffset;
int _EnableStaticSdfCollision;
int _EnableEnvironmentDistanceField;
int _EnableGpuInstanceCombat;
float3 _EnvironmentDistanceWorldCenter;
float3 _EnvironmentDistanceWorldSize;
float _EnvironmentDistanceDistanceScale;
float _EnvironmentDistanceDistanceBias;
float3 _StaticSdfWorldCenter;
float3 _StaticSdfWorldSize;
float _StaticSdfDistanceScale;
float _StaticSdfDistanceBias;
float _CombatRange;
float _CombatShotsPerSecond;
float _CombatShotIntervalJitter;
float _CombatShotSpreadDegrees;
float _CombatHitRadiusPadding;
float _CombatHitHeightPadding;
int _EnableCombatTerrainOcclusion;
float _CombatTerrainOcclusionSampleSpacing;
float _CombatTerrainOcclusionClearance;
int _CombatEnvironmentOcclusionMaxSteps;
float _CombatOriginHeight;
float _CombatTargetHeight;
float _CombatMuzzleFlashDecay;
float _CombatImpactFlashDuration;
float _CombatMaxHealth;
float _CombatDamagePerHit;
float _CombatHitFlashDecay;
float _CombatOccludedTargetRetryDelay;
float _CombatNoTargetRetryDelay;
float _TargetAcquisitionSearchIntervalMin;
float _TargetAcquisitionSearchIntervalMax;
float _TargetAcquisitionFovCosine;
int _TargetAcquisitionMaxCandidateChecks;
float _TargetAcquisitionCurrentTargetBonus;
float _TargetAcquisitionLastAttackerBonus;
float _TargetAcquisitionLockDuration;
float _TargetAcquisitionLostSightGrace;
float _TargetAcquisitionLineOfSightRecheckInterval;
float _TargetAcquisitionDistanceScoreWeight;
float _TargetAcquisitionViewScoreWeight;
int _CombatCandidateCapacityPerSquad;
int _CapsulePbdSampleCount;
int _EnableRuntimeSquadAnchors;
int _UseRuntimeFormationSlots;
int _SquadStateCount;
int _AgentSquadDataCount;
int _FormationSlotCount;
int _VisibleRuntimeSquadCount;
int _VisibleUnassignedInstances;
int _VisibleRenderArgsIndexCount;
int _VisibleRenderArgsStartIndex;
int _VisibleRenderArgsBaseVertex;
int _HasVisibleFrustumPlanes;
float4 _VisibleBoundsCenter;
float4 _VisibleBoundsExtents;
int _AiDebugTargetCount;
int _AiDebugStageId;
int _AiDebugFrameIndex;
int _AiDebugSolverIteration;
int _AiDebugStageWriteOffset;
int _AiDebugCandidateCapacity;
int _AiDebugDiscoveryFrameIndex;
int _AiDebugDiscoveryFlags;
float4 _AiDebugDiscoveryCenterRadius;
float4 _AiDebugDiscoveryBoxCenter;
float4 _AiDebugDiscoveryBoxExtents;
float4 _AiDebugDiscoveryScreenRect;
float4 _AiDebugDiscoveryWorldToClipRow0;
float4 _AiDebugDiscoveryWorldToClipRow1;
float4 _AiDebugDiscoveryWorldToClipRow2;
float4 _AiDebugDiscoveryWorldToClipRow3;
int _AiDebugDiscoverySquadId;
float _VisibleInstanceBoundsRadius;
float3 _VisibleCameraPosition;
int _VisibleLodTierCount;
float _VisibleLod1StartDistance;
float _VisibleLod2StartDistance;
float4 _WorldToLocalRow0;
float4 _WorldToLocalRow1;
float4 _WorldToLocalRow2;
float4 _VisibleFrustumPlane0;
float4 _VisibleFrustumPlane1;
float4 _VisibleFrustumPlane2;
float4 _VisibleFrustumPlane3;
float4 _VisibleFrustumPlane4;
float4 _VisibleFrustumPlane5;

static const uint CrowdVatSpatialTargetMaskCrowdAgent = 1u;
static const uint CrowdVatFactionMaskNone = 0u;
static const uint CrowdVatSpatialQueryShapeOverlapSphere = 0u;
static const uint CrowdVatSpatialQueryShapeSweepCapsule = 1u;
static const uint CrowdVatSpatialQueryFlagActiveOnly = 1u;
static const uint CrowdVatSpatialElementFlagActive = 1u;
static const uint GridBuildModePhysicsActiveOnly = 0u;
static const uint GridBuildModeAllQueryables = 1u;
static const uint CrowdVatSquadCommandTypeNone = 0u;
static const uint CrowdVatSquadCommandTypeHold = 1u;
static const uint CrowdVatSquadCommandTypeMoveTo = 2u;
static const uint CrowdVatSquadCommandTypeAdvance = 3u;
static const uint CrowdVatSquadCommandTypeCharge = 4u;
static const uint CrowdVatSquadCommandTypeRetreat = 5u;
static const uint CrowdVatSquadCommandTypeRegroup = 6u;
static const uint CrowdVatSquadFlagFaceTarget = 1u << 0;
static const uint CrowdVatSquadFlagFaceMovement = 1u << 1;
static const uint CrowdVatSquadMemberFlagUseSlotOffsetOverride = 1u << 0;
static const uint CrowdVatInstanceCombatFlagHasTarget = 1u << 0;
static const uint CrowdVatInstanceCombatFlagHasLineOfSight = 1u << 1;
static const uint CrowdVatInstanceCombatFlagFiredThisFrame = 1u << 2;
static const uint CrowdVatInstanceCombatFlagDead = 1u << 4;
static const uint CrowdVatCombatActiveReasonProbe = 1u << 0;
static const uint CrowdVatCombatActiveReasonTarget = 1u << 1;
static const uint CrowdVatCombatActiveReasonVisual = 1u << 2;
static const uint CrowdVatCombatActiveReasonHold = 1u << 3;
static const uint CrowdVatDeathStateDeadFlag = 0x80000000u;
static const uint CrowdVatDeathStateDamageMask = 0x7fffffffu;
static const uint CrowdVatTargetAcquisitionFlagHasTarget = 1u << 0;
static const uint CrowdVatTargetAcquisitionFlagHasLineOfSight = 1u << 1;
static const uint CrowdVatTargetAcquisitionFlagLocked = 1u << 2;
static const uint CrowdVatTargetRejectEnvironmentSdf = 1u << 1;
static const uint CrowdVatTargetAcquisitionTopK = 3u;
static const uint CrowdVatTargetAcquisitionCurrentCandidateSlot = 0u;
static const uint CrowdVatTargetAcquisitionSearchCandidateBaseSlot = 1u;
static const uint CrowdVatTargetAcquisitionCandidateSlots = CrowdVatTargetAcquisitionSearchCandidateBaseSlot + CrowdVatTargetAcquisitionTopK;
static const uint CrowdVatTargetCandidateFlagValid = 1u << 0;
static const uint CrowdVatTargetCandidateFlagHasLineOfSight = 1u << 1;
static const uint CrowdVatTargetCandidateFlagLineOfSightResolved = 1u << 2;
static const uint CrowdVatTargetCandidateFlagCurrentTargetRecheck = 1u << 3;
static const uint CrowdVatInvalidInstanceIndex = 0xFFFFFFFFu;
static const uint CrowdVatDispatchThreadGroupSize = 64u;
static const uint CrowdVatSquadAcquisitionCandidateStrideCount = 8u;
static const uint CrowdVatSquadAcquisitionMaxCandidateSlots =
    CrowdVatDispatchThreadGroupSize * CrowdVatSquadAcquisitionCandidateStrideCount;
static const uint CrowdVatAiDebugDiscoveryFlagRegionSphere = 1u << 0;
static const uint CrowdVatAiDebugDiscoveryFlagRegionBox = 1u << 1;
static const uint CrowdVatAiDebugDiscoveryFlagScreenRect = 1u << 2;
static const uint CrowdVatAiDebugDiscoveryFlagSquadFilter = 1u << 3;
static const uint CrowdVatAiDebugCandidateRegion = 1u << 0;
static const uint CrowdVatAiDebugCandidateScreen = 1u << 9;
static const uint CrowdVatAiDebugCandidateSquad = 1u << 10;
static const uint CrowdVatSquadMemberFlagUnassigned = 1u << 31;
static const float CrowdVatPi = 3.14159265;
static const float CrowdVatFacingVelocityThresholdSqr = 1e-5;
static const float CrowdVatInactiveReturnYawHoldVelocityThresholdSqr = 4e-4;
static const float CombatRayHitBias = 0.05;
static const int CombatEnvironmentOcclusionDefaultMaxSteps = 48;
static const int CombatEnvironmentOcclusionHardMaxSteps = 96;
static const float CombatEnvironmentOcclusionStepSafety = 0.75;
static const float CombatEnvironmentOcclusionMinStep = 0.1;
static const int CombatShotGridMaxCells = 96;
static const uint CombatShotMaxAgentChecks = 256u;
static const uint EnvironmentSurfaceNone = 0u;
static const uint EnvironmentSurfaceGround = 1u;
static const uint EnvironmentSurfaceWall = 2u;
static const uint EnvironmentSurfaceCeiling = 3u;
static const float EnvironmentGroundNormalYThreshold = 0.45;
static const float EnvironmentCeilingNormalYThreshold = -0.35;
static const float EnvironmentGroundSnapEpsilon = 1e-4;

bool IsDeathStateDead(uint deathState)
{
    return (deathState & CrowdVatDeathStateDeadFlag) != 0u;
}

bool IsInstanceDead(uint instanceIndex)
{
    return IsDeathStateDead(_DeathStateBuffer[instanceIndex]);
}

float GetCrowdPbdInverseMass(uint instanceIndex)
{
    return IsInstanceDead(instanceIndex) ? 0.0 : 1.0;
}

uint GetCombatMaxHealthUnits()
{
    return (uint)max(round(max(_CombatMaxHealth, 1.0)), 1.0);
}

uint GetCombatDamagePerHitUnits()
{
    return (uint)round(max(_CombatDamagePerHit, 0.0));
}

uint GetCombatDamageTakenUnits(uint deathState)
{
    return min(deathState & CrowdVatDeathStateDamageMask, GetCombatMaxHealthUnits());
}

float ComputeCombatHealth(uint deathState)
{
    uint maxHealth = GetCombatMaxHealthUnits();
    uint damageTaken = GetCombatDamageTakenUnits(deathState);
    return (float)max(maxHealth - damageTaken, 0u);
}

float4 BuildCombatHealthFeedback(float health, float hitFlash, float lastDamage)
{
    float maxHealth = max((float)GetCombatMaxHealthUnits(), 1.0);
    float currentHealth = saturate(health / maxHealth) * maxHealth;
    return float4(
        currentHealth,
        saturate(hitFlash),
        max(lastDamage, 0.0),
        saturate(currentHealth / maxHealth));
}

float3 TransformRootDirection(float3 localDirection)
{
    return _RootRight * localDirection.x
        + _RootUp * localDirection.y
        + _RootForward * localDirection.z;
}

float3 TransformLocalPointToWorld(float3 localPoint)
{
    return _RootPosition + TransformRootDirection(localPoint);
}

float3 TransformWorldPointToLocal(float3 worldPoint)
{
    return float3(
        dot(_WorldToLocalRow0, float4(worldPoint, 1.0)),
        dot(_WorldToLocalRow1, float4(worldPoint, 1.0)),
        dot(_WorldToLocalRow2, float4(worldPoint, 1.0)));
}

float3 TransformWorldDirectionToLocal(float3 worldDirection)
{
    return float3(
        dot(_WorldToLocalRow0.xyz, worldDirection),
        dot(_WorldToLocalRow1.xyz, worldDirection),
        dot(_WorldToLocalRow2.xyz, worldDirection));
}

float3 SafeNormalize3D(float3 value, float3 fallbackValue)
{
    float lengthSquared = dot(value, value);
    if (lengthSquared <= 1e-8)
        return fallbackValue;

    return value * rsqrt(lengthSquared);
}

float ComputeVisibleBoundsRootScale()
{
    return max(length(_RootRight), max(length(_RootUp), length(_RootForward)));
}

bool IsSphereInsidePlane(float3 worldCenter, float radius, float4 plane)
{
    return dot(plane.xyz, worldCenter) + plane.w >= -radius;
}

bool IsWorldSphereInsideVisibleFrustum(float3 worldCenter, float radius)
{
    return IsSphereInsidePlane(worldCenter, radius, _VisibleFrustumPlane0) &&
        IsSphereInsidePlane(worldCenter, radius, _VisibleFrustumPlane1) &&
        IsSphereInsidePlane(worldCenter, radius, _VisibleFrustumPlane2) &&
        IsSphereInsidePlane(worldCenter, radius, _VisibleFrustumPlane3) &&
        IsSphereInsidePlane(worldCenter, radius, _VisibleFrustumPlane4) &&
        IsSphereInsidePlane(worldCenter, radius, _VisibleFrustumPlane5);
}

bool IsInstanceVisibleInFrustum(InstanceSimulationState state)
{
    if (_HasVisibleFrustumPlanes == 0)
        return true;

    float uniformScale = max(state.scaleAndVelocity.x, 0.01);
    float worldRadius = max(_VisibleInstanceBoundsRadius * uniformScale * ComputeVisibleBoundsRootScale(), 0.01);
    float3 worldCenter = TransformLocalPointToWorld(state.localPositionAndYaw.xyz);
    return IsWorldSphereInsideVisibleFrustum(worldCenter, worldRadius);
}

uint EncodeOrderedFloat(float value)
{
    uint bits = asuint(value);
    return (bits & 0x80000000u) != 0u ? ~bits : (bits | 0x80000000u);
}

float DecodeOrderedFloat(uint value)
{
    uint bits = (value & 0x80000000u) != 0u ? (value & 0x7fffffffu) : ~value;
    return asfloat(bits);
}

void ComputeVisibleInstanceLocalAabb(InstanceSimulationState state, out float3 localMin, out float3 localMax)
{
    float safeScale = max(state.scaleAndVelocity.x, 0.01);
    float sinYaw;
    float cosYaw;
    sincos(state.localPositionAndYaw.w, sinYaw, cosYaw);

    float2 rotatedCenterXZ = float2(
        cosYaw * _VisibleBoundsCenter.x + sinYaw * _VisibleBoundsCenter.z,
        -sinYaw * _VisibleBoundsCenter.x + cosYaw * _VisibleBoundsCenter.z);
    float extentX = abs(cosYaw) * _VisibleBoundsExtents.x + abs(sinYaw) * _VisibleBoundsExtents.z;
    float extentZ = abs(sinYaw) * _VisibleBoundsExtents.x + abs(cosYaw) * _VisibleBoundsExtents.z;

    float3 localCenter = state.localPositionAndYaw.xyz + float3(
        rotatedCenterXZ.x * safeScale,
        _VisibleBoundsCenter.y * safeScale,
        rotatedCenterXZ.y * safeScale);
    float3 localExtents = float3(extentX, _VisibleBoundsExtents.y, extentZ) * safeScale;
    localMin = localCenter - localExtents;
    localMax = localCenter + localExtents;
}

float ComputeVisibleLocalBoundsPlaneRadius(float3 planeNormal, float3 localExtents)
{
    return abs(dot(planeNormal, _RootRight)) * localExtents.x +
        abs(dot(planeNormal, _RootUp)) * localExtents.y +
        abs(dot(planeNormal, _RootForward)) * localExtents.z;
}

bool IsWorldLocalBoundsInsidePlane(float3 worldCenter, float3 localExtents, float4 plane)
{
    float planeRadius = ComputeVisibleLocalBoundsPlaneRadius(plane.xyz, localExtents);
    return dot(plane.xyz, worldCenter) + plane.w >= -planeRadius;
}

bool IsLocalBoundsVisibleInFrustum(float3 localMin, float3 localMax)
{
    if (_HasVisibleFrustumPlanes == 0)
        return true;

    float3 localCenter = (localMin + localMax) * 0.5;
    float3 localExtents = max((localMax - localMin) * 0.5, 0.0.xxx);
    float3 worldCenter = TransformLocalPointToWorld(localCenter);
    return IsWorldLocalBoundsInsidePlane(worldCenter, localExtents, _VisibleFrustumPlane0) &&
        IsWorldLocalBoundsInsidePlane(worldCenter, localExtents, _VisibleFrustumPlane1) &&
        IsWorldLocalBoundsInsidePlane(worldCenter, localExtents, _VisibleFrustumPlane2) &&
        IsWorldLocalBoundsInsidePlane(worldCenter, localExtents, _VisibleFrustumPlane3) &&
        IsWorldLocalBoundsInsidePlane(worldCenter, localExtents, _VisibleFrustumPlane4) &&
        IsWorldLocalBoundsInsidePlane(worldCenter, localExtents, _VisibleFrustumPlane5);
}

bool TryLoadRuntimeSquadBounds(uint squadIndex, out float3 localMin, out float3 localMax)
{
    RuntimeSquadBoundsData bounds = _RuntimeSquadBoundsBuffer[squadIndex];
    if (bounds.validCount == 0u)
    {
        localMin = 0.0.xxx;
        localMax = 0.0.xxx;
        return false;
    }

    localMin = float3(
        DecodeOrderedFloat(bounds.minX),
        DecodeOrderedFloat(bounds.minY),
        DecodeOrderedFloat(bounds.minZ));
    localMax = float3(
        DecodeOrderedFloat(bounds.maxX),
        DecodeOrderedFloat(bounds.maxY),
        DecodeOrderedFloat(bounds.maxZ));
    return true;
}

bool TryResolveAliveDispatchInstanceIndex(uint dispatchIndex, out uint instanceIndex)
{
    uint aliveCount = _AliveInstanceCounterReadBuffer[0];
    if (dispatchIndex >= aliveCount)
    {
        instanceIndex = CrowdVatInvalidInstanceIndex;
        return false;
    }

    instanceIndex = _AliveInstanceIndexReadBuffer[dispatchIndex];
    return instanceIndex < (uint)_InstanceCount;
}

bool TryResolvePhysicsActiveDispatchInstanceIndex(uint dispatchIndex, out uint instanceIndex)
{
    uint activeCount = _PhysicsActiveInstanceCounterBuffer[0];
    if (dispatchIndex >= activeCount)
    {
        instanceIndex = CrowdVatInvalidInstanceIndex;
        return false;
    }

    instanceIndex = _PhysicsActiveInstanceIndexBuffer[dispatchIndex];
    return instanceIndex < (uint)_InstanceCount;
}

bool TryResolvePhysicsActiveDispatchInstanceIndexReadOnly(uint dispatchIndex, out uint instanceIndex)
{
    uint activeCount = _PhysicsActiveInstanceCounterReadBuffer[0];
    if (dispatchIndex >= activeCount)
    {
        instanceIndex = CrowdVatInvalidInstanceIndex;
        return false;
    }

    instanceIndex = _PhysicsActiveInstanceIndexReadBuffer[dispatchIndex];
    return instanceIndex < (uint)_InstanceCount;
}

bool TryResolveCombatActiveDispatchInstanceIndex(uint dispatchIndex, out uint instanceIndex)
{
    uint activeCount = _CombatActiveInstanceCounterBuffer[0];
    if (dispatchIndex >= activeCount)
    {
        instanceIndex = CrowdVatInvalidInstanceIndex;
        return false;
    }

    instanceIndex = _CombatActiveInstanceIndexBuffer[dispatchIndex];
    return instanceIndex < (uint)_InstanceCount;
}

bool TryResolveCombatActiveDispatchInstanceIndexReadOnly(uint dispatchIndex, out uint instanceIndex)
{
    uint activeCount = _CombatActiveInstanceCounterReadBuffer[0];
    if (dispatchIndex >= activeCount)
    {
        instanceIndex = CrowdVatInvalidInstanceIndex;
        return false;
    }

    instanceIndex = _CombatActiveInstanceIndexReadBuffer[dispatchIndex];
    return instanceIndex < (uint)_InstanceCount;
}

bool TryResolveGridDispatchInstanceIndex(uint dispatchIndex, out uint instanceIndex)
{
    return _GridBuildMode == (int)GridBuildModeAllQueryables
        ? TryResolveAliveDispatchInstanceIndex(dispatchIndex, instanceIndex)
        : TryResolvePhysicsActiveDispatchInstanceIndex(dispatchIndex, instanceIndex);
}

bool TrySampleBakedEnvironmentDistanceOnly(float3 worldPoint, out float signedDistance);
bool TrySampleBakedEnvironmentDistanceField(float3 worldPoint, out float signedDistance, out float3 environmentNormal);
bool TrySampleEnvironmentDistanceField(float3 worldPoint, out float signedDistance, out float3 environmentNormal);

float3 GetBakedEnvironmentDistanceFieldWorldSize()
{
    return max(_EnvironmentDistanceWorldSize, float3(1e-4, 1e-4, 1e-4));
}

float3 GetBakedEnvironmentDistanceFieldWorldMin()
{
    return _EnvironmentDistanceWorldCenter - GetBakedEnvironmentDistanceFieldWorldSize() * 0.5;
}

int GetCombatEnvironmentOcclusionMaxSteps()
{
    int configuredMaxSteps = _CombatEnvironmentOcclusionMaxSteps > 0
        ? _CombatEnvironmentOcclusionMaxSteps
        : CombatEnvironmentOcclusionDefaultMaxSteps;
    return min(max(configuredMaxSteps, 1), CombatEnvironmentOcclusionHardMaxSteps);
}

float GetSafeRayDirectionComponent(float value)
{
    if (abs(value) > 1e-6)
        return value;

    return value < 0.0 ? -1e-6 : 1e-6;
}

bool TryIntersectBakedEnvironmentDistanceFieldRay(
    float3 originWorld,
    float3 rayDirectionWorld,
    float maxDistance,
    out float entryDistance,
    out float exitDistance)
{
    entryDistance = 0.0;
    exitDistance = 0.0;
    if (_EnableEnvironmentDistanceField == 0 || maxDistance <= CombatRayHitBias)
        return false;

    float3 fieldWorldSize = GetBakedEnvironmentDistanceFieldWorldSize();
    float3 fieldWorldMin = GetBakedEnvironmentDistanceFieldWorldMin();
    float3 fieldWorldMax = fieldWorldMin + fieldWorldSize;
    float3 safeDirection = float3(
        GetSafeRayDirectionComponent(rayDirectionWorld.x),
        GetSafeRayDirectionComponent(rayDirectionWorld.y),
        GetSafeRayDirectionComponent(rayDirectionWorld.z));

    float3 t0 = (fieldWorldMin - originWorld) / safeDirection;
    float3 t1 = (fieldWorldMax - originWorld) / safeDirection;
    float3 tMin = min(t0, t1);
    float3 tMax = max(t0, t1);
    float nearDistance = max(max(tMin.x, tMin.y), tMin.z);
    float farDistance = min(min(tMax.x, tMax.y), tMax.z);

    entryDistance = max(nearDistance, 0.0);
    exitDistance = min(farDistance, maxDistance);
    return farDistance >= 0.0 &&
        farDistance >= nearDistance &&
        entryDistance < maxDistance &&
        exitDistance > entryDistance;
}

bool TryTraceCombatEnvironmentSdfHit(
    float3 originWorld,
    float3 rayDirectionWorld,
    float maxDistance,
    out float hitDistanceWorld,
    out float3 hitWorld);

bool TryTraceCombatEnvironmentSdfOcclusion(
    float3 originWorld,
    float3 rayDirectionWorld,
    float maxDistance)
{
    float hitDistanceWorld;
    float3 hitWorld;
    return TryTraceCombatEnvironmentSdfHit(originWorld, rayDirectionWorld, maxDistance, hitDistanceWorld, hitWorld);
}

bool TryTraceCombatEnvironmentSdfHit(
    float3 originWorld,
    float3 rayDirectionWorld,
    float maxDistance,
    out float hitDistanceWorld,
    out float3 hitWorld)
{
    hitDistanceWorld = maxDistance;
    hitWorld = originWorld + rayDirectionWorld * maxDistance;
    if (_EnableEnvironmentDistanceField == 0 || maxDistance <= CombatRayHitBias)
        return false;

    float directionLength = length(rayDirectionWorld);
    if (directionLength <= 1e-5)
        return false;

    float queryDistance = maxDistance;
    float3 rayDirection = rayDirectionWorld / directionLength;
    float entryDistance;
    float exitDistance;
    if (!TryIntersectBakedEnvironmentDistanceFieldRay(originWorld, rayDirection, queryDistance, entryDistance, exitDistance))
        return false;

    float fallbackStep = CombatEnvironmentOcclusionMinStep;
    float clearance = max(_CombatTerrainOcclusionClearance, 0.02);
    float startBias = min(max(clearance + 0.08, 0.12), queryDistance * 0.4);
    float travel = max(startBias, entryDistance);
    float traceEnd = min(queryDistance, exitDistance);
    int maxSteps = GetCombatEnvironmentOcclusionMaxSteps();

    [loop]
    for (int stepIndex = 0; stepIndex < maxSteps && travel < traceEnd; stepIndex++)
    {
        float3 sampleWorld = originWorld + rayDirection * travel;
        float signedDistance;
        if (!TrySampleBakedEnvironmentDistanceOnly(sampleWorld, signedDistance))
        {
            travel += min(fallbackStep, traceEnd - travel);
            continue;
        }

        if (signedDistance <= clearance)
        {
            hitDistanceWorld = travel;
            hitWorld = sampleWorld;
            return true;
        }

        float stepDistance = max(
            (signedDistance - clearance) * CombatEnvironmentOcclusionStepSafety,
            CombatEnvironmentOcclusionMinStep);
        travel += min(stepDistance, traceEnd - travel);
    }

    return false;
}

bool IsCombatEnvironmentSdfOccluded(float3 originLocal, float3 targetLocal, float maxDistance)
{
    float3 originWorld = TransformLocalPointToWorld(originLocal);
    float3 targetWorld = TransformLocalPointToWorld(targetLocal);
    float3 rayVector = targetWorld - originWorld;
    float rayLength = length(rayVector);
    if (rayLength <= 1e-5)
        return false;

    return TryTraceCombatEnvironmentSdfOcclusion(originWorld, rayVector / rayLength, min(maxDistance, rayLength));
}

bool IsCombatOccluded(float3 originLocal, float3 targetLocal, float maxDistance)
{
    return IsCombatEnvironmentSdfOccluded(originLocal, targetLocal, maxDistance);
}

uint GetCombatOcclusionRejectMask(float3 originLocal, float3 targetLocal, float maxDistance)
{
    uint rejectMask = 0u;
    if (IsCombatEnvironmentSdfOccluded(originLocal, targetLocal, maxDistance))
        rejectMask |= CrowdVatTargetRejectEnvironmentSdf;
    return rejectMask;
}

float DecodeTerrainHeight01(float4 packedHeight)
{
#if defined(SHADER_API_VULKAN) || defined(SHADER_API_GLES3) || defined(SHADER_API_WEBGPU)
    return (packedHeight.r + packedHeight.g * 256.0) / 257.0;
#else
    return packedHeight.r;
#endif
}

float ComputeGroundedLocalY(float2 localXZ, float fallbackLocalY)
{
    if (_EnableTerrainCollision == 0)
        return fallbackLocalY;

    float3 worldPoint = TransformLocalPointToWorld(float3(localXZ.x, fallbackLocalY, localXZ.y));
    float2 terrainSizeXZ = max(_TerrainSize.xz, float2(1e-4, 1e-4));
    float2 terrainUV = (worldPoint.xz - _TerrainPosition.xz) / terrainSizeXZ;
    if (any(terrainUV < 0.0) || any(terrainUV > 1.0))
        return fallbackLocalY;

    float sampledHeight01 = DecodeTerrainHeight01(_TerrainHeightmap.SampleLevel(sampler_TerrainHeightmap, terrainUV, 0));
    float sampledHeight = _TerrainPosition.y
        + sampledHeight01 * _TerrainSize.y
        + _TerrainHeightOffset;
    return dot(_WorldToLocalRow1, float4(worldPoint.x, sampledHeight, worldPoint.z, 1.0));
}

float SampleTerrainWorldHeightFromUV(float2 terrainUV)
{
    float sampledHeight01 = DecodeTerrainHeight01(_TerrainHeightmap.SampleLevel(sampler_TerrainHeightmap, terrainUV, 0));
    return _TerrainPosition.y
        + sampledHeight01 * _TerrainSize.y
        + _TerrainHeightOffset;
}

bool TrySampleTerrainSurface(float2 worldXZ, out float terrainHeight, out float3 terrainNormal)
{
    terrainHeight = 0.0;
    terrainNormal = float3(0.0, 1.0, 0.0);
    if (_EnableTerrainCollision == 0)
        return false;

    float2 terrainSizeXZ = max(_TerrainSize.xz, float2(1e-4, 1e-4));
    float2 terrainUV = (worldXZ - _TerrainPosition.xz) / terrainSizeXZ;
    if (any(terrainUV < 0.0) || any(terrainUV > 1.0))
        return false;

    terrainHeight = SampleTerrainWorldHeightFromUV(terrainUV);

    uint terrainWidth;
    uint terrainHeightResolution;
    _TerrainHeightmap.GetDimensions(terrainWidth, terrainHeightResolution);
    float2 texelSize = 1.0 / max(float2((float)terrainWidth - 1.0, (float)terrainHeightResolution - 1.0), 1.0);

    float leftHeight = SampleTerrainWorldHeightFromUV(clamp(terrainUV - float2(texelSize.x, 0.0), 0.0, 1.0));
    float rightHeight = SampleTerrainWorldHeightFromUV(clamp(terrainUV + float2(texelSize.x, 0.0), 0.0, 1.0));
    float downHeight = SampleTerrainWorldHeightFromUV(clamp(terrainUV - float2(0.0, texelSize.y), 0.0, 1.0));
    float upHeight = SampleTerrainWorldHeightFromUV(clamp(terrainUV + float2(0.0, texelSize.y), 0.0, 1.0));

    float worldStepX = max(terrainSizeXZ.x * texelSize.x, 1e-4);
    float worldStepZ = max(terrainSizeXZ.y * texelSize.y, 1e-4);
    float dhdx = (rightHeight - leftHeight) / max(worldStepX * 2.0, 1e-4);
    float dhdz = (upHeight - downHeight) / max(worldStepZ * 2.0, 1e-4);
    terrainNormal = normalize(float3(-dhdx, 1.0, -dhdz));
    return true;
}

bool TrySampleTerrainDistanceField(float3 worldPoint, out float signedDistance, out float3 terrainNormal)
{
    signedDistance = 0.0;
    terrainNormal = float3(0.0, 1.0, 0.0);

    float terrainHeight;
    if (!TrySampleTerrainSurface(worldPoint.xz, terrainHeight, terrainNormal))
        return false;

    float3 terrainSurfacePoint = float3(worldPoint.x, terrainHeight, worldPoint.z);
    signedDistance = dot(worldPoint - terrainSurfacePoint, terrainNormal);
    return true;
}

float SampleStaticSdfDistanceFromUVW(float3 uvw)
{
    return _StaticSdfTexture.SampleLevel(sampler_StaticSdfTexture, uvw, 0).r * _StaticSdfDistanceScale + _StaticSdfDistanceBias;
}

bool TrySampleStaticSdfWithEnable(int enabled, float3 worldPoint, out float signedDistance, out float3 sdfNormal)
{
    signedDistance = 0.0;
    sdfNormal = float3(0.0, 1.0, 0.0);
    if (enabled == 0)
        return false;

    float3 sdfWorldSize = max(_StaticSdfWorldSize, float3(1e-4, 1e-4, 1e-4));
    float3 sdfWorldMin = _StaticSdfWorldCenter - sdfWorldSize * 0.5;
    float3 uvw = (worldPoint - sdfWorldMin) / sdfWorldSize;
    if (any(uvw < 0.0) || any(uvw > 1.0))
        return false;

    signedDistance = SampleStaticSdfDistanceFromUVW(uvw);

    uint sdfWidth;
    uint sdfHeight;
    uint sdfDepth;
    _StaticSdfTexture.GetDimensions(sdfWidth, sdfHeight, sdfDepth);
    float3 texelSize = 1.0 / max(float3((float)sdfWidth - 1.0, (float)sdfHeight - 1.0, (float)sdfDepth - 1.0), 1.0);

    float distanceXNeg = SampleStaticSdfDistanceFromUVW(clamp(uvw - float3(texelSize.x, 0.0, 0.0), 0.0, 1.0));
    float distanceXPos = SampleStaticSdfDistanceFromUVW(clamp(uvw + float3(texelSize.x, 0.0, 0.0), 0.0, 1.0));
    float distanceYNeg = SampleStaticSdfDistanceFromUVW(clamp(uvw - float3(0.0, texelSize.y, 0.0), 0.0, 1.0));
    float distanceYPos = SampleStaticSdfDistanceFromUVW(clamp(uvw + float3(0.0, texelSize.y, 0.0), 0.0, 1.0));
    float distanceZNeg = SampleStaticSdfDistanceFromUVW(clamp(uvw - float3(0.0, 0.0, texelSize.z), 0.0, 1.0));
    float distanceZPos = SampleStaticSdfDistanceFromUVW(clamp(uvw + float3(0.0, 0.0, texelSize.z), 0.0, 1.0));

    float gradientScaleX = max(sdfWorldSize.x * texelSize.x * 2.0, 1e-4);
    float gradientScaleY = max(sdfWorldSize.y * texelSize.y * 2.0, 1e-4);
    float gradientScaleZ = max(sdfWorldSize.z * texelSize.z * 2.0, 1e-4);
    float3 sdfGradient = float3(
        (distanceXPos - distanceXNeg) / gradientScaleX,
        (distanceYPos - distanceYNeg) / gradientScaleY,
        (distanceZPos - distanceZNeg) / gradientScaleZ);

    if (dot(sdfGradient, sdfGradient) < 1e-8)
    {
        float3 fallbackNormal = worldPoint - _StaticSdfWorldCenter;
        sdfNormal = dot(fallbackNormal, fallbackNormal) > 1e-8
            ? normalize(fallbackNormal)
            : float3(0.0, 1.0, 0.0);
    }
    else
    {
        sdfNormal = normalize(sdfGradient);
    }

    return true;
}

bool TrySampleStaticSdf(float3 worldPoint, out float signedDistance, out float3 sdfNormal)
{
    return TrySampleStaticSdfWithEnable(_EnableStaticSdfCollision, worldPoint, signedDistance, sdfNormal);
}

float SampleEnvironmentDistanceFromUVW(float3 uvw)
{
    return _EnvironmentDistanceFieldTexture.SampleLevel(sampler_EnvironmentDistanceFieldTexture, uvw, 0).r
        * _EnvironmentDistanceDistanceScale
        + _EnvironmentDistanceDistanceBias;
}

bool TryWorldToBakedEnvironmentDistanceUv(float3 worldPoint, out float3 uvw)
{
    uvw = 0.0;
    if (_EnableEnvironmentDistanceField == 0)
        return false;

    float3 fieldWorldSize = GetBakedEnvironmentDistanceFieldWorldSize();
    float3 fieldWorldMin = GetBakedEnvironmentDistanceFieldWorldMin();
    uvw = (worldPoint - fieldWorldMin) / fieldWorldSize;
    if (any(uvw < 0.0) || any(uvw > 1.0))
        return false;

    return true;
}

bool TrySampleBakedEnvironmentDistanceOnly(float3 worldPoint, out float signedDistance)
{
    signedDistance = 0.0;
    float3 uvw;
    if (!TryWorldToBakedEnvironmentDistanceUv(worldPoint, uvw))
        return false;

    signedDistance = SampleEnvironmentDistanceFromUVW(uvw);
    return true;
}

bool TrySampleBakedEnvironmentDistanceField(float3 worldPoint, out float signedDistance, out float3 environmentNormal)
{
    signedDistance = 0.0;
    environmentNormal = float3(0.0, 1.0, 0.0);
    float3 uvw;
    if (!TryWorldToBakedEnvironmentDistanceUv(worldPoint, uvw))
        return false;

    signedDistance = SampleEnvironmentDistanceFromUVW(uvw);

    uint fieldWidth;
    uint fieldHeight;
    uint fieldDepth;
    _EnvironmentDistanceFieldTexture.GetDimensions(fieldWidth, fieldHeight, fieldDepth);
    float3 fieldWorldSize = GetBakedEnvironmentDistanceFieldWorldSize();
    float3 texelSize = 1.0 / max(float3((float)fieldWidth - 1.0, (float)fieldHeight - 1.0, (float)fieldDepth - 1.0), 1.0);

    float distanceXNeg = SampleEnvironmentDistanceFromUVW(clamp(uvw - float3(texelSize.x, 0.0, 0.0), 0.0, 1.0));
    float distanceXPos = SampleEnvironmentDistanceFromUVW(clamp(uvw + float3(texelSize.x, 0.0, 0.0), 0.0, 1.0));
    float distanceYNeg = SampleEnvironmentDistanceFromUVW(clamp(uvw - float3(0.0, texelSize.y, 0.0), 0.0, 1.0));
    float distanceYPos = SampleEnvironmentDistanceFromUVW(clamp(uvw + float3(0.0, texelSize.y, 0.0), 0.0, 1.0));
    float distanceZNeg = SampleEnvironmentDistanceFromUVW(clamp(uvw - float3(0.0, 0.0, texelSize.z), 0.0, 1.0));
    float distanceZPos = SampleEnvironmentDistanceFromUVW(clamp(uvw + float3(0.0, 0.0, texelSize.z), 0.0, 1.0));

    float gradientScaleX = max(fieldWorldSize.x * texelSize.x * 2.0, 1e-4);
    float gradientScaleY = max(fieldWorldSize.y * texelSize.y * 2.0, 1e-4);
    float gradientScaleZ = max(fieldWorldSize.z * texelSize.z * 2.0, 1e-4);
    float3 fieldGradient = float3(
        (distanceXPos - distanceXNeg) / gradientScaleX,
        (distanceYPos - distanceYNeg) / gradientScaleY,
        (distanceZPos - distanceZNeg) / gradientScaleZ);

    if (dot(fieldGradient, fieldGradient) < 1e-8)
    {
        float3 fallbackNormal = worldPoint - _EnvironmentDistanceWorldCenter;
        environmentNormal = dot(fallbackNormal, fallbackNormal) > 1e-8
            ? normalize(fallbackNormal)
            : float3(0.0, 1.0, 0.0);
    }
    else
    {
        environmentNormal = normalize(fieldGradient);
    }

    return true;
}

bool TrySampleEnvironmentDistanceField(float3 worldPoint, out float signedDistance, out float3 environmentNormal)
{
    signedDistance = 0.0;
    environmentNormal = float3(0.0, 1.0, 0.0);

    if (TrySampleBakedEnvironmentDistanceField(worldPoint, signedDistance, environmentNormal))
        return true;

    float terrainSignedDistance;
    float3 terrainNormal;
    bool hasTerrain = TrySampleTerrainDistanceField(worldPoint, terrainSignedDistance, terrainNormal);

    float obstacleSignedDistance;
    float3 obstacleNormal;
    bool hasObstacle = TrySampleStaticSdf(worldPoint, obstacleSignedDistance, obstacleNormal);

    if (!hasTerrain && !hasObstacle)
        return false;

    if (hasTerrain && (!hasObstacle || terrainSignedDistance <= obstacleSignedDistance))
    {
        signedDistance = terrainSignedDistance;
        environmentNormal = terrainNormal;
        return true;
    }

    signedDistance = obstacleSignedDistance;
    environmentNormal = obstacleNormal;
    return true;
}

float ComputeClipTime(InstanceSpawnData spawnData)
{
    return spawnData.normalizedTimeOffset * _ClipLength
        + _PlaybackTime * _BasePlaybackSpeed * spawnData.playbackSpeedMultiplier;
}

AnimationClipGpuData BuildFallbackClipData()
{
    AnimationClipGpuData clip;
    clip.startFrame = _ClipStartFrame;
    clip.frameCount = max(_ClipFrameCount, 1);
    clip.lengthSeconds = max(_ClipLength, 0.0);
    clip.loop = _ClipLoop;
    return clip;
}

bool TryGetAnimationClipData(int clipIndex, out AnimationClipGpuData clip)
{
    if (clipIndex < 0 || clipIndex >= _AnimationClipCount)
    {
        clip = BuildFallbackClipData();
        return false;
    }

    clip = _AnimationClipMetadataBuffer[clipIndex];
    return clip.frameCount > 0;
}

float4 BuildAnimationFrameData(AnimationClipGpuData clip, float clipTime)
{
    int clipFrameCount = max(clip.frameCount, 1);
    if (clipFrameCount <= 1)
        return float4(clip.startFrame, clip.startFrame, 0.0, 0.0);

    float clipLength = max(clip.lengthSeconds, 1e-5);
    bool shouldLoop = clip.loop != 0;

    if (shouldLoop)
        clipTime = fmod(max(clipTime, 0.0), clipLength);
    else
        clipTime = clamp(clipTime, 0.0, clipLength);

    float normalizedTime = shouldLoop
        ? clipTime / clipLength
        : saturate(clipTime / clipLength);

    float frameFloat = shouldLoop
        ? normalizedTime * clipFrameCount
        : normalizedTime * (clipFrameCount - 1);

    int localFrame0 = clamp((int)floor(frameFloat), 0, clipFrameCount - 1);
    int localFrame1 = shouldLoop
        ? (localFrame0 + 1) % clipFrameCount
        : min(localFrame0 + 1, clipFrameCount - 1);
    float frameBlend = saturate(frameFloat - localFrame0);

    return float4(
        clip.startFrame + localFrame0,
        clip.startFrame + localFrame1,
        frameBlend,
        0.0);
}

float2 GetLocalXZ(float4 localPositionAndYaw)
{
    return float2(localPositionAndYaw.x, localPositionAndYaw.z);
}

float2 SafeNormalize2D(float2 value, float2 fallbackValue)
{
    float lengthSquared = dot(value, value);
    if (lengthSquared < 1e-8)
        return fallbackValue;

    return value * rsqrt(lengthSquared);
}

uint DecodeRuntimeUInt(float encodedValue)
{
    return (uint)max(0.0, floor(encodedValue + 0.5));
}

float2 Rotate2D(float2 localOffset, float2 forwardXZ)
{
    float2 safeForward = SafeNormalize2D(forwardXZ, float2(0.0, 1.0));
    float2 right = float2(safeForward.y, -safeForward.x);
    return right * localOffset.x + safeForward * localOffset.y;
}

bool TryGetCellCoord(float2 positionXZ, out int2 coord)
{
    coord = 0;
    if (any(positionXZ < _GridMinXZ) || any(positionXZ >= _GridMaxXZ))
        return false;

    coord = (int2)floor((positionXZ - _GridMinXZ) * _InvCellSize);
    coord = clamp(coord, 0, _GridDim - 1);
    return true;
}

int2 GetClampedCellCoord(float2 positionXZ)
{
    int2 coord = (int2)floor((positionXZ - _GridMinXZ) * _InvCellSize);
    return clamp(coord, 0, _GridDim - 1);
}

bool TryGetCellRange(float2 minXZ, float2 maxXZ, out int2 minCoord, out int2 maxCoord)
{
    minCoord = 0;
    maxCoord = 0;

    if (any(maxXZ < _GridMinXZ) || any(minXZ >= _GridMaxXZ))
        return false;

    float2 clampedMin = max(minXZ, _GridMinXZ);
    float2 clampedMax = min(maxXZ, _GridMaxXZ - 1e-4);
    minCoord = GetClampedCellCoord(clampedMin);
    maxCoord = GetClampedCellCoord(clampedMax);
    return true;
}

uint GetCellKey(int2 coord)
{
    return (uint)(coord.x + coord.y * _GridDim.x);
}

float2 GetCellCenterXZ(int2 coord)
{
    return _GridMinXZ + (float2((float)coord.x, (float)coord.y) + 0.5) * _CellSize;
}

bool IsGridCoordValid(int2 coord)
{
    return coord.x >= 0 &&
        coord.y >= 0 &&
        coord.x < _GridDim.x &&
        coord.y < _GridDim.y;
}

bool TryGetWakeGridCellCoord(float2 positionXZ, out int2 coord)
{
    if (any(positionXZ < _GridMinXZ) || any(positionXZ >= _GridMaxXZ))
    {
        coord = int2(0, 0);
        return false;
    }

    coord = (int2)floor((positionXZ - _GridMinXZ) * _WakeInvGridCellSize);
    coord = clamp(coord, 0, _WakeGridDim - 1);
    return true;
}

uint GetWakeGridCellKey(int2 coord)
{
    return (uint)(coord.x + coord.y * _WakeGridDim.x);
}

bool IsWakeGridCoordValid(int2 coord)
{
    return coord.x >= 0 &&
        coord.y >= 0 &&
        coord.x < _WakeGridDim.x &&
        coord.y < _WakeGridDim.y;
}

bool ShouldMoveForSquadCommand(uint commandType)
{
    return commandType == CrowdVatSquadCommandTypeMoveTo ||
        commandType == CrowdVatSquadCommandTypeAdvance ||
        commandType == CrowdVatSquadCommandTypeCharge ||
        commandType == CrowdVatSquadCommandTypeRetreat ||
        commandType == CrowdVatSquadCommandTypeRegroup;
}

float2 ResolveSquadCommandVelocity(float2 desiredDirectionXZ, float2 fallbackDirectionXZ, float moveSpeed)
{
    if (moveSpeed <= 1e-4)
        return 0.0;

    float2 direction = SafeNormalize2D(desiredDirectionXZ, fallbackDirectionXZ);
    return direction * moveSpeed;
}

bool TryResolveRuntimeSquadMotionXZ(
    uint instanceIndex,
    float2 staticAnchorXZ,
    float2 currentXZ,
    out float2 anchorXZ,
    out float2 formationCenterXZ,
    out float2 commandVelocityXZ,
    out float formationDeviationRadius,
    out float squadProjectionStrength,
    out float2 desiredForwardXZ)
{
    anchorXZ = staticAnchorXZ;
    formationCenterXZ = staticAnchorXZ;
    commandVelocityXZ = 0.0;
    formationDeviationRadius = max(_MaxDisplacementFromSpawn, 1e-4);
    squadProjectionStrength = 0.0;
    desiredForwardXZ = 0.0;

    if (_EnableRuntimeSquadAnchors == 0 ||
        _SquadStateCount <= 0 ||
        _AgentSquadDataCount <= 0 ||
        instanceIndex >= (uint)_AgentSquadDataCount)
    {
        return false;
    }

    AgentSquadData agentData = _AgentSquadDataBuffer[instanceIndex];
    if (agentData.squadAndSlotAndRoleAndFlags.x < 0.0)
        return false;

    uint squadIndex = DecodeRuntimeUInt(agentData.squadAndSlotAndRoleAndFlags.x);
    if (squadIndex >= (uint)_SquadStateCount)
        return false;

    SquadStateData squadState = _SquadStateBuffer[squadIndex];
    float2 squadCenterXZ = squadState.localCenterAndAnchorBlend.xz;
    float2 squadForwardXZ = SafeNormalize2D(squadState.localForwardAndMoveSpeed.xz, float2(0.0, 1.0));
    float moveSpeed = max(0.0, squadState.localForwardAndMoveSpeed.w);
    uint commandType = DecodeRuntimeUInt(squadState.metadata0.z);

    uint squadFlags = DecodeRuntimeUInt(squadState.metadata1.x);
    anchorXZ = currentXZ;
    formationCenterXZ = currentXZ;
    formationDeviationRadius = max(_MaxDisplacementFromSpawn, 1e-4);
    squadProjectionStrength = 0.0;
    desiredForwardXZ = squadForwardXZ;

    uint slotIndex = DecodeRuntimeUInt(agentData.squadAndSlotAndRoleAndFlags.y);
    uint memberFlags = DecodeRuntimeUInt(agentData.squadAndSlotAndRoleAndFlags.w);
    if (_UseRuntimeFormationSlots != 0 &&
        _FormationSlotCount > 0 &&
        slotIndex < (uint)_FormationSlotCount)
    {
        FormationSlotData formationSlot = _FormationSlotBuffer[slotIndex];
        float2 slotOffsetXZ = formationSlot.localOffsetAndRoleAndRank.xy;
        if ((memberFlags & CrowdVatSquadMemberFlagUseSlotOffsetOverride) != 0u)
            slotOffsetXZ += agentData.slotOffsetAndWeight.xy;

        anchorXZ = squadCenterXZ + Rotate2D(slotOffsetXZ, squadForwardXZ);
        formationCenterXZ = squadCenterXZ;
        squadProjectionStrength = saturate(squadState.localCenterAndAnchorBlend.w);
    }

    float2 targetDirectionXZ = squadState.localTargetAndSpacingX.xz - squadCenterXZ;
    if (ShouldMoveForSquadCommand(commandType) && moveSpeed > 0.0)
    {
        float2 commandDirectionXZ = commandType == CrowdVatSquadCommandTypeRetreat
            ? -targetDirectionXZ
            : targetDirectionXZ;
        commandVelocityXZ = ResolveSquadCommandVelocity(commandDirectionXZ, squadForwardXZ, moveSpeed);
    }

    if ((squadFlags & CrowdVatSquadFlagFaceTarget) != 0u || commandType == CrowdVatSquadCommandTypeMoveTo)
        desiredForwardXZ = SafeNormalize2D(targetDirectionXZ, desiredForwardXZ);

    if ((squadFlags & CrowdVatSquadFlagFaceMovement) != 0u)
    {
        float2 movementForwardXZ = dot(commandVelocityXZ, commandVelocityXZ) > 1e-6
            ? commandVelocityXZ
            : anchorXZ - currentXZ;
        desiredForwardXZ = SafeNormalize2D(movementForwardXZ, desiredForwardXZ);
    }

    return true;
}

float ResolveFacingYawRadians(
    float currentYawRadians,
    bool hasRuntimeSquadAnchor,
    float2 desiredForwardXZ,
    float2 commandVelocityXZ,
    float2 displacementVelocityXZ)
{
    bool hasDesiredForward = dot(desiredForwardXZ, desiredForwardXZ) > CrowdVatFacingVelocityThresholdSqr;
    bool hasCommandVelocity = dot(commandVelocityXZ, commandVelocityXZ) > CrowdVatFacingVelocityThresholdSqr;
    bool hasDisplacementVelocity = dot(displacementVelocityXZ, displacementVelocityXZ) > CrowdVatFacingVelocityThresholdSqr;

    if (hasRuntimeSquadAnchor)
    {
        if (hasDesiredForward)
            return atan2(desiredForwardXZ.x, desiredForwardXZ.y);

        if (hasCommandVelocity)
            return atan2(commandVelocityXZ.x, commandVelocityXZ.y);

        return currentYawRadians;
    }

    if (hasDisplacementVelocity)
        return atan2(displacementVelocityXZ.x, displacementVelocityXZ.y);

    return currentYawRadians;
}

bool TryGetAssignedRuntimeSquadIndex(uint instanceIndex, out uint squadIndex)
{
    squadIndex = 0u;

    if (_EnableRuntimeSquadAnchors == 0 ||
        _SquadStateCount <= 0 ||
        _AgentSquadDataCount <= 0 ||
        instanceIndex >= (uint)_AgentSquadDataCount)
    {
        return false;
    }

    AgentSquadData agentData = _AgentSquadDataBuffer[instanceIndex];
    if (agentData.squadAndSlotAndRoleAndFlags.x < 0.0)
        return false;

    squadIndex = DecodeRuntimeUInt(agentData.squadAndSlotAndRoleAndFlags.x);
    return squadIndex < (uint)_SquadStateCount;
}

bool IsRuntimeInstanceVisibleForRender(uint instanceIndex)
{
    if (_VisibleRuntimeSquadCount <= 0)
        return _VisibleUnassignedInstances != 0;

    if (_VisibleUnassignedInstances != 0 &&
        (_AgentSquadDataCount <= 0 || instanceIndex >= (uint)_AgentSquadDataCount))
    {
        return true;
    }

    if (_AgentSquadDataCount <= 0 || instanceIndex >= (uint)_AgentSquadDataCount)
        return false;

    AgentSquadData agentData = _AgentSquadDataBuffer[instanceIndex];
    if (agentData.squadAndSlotAndRoleAndFlags.x < 0.0)
        return _VisibleUnassignedInstances != 0;

    uint squadIndex = DecodeRuntimeUInt(agentData.squadAndSlotAndRoleAndFlags.x);
    if (squadIndex >= (uint)_VisibleRuntimeSquadCount)
        return _VisibleUnassignedInstances != 0;

    return _VisibleRuntimeSquadMaskBuffer[squadIndex] != 0u;
}

float2 FallbackDirection(uint instanceIndex, uint neighborIndex)
{
    float angle = ((instanceIndex * 92821u + neighborIndex * 68917u) & 1023u) * 0.00613592315;
    return float2(cos(angle), sin(angle));
}

float2 PairFallbackDirection(uint instanceIndex, uint neighborIndex)
{
    uint lowerIndex = min(instanceIndex, neighborIndex);
    uint upperIndex = max(instanceIndex, neighborIndex);
    float angle = ((lowerIndex * 92821u + upperIndex * 68917u) & 1023u) * 0.00613592315;
    float2 direction = float2(cos(angle), sin(angle));
    return instanceIndex == lowerIndex ? direction : -direction;
}

void AccumulateCrowdPbdCorrection(
    float2 pairCorrection,
    inout float2 accumulatedCorrection,
    inout float2 strongestCorrection,
    inout float strongestCorrectionLengthSquared,
    inout float correctionCount)
{
    accumulatedCorrection += pairCorrection;
    float pairCorrectionLengthSquared = dot(pairCorrection, pairCorrection);
    if (pairCorrectionLengthSquared > strongestCorrectionLengthSquared)
    {
        strongestCorrection = pairCorrection;
        strongestCorrectionLengthSquared = pairCorrectionLengthSquared;
    }

    correctionCount += 1.0;
}

float2 ResolveCrowdPbdCorrection(
    float2 accumulatedCorrection,
    float2 strongestCorrection,
    float strongestCorrectionLengthSquared,
    float maxPushPerStep)
{
    float2 correction = accumulatedCorrection;
    float accumulatedCorrectionLengthSquared = dot(correction, correction);
    if (strongestCorrectionLengthSquared > 0.0 &&
        accumulatedCorrectionLengthSquared < strongestCorrectionLengthSquared * 0.25)
    {
        correction = strongestCorrection;
    }

    float correctionLength = length(correction);
    float safeMaxPush = max(maxPushPerStep, 0.01);
    if (correctionLength > safeMaxPush)
        correction *= safeMaxPush / max(correctionLength, 1e-4);

    return correction;
}

float ComputeInstanceRadius(float uniformScale)
{
    return max(0.01, _CollisionRadius * max(uniformScale, 0.01));
}

float ComputeInstanceCapsuleHeight(float uniformScale)
{
    float radius = ComputeInstanceRadius(uniformScale);
    return max(radius * 2.0, _CapsuleHeight * max(uniformScale, 0.01));
}

float ComputeCombatHitRadius(float uniformScale)
{
    float safeScale = max(uniformScale, 0.01);
    float baseRadius = _CollisionRadius * safeScale;
    float paddedRadius = (_CollisionRadius + max(_CombatHitRadiusPadding, 0.0)) * safeScale;
    return max(0.01, max(baseRadius, paddedRadius));
}

float ComputeCombatHitCapsuleHeight(float uniformScale, float hitRadius)
{
    float safeScale = max(uniformScale, 0.01);
    float paddedHeight = (_CapsuleHeight + max(_CombatHitHeightPadding, 0.0)) * safeScale;
    return max(hitRadius * 2.0, paddedHeight);
}

float ComputeEnvironmentGroundSnapMaxDistance(float uniformScale)
{
    return max(0.5, _CapsuleHeight * max(uniformScale, 0.01) * 6.0);
}

uint ClassifyEnvironmentSurface(float3 environmentNormal)
{
    if (environmentNormal.y >= EnvironmentGroundNormalYThreshold)
        return EnvironmentSurfaceGround;

    if (environmentNormal.y <= EnvironmentCeilingNormalYThreshold)
        return EnvironmentSurfaceCeiling;

    return EnvironmentSurfaceWall;
}

void RemoveVelocityIntoEnvironmentSurface(float3 environmentNormal, uint surfaceType, inout float2 velocityXZ)
{
    if (surfaceType == EnvironmentSurfaceWall)
    {
        float3 localNormal = TransformWorldDirectionToLocal(environmentNormal);
        float2 wallNormalXZ = localNormal.xz;
        float normalLengthSquared = dot(wallNormalXZ, wallNormalXZ);
        if (normalLengthSquared <= 1e-8)
            return;

        wallNormalXZ *= rsqrt(normalLengthSquared);
        float velocityIntoWall = dot(velocityXZ, wallNormalXZ);
        if (velocityIntoWall < 0.0)
            velocityXZ -= wallNormalXZ * velocityIntoWall;
    }
}

float3 ComputeCapsuleBottom(float3 localPosition, float radius)
{
    return localPosition + float3(0.0, radius, 0.0);
}

float3 ComputeCapsuleTop(float3 localPosition, float uniformScale, float radius)
{
    float capsuleHeight = ComputeInstanceCapsuleHeight(uniformScale);
    return localPosition + float3(0.0, capsuleHeight - radius, 0.0);
}

float3 ComputeCombatHitCapsuleTop(float3 localPosition, float uniformScale, float hitRadius)
{
    float hitCapsuleHeight = ComputeCombatHitCapsuleHeight(uniformScale, hitRadius);
    return localPosition + float3(0.0, hitCapsuleHeight - hitRadius, 0.0);
}

bool ComputeEnvironmentConstraintCorrection(
    float3 sampleLocalCenter,
    float sphereRadius,
    float uniformScale,
    bool allowGroundSnap,
    out float3 correctionLocal,
    out float3 environmentNormal,
    out uint surfaceType)
{
    correctionLocal = 0.0;
    environmentNormal = float3(0.0, 1.0, 0.0);
    surfaceType = EnvironmentSurfaceNone;
    float signedDistance;
    if (!TrySampleEnvironmentDistanceField(TransformLocalPointToWorld(sampleLocalCenter), signedDistance, environmentNormal))
        return false;

    surfaceType = ClassifyEnvironmentSurface(environmentNormal);
    if (signedDistance >= sphereRadius)
    {
        float snapDistance = signedDistance - sphereRadius;
        if (!allowGroundSnap ||
            surfaceType != EnvironmentSurfaceGround ||
            snapDistance <= EnvironmentGroundSnapEpsilon ||
            snapDistance > ComputeEnvironmentGroundSnapMaxDistance(uniformScale))
        {
            return false;
        }

        float3 correctionWorld = -environmentNormal * min(snapDistance, _MaxPushPerStep);
        correctionLocal = TransformWorldDirectionToLocal(correctionWorld);
        return dot(correctionLocal, correctionLocal) > 1e-10;
    }

    float penetration = sphereRadius - signedDistance;
    float3 correctionWorld = environmentNormal * min(penetration, _MaxPushPerStep);
    correctionLocal = TransformWorldDirectionToLocal(correctionWorld);
    return dot(correctionLocal, correctionLocal) > 1e-10;
}

float3 ResolveEnvironmentConstraints(float3 localPosition, float uniformScale, inout float2 velocityXZ)
{
    if (_EnableEnvironmentDistanceField == 0 && _EnableTerrainCollision == 0 && _EnableStaticSdfCollision == 0)
        return localPosition;

    float radius = ComputeInstanceRadius(uniformScale);
    int sampleCount = clamp(_CapsulePbdSampleCount, 2, 8);

    [loop]
    for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
    {
        float sampleT = sampleCount <= 1
            ? 0.0
            : sampleIndex / max((float)(sampleCount - 1), 1.0);

        [unroll]
        for (int environmentSubstep = 0; environmentSubstep < 2; environmentSubstep++)
        {
            float3 capsuleStart = ComputeCapsuleBottom(localPosition, radius);
            float3 capsuleEnd = ComputeCapsuleTop(localPosition, uniformScale, radius);
            float3 sampleLocalCenter = lerp(capsuleStart, capsuleEnd, sampleT);

            float3 correctionLocal;
            float3 environmentNormal;
            uint surfaceType;
            if (!ComputeEnvironmentConstraintCorrection(
                sampleLocalCenter,
                radius,
                uniformScale,
                sampleIndex == 0,
                correctionLocal,
                environmentNormal,
                surfaceType))
            {
                break;
            }

            localPosition += correctionLocal;
            RemoveVelocityIntoEnvironmentSurface(environmentNormal, surfaceType, velocityXZ);
        }
    }

    return localPosition;
}

float2 ClampToSimulationBounds(float2 positionXZ, float radius)
{
    float2 minXZ = _GridMinXZ + radius;
    float2 maxXZ = _GridMaxXZ - radius;
    return clamp(positionXZ, minXZ, maxXZ);
}

float2 ClampDisplacementToAnchor(float2 predictedXZ, float2 referenceCenterXZ, float maxDistance)
{
    float2 delta = predictedXZ - referenceCenterXZ;
    float distanceSquared = dot(delta, delta);
    maxDistance = max(maxDistance, 1e-4);
    if (distanceSquared <= maxDistance * maxDistance)
        return predictedXZ;

    float distance = sqrt(distanceSquared);
    return referenceCenterXZ + delta * (maxDistance / max(distance, 1e-4));
}

bool IsWithinBubble(float2 positionXZ, float2 centerXZ, float radius)
{
    float safeRadius = max(radius, 1e-4);
    float2 delta = positionXZ - centerXZ;
    return dot(delta, delta) <= safeRadius * safeRadius;
}

InstanceSimulationState BuildState(float3 localPosition, float yawRadians, float uniformScale, float2 velocityXZ)
{
    InstanceSimulationState state;
    state.localPositionAndYaw = float4(localPosition, yawRadians);
    state.scaleAndVelocity = float4(uniformScale, velocityXZ.x, velocityXZ.y, 0.0);
    return state;
}

InstanceSimulationState LoadSimulationState(uint instanceIndex)
{
    InstanceSimulationState state;
    state.localPositionAndYaw = _SimulationPositionYawReadBuffer[instanceIndex];
    float2 velocityXZ = _SimulationVelocityReadBuffer[instanceIndex];
    state.scaleAndVelocity = float4(
        _SimulationScaleReadBuffer[instanceIndex],
        velocityXZ.x,
        velocityXZ.y,
        0.0);
    return state;
}

void StoreSimulationState(uint instanceIndex, InstanceSimulationState state)
{
    _SimulationPositionYawWriteBuffer[instanceIndex] = state.localPositionAndYaw;
    _SimulationScaleWriteBuffer[instanceIndex] = state.scaleAndVelocity.x;
    _SimulationVelocityWriteBuffer[instanceIndex] = state.scaleAndVelocity.yz;
}

void CopySimulationState(uint instanceIndex)
{
    _SimulationPositionYawWriteBuffer[instanceIndex] = _SimulationPositionYawReadBuffer[instanceIndex];
    _SimulationScaleWriteBuffer[instanceIndex] = _SimulationScaleReadBuffer[instanceIndex];
    _SimulationVelocityWriteBuffer[instanceIndex] = _SimulationVelocityReadBuffer[instanceIndex];
}

float2 ResolveLocalAvoidanceVelocity(
    uint instanceIndex,
    float2 currentXZ,
    float instanceRadius,
    float2 desiredVelocityXZ)
{
    if (_EnableLocalAvoidance == 0 || _EnableApproximateCollision == 0)
        return desiredVelocityXZ;

    float desiredSpeed = length(desiredVelocityXZ);
    if (desiredSpeed <= 1e-4)
        return desiredVelocityXZ;

    int2 originCoord;
    if (!TryGetWakeGridCellCoord(currentXZ, originCoord))
        return desiredVelocityXZ;

    float2 forward = desiredVelocityXZ / max(desiredSpeed, 1e-4);
    float2 right = float2(forward.y, -forward.x);
    float avoidanceRadius = max(_LocalAvoidanceRadius, instanceRadius * 2.5);
    float avoidanceRadiusSquared = avoidanceRadius * avoidanceRadius;
    float lateralPressure = 0.0;
    uint candidateChecks = 0u;
    uint maxCandidateChecks = (uint)max(_LocalAvoidanceMaxNeighbors, 1);
    int neighborCellRange = min(max(1, (int)ceil(avoidanceRadius * _WakeInvGridCellSize)), 6);

    [loop]
    for (int y = -neighborCellRange; y <= neighborCellRange; y++)
    {
        if (candidateChecks >= maxCandidateChecks)
            break;

        [loop]
        for (int x = -neighborCellRange; x <= neighborCellRange; x++)
        {
            if (candidateChecks >= maxCandidateChecks)
                break;

            int2 neighborCoord = originCoord + int2(x, y);
            if (!IsWakeGridCoordValid(neighborCoord))
                continue;

            uint cellKey = GetWakeGridCellKey(neighborCoord);
            uint occupantCount = min(_WakeGridCounterBuffer[cellKey], (uint)_MaxCellOccupancy);
            [loop]
            for (uint occupantIndex = 0u; occupantIndex < occupantCount; occupantIndex++)
            {
                if (candidateChecks >= maxCandidateChecks)
                    break;

                uint neighborIndex = _WakeGridOccupantBuffer[cellKey * (uint)_MaxCellOccupancy + occupantIndex];
                if (neighborIndex == instanceIndex || neighborIndex >= (uint)_InstanceCount || IsInstanceDead(neighborIndex))
                    continue;

                candidateChecks += 1u;
                InstanceSimulationState neighborState = LoadSimulationState(neighborIndex);
                float2 neighborXZ = GetLocalXZ(neighborState.localPositionAndYaw);
                float neighborRadius = ComputeInstanceRadius(neighborState.scaleAndVelocity.x);
                float combinedRadius = instanceRadius + neighborRadius;
                float2 toNeighbor = neighborXZ - currentXZ;
                float distanceSquared = dot(toNeighbor, toNeighbor);
                if (distanceSquared <= 1e-8 || distanceSquared > avoidanceRadiusSquared)
                    continue;

                float distance = sqrt(distanceSquared);
                float forwardDistance = dot(toNeighbor, forward);
                if (forwardDistance < -combinedRadius)
                    continue;

                float sideDistance = dot(toNeighbor, right);
                float distanceWeight = saturate(1.0 - distance / max(avoidanceRadius, 1e-4));
                float forwardWeight = saturate((forwardDistance + combinedRadius) / max(avoidanceRadius, 1e-4));
                float neighborWeight = distanceWeight * max(forwardWeight, 0.15);

                float sideSign = sideDistance >= 0.0 ? -1.0 : 1.0;
                if (abs(sideDistance) < instanceRadius * 0.25)
                    sideSign = dot(FallbackDirection(instanceIndex, neighborIndex), right) >= 0.0 ? 1.0 : -1.0;

                lateralPressure += sideSign * neighborWeight;
            }
        }
    }

    float lateralScale = clamp(lateralPressure * saturate(_LocalAvoidanceStrength), -1.0, 1.0);
    float2 adjustedVelocityXZ = forward * desiredSpeed + right * lateralScale * desiredSpeed;
    float maxAdjustedSpeed = desiredSpeed * (1.0 + saturate(_LocalAvoidanceStrength) * 0.35);
    float adjustedSpeed = length(adjustedVelocityXZ);
    if (adjustedSpeed > maxAdjustedSpeed)
        adjustedVelocityXZ *= maxAdjustedSpeed / max(adjustedSpeed, 1e-4);

    return adjustedVelocityXZ;
}

bool HasPhysicsActiveNeighbor(uint instanceIndex, float2 currentXZ)
{
    int2 originCoord;
    if (!TryGetWakeGridCellCoord(currentXZ, originCoord))
        return false;

    float neighborRadius = max(_PhysicsWakeNeighborRadius, _CollisionRadius * 2.0);
    float neighborRadiusSquared = neighborRadius * neighborRadius;
    int neighborCellRange = min(max(1, (int)ceil(neighborRadius * _WakeInvGridCellSize)), 6);

    [loop]
    for (int y = -neighborCellRange; y <= neighborCellRange; y++)
    {
        [loop]
        for (int x = -neighborCellRange; x <= neighborCellRange; x++)
        {
            int2 neighborCoord = originCoord + int2(x, y);
            if (!IsWakeGridCoordValid(neighborCoord))
                continue;

            uint cellKey = GetWakeGridCellKey(neighborCoord);
            uint occupantCount = min(_WakeGridCounterBuffer[cellKey], (uint)_MaxCellOccupancy);
            [loop]
            for (uint occupantIndex = 0u; occupantIndex < occupantCount; occupantIndex++)
            {
                uint neighborIndex = _WakeGridOccupantBuffer[cellKey * (uint)_MaxCellOccupancy + occupantIndex];
                if (neighborIndex == instanceIndex || neighborIndex >= (uint)_InstanceCount || IsInstanceDead(neighborIndex))
                    continue;

                if (_PhysicsActiveStateBuffer[neighborIndex] == 0u)
                    continue;

                InstanceSimulationState neighborState = LoadSimulationState(neighborIndex);
                float2 neighborXZ = GetLocalXZ(neighborState.localPositionAndYaw);
                float2 delta = neighborXZ - currentXZ;
                if (dot(delta, delta) <= neighborRadiusSquared)
                    return true;
            }
        }
    }

    return false;
}

SpatialElementData BuildSpatialElement(uint instanceIndex, InstanceSpawnData spawnData, InstanceSimulationState state, uint isPhysicsActive)
{
    SpatialElementData element;
    float radius = ComputeInstanceRadius(state.scaleAndVelocity.x);
    float3 localPosition = state.localPositionAndYaw.xyz;
    element.localCapsuleStartAndRadius = float4(
        ComputeCapsuleBottom(localPosition, radius),
        radius);
    element.localCapsuleEndAndHeight = float4(
        ComputeCapsuleTop(localPosition, state.scaleAndVelocity.x, radius),
        ComputeInstanceCapsuleHeight(state.scaleAndVelocity.x));
    element.ownerIndex = instanceIndex;
    element.targetMask = CrowdVatSpatialTargetMaskCrowdAgent;
    element.flags = isPhysicsActive != 0u ? CrowdVatSpatialElementFlagActive : 0u;
    element.faction = spawnData.faction;
    return element;
}

SpatialElementData BuildEmptySpatialElement()
{
    SpatialElementData element;
    element.localCapsuleStartAndRadius = 0.0;
    element.localCapsuleEndAndHeight = 0.0;
    element.ownerIndex = 0u;
    element.targetMask = 0u;
    element.flags = 0u;
    element.faction = 0u;
    return element;
}

SpatialElementData LoadSpatialElement(uint instanceIndex)
{
    SpatialElementData element;
    element.localCapsuleStartAndRadius = _SpatialCapsuleStartRadiusBuffer[instanceIndex];
    element.localCapsuleEndAndHeight = _SpatialCapsuleEndHeightBuffer[instanceIndex];
    element.ownerIndex = _SpatialOwnerIndexBuffer[instanceIndex];
    element.targetMask = _SpatialTargetMaskBuffer[instanceIndex];
    element.flags = _SpatialFlagsBuffer[instanceIndex];
    element.faction = _SpatialFactionBuffer[instanceIndex];
    return element;
}

void StoreSpatialElement(uint instanceIndex, SpatialElementData element)
{
    _SpatialCapsuleStartRadiusBuffer[instanceIndex] = element.localCapsuleStartAndRadius;
    _SpatialCapsuleEndHeightBuffer[instanceIndex] = element.localCapsuleEndAndHeight;
    _SpatialOwnerIndexBuffer[instanceIndex] = element.ownerIndex;
    _SpatialTargetMaskBuffer[instanceIndex] = element.targetMask;
    _SpatialFlagsBuffer[instanceIndex] = element.flags;
    _SpatialFactionBuffer[instanceIndex] = element.faction;
}

bool ShouldInsertIntoGrid(uint isActive)
{
    return _GridBuildMode == (int)GridBuildModeAllQueryables || isActive != 0u;
}

bool AreFactionsHostile(uint sourceFaction, uint targetFaction)
{
    return sourceFaction != CrowdVatFactionMaskNone &&
        targetFaction != CrowdVatFactionMaskNone &&
        (sourceFaction & targetFaction) == CrowdVatFactionMaskNone;
}

float ComputeCombatTargetTopHeight(float uniformScale)
{
    float safeScale = max(uniformScale, 0.01);
    float requestedHeight = _CombatTargetHeight * safeScale;
    return max(requestedHeight, 0.0);
}

float3 BuildCombatTargetTopLocal(InstanceSimulationState state)
{
    return state.localPositionAndYaw.xyz + float3(0.0, ComputeCombatTargetTopHeight(state.scaleAndVelocity.x), 0.0);
}

float DecayCombatMuzzleFlash(float previousFlash)
{
    return max(previousFlash - _DeltaTime * max(_CombatMuzzleFlashDecay, 0.0), 0.0);
}

float DecayCombatHitFlash(float previousFlash)
{
    return max(previousFlash - _DeltaTime * max(_CombatHitFlashDecay, 0.0), 0.0);
}

float DecayCombatImpactFlash(float previousFlash)
{
    return max(previousFlash - _DeltaTime / max(_CombatImpactFlashDuration, 1e-4), 0.0);
}

float HashUnitFloat(uint seed)
{
    seed ^= 2747636419u;
    seed *= 2654435769u;
    seed ^= seed >> 16;
    seed *= 2654435769u;
    seed ^= seed >> 16;
    seed *= 2654435769u;
    return (seed & 0x00FFFFFFu) / 16777215.0;
}

float ComputeInstanceCombatCooldownDuration(uint instanceIndex)
{
    float shotsPerSecond = max(_CombatShotsPerSecond, 0.0);
    if (shotsPerSecond <= 0.0)
        return 0.0;

    float baseCooldown = rcp(shotsPerSecond);
    float jitter = saturate(_CombatShotIntervalJitter);
    if (jitter <= 1e-5)
        return baseCooldown;

    float random01 = HashUnitFloat(instanceIndex ^ 0x9E3779B9u);
    float multiplier = lerp(1.0 - jitter, 1.0 + jitter, random01);
    return baseCooldown * max(multiplier, 0.05);
}

float ComputeCombatSearchRetryDelay(float baseDelay, uint seed)
{
    baseDelay = max(baseDelay, 0.0);
    if (baseDelay <= 1e-5)
        return 0.0;

    float random01 = HashUnitFloat(seed);
    float multiplier = lerp(0.85, 1.15, random01);
    return baseDelay * multiplier;
}

uint PackAiDebugCombatShotInfo(int hitAgentIndex, bool hitScene, bool blockedBySceneBeforeAgent, int visibleSampleIndex)
{
    uint packed = 0u;
    if (hitAgentIndex >= 0)
        packed |= min((uint)hitAgentIndex + 1u, 65535u);

    if (visibleSampleIndex >= 0)
        packed |= min((uint)visibleSampleIndex + 1u, 255u) << 16;

    if (hitScene)
        packed |= 1u << 24;

    if (blockedBySceneBeforeAgent)
        packed |= 1u << 25;

    return packed;
}

int DecodeAiDebugShotHitAgent(uint packed)
{
    uint encoded = packed & 0xffffu;
    return encoded == 0u ? -1 : (int)(encoded - 1u);
}

int DecodeAiDebugVisibleSampleIndex(uint packed)
{
    uint encoded = (packed >> 16) & 0xffu;
    return encoded == 0u ? -1 : (int)(encoded - 1u);
}

float4 DecodeAiDebugCombatShotInfo(uint packed)
{
    return float4(
        (float)DecodeAiDebugVisibleSampleIndex(packed),
        (float)DecodeAiDebugShotHitAgent(packed),
        ((packed & (1u << 24)) != 0u) ? 1.0 : 0.0,
        ((packed & (1u << 25)) != 0u) ? 1.0 : 0.0);
}

float4 BuildAiDebugCombatShotTraceMeta(int hitAgentIndex, float occupantCount, bool damageApplied, bool hitAgentPreservedVisual)
{
    return float4(
        (float)hitAgentIndex,
        max(occupantCount, 0.0),
        damageApplied ? 1.0 : 0.0,
        hitAgentPreservedVisual ? 1.0 : 0.0);
}

InstanceCombatStateData BuildDefaultCombatState(
    InstanceSimulationState state,
    float cooldown,
    uint debugShotInfoPacked,
    float muzzleFlash,
    float4 healthAndHitFeedback)
{
    InstanceCombatStateData combatState;
    float3 originLocal = BuildCombatTargetTopLocal(state);
    combatState.localOriginAndDistance = float4(originLocal, 1e20);
    combatState.localTargetAndCooldown = float4(originLocal, cooldown);
    combatState.localImpactNormalAndHit = float4(0.0, 1.0, 0.0, 0.0);
    combatState.healthAndHitFeedback = healthAndHitFeedback;
    combatState.muzzleFlash = muzzleFlash;
    combatState.targetIndex = -1;
    combatState.flags = 0u;
    combatState.debugShotInfoPacked = debugShotInfoPacked;
    combatState.debugShotTraceMeta = BuildAiDebugCombatShotTraceMeta(-1, 0.0, false, false);
    return combatState;
}

bool CanPreserveCombatShotVisual(InstanceCombatStateData previousCombatState, float muzzleFlash, float impactFlash)
{
    float3 shotVector = previousCombatState.localTargetAndCooldown.xyz - previousCombatState.localOriginAndDistance.xyz;
    return max(muzzleFlash, impactFlash) > 1e-4 &&
        previousCombatState.localOriginAndDistance.w < 1e19 &&
        dot(shotVector, shotVector) > 1e-8;
}

void PreserveCombatShotVisual(inout InstanceCombatStateData combatState, InstanceCombatStateData previousCombatState, float impactFlash)
{
    combatState.localOriginAndDistance = previousCombatState.localOriginAndDistance;
    combatState.localTargetAndCooldown.xyz = previousCombatState.localTargetAndCooldown.xyz;
    combatState.localImpactNormalAndHit = float4(previousCombatState.localImpactNormalAndHit.xyz, impactFlash);
    combatState.debugShotInfoPacked = previousCombatState.debugShotInfoPacked;
    combatState.debugShotTraceMeta = BuildAiDebugCombatShotTraceMeta(-1, 0.0, false, true);
    combatState.flags |= CrowdVatInstanceCombatFlagHasTarget;
}

void ApplyInstanceDeathSideEffects(uint instanceIndex)
{
    _PhysicsActiveStateBuffer[instanceIndex] = 0u;

    uint squadIndex;
    if (!TryGetAssignedRuntimeSquadIndex(instanceIndex, squadIndex))
        return;

    uint previousAliveCount;
    InterlockedAdd(_SquadAliveCountBuffer[squadIndex], 0xffffffffu, previousAliveCount);
}

void TryMarkInstanceDead(uint instanceIndex)
{
    uint previousDeathState;
    InterlockedOr(_DeathStateBuffer[instanceIndex], CrowdVatDeathStateDeadFlag, previousDeathState);
    if (IsDeathStateDead(previousDeathState))
        return;

    ApplyInstanceDeathSideEffects(instanceIndex);
}

bool ApplyCombatDamage(uint targetInstanceIndex, uint attackerInstanceIndex)
{
    if (targetInstanceIndex >= (uint)_InstanceCount)
        return false;

    uint damageUnits = GetCombatDamagePerHitUnits();
    if (damageUnits == 0u)
        return false;

    uint maxHealth = GetCombatMaxHealthUnits();
    [loop]
    for (uint attempt = 0u; attempt < 16u; attempt++)
    {
        uint currentDeathState = _DeathStateBuffer[targetInstanceIndex];
        if (IsDeathStateDead(currentDeathState))
            return false;

        uint currentDamage = min(currentDeathState & CrowdVatDeathStateDamageMask, maxHealth);
        uint damageTaken = min(currentDamage + damageUnits, maxHealth);
        bool killedByThisHit = damageTaken >= maxHealth;
        uint nextDeathState = damageTaken | (killedByThisHit ? CrowdVatDeathStateDeadFlag : 0u);

        uint observedDeathState;
        InterlockedCompareExchange(_DeathStateBuffer[targetInstanceIndex], currentDeathState, nextDeathState, observedDeathState);
        if (observedDeathState != currentDeathState)
            continue;

        float health = (float)max(maxHealth - damageTaken, 0u);
        InstanceCombatStateData targetCombatState = _CombatStateBuffer[targetInstanceIndex];
        targetCombatState.healthAndHitFeedback = BuildCombatHealthFeedback(health, 1.0, (float)damageUnits);
        _CombatStateBuffer[targetInstanceIndex] = targetCombatState;

        TargetAcquisitionStateData targetAcquisitionState = _TargetAcquisitionStateBuffer[targetInstanceIndex];
        targetAcquisitionState.lastAttackerIndex = (int)attackerInstanceIndex;
        _TargetAcquisitionStateBuffer[targetInstanceIndex] = targetAcquisitionState;

        if (killedByThisHit)
            ApplyInstanceDeathSideEffects(targetInstanceIndex);

        return true;
    }

    return false;
}

bool TryResolveCombatTargetByIndex(
    uint instanceIndex,
    uint selfFaction,
    float combatRange,
    float3 originLocal,
    float3 originWorld,
    float3 lineOfSightOriginLocal,
    float3 lineOfSightOriginWorld,
    int candidateTargetIndex,
    out float3 targetLocal,
    out float targetDistance,
    out bool failedBecauseOccluded,
    bool skipOcclusion)
{
    targetLocal = originLocal;
    targetDistance = combatRange;
    failedBecauseOccluded = false;

    if (candidateTargetIndex < 0)
        return false;

    uint targetInstanceIndex = (uint)candidateTargetIndex;
    if (targetInstanceIndex >= (uint)_InstanceCount || targetInstanceIndex == instanceIndex)
        return false;

    if (IsInstanceDead(targetInstanceIndex))
        return false;

    InstanceSpawnData targetSpawnData = _SpawnData[targetInstanceIndex];
    if (!AreFactionsHostile(selfFaction, targetSpawnData.faction))
        return false;

    InstanceSimulationState targetState = LoadSimulationState(targetInstanceIndex);
    targetLocal = BuildCombatTargetTopLocal(targetState);
    float3 targetWorld = TransformLocalPointToWorld(targetLocal);
    targetDistance = length(targetWorld - originWorld);
    if (targetDistance <= 1e-4 || targetDistance > combatRange)
        return false;

    float lineOfSightDistance = length(targetWorld - lineOfSightOriginWorld);
    if (!skipOcclusion && lineOfSightDistance > 1e-4 &&
        IsCombatOccluded(lineOfSightOriginLocal, targetLocal, lineOfSightDistance))
    {
        failedBecauseOccluded = true;
        return false;
    }

    return true;
}

bool TryResolveCachedCombatTarget(
    uint instanceIndex,
    uint selfFaction,
    float combatRange,
    float3 originLocal,
    float3 originWorld,
    float3 lineOfSightOriginLocal,
    float3 lineOfSightOriginWorld,
    InstanceCombatStateData previousCombatState,
    out int targetIndex,
    out float3 targetLocal,
    out float targetDistance,
    out bool failedBecauseOccluded)
{
    targetIndex = previousCombatState.targetIndex;
    targetLocal = originLocal;
    targetDistance = combatRange;
    failedBecauseOccluded = false;

    return TryResolveCombatTargetByIndex(
        instanceIndex,
        selfFaction,
        combatRange,
        originLocal,
        originWorld,
        lineOfSightOriginLocal,
        lineOfSightOriginWorld,
        targetIndex,
        targetLocal,
        targetDistance,
        failedBecauseOccluded,
        false);
}

float2 ComputeCombatForwardXZ(float yawRadians)
{
    float sineYaw;
    float cosineYaw;
    sincos(yawRadians, sineYaw, cosineYaw);
    return float2(sineYaw, cosineYaw);
}

float ComputeTargetViewDot(float2 selfForwardXZ, float3 originLocal, float3 targetBaseLocal)
{
    float2 toTargetXZ = targetBaseLocal.xz - originLocal.xz;
    float distanceSquared = dot(toTargetXZ, toTargetXZ);
    if (distanceSquared <= 1e-8)
        return 1.0;

    return dot(selfForwardXZ, toTargetXZ * rsqrt(distanceSquared));
}

float ComputeTargetAcquisitionSearchInterval(uint instanceIndex, InstanceSimulationState state)
{
    float minInterval = max(_TargetAcquisitionSearchIntervalMin, 0.02);
    float maxInterval = max(_TargetAcquisitionSearchIntervalMax, minInterval);
    float random01 = HashUnitFloat(instanceIndex ^ 0x5A17C0DEu);
    return lerp(minInterval, maxInterval, random01);
}

float ComputeTargetAcquisitionLineOfSightRecheckInterval(uint instanceIndex)
{
    float interval = max(_TargetAcquisitionLineOfSightRecheckInterval, 0.0);
    float jitter = lerp(0.85, 1.15, HashUnitFloat(instanceIndex ^ 0x8E3779B9u));
    return interval * jitter;
}

TargetAcquisitionStateData BuildDefaultTargetAcquisitionState(
    InstanceSimulationState state,
    float nextSearchTimer,
    float lockTimer,
    float lostSightTimer,
    float lineOfSightRecheckTimer,
    int lastAttackerIndex)
{
    TargetAcquisitionStateData targetState;
    targetState.lastKnownTargetAndScore = float4(BuildCombatTargetTopLocal(state), 0.0);
    targetState.timers = float4(
        max(nextSearchTimer, 0.0),
        max(lockTimer, 0.0),
        max(lostSightTimer, 0.0),
        max(lineOfSightRecheckTimer, 0.0));
    targetState.debugRejectCounts0 = 0.0;
    targetState.debugRejectCounts1 = 0.0;
    targetState.currentTargetIndex = -1;
    targetState.lastAttackerIndex = lastAttackerIndex;
    targetState.flags = 0u;
    targetState.debugVisibleSampleIndex = -1;
    return targetState;
}

TargetAcquisitionCandidateData BuildInvalidTargetAcquisitionCandidate()
{
    TargetAcquisitionCandidateData candidate;
    candidate.targetLocalAndScore = float4(0.0, 0.0, 0.0, -1.0);
    candidate.targetDistance = 0.0;
    candidate.targetIndex = -1;
    candidate.rejectReason = 0u;
    candidate.flags = 0u;
    return candidate;
}

uint GetTargetAcquisitionCandidateBaseIndex(uint instanceIndex)
{
    return instanceIndex * CrowdVatTargetAcquisitionCandidateSlots;
}

void ClearTargetAcquisitionTopCandidates(uint instanceIndex)
{
    uint candidateBase = GetTargetAcquisitionCandidateBaseIndex(instanceIndex);
    TargetAcquisitionCandidateData invalidCandidate = BuildInvalidTargetAcquisitionCandidate();
    [unroll]
    for (uint candidateSlot = 0u; candidateSlot < CrowdVatTargetAcquisitionCandidateSlots; candidateSlot++)
        _TargetAcquisitionCandidateBuffer[candidateBase + candidateSlot] = invalidCandidate;
}

void SetTargetAcquisitionCurrentRecheckCandidate(uint instanceIndex, TargetAcquisitionCandidateData candidate)
{
    if ((candidate.flags & CrowdVatTargetCandidateFlagValid) == 0u)
        return;

    candidate.flags |= CrowdVatTargetCandidateFlagCurrentTargetRecheck;
    uint candidateBase = GetTargetAcquisitionCandidateBaseIndex(instanceIndex);
    _TargetAcquisitionCandidateBuffer[candidateBase + CrowdVatTargetAcquisitionCurrentCandidateSlot] = candidate;
}

void InsertTargetAcquisitionTopCandidate(uint instanceIndex, TargetAcquisitionCandidateData candidate)
{
    if ((candidate.flags & CrowdVatTargetCandidateFlagValid) == 0u)
        return;

    uint candidateBase = GetTargetAcquisitionCandidateBaseIndex(instanceIndex);
    [unroll]
    for (uint candidateSlot = 0u; candidateSlot < CrowdVatTargetAcquisitionTopK; candidateSlot++)
    {
        uint bufferSlot = CrowdVatTargetAcquisitionSearchCandidateBaseSlot + candidateSlot;
        TargetAcquisitionCandidateData existingCandidate = _TargetAcquisitionCandidateBuffer[candidateBase + bufferSlot];
        if ((existingCandidate.flags & CrowdVatTargetCandidateFlagValid) != 0u &&
            existingCandidate.targetIndex == candidate.targetIndex)
        {
            if (candidate.targetLocalAndScore.w > existingCandidate.targetLocalAndScore.w)
                _TargetAcquisitionCandidateBuffer[candidateBase + bufferSlot] = candidate;
            return;
        }
    }

    [unroll]
    for (uint candidateSlot = 0u; candidateSlot < CrowdVatTargetAcquisitionTopK; candidateSlot++)
    {
        uint bufferSlot = CrowdVatTargetAcquisitionSearchCandidateBaseSlot + candidateSlot;
        TargetAcquisitionCandidateData existingCandidate = _TargetAcquisitionCandidateBuffer[candidateBase + bufferSlot];
        if ((existingCandidate.flags & CrowdVatTargetCandidateFlagValid) == 0u ||
            candidate.targetLocalAndScore.w > existingCandidate.targetLocalAndScore.w)
        {
            for (uint shiftSlot = CrowdVatTargetAcquisitionTopK - 1u; shiftSlot > candidateSlot; shiftSlot--)
            {
                uint dstSlot = CrowdVatTargetAcquisitionSearchCandidateBaseSlot + shiftSlot;
                uint srcSlot = dstSlot - 1u;
                _TargetAcquisitionCandidateBuffer[candidateBase + dstSlot] = _TargetAcquisitionCandidateBuffer[candidateBase + srcSlot];
            }

            _TargetAcquisitionCandidateBuffer[candidateBase + bufferSlot] = candidate;
            return;
        }
    }
}

bool TryValidateTargetAcquisitionTarget(
    uint instanceIndex,
    uint selfFaction,
    float combatRange,
    float3 originLocal,
    float3 originWorld,
    float2 selfForwardXZ,
    int candidateTargetIndex,
    out float3 targetLocal,
    out float targetDistance,
    out float viewDot,
    out uint rejectReason)
{
    targetLocal = originLocal;
    targetDistance = combatRange;
    viewDot = 0.0;
    rejectReason = 0u;

    if (candidateTargetIndex < 0)
        return false;

    uint targetInstanceIndex = (uint)candidateTargetIndex;
    if (targetInstanceIndex >= (uint)_InstanceCount || targetInstanceIndex == instanceIndex)
        return false;

    if (IsInstanceDead(targetInstanceIndex))
        return false;

    InstanceSpawnData targetSpawnData = _SpawnData[targetInstanceIndex];
    if (!AreFactionsHostile(selfFaction, targetSpawnData.faction))
        return false;

    InstanceSimulationState targetState = LoadSimulationState(targetInstanceIndex);
    viewDot = ComputeTargetViewDot(selfForwardXZ, originLocal, targetState.localPositionAndYaw.xyz);
    if (viewDot < _TargetAcquisitionFovCosine)
    {
        rejectReason = 1u << 0;
        return false;
    }

    targetLocal = BuildCombatTargetTopLocal(targetState);
    float3 targetWorld = TransformLocalPointToWorld(targetLocal);
    targetDistance = length(targetWorld - originWorld);
    if (targetDistance <= 1e-4 || targetDistance > combatRange)
    {
        rejectReason = 1u << 1;
        return false;
    }

    return true;
}

bool TryEvaluateTargetAcquisitionCandidate(
    uint instanceIndex,
    uint candidateTargetIndex,
    uint selfFaction,
    float combatRange,
    float3 originLocal,
    float3 originWorld,
    float2 selfForwardXZ,
    int currentTargetIndex,
    int lastAttackerIndex,
    out float score,
    out float3 targetLocal,
    out float targetDistance,
    out uint rejectReason)
{
    score = -1.0;
    float viewDot;
    if (!TryValidateTargetAcquisitionTarget(
        instanceIndex,
        selfFaction,
        combatRange,
        originLocal,
        originWorld,
        selfForwardXZ,
        (int)candidateTargetIndex,
        targetLocal,
        targetDistance,
        viewDot,
        rejectReason))
    {
        return false;
    }

    float distanceScore = 1.0 - saturate(targetDistance / max(combatRange, 1e-4));
    float viewScore = saturate((viewDot - _TargetAcquisitionFovCosine) / max(1.0 - _TargetAcquisitionFovCosine, 1e-4));
    score = distanceScore * max(_TargetAcquisitionDistanceScoreWeight, 0.0) +
        viewScore * max(_TargetAcquisitionViewScoreWeight, 0.0);

    if ((int)candidateTargetIndex == currentTargetIndex)
        score += max(_TargetAcquisitionCurrentTargetBonus, 0.0);

    if ((int)candidateTargetIndex == lastAttackerIndex)
        score += max(_TargetAcquisitionLastAttackerBonus, 0.0);

    return true;
}

TargetAcquisitionCandidateData BuildTargetAcquisitionCandidate(
    uint targetIndex,
    float score,
    float3 targetLocal,
    float targetDistance,
    uint rejectReason)
{
    TargetAcquisitionCandidateData candidate;
    candidate.targetLocalAndScore = float4(targetLocal, score);
    candidate.targetDistance = targetDistance;
    candidate.targetIndex = (int)targetIndex;
    candidate.rejectReason = rejectReason;
    candidate.flags = CrowdVatTargetCandidateFlagValid;
    return candidate;
}

void BuildPerpendicularBasis(float3 forward, out float3 right, out float3 up)
{
    float3 referenceUp = abs(forward.y) < 0.98 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
    right = cross(referenceUp, forward);
    float rightLengthSquared = dot(right, right);
    if (rightLengthSquared <= 1e-8)
        right = float3(1.0, 0.0, 0.0);
    else
        right *= rsqrt(rightLengthSquared);

    up = normalize(cross(forward, right));
}

float3 BuildCombatShotDirectionLocal(uint instanceIndex, float3 originLocal, float3 aimTargetLocal)
{
    float3 aimVector = aimTargetLocal - originLocal;
    float aimLength = length(aimVector);
    if (aimLength <= 1e-5)
        return float3(0.0, 0.0, 1.0);

    float3 aimDirection = aimVector / aimLength;
    float spreadDegrees = max(_CombatShotSpreadDegrees, 0.0);
    if (spreadDegrees <= 1e-5)
        return aimDirection;

    float3 right;
    float3 up;
    BuildPerpendicularBasis(aimDirection, right, up);

    uint timeSeed = asuint(_PlaybackTime * 173.317 + _DeltaTime * 997.131);
    uint shotSeed = instanceIndex ^ timeSeed ^ 0xA5C3D721u;
    float angle = HashUnitFloat(shotSeed ^ 0x68BC21EBu) * 6.28318530718;
    float radius = sqrt(HashUnitFloat(shotSeed ^ 0xB5297A4Du));
    float sineAngle;
    float cosineAngle;
    sincos(angle, sineAngle, cosineAngle);

    float spreadRadians = min(spreadDegrees, 45.0) * 0.01745329252;
    float spreadScale = tan(spreadRadians);
    float2 disk = float2(cosineAngle, sineAngle) * radius * spreadScale;
    return normalize(aimDirection + right * disk.x + up * disk.y);
}

bool TryRaySphereHit(
    float3 rayOrigin,
    float3 rayDirection,
    float3 sphereCenter,
    float sphereRadius,
    float maxDistance,
    out float hitDistance)
{
    hitDistance = maxDistance;
    float3 originToCenter = rayOrigin - sphereCenter;
    float b = dot(originToCenter, rayDirection);
    float c = dot(originToCenter, originToCenter) - sphereRadius * sphereRadius;
    float h = b * b - c;
    if (h < 0.0)
        return false;

    h = sqrt(h);
    float nearT = -b - h;
    float farT = -b + h;
    float candidateT = nearT > CombatRayHitBias ? nearT : farT;
    if (candidateT <= CombatRayHitBias || candidateT > maxDistance)
        return false;

    hitDistance = candidateT;
    return true;
}

bool TryRayCapsuleHit(
    float3 rayOrigin,
    float3 rayDirection,
    float maxDistance,
    float3 capsuleStart,
    float3 capsuleEnd,
    float capsuleRadius,
    out float hitDistance)
{
    hitDistance = maxDistance;
    bool hasHit = false;

    float3 capsuleAxis = capsuleEnd - capsuleStart;
    float axisLengthSquared = dot(capsuleAxis, capsuleAxis);
    if (axisLengthSquared <= 1e-8)
        return TryRaySphereHit(rayOrigin, rayDirection, capsuleStart, capsuleRadius, maxDistance, hitDistance);

    float3 originToStart = rayOrigin - capsuleStart;
    float axisDotRay = dot(capsuleAxis, rayDirection);
    float axisDotOrigin = dot(capsuleAxis, originToStart);
    float rayDotOrigin = dot(rayDirection, originToStart);
    float originDotOrigin = dot(originToStart, originToStart);

    float a = axisLengthSquared - axisDotRay * axisDotRay;
    float b = axisLengthSquared * rayDotOrigin - axisDotOrigin * axisDotRay;
    float c = axisLengthSquared * originDotOrigin - axisDotOrigin * axisDotOrigin -
        capsuleRadius * capsuleRadius * axisLengthSquared;
    float h = b * b - a * c;
    if (abs(a) > 1e-8 && h >= 0.0)
    {
        float cylinderT = (-b - sqrt(h)) / a;
        float axisT = axisDotOrigin + cylinderT * axisDotRay;
        if (cylinderT > CombatRayHitBias &&
            cylinderT <= maxDistance &&
            axisT >= 0.0 &&
            axisT <= axisLengthSquared)
        {
            hitDistance = cylinderT;
            hasHit = true;
        }
    }

    float sphereHitDistance;
    if (TryRaySphereHit(rayOrigin, rayDirection, capsuleStart, capsuleRadius, maxDistance, sphereHitDistance) &&
        sphereHitDistance < hitDistance)
    {
        hitDistance = sphereHitDistance;
        hasHit = true;
    }

    if (TryRaySphereHit(rayOrigin, rayDirection, capsuleEnd, capsuleRadius, maxDistance, sphereHitDistance) &&
        sphereHitDistance < hitDistance)
    {
        hitDistance = sphereHitDistance;
        hasHit = true;
    }

    return hasHit;
}

void TraceCombatShotAgentCell(
    uint selfInstanceIndex,
    uint selfFaction,
    int2 coord,
    float3 originLocal,
    float3 rayDirectionLocal,
    float3 originWorld,
    float maxLocalDistance,
    inout float nearestWorldDistance,
    inout float nearestLocalDistance,
    inout int nearestAgentIndex,
    inout float3 nearestHitLocal,
    inout float3 nearestHitNormalLocal,
    inout uint candidateChecks,
    inout uint maxTraceOccupantCount)
{
    if (!IsGridCoordValid(coord) || candidateChecks >= CombatShotMaxAgentChecks)
        return;

    uint cellKey = GetCellKey(coord);
    uint rawOccupantCount = _GridCounterBuffer[cellKey];
    maxTraceOccupantCount = max(maxTraceOccupantCount, rawOccupantCount);
    uint occupantCount = min(rawOccupantCount, (uint)_MaxCellOccupancy);
    [loop]
    for (uint occupantIndex = 0u; occupantIndex < occupantCount; occupantIndex++)
    {
        if (candidateChecks >= CombatShotMaxAgentChecks)
            return;

        uint candidateIndex = _GridOccupantBuffer[cellKey * (uint)_MaxCellOccupancy + occupantIndex];
        if (candidateIndex >= (uint)_InstanceCount ||
            candidateIndex == selfInstanceIndex ||
            IsInstanceDead(candidateIndex))
        {
            continue;
        }

        InstanceSpawnData candidateSpawnData = _SpawnData[candidateIndex];
        if (!AreFactionsHostile(selfFaction, candidateSpawnData.faction))
            continue;

        candidateChecks++;
        InstanceSimulationState candidateState = LoadSimulationState(candidateIndex);
        float candidateScale = max(candidateState.scaleAndVelocity.x, candidateSpawnData.uniformScale);
        float candidateRadius = ComputeCombatHitRadius(candidateScale);
        float3 candidatePosition = candidateState.localPositionAndYaw.xyz;
        float3 candidateCapsuleStart = ComputeCapsuleBottom(candidatePosition, candidateRadius);
        float3 candidateCapsuleEnd = ComputeCombatHitCapsuleTop(candidatePosition, candidateScale, candidateRadius);
        float candidateHitLocalDistance;
        if (!TryRayCapsuleHit(
            originLocal,
            rayDirectionLocal,
            maxLocalDistance,
            candidateCapsuleStart,
            candidateCapsuleEnd,
            candidateRadius,
            candidateHitLocalDistance))
        {
            continue;
        }

        float3 candidateHitLocal = originLocal + rayDirectionLocal * candidateHitLocalDistance;
        float candidateWorldDistance = length(TransformLocalPointToWorld(candidateHitLocal) - originWorld);
        if (candidateWorldDistance < nearestWorldDistance)
        {
            float3 capsuleAxis = candidateCapsuleEnd - candidateCapsuleStart;
            float capsuleAxisLengthSquared = dot(capsuleAxis, capsuleAxis);
            float capsuleT = capsuleAxisLengthSquared > 1e-8
                ? saturate(dot(candidateHitLocal - candidateCapsuleStart, capsuleAxis) / capsuleAxisLengthSquared)
                : 0.0;
            float3 closestCapsulePoint = lerp(candidateCapsuleStart, candidateCapsuleEnd, capsuleT);
            nearestWorldDistance = candidateWorldDistance;
            nearestLocalDistance = candidateHitLocalDistance;
            nearestAgentIndex = (int)candidateIndex;
            nearestHitLocal = candidateHitLocal;
            nearestHitNormalLocal = SafeNormalize3D(candidateHitLocal - closestCapsulePoint, -rayDirectionLocal);
        }
    }
}

bool TryTraceCombatShotAgents(
    uint selfInstanceIndex,
    uint selfFaction,
    float3 originLocal,
    float3 rayDirectionLocal,
    float3 originWorld,
    float maxLocalDistance,
    inout float nearestWorldDistance,
    out int hitAgentIndex,
    out float3 hitLocal,
    out float3 hitLocalNormal,
    out float hitLocalDistance,
    out uint traceOccupantCount)
{
    hitAgentIndex = -1;
    hitLocal = originLocal + rayDirectionLocal * maxLocalDistance;
    hitLocalNormal = SafeNormalize3D(-rayDirectionLocal, float3(0.0, 1.0, 0.0));
    hitLocalDistance = maxLocalDistance;
    traceOccupantCount = 0u;

    int2 coord;
    if (!TryGetCellCoord(originLocal.xz, coord))
        return false;

    float2 directionXZ = rayDirectionLocal.xz;
    float directionXZLengthSquared = dot(directionXZ, directionXZ);
    uint candidateChecks = 0u;

    float nearestLocalDistance = maxLocalDistance;
    float3 nearestHitLocal = hitLocal;
    float3 nearestHitNormalLocal = hitLocalNormal;
    int nearestAgentIndex = -1;

    if (directionXZLengthSquared <= 1e-8)
    {
        [loop]
        for (int y = -1; y <= 1; y++)
        {
            [loop]
            for (int x = -1; x <= 1; x++)
            {
                TraceCombatShotAgentCell(
                    selfInstanceIndex,
                    selfFaction,
                    coord + int2(x, y),
                    originLocal,
                    rayDirectionLocal,
                    originWorld,
                    maxLocalDistance,
                    nearestWorldDistance,
                    nearestLocalDistance,
                    nearestAgentIndex,
                    nearestHitLocal,
                    nearestHitNormalLocal,
                    candidateChecks,
                    traceOccupantCount);
            }
        }
    }
    else
    {
        int stepX = directionXZ.x >= 0.0 ? 1 : -1;
        int stepY = directionXZ.y >= 0.0 ? 1 : -1;
        float nextBoundaryX = _GridMinXZ.x + (stepX > 0 ? (coord.x + 1) : coord.x) * _CellSize;
        float nextBoundaryY = _GridMinXZ.y + (stepY > 0 ? (coord.y + 1) : coord.y) * _CellSize;
        float tMaxX = abs(directionXZ.x) > 1e-6 ? (nextBoundaryX - originLocal.x) / directionXZ.x : 1e20;
        float tMaxY = abs(directionXZ.y) > 1e-6 ? (nextBoundaryY - originLocal.z) / directionXZ.y : 1e20;
        float tDeltaX = abs(directionXZ.x) > 1e-6 ? _CellSize / abs(directionXZ.x) : 1e20;
        float tDeltaY = abs(directionXZ.y) > 1e-6 ? _CellSize / abs(directionXZ.y) : 1e20;

        [loop]
        for (int cellStep = 0; cellStep < CombatShotGridMaxCells && IsGridCoordValid(coord); cellStep++)
        {
            [loop]
            for (int y = -1; y <= 1; y++)
            {
                [loop]
                for (int x = -1; x <= 1; x++)
                {
                    TraceCombatShotAgentCell(
                        selfInstanceIndex,
                        selfFaction,
                        coord + int2(x, y),
                        originLocal,
                        rayDirectionLocal,
                        originWorld,
                        maxLocalDistance,
                        nearestWorldDistance,
                        nearestLocalDistance,
                        nearestAgentIndex,
                        nearestHitLocal,
                        nearestHitNormalLocal,
                        candidateChecks,
                        traceOccupantCount);
                }
            }

            if (candidateChecks >= CombatShotMaxAgentChecks)
                break;

            if (min(tMaxX, tMaxY) > maxLocalDistance)
                break;

            if (tMaxX < tMaxY)
            {
                coord.x += stepX;
                tMaxX += tDeltaX;
            }
            else
            {
                coord.y += stepY;
                tMaxY += tDeltaY;
            }
        }
    }

    if (nearestAgentIndex < 0)
        return false;

    hitAgentIndex = nearestAgentIndex;
    hitLocal = nearestHitLocal;
    hitLocalNormal = nearestHitNormalLocal;
    hitLocalDistance = nearestLocalDistance;
    return true;
}

bool TryTraceCombatShotScene(
    float3 originLocal,
    float3 rayDirectionLocal,
    float3 originWorld,
    float3 rayDirectionWorld,
    float maxLocalDistance,
    float maxWorldDistance,
    inout float nearestWorldDistance,
    out float3 hitLocal)
{
    hitLocal = originLocal + rayDirectionLocal * maxLocalDistance;
    bool hasHit = false;

    float hitDistanceWorld;
    float3 hitWorld;
    if (TryTraceCombatEnvironmentSdfHit(originWorld, rayDirectionWorld, maxWorldDistance, hitDistanceWorld, hitWorld) &&
        hitDistanceWorld < nearestWorldDistance)
    {
        nearestWorldDistance = hitDistanceWorld;
        hitLocal = TransformWorldPointToLocal(hitWorld);
        hasHit = true;
    }

    return hasHit;
}

bool ResolveCombatShotHit(
    uint instanceIndex,
    float3 originLocal,
    float3 aimTargetLocal,
    float combatRange,
    bool useCachedSdfResult,
    float cachedSdfHitWorldDistance,
    float3 cachedSdfHitWorld,
    out float3 shotTargetLocal,
    out float shotDistance,
    out float3 shotHitNormalLocal,
    out int hitAgentIndex,
    out bool hitScene,
    out bool blockedBySceneBeforeAgent,
    out uint traceOccupantCount)
{
    float3 rayDirectionLocal = BuildCombatShotDirectionLocal(instanceIndex, originLocal, aimTargetLocal);
    float maxLocalDistance = max(combatRange, 0.0);
    shotTargetLocal = originLocal + rayDirectionLocal * maxLocalDistance;
    shotDistance = maxLocalDistance;
    shotHitNormalLocal = float3(0.0, 1.0, 0.0);
    hitAgentIndex = -1;
    hitScene = false;
    blockedBySceneBeforeAgent = false;
    traceOccupantCount = 0u;

    if (maxLocalDistance <= CombatRayHitBias)
        return false;

    float3 originWorld = TransformLocalPointToWorld(originLocal);
    float3 maxTargetWorld = TransformLocalPointToWorld(shotTargetLocal);
    float3 worldRay = maxTargetWorld - originWorld;
    float maxWorldDistance = length(worldRay);
    if (maxWorldDistance <= CombatRayHitBias)
        return false;

    float3 rayDirectionWorld = worldRay / maxWorldDistance;
    float nearestWorldDistance = maxWorldDistance;

    float3 sceneHitLocal = originLocal + rayDirectionLocal * maxLocalDistance;
    bool hasSceneHit = false;
    if (useCachedSdfResult)
    {
        if (cachedSdfHitWorldDistance < nearestWorldDistance)
        {
            nearestWorldDistance = cachedSdfHitWorldDistance;
            sceneHitLocal = TransformWorldPointToLocal(cachedSdfHitWorld);
            hasSceneHit = true;
        }
    }
    else
    {
        hasSceneHit = TryTraceCombatShotScene(
            originLocal,
            rayDirectionLocal,
            originWorld,
            rayDirectionWorld,
            maxLocalDistance,
            maxWorldDistance,
            nearestWorldDistance,
            sceneHitLocal);
    }
    hitScene = hasSceneHit;
    if (hasSceneHit)
    {
        shotTargetLocal = sceneHitLocal;
    }

    float3 agentHitLocal;
    float3 agentHitNormalLocal;
    float agentHitLocalDistance;
    int agentHitIndex;
    uint selfFaction = _SpawnData[instanceIndex].faction;
    if (TryTraceCombatShotAgents(
        instanceIndex,
        selfFaction,
        originLocal,
        rayDirectionLocal,
        originWorld,
        maxLocalDistance,
        nearestWorldDistance,
        agentHitIndex,
        agentHitLocal,
        agentHitNormalLocal,
        agentHitLocalDistance,
        traceOccupantCount))
    {
        hitAgentIndex = agentHitIndex;
        shotTargetLocal = agentHitLocal;
        shotHitNormalLocal = agentHitNormalLocal;
    }

    blockedBySceneBeforeAgent = hasSceneHit && hitAgentIndex < 0;
    shotDistance = nearestWorldDistance;
    return hasSceneHit || hitAgentIndex >= 0;
}

float ComputePointSegmentT(float3 samplePoint, float3 segmentStart, float3 segmentEnd)
{
    float3 segment = segmentEnd - segmentStart;
    float segmentLengthSquared = dot(segment, segment);
    if (segmentLengthSquared < 1e-8)
        return 0.0;

    return saturate(dot(samplePoint - segmentStart, segment) / segmentLengthSquared);
}

void ClosestPointsBetweenSegments(
    float3 segmentAStart,
    float3 segmentAEnd,
    float3 segmentBStart,
    float3 segmentBEnd,
    out float segmentAT,
    out float segmentBT,
    out float3 closestA,
    out float3 closestB)
{
    float3 d1 = segmentAEnd - segmentAStart;
    float3 d2 = segmentBEnd - segmentBStart;
    float3 r = segmentAStart - segmentBStart;
    float a = dot(d1, d1);
    float e = dot(d2, d2);
    float f = dot(d2, r);

    if (a < 1e-8 && e < 1e-8)
    {
        segmentAT = 0.0;
        segmentBT = 0.0;
        closestA = segmentAStart;
        closestB = segmentBStart;
        return;
    }

    if (a < 1e-8)
    {
        segmentAT = 0.0;
        segmentBT = saturate(f / max(e, 1e-8));
    }
    else
    {
        float c = dot(d1, r);
        if (e < 1e-8)
        {
            segmentBT = 0.0;
            segmentAT = saturate(-c / max(a, 1e-8));
        }
        else
        {
            float b = dot(d1, d2);
            float denom = a * e - b * b;
            segmentAT = denom > 1e-8 ? saturate((b * f - c * e) / denom) : 0.0;

            float unclampedT = (b * segmentAT + f) / e;
            if (unclampedT < 0.0)
            {
                segmentBT = 0.0;
                segmentAT = saturate(-c / max(a, 1e-8));
            }
            else if (unclampedT > 1.0)
            {
                segmentBT = 1.0;
                segmentAT = saturate((b - c) / max(a, 1e-8));
            }
            else
            {
                segmentBT = unclampedT;
            }
        }
    }

    closestA = segmentAStart + d1 * segmentAT;
    closestB = segmentBStart + d2 * segmentBT;
}

bool OverlapSphereMatches(
    float3 sphereCenter,
    float sphereRadius,
    float3 capsuleStart,
    float3 capsuleEnd,
    float capsuleRadius,
    out float normalizedDistance)
{
    float combinedRadius = max(sphereRadius + capsuleRadius, 1e-4);
    float capsuleT = ComputePointSegmentT(sphereCenter, capsuleStart, capsuleEnd);
    float3 closestPoint = lerp(capsuleStart, capsuleEnd, capsuleT);
    float3 delta = sphereCenter - closestPoint;
    float distanceSquared = dot(delta, delta);
    if (distanceSquared > combinedRadius * combinedRadius)
    {
        normalizedDistance = 0.0;
        return false;
    }

    normalizedDistance = sqrt(max(distanceSquared, 0.0)) / combinedRadius;
    return true;
}

bool SweepCapsuleMatches(
    float3 queryStart,
    float3 queryEnd,
    float queryRadius,
    float3 capsuleStart,
    float3 capsuleEnd,
    float capsuleRadius,
    out float normalizedDistance)
{
    float combinedRadius = max(queryRadius + capsuleRadius, 1e-4);
    float queryT;
    float capsuleT;
    float3 closestQuery;
    float3 closestCapsule;
    ClosestPointsBetweenSegments(queryStart, queryEnd, capsuleStart, capsuleEnd, queryT, capsuleT, closestQuery, closestCapsule);

    float3 delta = closestQuery - closestCapsule;
    float distanceSquared = dot(delta, delta);
    if (distanceSquared > combinedRadius * combinedRadius)
    {
        normalizedDistance = 0.0;
        return false;
    }

    float queryLengthSquared = dot(queryEnd - queryStart, queryEnd - queryStart);
    normalizedDistance = queryLengthSquared < 1e-8
        ? sqrt(max(distanceSquared, 0.0)) / combinedRadius
        : queryT;
    return true;
}

