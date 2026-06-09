using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;

public sealed partial class CrowdVatIndirectRenderer
{
    private const string CrowdGpuShaderFile = "Assets/Project/Crowds/VAT/Shader/CrowdVatIndirect.compute";
    private const string CrowdMainShaderFile = "Assets/Project/Crowds/VAT/Shader/CrowdVatIndirectLit.shader";
    private const string CrowdCombatTracerShaderFile = "Assets/Project/Crowds/VAT/Shader/CrowdVatCombatTracer.shader";
    private const string CrowdSimulationCppFile = "Assets/Project/Crowds/VAT/Runtime/CrowdVatIndirectRenderer.Simulation.cs";
    private const string CrowdVisibilityCppFile = "Assets/Project/Crowds/VAT/Runtime/CrowdVatIndirectRenderer.Visibility.cs";
    private const string CrowdAiDebugCppFile = "Assets/Project/Crowds/VAT/Runtime/CrowdVatIndirectRenderer.AiDebug.cs";

    private const string GpuPassBuildAliveList = "Crowd.Update.BuildAliveList";
    private const string GpuPassBuildAliveDispatchArgs = "Crowd.Update.BuildAliveDispatchArgs";
    private const string GpuPassWakeGridClear = "Crowd.Update.WakeGridClear";
    private const string GpuPassWakeGridBuild = "Crowd.Update.WakeGridBuild";
    private const string GpuPassEvaluatePhysicsActive = "Crowd.Update.EvaluatePhysicsActive";
    private const string GpuPassBuildPhysicsActiveList = "Crowd.Update.BuildPhysicsActiveList";
    private const string GpuPassBuildPhysicsActiveDispatchArgs = "Crowd.Update.BuildPhysicsActiveDispatchArgs";
    private const string GpuPassEvaluateCombatActive = "Crowd.Combat.EvaluateActive";
    private const string GpuPassBuildCombatActiveList = "Crowd.Combat.BuildActiveList";
    private const string GpuPassCompactCombatActiveList = "Crowd.Combat.CompactActiveList";
    private const string GpuPassBuildCombatActiveDispatchArgs = "Crowd.Combat.BuildActiveDispatchArgs";
    private const string GpuPassClearCombatSquadCandidateCounters = "Crowd.Combat.ClearCandidateCounters";
    private const string GpuPassBuildCombatCandidateClusters = "Crowd.Combat.BuildCandidateClusters";
    private const string GpuPassClearVisibleRuntimeSquadBounds = "Crowd.Culling.ClearRuntimeSquadBounds";
    private const string GpuPassBuildVisibleRuntimeSquadBounds = "Crowd.Culling.BuildRuntimeSquadBounds";
    private const string GpuPassCullVisibleRuntimeSquads = "Crowd.Culling.CullRuntimeSquads";
    private const string GpuPassBuildVisibleRuntimeList = "Crowd.Culling.BuildVisibleRuntimeList";
    private const string GpuPassBuildVisibleIndirectArgs = "Crowd.Culling.BuildVisibleIndirectArgs";
    private const string GpuPassPredictInstances = "Crowd.Update.PredictAnimation";
    private const string GpuPassBuildGridClearDispatchArgs = "Crowd.QueryGrid.BuildClearDispatchArgs";
    private const string GpuPassClearGrid = "Crowd.QueryGrid.ClearTouchedCells";
    private const string GpuPassBuildGrid = "Crowd.QueryGrid.Build";
    private const string GpuPassBuildSpatialElements = "Crowd.SceneQuery.BuildSpatialElements";
    private const string GpuPassSolveCrowd = "Crowd.Simulation.SolveCrowd";
    private const string GpuPassResolveTargetAcquisition = "Crowd.Combat.ResolveTargetAcquisition";
    private const string GpuPassBuildTargetAcquisitionLosDispatchArgs = "Crowd.Combat.BuildTargetAcquisitionLosDispatchArgs";
    private const string GpuPassResolveTargetAcquisitionLineOfSight = "Crowd.Combat.ResolveTargetAcquisitionLineOfSight";
    private const string GpuPassBuildSquadAcquisitionDispatchArgs = "Crowd.Combat.BuildSquadAcquisitionDispatchArgs";
    private const string GpuPassEvaluateSquadAcquisitionCandidates = "Crowd.Combat.EvaluateSquadAcquisitionCandidates";
    private const string GpuPassFinalizeTargetAcquisition = "Crowd.Combat.FinalizeTargetAcquisition";
    private const string GpuPassResolveInstanceCombat = "Crowd.Combat.ResolveInstance";
    private const string GpuPassClearSpatialQueries = "Crowd.SceneQuery.ClearResults";
    private const string GpuPassResolveSpatialQueries = "Crowd.SceneQuery.Resolve";
    private const string GpuPassFinalizeInstances = "Crowd.Update.FinalizeAnimation";
    private const string GpuPassCaptureAiDebugGpuStage = "Crowd.Debug.CaptureGpuStage";
    private const string GpuPassDiscoverAiDebugGpuCandidates = "Crowd.Debug.DiscoverGpuCandidates";
    private const string GpuPassRenderSkinnedMeshLod0 = "Crowd.Render.SkinnedMesh.LOD0";
    private const string GpuPassRenderSkinnedMeshLod1 = "Crowd.Render.SkinnedMesh.LOD1";
    private const string GpuPassRenderSkinnedMeshLod2 = "Crowd.Render.SkinnedMesh.LOD2";
    private const string GpuPassRenderCombatTracer = "Crowd.Render.CombatTracer";
    private const string CrowdForwardLitPassName = "ForwardLit";
    private const string CrowdCombatTracerForwardPassName = "Forward";
    private const string GpuPassRenderSkinnedMeshLod0PassId = "crowd.render.skinned_mesh.lod0";
    private const string GpuPassRenderSkinnedMeshLod1PassId = "crowd.render.skinned_mesh.lod1";
    private const string GpuPassRenderSkinnedMeshLod2PassId = "crowd.render.skinned_mesh.lod2";
    private const string GpuPassRenderCombatTracerPassId = "crowd.render.combat_tracer";
    private static readonly string GpuPassRenderSkinnedMeshLod0Marker =
        GpuPassMarkerUtility.BuildMarkerLabel(GpuPassRenderSkinnedMeshLod0PassId, "Crowd / Render / Skinned Mesh / LOD0");
    private static readonly string GpuPassRenderSkinnedMeshLod1Marker =
        GpuPassMarkerUtility.BuildMarkerLabel(GpuPassRenderSkinnedMeshLod1PassId, "Crowd / Render / Skinned Mesh / LOD1");
    private static readonly string GpuPassRenderSkinnedMeshLod2Marker =
        GpuPassMarkerUtility.BuildMarkerLabel(GpuPassRenderSkinnedMeshLod2PassId, "Crowd / Render / Skinned Mesh / LOD2");
    private static readonly string GpuPassRenderCombatTracerMarker =
        GpuPassMarkerUtility.BuildMarkerLabel(GpuPassRenderCombatTracerPassId, "Crowd / Render / Combat Tracer");

