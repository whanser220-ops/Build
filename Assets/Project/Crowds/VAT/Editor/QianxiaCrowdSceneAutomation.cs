using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class QianxiaCrowdSceneAutomation
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string CrowdObjectName = "QianxiaCrowdIndirect";
    private const string CharacterPrefabPath = "Assets/Project/Characters/Qianxia/Generated/QianxiaThirdPerson.prefab";
    private const string VatPrefabPath = "Assets/Project/Characters/Qianxia/Generated/VAT/QianxiaCrowdLod2Vat.prefab";
    private const string ComputeShaderPath = "Assets/Project/Crowds/VAT/Shader/CrowdVatIndirect.compute";
    private const string ShaderPath = "Assets/Project/Crowds/VAT/Shader/CrowdVatIndirectLit.shader";

    [MenuItem("Tools/Qianxia/Setup Crowd In Sample Scene")]
    public static void SetupSampleSceneMenu()
    {
        SetupSampleScene();
    }

    public static void SetupSampleScene()
    {
        SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        if (sceneAsset == null)
            throw new InvalidOperationException($"未找到场景: {ScenePath}");

        GameObject characterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPrefabPath);
        GameObject vatPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VatPrefabPath);
        ComputeShader crowdCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputeShaderPath);
        Shader crowdShader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);

        if (characterPrefab == null)
            throw new InvalidOperationException($"未找到角色 prefab: {CharacterPrefabPath}");

        if (vatPrefab == null)
            throw new InvalidOperationException($"未找到 VAT prefab: {VatPrefabPath}");

        if (crowdCompute == null)
            throw new InvalidOperationException($"未找到 ComputeShader: {ComputeShaderPath}");

        if (crowdShader == null)
            throw new InvalidOperationException($"未找到 Shader: {ShaderPath}");

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject qianxiaCharacter = EnsureQianxiaCharacter(characterPrefab);
        GameObject crowdObject = EnsureCrowdObject(vatPrefab, crowdCompute, crowdShader, qianxiaCharacter);
        EnsureRuntimeSquadControllers(crowdObject, qianxiaCharacter);

        EditorUtility.SetDirty(qianxiaCharacter);
        EditorUtility.SetDirty(crowdObject);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"已更新示例场景中的 crowd 配置: {ScenePath}");
    }

    private static GameObject EnsureQianxiaCharacter(GameObject characterPrefab)
    {
        GameObject existingCharacter = GameObject.Find("QianxiaThirdPerson");
        if (existingCharacter != null)
            return existingCharacter;

        GameObject instance = PrefabUtility.InstantiatePrefab(characterPrefab) as GameObject;
        if (instance == null)
            throw new InvalidOperationException("无法实例化千夏角色 prefab。");

        instance.name = "QianxiaThirdPerson";
        instance.transform.SetPositionAndRotation(new Vector3(0.0f, 1.23f, 0.0f), Quaternion.identity);
        return instance;
    }

    private static GameObject EnsureCrowdObject(
        GameObject vatPrefab,
        ComputeShader crowdCompute,
        Shader crowdShader,
        GameObject qianxiaCharacter)
    {
        GameObject crowdObject = GameObject.Find(CrowdObjectName);
        if (crowdObject == null)
            crowdObject = new GameObject(CrowdObjectName);

        Vector3 crowdForward = ResolveCrowdForward(qianxiaCharacter != null ? qianxiaCharacter.transform : null);
        crowdObject.transform.SetPositionAndRotation(
            ResolveCrowdPosition(qianxiaCharacter != null ? qianxiaCharacter.transform : null, crowdForward),
            Quaternion.LookRotation(crowdForward, Vector3.up));

        CrowdVatIndirectRenderer renderer = crowdObject.GetComponent<CrowdVatIndirectRenderer>();
        if (renderer == null)
            renderer = crowdObject.AddComponent<CrowdVatIndirectRenderer>();

        CrowdVatPlayer templatePlayer = ResolveTemplatePlayer(vatPrefab);
        if (templatePlayer == null)
            throw new InvalidOperationException("VAT prefab 上未找到 CrowdVatPlayer。");

        SerializedObject serializedObject = new SerializedObject(renderer);
        CharacterController characterController = qianxiaCharacter != null ? qianxiaCharacter.GetComponent<CharacterController>() : null;
        Terrain terrain = UnityEngine.Object.FindFirstObjectByType<Terrain>();
        Vector2 crowdAreaSize = terrain != null
            ? new Vector2(Mathf.Max(1.0f, terrain.terrainData.size.x), Mathf.Max(1.0f, terrain.terrainData.size.z))
            : new Vector2(10.0f, 6.0f);

        SetObjectReference(serializedObject, "_templatePrefab", templatePlayer);
        SetObjectReference(serializedObject, "_updateCompute", crowdCompute);
        SetObjectReference(serializedObject, "_indirectShader", crowdShader);
        SetObjectReference(serializedObject, "_characterController", characterController);
        SetObjectReference(serializedObject, "_activeBubbleTarget", qianxiaCharacter != null ? qianxiaCharacter.transform : null);
        SetString(serializedObject, "_clipName", string.Empty);
        SetInt(serializedObject, "_instanceCount", 1024);
        SetBool(serializedObject, "_distributeAcrossWholeTerrain", true);
        SetVector2(serializedObject, "_areaSize", crowdAreaSize);
        SetFloat(serializedObject, "_cellJitter", 0.68f);
        SetFloat(serializedObject, "_heightOffset", 0.0f);
        SetInt(serializedObject, "_randomSeed", 20260416);
        SetFloat(serializedObject, "_factionCenterGap", 1.5f);
        SetBool(serializedObject, "_enableTerrainCollision", true);
        SetBool(serializedObject, "_autoResolveTerrain", true);
        SetObjectReference(serializedObject, "_terrain", terrain);
        SetFloat(serializedObject, "_terrainHeightOffset", 0.02f);
        SetFloat(serializedObject, "_baseScale", 1.6f);
        SetVector2(serializedObject, "_scaleMultiplierRange", new Vector2(0.95f, 1.05f));
        SetBool(serializedObject, "_playOnEnable", true);
        SetBool(serializedObject, "_loopOverride", true);
        SetFloat(serializedObject, "_playbackSpeed", 1.0f);
        SetVector2(serializedObject, "_playbackSpeedMultiplierRange", new Vector2(0.9f, 1.1f));
        SetFloat(serializedObject, "_normalizedStartOffsetRandom", 1.0f);
        SetBool(serializedObject, "_enableApproximateCollision", true);
        SetFloat(serializedObject, "_collisionRadius", 0.38f);
        SetFloat(serializedObject, "_queryCellSize", 0.95f);
        SetInt(serializedObject, "_maxCellOccupancy", 32);
        SetInt(serializedObject, "_solverIterations", 2);
        SetFloat(serializedObject, "_selfCollisionStrength", 0.92f);
        SetFloat(serializedObject, "_velocityDamping", 0.82f);
        SetFloat(serializedObject, "_maxPushPerStep", 0.22f);
        SetFloat(serializedObject, "_maxDisplacementFromSpawn", 1.4f);
        SetFloat(serializedObject, "_inactiveReturnStrength", 0.24f);
        SetBool(serializedObject, "_enableActiveBubble", true);
        SetBool(serializedObject, "_autoResolveCharacterController", true);
        SetFloat(serializedObject, "_activeBubbleRadius", 13.0f);
        SetFloat(serializedObject, "_activeBubbleRetentionRadius", 17.0f);
        SetBool(serializedObject, "_useCharacterAsInteractionSphere", false);
        SetFloat(serializedObject, "_characterInteractionRadiusMultiplier", 2.4f);
        SetFloat(serializedObject, "_characterInteractionStrength", 0.16f);
        SetEnum(serializedObject, "_shadowCastingMode", (int)ShadowCastingMode.Off);
        SetBool(serializedObject, "_receiveShadows", false);
        SetFloat(serializedObject, "_boundsPadding", 4.0f);
        SetFloat(serializedObject, "_renderChunkWorldSize", 5.0f);
        serializedObject.ApplyModifiedPropertiesWithoutUndo();

        renderer.RebuildCrowdLayout();
        return crowdObject;
    }

    private static void EnsureRuntimeSquadControllers(GameObject crowdObject, GameObject qianxiaCharacter)
    {
        if (crowdObject == null)
            return;

        CrowdVatIndirectRenderer renderer = crowdObject.GetComponent<CrowdVatIndirectRenderer>();
        if (renderer == null)
            return;

        CrowdVatSquadController squadController = crowdObject.GetComponent<CrowdVatSquadController>();
        if (squadController == null)
            squadController = crowdObject.AddComponent<CrowdVatSquadController>();

        SerializedObject squadSerializedObject = new SerializedObject(squadController);
        SetObjectReference(squadSerializedObject, "_renderer", renderer);
        SetBool(squadSerializedObject, "_autoResolveRenderer", true);
        squadSerializedObject.ApplyModifiedPropertiesWithoutUndo();

        if (!squadController.HasAnchorTransforms())
            squadController.CreateDefaultTacticalSquads();

        if (qianxiaCharacter == null)
            return;

        CrowdVatSquadCommandController commandController = qianxiaCharacter.GetComponent<CrowdVatSquadCommandController>();
        if (commandController == null)
            commandController = qianxiaCharacter.AddComponent<CrowdVatSquadCommandController>();

        Camera mainCamera = Camera.main;
        if (mainCamera == null)
            mainCamera = UnityEngine.Object.FindFirstObjectByType<Camera>();

        SerializedObject commandSerializedObject = new SerializedObject(commandController);
        SetObjectReference(commandSerializedObject, "_squadController", squadController);
        SetObjectReference(commandSerializedObject, "_targetCamera", mainCamera);
        commandSerializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static CrowdVatPlayer ResolveTemplatePlayer(GameObject vatPrefab)
    {
        return vatPrefab != null ? vatPrefab.GetComponentInChildren<CrowdVatPlayer>(true) : null;
    }

    private static Vector3 ResolveCrowdPosition(Transform characterTransform, Vector3 crowdForward)
    {
        Vector3 forward = crowdForward.sqrMagnitude > 0.001f ? crowdForward.normalized : Vector3.forward;
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
            mainCamera = UnityEngine.Object.FindFirstObjectByType<Camera>();

        Vector3 basePosition;
        if (TryResolveNearCameraTerrainAnchor(mainCamera, forward, out Vector3 visibleAnchor))
        {
            basePosition = visibleAnchor;
        }
        else if (TryResolveVisibleGroundAnchor(mainCamera, forward, out visibleAnchor))
        {
            basePosition = visibleAnchor;
        }
        else
        {
            basePosition = characterTransform != null
                ? characterTransform.position + forward * 18.0f
                : forward * 18.0f;
        }

        Terrain terrain = UnityEngine.Object.FindFirstObjectByType<Terrain>();
        if (terrain != null)
        {
            float terrainHeight = terrain.SampleHeight(basePosition) + terrain.transform.position.y;
            basePosition.y = terrainHeight + 0.02f;
        }
        else
        {
            basePosition.y = Mathf.Max(basePosition.y, 0.02f);
        }

        return basePosition;
    }

    private static bool TryResolveNearCameraTerrainAnchor(Camera camera, Vector3 crowdForward, out Vector3 worldPosition)
    {
        worldPosition = default;
        if (camera == null)
            return false;

        Vector3 flattenedForward = Vector3.ProjectOnPlane(crowdForward, Vector3.up);
        if (flattenedForward.sqrMagnitude <= 1e-4f)
            flattenedForward = Vector3.forward;

        Vector3 flattenedRight = Vector3.Cross(Vector3.up, flattenedForward).normalized;
        Terrain terrain = UnityEngine.Object.FindFirstObjectByType<Terrain>();
        float[] forwardDistances = { 10.0f, 13.0f, 16.0f };
        float[] lateralOffsets = { 0.0f, -2.0f, 2.0f };

        for (int distanceIndex = 0; distanceIndex < forwardDistances.Length; distanceIndex++)
        {
            for (int offsetIndex = 0; offsetIndex < lateralOffsets.Length; offsetIndex++)
            {
                Vector3 candidate = camera.transform.position
                    + flattenedForward.normalized * forwardDistances[distanceIndex]
                    + flattenedRight * lateralOffsets[offsetIndex];

                if (terrain != null)
                {
                    Vector3 terrainLocal = candidate - terrain.transform.position;
                    if (terrainLocal.x < 0.0f || terrainLocal.z < 0.0f ||
                        terrainLocal.x > terrain.terrainData.size.x ||
                        terrainLocal.z > terrain.terrainData.size.z)
                    {
                        continue;
                    }

                    candidate.y = terrain.SampleHeight(candidate) + terrain.transform.position.y + 0.02f;
                }
                else
                {
                    candidate.y = Mathf.Max(0.02f, candidate.y - 2.0f);
                }

                Vector3 viewport = camera.WorldToViewportPoint(candidate);
                if (viewport.z <= 0.0f)
                    continue;

                if (viewport.x < 0.18f || viewport.x > 0.82f || viewport.y < 0.24f || viewport.y > 0.72f)
                    continue;

                worldPosition = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveVisibleGroundAnchor(Camera camera, Vector3 crowdForward, out Vector3 worldPosition)
    {
        worldPosition = default;
        if (camera == null)
            return false;

        Ray ray = camera.ViewportPointToRay(new Vector3(0.68f, 0.58f, 0.0f));
        if (!Physics.Raycast(ray, out RaycastHit hit, 200.0f))
            return false;

        Vector3 flattenedForward = Vector3.ProjectOnPlane(crowdForward, Vector3.up);
        if (flattenedForward.sqrMagnitude <= 1e-4f)
            flattenedForward = Vector3.forward;

        worldPosition = hit.point - flattenedForward.normalized * 2.0f;
        return true;
    }

    private static Vector3 ResolveCrowdForward(Transform characterTransform)
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
            mainCamera = UnityEngine.Object.FindFirstObjectByType<Camera>();

        Vector3 preferredForward = mainCamera != null
            ? Vector3.ProjectOnPlane(mainCamera.transform.forward, Vector3.up)
            : characterTransform != null
                ? Vector3.ProjectOnPlane(characterTransform.forward, Vector3.up)
                : Vector3.forward;

        if (preferredForward.sqrMagnitude <= 1e-4f)
            preferredForward = Vector3.forward;

        return preferredForward.normalized;
    }

    private static void SetObjectReference(SerializedObject serializedObject, string propertyName, UnityEngine.Object value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
            property.objectReferenceValue = value;
    }

    private static void SetString(SerializedObject serializedObject, string propertyName, string value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
            property.stringValue = value;
    }

    private static void SetInt(SerializedObject serializedObject, string propertyName, int value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
            property.intValue = value;
    }

    private static void SetFloat(SerializedObject serializedObject, string propertyName, float value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
            property.floatValue = value;
    }

    private static void SetBool(SerializedObject serializedObject, string propertyName, bool value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
            property.boolValue = value;
    }

    private static void SetVector2(SerializedObject serializedObject, string propertyName, Vector2 value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
            property.vector2Value = value;
    }

    private static void SetEnum(SerializedObject serializedObject, string propertyName, int value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
            property.enumValueIndex = value;
    }
}