    [Header("GPU Debug")]
    [SerializeField] private bool _enableGpuPassDebugScopes = true;
    [SerializeField] private bool _enableGpuPassBindingSidecar = true;
    private bool _gpuPassDebugScopesFailed;
    private bool _gpuPassDebugScopesActive;
    private bool _gpuPassBindingSidecarActive;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterGpuPassDebugInfosAtStartup()
    {
        RegisterGpuPassDebugInfos();
    }

    private static void RegisterGpuPassDebugInfos()
    {
        RegisterComputeGpuPass(GpuPassBuildAliveList, CrowdSimulationCppFile, "CrowdVatIndirectRenderer.RebuildAliveInstanceCompactionData", BuildAliveInstanceListKernelName, 64, "direct");
        RegisterComputeGpuPass(GpuPassBuildAliveDispatchArgs, CrowdSimulationCppFile, "CrowdVatIndirectRenderer.RebuildAliveInstanceCompactionData", BuildAliveDispatchArgsKernelName, 1, "direct");
        RegisterComputeGpuPass(GpuPassWakeGridClear, CrowdSimulationCppFile, "CrowdVatIndirectRenderer.UpdateGpuBuffers", ClearWakeGridKernelName, 64, "direct");
        RegisterComputeGpuPass(GpuPassWakeGridBuild, CrowdSimulationCppFile, "CrowdVatIndirectRenderer.UpdateGpuBuffers", BuildWakeGridKernelName, 64, "indirect_alive");
        RegisterComputeGpuPass(GpuPassEvaluatePhysicsActive, CrowdSimulationCppFile, "CrowdVatIndirectRenderer.EvaluatePhysicsActive", EvaluatePhysicsActiveKernelName, 64, "indirect_alive");
        RegisterComputeGpuPass(GpuPassBuildPhysicsActiveList, CrowdSimulationCppFile, "CrowdVatIndirectRenderer.RebuildPhysicsActiveInstanceCompactionData", BuildPhysicsActiveInstanceListKernelName, 64, "indirect_alive");
        RegisterComputeGpuPass(GpuPassBuildPhysicsActiveDispatchArgs, CrowdSimulationCppFile, "CrowdVatIndirectRenderer.RebuildPhysicsActiveInstanceCompactionData", BuildPhysicsActiveDispatchArgsKernelName, 1, "direct");
        RegisterComputeGpuPass(GpuPassEvaluateCombatActive, CrowdSimulationCppFile, "CrowdVatIndirectRenderer.RebuildCombatActiveInstanceCompactionData", EvaluateCombatActiveKernelName, 64, "indirect_alive");
        RegisterComputeGpuPass(GpuPassBuildCombatActiveList, CrowdSimulationCppFile, "CrowdVatIndirectRenderer.RebuildCombatActiveInstanceCompactionData", BuildCombatActiveInstanceListKernelName, 64, "indirect_alive");
        RegisterComputeGpuPass(GpuPassCompactCombatActiveList, CrowdSimulationCppFile, "CrowdVatIndirectRenderer.RebuildCombatActiveInstanceCompactionData", CompactCombatActiveInstanceListKernelName, 64, "direct_all_instances");
        RegisterComputeGpuPass(GpuPassBuildCombatActiveDispatchArgs, CrowdSimulationCppFile, "CrowdVatIndirectRenderer.RebuildCombatActiveInstanceCompactionData", BuildCombatActiveDispatchArgsKernelName, 1, "direct");
        RegisterComputeGpuPass(GpuPassClearCombatSquadCandidateCounters, CrowdSimulationCppFile, "CrowdVatIndirectRenderer.BuildCombatSquadCandidates", ClearCombatSquadCandidateCountersKernelName, 64, "direct_squads");
        RegisterComputeGpuPass(GpuPassBuildCombatCandidateClusters, CrowdSimulationCppFile, "CrowdVatIndirectRenderer.BuildCombatSquadCandidates", BuildCombatCandidateClustersKernelName, 64, "indirect_squad_tile_workset");
        RegisterComputeGpuPass(GpuPassClearVisibleRuntimeSquadBounds, CrowdVisibilityCppFile, "CrowdVatIndirectRenderer.BuildVisibleRuntimeSquadAabbCommandsGpu", ClearVisibleRuntimeSquadBoundsKernelName, 64, "direct_squads");
        RegisterComputeGpuPass(GpuPassBuildVisibleRuntimeSquadBounds, CrowdVisibilityCppFile, "CrowdVatIndirectRenderer.BuildVisibleRuntimeSquadAabbCommandsGpu", BuildVisibleRuntimeSquadBoundsKernelName, 64, "indirect_alive");
        RegisterComputeGpuPass(GpuPassCullVisibleRuntimeSquads, CrowdVisibilityCppFile, "CrowdVatIndirectRenderer.BuildVisibleRuntimeSquadAabbCommandsGpu", CullVisibleRuntimeSquadsKernelName, 64, "direct_squads");
        RegisterComputeGpuPass(GpuPassBuildVisibleRuntimeList, CrowdVisibilityCppFile, "CrowdVatIndirectRenderer.BuildVisibleRuntimeInstanceListGpu", BuildVisibleRuntimeInstanceListKernelName, 64, "indirect_alive");
        RegisterComputeGpuPass(GpuPassBuildVisibleIndirectArgs, CrowdVisibilityCppFile, "CrowdVatIndirectRenderer.BuildVisibleIndirectArgsGpu", BuildVisibleIndirectArgsKernelName, 1, "direct");
        RegisterComputeGpuPass(GpuPassPredictInstances, CrowdSimulationCppFile, "CrowdVatIndirectRenderer.UpdateGpuBuffers", PredictKernelName, 64, "indirect_alive");
        RegisterComputeGpuPass(GpuPassBuildGridClearDispatchArgs, CrowdSimulationCppFile, "CrowdVatIndirectRenderer.ClearGridForRebuild", BuildGridClearDispatchArgsKernelName, 1, "direct");
        RegisterComputeGpuPass(GpuPassClearGrid, CrowdSimulationCppFile, "CrowdVatIndirectRenderer.DispatchGridClearTouchedCells", ClearGridKernelName, 64, "indirect_grid_clear");
        RegisterComputeGpuPass(GpuPassBuildGrid, CrowdSimulationCppFile, "CrowdVatIndirectRenderer.UpdateGpuBuffers", BuildGridKernelName, 64, "indirect_alive_or_physics_active");
        RegisterComputeGpuPass(GpuPassBuildSpatialElements, CrowdSimulationCppFile, "CrowdVatIndirectRenderer.UpdateGpuBuffers", BuildSpatialElementsKernelName, 64, "direct_all_instances");
        RegisterComputeGpuPass(GpuPassSolveCrowd, CrowdSimulationCppFile, "CrowdVatIndirectRenderer.UpdateGpuBuffers", SolveCrowdKernelName, 64, "indirect_physics_active");
        RegisterComputeGpuPass(GpuPassResolveTargetAcquisition, CrowdSimulationCppFile, "CrowdVatIndirectRenderer.UpdateGpuBuffers", ResolveTargetAcquisitionKernelName, 64, "indirect_combat_active");
        RegisterComputeGpuPass(GpuPassBuildTargetAcquisitionLosDispatchArgs, CrowdSimulationCppFile, "CrowdVatIndirectRenderer.UpdateGpuBuffers", BuildTargetAcquisitionLosDispatchArgsKernelName, 1, "direct");
        RegisterComputeGpuPass(GpuPassResolveTargetAcquisitionLineOfSight, CrowdSimulationCppFile, "CrowdVatIndirectRenderer.UpdateGpuBuffers", ResolveTargetAcquisitionLineOfSightKernelName, 64, "indirect_target_acquisition_los_queries");
        RegisterComputeGpuPass(GpuPassBuildSquadAcquisitionDispatchArgs, CrowdSimulationCppFile, "CrowdVatIndirectRenderer.UpdateGpuBuffers", BuildSquadAcquisitionDispatchArgsKernelName, 1, "direct");
        RegisterComputeGpuPass(GpuPassEvaluateSquadAcquisitionCandidates, CrowdSimulationCppFile, "CrowdVatIndirectRenderer.UpdateGpuBuffers", EvaluateSquadAcquisitionCandidatesKernelName, 64, "indirect_squad_acquisition");
        RegisterComputeGpuPass(GpuPassFinalizeTargetAcquisition, CrowdSimulationCppFile, "CrowdVatIndirectRenderer.UpdateGpuBuffers", FinalizeTargetAcquisitionKernelName, 64, "indirect_combat_active");
        RegisterComputeGpuPass(GpuPassResolveInstanceCombat, CrowdSimulationCppFile, "CrowdVatIndirectRenderer.UpdateGpuBuffers", ResolveInstanceCombatKernelName, 64, "indirect_combat_active");
        RegisterComputeGpuPass(GpuPassClearSpatialQueries, CrowdSimulationCppFile, "CrowdVatIndirectRenderer.UpdateGpuBuffers", ClearSpatialQueriesKernelName, 8, "direct_spatial_queries");
        RegisterComputeGpuPass(GpuPassResolveSpatialQueries, CrowdSimulationCppFile, "CrowdVatIndirectRenderer.UpdateGpuBuffers", ResolveSpatialQueriesKernelName, 8, "direct_spatial_queries");
        RegisterComputeGpuPass(GpuPassFinalizeInstances, CrowdSimulationCppFile, "CrowdVatIndirectRenderer.UpdateGpuBuffers", FinalizeKernelName, 64, "indirect_alive");
        RegisterComputeGpuPass(GpuPassCaptureAiDebugGpuStage, CrowdAiDebugCppFile, "CrowdVatIndirectRenderer.CaptureAiDebugGpuStage", CaptureAiDebugGpuStageKernelName, 64, "direct_ai_debug_targets");
        RegisterComputeGpuPass(GpuPassDiscoverAiDebugGpuCandidates, CrowdAiDebugCppFile, "CrowdVatIndirectRenderer.CaptureAiDebugGpuCandidates", DiscoverAiDebugGpuCandidatesKernelName, 64, "direct_ai_debug_candidates");

        RegisterRenderGpuPass(GpuPassRenderSkinnedMeshLod0Marker);
        RegisterRenderGpuPass(GpuPassRenderSkinnedMeshLod1Marker);
        RegisterRenderGpuPass(GpuPassRenderSkinnedMeshLod2Marker);

        GpuPassDebugRegistry.Register(new GpuPassDebugInfo
        {
            passName = GpuPassRenderCombatTracerMarker,
            cppFile = CrowdVisibilityCppFile,
            cppFunction = "CrowdVatIndirectRenderer.Draw",
            shaderFile = CrowdCombatTracerShaderFile,
            shaderEntry = "UniversalForward",
            passType = "indirect_draw",
            dispatchKind = "render_mesh_indirect"
        });
    }

    private static void RegisterRenderGpuPass(string passName)
    {
        GpuPassDebugRegistry.Register(new GpuPassDebugInfo
        {
            passName = passName,
            cppFile = CrowdVisibilityCppFile,
            cppFunction = "CrowdVatIndirectRenderer.Draw",
            shaderFile = CrowdMainShaderFile,
            shaderEntry = "UniversalForward",
            passType = "indirect_draw",
            dispatchKind = "render_mesh_indirect"
        });
    }

    private static void RegisterComputeGpuPass(
        string passName,
        string cppFile,
        string cppFunction,
        string shaderEntry,
        uint threadGroupX,
        string dispatchKind)
    {
        GpuPassDebugRegistry.Register(new GpuPassDebugInfo
        {
            passName = passName,
            cppFile = cppFile,
            cppFunction = cppFunction,
            shaderFile = CrowdGpuShaderFile,
            shaderEntry = shaderEntry,
            threadGroupX = threadGroupX,
            threadGroupY = 1,
            threadGroupZ = 1,
            passType = "compute",
            dispatchKind = dispatchKind
        });
    }

    private void RefreshGpuPassDebugMetadataState()
    {
        bool captureOrDebugActive =
            GpuPassDebugRuntime.CaptureMetadataEnabled ||
            _aiDebugGpuStageCaptureActive ||
            _aiDebugGpuDiscoveryActive;

        _gpuPassDebugScopesActive = captureOrDebugActive && _enableGpuPassDebugScopes;
        _gpuPassBindingSidecarActive = captureOrDebugActive && _enableGpuPassBindingSidecar;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetGpuPassBuffer(
        int kernel,
        int propertyId,
        string shaderName,
        string runtimeName,
        ComputeBuffer buffer,
        GpuPassBindingAccess access)
    {
        _updateCompute.SetBuffer(kernel, propertyId, buffer);
        if (!_gpuPassBindingSidecarActive || buffer == null)
            return;

        RecordGpuPassBufferBinding(ResolveGpuPassNameForKernel(kernel), shaderName, runtimeName, buffer, access);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetGpuPassBuffer(
        int kernel,
        int propertyId,
        string shaderName,
        string runtimeName,
        GraphicsBuffer buffer,
        GpuPassBindingAccess access)
    {
        _updateCompute.SetBuffer(kernel, propertyId, buffer);
        if (!_gpuPassBindingSidecarActive || buffer == null)
            return;

        RecordGpuPassBufferBinding(ResolveGpuPassNameForKernel(kernel), shaderName, runtimeName, buffer, access);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetGpuPassMaterialBuffer(
        Material material,
        string passName,
        int propertyId,
        string shaderName,
        string runtimeName,
        ComputeBuffer buffer,
        GpuPassBindingAccess access)
    {
        material.SetBuffer(propertyId, buffer);
        if (!_gpuPassBindingSidecarActive || buffer == null)
            return;

        RecordGpuPassBufferBinding(passName, shaderName, runtimeName, buffer, access);
    }

    private void RecordGpuPassBufferBinding(
        string passName,
        string shaderName,
        string runtimeName,
        ComputeBuffer buffer,
        GpuPassBindingAccess access)
    {
        GpuPassBindingDebugRegistry.RecordBufferBinding(
            passName,
            shaderName,
            runtimeName,
            runtimeName,
            ResolveGpuPassResourceType(shaderName, access),
            access,
            buffer.count,
            buffer.stride,
            ResolveGpuPassAccessPattern(runtimeName, access),
            ResolveGpuPassRandomAccess(runtimeName));
    }

    private void RecordGpuPassBufferBinding(
        string passName,
        string shaderName,
        string runtimeName,
        GraphicsBuffer buffer,
        GpuPassBindingAccess access)
    {
        GpuPassBindingDebugRegistry.RecordBufferBinding(
            passName,
            shaderName,
            runtimeName,
            runtimeName,
            ResolveGpuPassResourceType(shaderName, access),
            access,
            buffer.count,
            buffer.stride,
            ResolveGpuPassAccessPattern(runtimeName, access),
            ResolveGpuPassRandomAccess(runtimeName));
    }

    private static string ResolveGpuPassResourceType(string shaderName, GpuPassBindingAccess access)
    {
        switch (shaderName)
        {
            case "_SpawnData":
                return "StructuredBuffer<InstanceSpawnData>";
            case "_SimulationPositionYawReadBuffer":
                return "StructuredBuffer<float4>";
            case "_SimulationPositionYawWriteBuffer":
                return "RWStructuredBuffer<float4>";
            case "_SimulationScaleReadBuffer":
                return "StructuredBuffer<float>";
            case "_SimulationScaleWriteBuffer":
                return "RWStructuredBuffer<float>";
            case "_SimulationVelocityReadBuffer":
                return "StructuredBuffer<float2>";
            case "_SimulationVelocityWriteBuffer":
                return "RWStructuredBuffer<float2>";
            case "_DeathStateBuffer":
                return "RWStructuredBuffer<uint>";
            case "_AliveInstanceIndexBuffer":
            case "_AliveInstanceCounterBuffer":
            case "_AliveInstanceDispatchArgsBuffer":
            case "_PhysicsActiveStateBuffer":
            case "_PhysicsActiveInstanceIndexBuffer":
            case "_PhysicsActiveInstanceCounterBuffer":
            case "_PhysicsActiveInstanceDispatchArgsBuffer":
            case "_CombatActiveStateBuffer":
            case "_CombatActiveInstanceIndexBuffer":
            case "_CombatActiveInstanceCounterBuffer":
            case "_CombatActiveInstanceDispatchArgsBuffer":
            case "_TargetAcquisitionLosDispatchArgsBuffer":
            case "_SquadAcquisitionDispatchArgsBuffer":
            case "_WakeGridCounterBuffer":
            case "_WakeGridOccupantBuffer":
            case "_GridCounterBuffer":
            case "_GridOccupantBuffer":
            case "_GridPrevTouchedCellBuffer":
            case "_GridPrevTouchedCounterBuffer":
            case "_GridCurrTouchedCellBuffer":
            case "_GridCurrTouchedCounterBuffer":
            case "_GridClearDispatchArgsBuffer":
            case "_SpatialOwnerIndexBuffer":
            case "_SpatialTargetMaskBuffer":
            case "_SpatialFlagsBuffer":
            case "_SpatialFactionBuffer":
            case "_SquadAliveCountBuffer":
            case "_CombatSquadCandidateCounterBuffer":
            case "_AiDebugCandidateCounterBuffer":
            case "_VisibleInstanceCounterBuffer":
            case "_VisibleLod1InstanceCounterBuffer":
            case "_VisibleLod2InstanceCounterBuffer":
            case "_VisibleRuntimeSquadMaskBuffer":
                return access == GpuPassBindingAccess.Uav ? "RWStructuredBuffer<uint>" : "StructuredBuffer<uint>";
            case "_AliveInstanceIndexReadBuffer":
            case "_AliveInstanceCounterReadBuffer":
            case "_PhysicsActiveInstanceIndexReadBuffer":
            case "_PhysicsActiveInstanceCounterReadBuffer":
            case "_CombatActiveInstanceIndexReadBuffer":
            case "_CombatActiveInstanceCounterReadBuffer":
            case "_AiDebugTargetIndexBuffer":
            case "_VisibleInstanceIndices":
            case "_VisibleLod1InstanceIndices":
            case "_VisibleLod2InstanceIndices":
                return access == GpuPassBindingAccess.Uav ? "RWStructuredBuffer<uint>" : "StructuredBuffer<uint>";
            case "_PhysicsActivationMetaBuffer":
            case "_CombatActivationMetaBuffer":
            case "_SpatialCapsuleStartRadiusBuffer":
            case "_SpatialCapsuleEndHeightBuffer":
            case "_InstanceFrameData":
            case "_InstanceFrameBlendData":
                return access == GpuPassBindingAccess.Uav ? "RWStructuredBuffer<float4>" : "StructuredBuffer<float4>";
            case "_InteractionSphereBuffer":
                return "StructuredBuffer<InteractionSphereData>";
            case "_SquadStateBuffer":
                return "StructuredBuffer<SquadStateData>";
            case "_AgentSquadDataBuffer":
                return "StructuredBuffer<AgentSquadData>";
            case "_FormationSlotBuffer":
                return "StructuredBuffer<FormationSlotData>";
            case "_RuntimeSquadBoundsBuffer":
                return access == GpuPassBindingAccess.Uav ? "RWStructuredBuffer<RuntimeSquadBoundsData>" : "StructuredBuffer<RuntimeSquadBoundsData>";
            case "_AnimationClipMetadataBuffer":
                return "StructuredBuffer<AnimationClipGpuData>";
            case "_InstanceAnimationStateBuffer":
                return "StructuredBuffer<InstanceAnimationStateGpuData>";
            case "_InstanceTransforms":
                return access == GpuPassBindingAccess.Uav ? "RWStructuredBuffer<MatrixRows>" : "StructuredBuffer<MatrixRows>";
            case "_SpatialQueryBuffer":
                return "StructuredBuffer<SpatialQueryData>";
            case "_SpatialQueryResultBuffer":
                return "RWStructuredBuffer<SpatialQueryResultData>";
            case "_SpatialQueryHitBuffer":
                return "RWStructuredBuffer<SpatialQueryHitData>";
            case "_CombatStateBuffer":
                return access == GpuPassBindingAccess.Uav ? "RWStructuredBuffer<InstanceCombatStateData>" : "StructuredBuffer<InstanceCombatStateData>";
            case "_TargetAcquisitionStateBuffer":
                return "RWStructuredBuffer<TargetAcquisitionStateData>";
            case "_TargetAcquisitionCandidateBuffer":
                return "RWStructuredBuffer<TargetAcquisitionCandidateData>";
            case "_CombatSquadCandidateBuffer":
                return "RWStructuredBuffer<CombatSquadCandidateData>";
            case "_CombatCandidateClusterWorkItemBuffer":
                return "StructuredBuffer<CombatCandidateClusterWorkItemData>";
            case "_AiDebugStageRecordBuffer":
                return "RWStructuredBuffer<CrowdVatAiDebugGpuStageRecord>";
            case "_AiDebugCandidateRecordBuffer":
                return "RWStructuredBuffer<CrowdVatAiDebugGpuCandidateRecord>";
            case "_VisibleRenderArgsBuffer":
                return "RWStructuredBuffer<IndirectDrawIndexedArgsData>";
            default:
                return access == GpuPassBindingAccess.Uav ? "RWStructuredBuffer<unknown>" : "StructuredBuffer<unknown>";
        }
    }

    private static string ResolveGpuPassAccessPattern(string runtimeName, GpuPassBindingAccess access)
    {
        if (string.IsNullOrEmpty(runtimeName))
            return string.Empty;

        string lowerName = runtimeName.ToLowerInvariant();
        bool writes = access == GpuPassBindingAccess.Uav;
        if (lowerName.Contains("gridcounter") || lowerName.Contains("gridoccupant"))
            return writes ? "scatter/interlocked write by spatial grid cell" : "random lookup by spatial grid cell";

        if (lowerName.Contains("wakegrid"))
            return writes ? "scatter/interlocked write by wake grid cell" : "random lookup by wake grid cell";

        if (lowerName.Contains("simulationposition") ||
            lowerName.Contains("simulationscale") ||
            lowerName.Contains("simulationvelocity"))
            return writes ? "one value written per active entity, mostly sequential" : "one value read per active entity, mostly sequential";

        if (lowerName.Contains("instanceframe") ||
            lowerName.Contains("instancetransform"))
            return writes ? "per visible/alive entity animation output, sequential by instance" : "per visible entity render read, indexed by visible instance id";

        if (lowerName.Contains("aliveinstance") ||
            lowerName.Contains("physicsactiveinstance") ||
            lowerName.Contains("combatactiveinstance") ||
            lowerName.Contains("visibleinstance"))
            return writes ? "compacted index/counter append writes" : "indirect compacted index/counter reads";

        if (lowerName.Contains("combat") || lowerName.Contains("targetacquisition"))
            return writes ? "per combat-active entity state update with spatial candidate lookups" : "per entity combat state read";

        if (lowerName.Contains("spatialquery"))
            return writes ? "per query result writes, bounded by query capacity" : "per query reads, mostly sequential";

        if (lowerName.Contains("spatial") || lowerName.Contains("agent") || lowerName.Contains("squad") || lowerName.Contains("formation"))
            return writes ? "per entity or squad write, mostly sequential" : "per entity or squad read, mostly sequential";

        if (lowerName.Contains("dispatchargs") || lowerName.Contains("renderargs"))
            return writes ? "single indirect argument write" : "single indirect argument read";

        return writes ? "write pattern not annotated" : "read pattern not annotated";
    }

    private static string ResolveGpuPassRandomAccess(string runtimeName)
    {
        if (string.IsNullOrEmpty(runtimeName))
            return string.Empty;

        string lowerName = runtimeName.ToLowerInvariant();
        if (lowerName.Contains("animationclipmetadata"))
            return "clip metadata sampled by current clip id";

        if (lowerName.Contains("gridcounter") || lowerName.Contains("gridoccupant"))
            return "spatial grid cell indexed from quantized entity position";

        if (lowerName.Contains("combat") || lowerName.Contains("targetacquisition"))
            return "combat target/candidate state indexed by instance id and squad candidate";

        if (lowerName.Contains("visibleinstance"))
            return "render shader maps visible instance id to source instance id";

        if (lowerName.Contains("runtimesquadbounds"))
            return "runtime squad indexed buffer used by coarse visibility prepass";

        return string.Empty;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DispatchGpuPass(int kernel, int threadGroupsX, int threadGroupsY, int threadGroupsZ)
    {
        if (!CanUseGpuPassDebugScopes())
        {
            _updateCompute.Dispatch(kernel, threadGroupsX, threadGroupsY, threadGroupsZ);
            return;
        }

        DispatchGpuPass(ResolveGpuPassNameForKernel(kernel), kernel, threadGroupsX, threadGroupsY, threadGroupsZ);
    }

    private void DispatchGpuPass(string passName, int kernel, int threadGroupsX, int threadGroupsY, int threadGroupsZ)
    {
        if (!ShouldWrapGpuPassDispatch(passName))
        {
            _updateCompute.Dispatch(kernel, threadGroupsX, threadGroupsY, threadGroupsZ);
            return;
        }

        // Keep the command buffer unnamed so RenderDoc only shows one semantic marker layer.
        CommandBuffer commandBuffer = CommandBufferPool.Get();
        try
        {
            commandBuffer.BeginSample(passName);
            commandBuffer.DispatchCompute(_updateCompute, kernel, threadGroupsX, threadGroupsY, threadGroupsZ);
            commandBuffer.EndSample(passName);
            Graphics.ExecuteCommandBuffer(commandBuffer);
        }
        catch (Exception)
        {
            _gpuPassDebugScopesFailed = true;
            _updateCompute.Dispatch(kernel, threadGroupsX, threadGroupsY, threadGroupsZ);
        }
        finally
        {
            CommandBufferPool.Release(commandBuffer);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DispatchGpuPassIndirect(int kernel, ComputeBuffer argsBuffer)
    {
        if (!CanUseGpuPassDebugScopes())
        {
            _updateCompute.DispatchIndirect(kernel, argsBuffer, 0u);
            return;
        }

        DispatchGpuPassIndirect(ResolveGpuPassNameForKernel(kernel), kernel, argsBuffer, 0u);
    }

    private void DispatchGpuPassIndirect(string passName, int kernel, ComputeBuffer argsBuffer, uint argsOffset)
    {
        if (!ShouldWrapGpuPassDispatch(passName))
        {
            _updateCompute.DispatchIndirect(kernel, argsBuffer, argsOffset);
            return;
        }

        // Keep the command buffer unnamed so RenderDoc only shows one semantic marker layer.
        CommandBuffer commandBuffer = CommandBufferPool.Get();
        try
        {
            commandBuffer.BeginSample(passName);
            commandBuffer.DispatchCompute(_updateCompute, kernel, argsBuffer, argsOffset);
            commandBuffer.EndSample(passName);
            Graphics.ExecuteCommandBuffer(commandBuffer);
        }
        catch (Exception)
        {
            _gpuPassDebugScopesFailed = true;
            _updateCompute.DispatchIndirect(kernel, argsBuffer, argsOffset);
        }
        finally
        {
            CommandBufferPool.Release(commandBuffer);
        }
    }

    private void DrawGpuPassRenderMeshIndirect(
        string markerLabel,
        RenderParams renderParams,
        Mesh mesh,
        GraphicsBuffer argsBuffer,
        int commandCount,
        int startCommand)
    {
        if (!ShouldWrapGpuPassDispatch(markerLabel) || !TryBeginImmediateGpuPassMarker(markerLabel, out CommandBuffer commandBuffer))
        {
            Graphics.RenderMeshIndirect(renderParams, mesh, argsBuffer, commandCount, startCommand);
            return;
        }

        try
        {
            Graphics.RenderMeshIndirect(renderParams, mesh, argsBuffer, commandCount, startCommand);
        }
        finally
        {
            EndImmediateGpuPassMarker(markerLabel, commandBuffer);
        }
    }

    private bool TryBeginImmediateGpuPassMarker(string markerLabel, out CommandBuffer commandBuffer)
    {
        commandBuffer = CommandBufferPool.Get();
        try
        {
            commandBuffer.BeginSample(markerLabel);
            Graphics.ExecuteCommandBuffer(commandBuffer);
            commandBuffer.Clear();
            return true;
        }
        catch (Exception)
        {
            _gpuPassDebugScopesFailed = true;
            CommandBufferPool.Release(commandBuffer);
            commandBuffer = null;
            return false;
        }
    }

    private void EndImmediateGpuPassMarker(string markerLabel, CommandBuffer commandBuffer)
    {
        try
        {
            commandBuffer.EndSample(markerLabel);
            Graphics.ExecuteCommandBuffer(commandBuffer);
        }
        catch (Exception)
        {
            _gpuPassDebugScopesFailed = true;
        }
        finally
        {
            CommandBufferPool.Release(commandBuffer);
        }
    }

    private bool ShouldWrapGpuPassDispatch(string passName)
    {
        return CanUseGpuPassDebugScopes() && !string.IsNullOrEmpty(passName);
    }

    private bool CanUseGpuPassDebugScopes()
    {
        return _gpuPassDebugScopesActive && !_gpuPassDebugScopesFailed;
    }

    private static string ResolveCrowdRenderSkinnedMeshPassName(int tierIndex)
    {
        return tierIndex switch
        {
            0 => GpuPassRenderSkinnedMeshLod0,
            1 => GpuPassRenderSkinnedMeshLod1,
            2 => GpuPassRenderSkinnedMeshLod2,
            _ => GpuPassRenderSkinnedMeshLod0
        };
    }

    private static string ResolveCrowdRenderSkinnedMeshMarker(int tierIndex)
    {
        return tierIndex switch
        {
            0 => GpuPassRenderSkinnedMeshLod0Marker,
            1 => GpuPassRenderSkinnedMeshLod1Marker,
            2 => GpuPassRenderSkinnedMeshLod2Marker,
            _ => GpuPassRenderSkinnedMeshLod0Marker
        };
    }

    private string ResolveGpuPassNameForKernel(int kernel)
    {
        if (kernel < 0)
            return string.Empty;

        if (kernel == _buildAliveInstanceListKernel)
            return GpuPassBuildAliveList;
        if (kernel == _buildAliveDispatchArgsKernel)
            return GpuPassBuildAliveDispatchArgs;
        if (kernel == _clearWakeGridKernel)
            return GpuPassWakeGridClear;
        if (kernel == _buildWakeGridKernel)
            return GpuPassWakeGridBuild;
        if (kernel == _evaluatePhysicsActiveKernel)
            return GpuPassEvaluatePhysicsActive;
        if (kernel == _buildPhysicsActiveInstanceListKernel)
            return GpuPassBuildPhysicsActiveList;
        if (kernel == _buildPhysicsActiveDispatchArgsKernel)
            return GpuPassBuildPhysicsActiveDispatchArgs;
        if (kernel == _evaluateCombatActiveKernel)
            return GpuPassEvaluateCombatActive;
        if (kernel == _buildCombatActiveInstanceListKernel)
            return GpuPassBuildCombatActiveList;
        if (kernel == _compactCombatActiveInstanceListKernel)
            return GpuPassCompactCombatActiveList;
        if (kernel == _buildCombatActiveDispatchArgsKernel)
            return GpuPassBuildCombatActiveDispatchArgs;
        if (kernel == _clearCombatSquadCandidateCountersKernel)
            return GpuPassClearCombatSquadCandidateCounters;
        if (kernel == _buildCombatCandidateClustersKernel)
            return GpuPassBuildCombatCandidateClusters;
        if (kernel == _clearVisibleRuntimeSquadBoundsKernel)
            return GpuPassClearVisibleRuntimeSquadBounds;
        if (kernel == _buildVisibleRuntimeSquadBoundsKernel)
            return GpuPassBuildVisibleRuntimeSquadBounds;
        if (kernel == _cullVisibleRuntimeSquadsKernel)
            return GpuPassCullVisibleRuntimeSquads;
        if (kernel == _buildVisibleRuntimeInstanceListKernel)
            return GpuPassBuildVisibleRuntimeList;
        if (kernel == _buildVisibleIndirectArgsKernel)
            return GpuPassBuildVisibleIndirectArgs;
        if (kernel == _predictKernel)
            return GpuPassPredictInstances;
        if (kernel == _buildGridClearDispatchArgsKernel)
            return GpuPassBuildGridClearDispatchArgs;
        if (kernel == _clearGridKernel)
            return GpuPassClearGrid;
        if (kernel == _buildGridKernel)
            return GpuPassBuildGrid;
        if (kernel == _buildSpatialElementsKernel)
            return GpuPassBuildSpatialElements;
        if (kernel == _solveCrowdKernel)
            return GpuPassSolveCrowd;
        if (kernel == _resolveTargetAcquisitionKernel)
            return GpuPassResolveTargetAcquisition;
        if (kernel == _buildTargetAcquisitionLosDispatchArgsKernel)
            return GpuPassBuildTargetAcquisitionLosDispatchArgs;
        if (kernel == _resolveTargetAcquisitionLineOfSightKernel)
            return GpuPassResolveTargetAcquisitionLineOfSight;
        if (kernel == _buildSquadAcquisitionDispatchArgsKernel)
            return GpuPassBuildSquadAcquisitionDispatchArgs;
        if (kernel == _evaluateSquadAcquisitionCandidatesKernel)
            return GpuPassEvaluateSquadAcquisitionCandidates;
        if (kernel == _finalizeTargetAcquisitionKernel)
            return GpuPassFinalizeTargetAcquisition;
        if (kernel == _resolveInstanceCombatKernel)
            return GpuPassResolveInstanceCombat;
        if (kernel == _clearSpatialQueriesKernel)
            return GpuPassClearSpatialQueries;
        if (kernel == _resolveSpatialQueriesKernel)
            return GpuPassResolveSpatialQueries;
        if (kernel == _finalizeKernel)
            return GpuPassFinalizeInstances;
        if (kernel == _captureAiDebugGpuStageKernel)
            return GpuPassCaptureAiDebugGpuStage;
        if (kernel == _discoverAiDebugGpuCandidatesKernel)
            return GpuPassDiscoverAiDebugGpuCandidates;

        return string.Empty;
    }
}
