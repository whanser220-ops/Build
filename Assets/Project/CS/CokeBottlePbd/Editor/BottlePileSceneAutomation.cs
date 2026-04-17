using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class BottlePileSceneAutomation
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string ComputeShaderPath = "Assets/Project/CS/CokeBottlePbd/Shader/BottlePilePbd.compute";
    private const string ShaderAssetPath = "Assets/Project/CS/CokeBottlePbd/Shader/BottlePileInstanced.shader";
    private const string MaterialFolderPath = "Assets/Project/CS/CokeBottlePbd/Materials";
    private const string MaterialAssetPath = "Assets/Project/CS/CokeBottlePbd/Materials/BottlePileInstanced.mat";
    private const string ConfigFolderPath = "Assets/Project/CS/CokeBottlePbd/Config";
    private const string ArchetypeAssetPath = "Assets/Project/CS/CokeBottlePbd/Config/BottleArchetype_Default.asset";
    private const string InteractionProfileAssetPath = "Assets/Project/CS/CokeBottlePbd/Config/BottleInteractionProfile_Default.asset";
    private const string SimulationConfigAssetPath = "Assets/Project/CS/CokeBottlePbd/Config/BottleSimulationConfig_Default.asset";
    private const string PileObjectName = "CokeBottlePile";
    private const string CharacterProxyObjectName = "BottlePileCharacterProxyBox";

    [MenuItem("Tools/CokeBottlePbd/Setup Sample Scene")]
    public static void SetupSampleSceneMenu()
    {
        SetupSampleScene();
    }

    public static void SetupSampleScene()
    {
        SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        if (sceneAsset == null)
        {
            Debug.LogError($"未找到场景: {ScenePath}");
            return;
        }

        ComputeShader solverCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputeShaderPath);
        if (solverCompute == null)
        {
            Debug.LogError($"未找到 Compute Shader: {ComputeShaderPath}");
            return;
        }

        Material instancedMaterial = LoadOrCreateMaterial();
        if (instancedMaterial == null)
        {
            Debug.LogError("无法创建或加载瓶堆实例化材质。");
            return;
        }

        BottleArchetypeAsset archetype = LoadOrCreateArchetype(instancedMaterial);
        BottleInteractionProfile interactionProfile = LoadOrCreateInteractionProfile();
        BottleSimulationConfig simulationConfig = LoadOrCreateSimulationConfig();
        if (archetype == null || interactionProfile == null || simulationConfig == null)
        {
            Debug.LogError("无法创建瓶堆默认配置资产。");
            return;
        }

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        CharacterController characterController = Object.FindFirstObjectByType<CharacterController>();
        BoxCollider characterBoxCollider = ResolveOrCreateCharacterBoxCollider(characterController, interactionProfile);
        Terrain terrain = Object.FindFirstObjectByType<Terrain>();

        GameObject pileObject = GameObject.Find(PileObjectName);
        if (pileObject == null)
            pileObject = new GameObject(PileObjectName);

        pileObject.transform.position = ResolvePilePosition(characterController != null ? characterController.transform : null, terrain);
        pileObject.transform.rotation = Quaternion.identity;

        BottlePileVolume pileVolume = pileObject.GetComponent<BottlePileVolume>();
        if (pileVolume == null)
            pileVolume = pileObject.AddComponent<BottlePileVolume>();

        BottlePileSystem pileSystem = pileObject.GetComponent<BottlePileSystem>();
        if (pileSystem == null)
            pileSystem = pileObject.AddComponent<BottlePileSystem>();

        ConfigureVolume(pileVolume);
        ConfigureSystem(
            pileSystem,
            solverCompute,
            archetype,
            interactionProfile,
            simulationConfig,
            characterController,
            characterBoxCollider,
            pileVolume);

        EditorUtility.SetDirty(pileObject);
        EditorUtility.SetDirty(pileVolume);
        EditorUtility.SetDirty(pileSystem);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"已将 {PileObjectName} 挂入 {ScenePath}");
    }

    private static void ConfigureVolume(BottlePileVolume pileVolume)
    {
        SerializedObject serializedObject = new SerializedObject(pileVolume);
        SetInt(serializedObject, "_bottleCount", 512);
        SetVector3(serializedObject, "_pileSize", new Vector3(2.8f, 2.4f, 2.8f));
        SetInt(serializedObject, "_randomSeed", 12345);
        SetFloat(serializedObject, "_horizontalJitterFraction", 0.12f);
        SetFloat(serializedObject, "_maxTiltDegrees", 6.0f);
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ConfigureSystem(
        BottlePileSystem pileSystem,
        ComputeShader solverCompute,
        BottleArchetypeAsset archetype,
        BottleInteractionProfile interactionProfile,
        BottleSimulationConfig simulationConfig,
        CharacterController characterController,
        BoxCollider characterBoxCollider,
        BottlePileVolume pileVolume)
    {
        SerializedObject serializedObject = new SerializedObject(pileSystem);

        SetObjectReference(serializedObject, "_solverCompute", solverCompute);
        SetObjectReference(serializedObject, "_archetype", archetype);
        SetObjectReference(serializedObject, "_interactionProfile", interactionProfile);
        SetObjectReference(serializedObject, "_simulationConfig", simulationConfig);
        SetObjectReference(serializedObject, "_pileVolume", pileVolume);
        SetObjectReference(serializedObject, "_characterController", characterController);
        SetObjectReference(serializedObject, "_characterBoxCollider", characterBoxCollider);
        SetBool(serializedObject, "_useTerrainBoundsXZ", true);
        SetObjectReference(serializedObject, "_simulationTerrain", Object.FindFirstObjectByType<Terrain>());
        SetObjectReference(
            serializedObject,
            "_characterTransform",
            characterController != null ? characterController.transform : null);

        SetEnum(serializedObject, "_shadowCastingMode", (int)ShadowCastingMode.Off);
        SetBool(serializedObject, "_receiveShadows", false);
        SetFloat(serializedObject, "_renderBoundsPadding", 2.0f);
        SetBool(serializedObject, "_drawSimulationBounds", true);
        SetBool(serializedObject, "_drawPileBounds", true);
        SetBool(serializedObject, "_enableDebugStatsReadback", false);
        SetInt(serializedObject, "_debugReadbackInterval", 30);

        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static BoxCollider ResolveOrCreateCharacterBoxCollider(
        CharacterController characterController,
        BottleInteractionProfile interactionProfile)
    {
        if (characterController == null)
            return null;

        Transform proxyTransform = characterController.transform.Find(CharacterProxyObjectName);
        GameObject proxyObject;
        if (proxyTransform == null)
        {
            proxyObject = new GameObject(CharacterProxyObjectName);
            proxyObject.transform.SetParent(characterController.transform, false);
        }
        else
        {
            proxyObject = proxyTransform.gameObject;
        }

        proxyObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        proxyObject.transform.localScale = Vector3.one;

        BoxCollider boxCollider = proxyObject.GetComponent<BoxCollider>();
        if (boxCollider == null)
            boxCollider = proxyObject.AddComponent<BoxCollider>();

        boxCollider.center = interactionProfile != null
            ? interactionProfile.FallbackCharacterBoxCenter
            : characterController.center;
        boxCollider.size = interactionProfile != null
            ? interactionProfile.FallbackCharacterBoxSize
            : Vector3.one;
        boxCollider.isTrigger = true;
        boxCollider.enabled = false;
        return boxCollider;
    }

    private static Material LoadOrCreateMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialAssetPath);
        if (material != null)
            return material;

        EnsureFolder(MaterialFolderPath);

        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderAssetPath);
        if (shader == null)
            return null;

        material = new Material(shader);
        material.enableInstancing = true;
        material.SetColor("_BaseColor", new Color(0.76f, 0.07f, 0.08f, 1.0f));
        material.SetFloat("_Smoothness", 0.24f);

        AssetDatabase.CreateAsset(material, MaterialAssetPath);
        AssetDatabase.SaveAssets();
        return material;
    }

    private static BottleArchetypeAsset LoadOrCreateArchetype(Material instancedMaterial)
    {
        BottleArchetypeAsset asset = AssetDatabase.LoadAssetAtPath<BottleArchetypeAsset>(ArchetypeAssetPath);
        if (asset == null)
        {
            EnsureFolder(ConfigFolderPath);
            asset = ScriptableObject.CreateInstance<BottleArchetypeAsset>();
            AssetDatabase.CreateAsset(asset, ArchetypeAssetPath);
        }

        SerializedObject serializedObject = new SerializedObject(asset);
        SetObjectReference(serializedObject, "_instancedMaterial", instancedMaterial);
        SetFloat(serializedObject, "_bottleHeight", 0.24f);
        SetFloat(serializedObject, "_bottleRadius", 0.04f);
        SetFloat(serializedObject, "_particleRadius", 0.028f);
        SetBool(serializedObject, "_autoScaleBuiltInCapsule", true);
        SetVector3(serializedObject, "_meshScale", Vector3.one);
        SetVector3(serializedObject, "_meshOffset", Vector3.zero);
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);
        return asset;
    }

    private static BottleInteractionProfile LoadOrCreateInteractionProfile()
    {
        BottleInteractionProfile asset = AssetDatabase.LoadAssetAtPath<BottleInteractionProfile>(InteractionProfileAssetPath);
        if (asset == null)
        {
            EnsureFolder(ConfigFolderPath);
            asset = ScriptableObject.CreateInstance<BottleInteractionProfile>();
            AssetDatabase.CreateAsset(asset, InteractionProfileAssetPath);
        }

        SerializedObject serializedObject = new SerializedObject(asset);
        SetFloat(serializedObject, "_characterPushBase", 0.12f);
        SetFloat(serializedObject, "_characterPushFromSpeed", 0.18f);
        SetFloat(serializedObject, "_characterPushActivationSpeed", 0.08f);
        SetFloat(serializedObject, "_characterTangentialDamping", 0.08f);
        SetVector3(serializedObject, "_fallbackCharacterBoxCenter", new Vector3(0.0f, 0.5f, 0.0f));
        SetVector3(serializedObject, "_fallbackCharacterBoxSize", Vector3.one);
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);
        return asset;
    }

    private static BottleSimulationConfig LoadOrCreateSimulationConfig()
    {
        BottleSimulationConfig asset = AssetDatabase.LoadAssetAtPath<BottleSimulationConfig>(SimulationConfigAssetPath);
        if (asset == null)
        {
            EnsureFolder(ConfigFolderPath);
            asset = ScriptableObject.CreateInstance<BottleSimulationConfig>();
            AssetDatabase.CreateAsset(asset, SimulationConfigAssetPath);
        }

        SerializedObject serializedObject = new SerializedObject(asset);
        SetInt(serializedObject, "_solverIterations", 1);
        SetInt(serializedObject, "_substeps", 2);
        SetFloat(serializedObject, "_cellSize", 0.09f);
        SetFloat(serializedObject, "_shapeStiffness", 0.78f);
        SetFloat(serializedObject, "_velocityDamping", 0.992f);
        SetFloat(serializedObject, "_gravity", -9.81f);
        SetFloat(serializedObject, "_contactSlop", 0.0015f);
        SetFloat(serializedObject, "_maxSpeed", 15.0f);
        SetInt(serializedObject, "_sleepFrameThreshold", 18);
        SetFloat(serializedObject, "_sleepSpeedThreshold", 0.05f);
        SetFloat(serializedObject, "_sleepCharacterWakeRadius", 1.4f);
        SetFloat(serializedObject, "_sleepActiveNeighborWakeRadius", 0.4f);
        SetVector3(serializedObject, "_simulationBoundsSize", new Vector3(14.0f, 8.0f, 14.0f));
        SerializedProperty gridRefreshMode = serializedObject.FindProperty("_gridRefreshMode");
        if (gridRefreshMode != null)
            gridRefreshMode.enumValueIndex = 0;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);
        return asset;
    }

    private static void EnsureFolder(string folderPath)
    {
        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);

            current = next;
        }
    }

    private static Vector3 ResolvePilePosition(Transform characterTransform, Terrain terrain)
    {
        Vector3 candidatePosition;
        if (characterTransform != null)
        {
            Vector3 forward = Vector3.ProjectOnPlane(characterTransform.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f)
                forward = Vector3.forward;

            candidatePosition = characterTransform.position + forward.normalized * 5.5f + characterTransform.right * 0.6f;
        }
        else if (terrain != null)
        {
            Vector3 terrainCenter = terrain.transform.position + terrain.terrainData.size * 0.5f;
            candidatePosition = new Vector3(terrainCenter.x, terrain.transform.position.y, terrainCenter.z);
        }
        else
        {
            candidatePosition = new Vector3(0.0f, 0.0f, 6.0f);
        }

        if (terrain != null)
        {
            float terrainHeight = terrain.SampleHeight(candidatePosition) + terrain.transform.position.y;
            candidatePosition.y = terrainHeight + 0.02f;
        }
        else
        {
            candidatePosition.y = Mathf.Max(0.02f, candidatePosition.y);
        }

        return candidatePosition;
    }

    private static void SetObjectReference(SerializedObject serializedObject, string propertyName, Object value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
            property.objectReferenceValue = value;
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

    private static void SetVector3(SerializedObject serializedObject, string propertyName, Vector3 value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
            property.vector3Value = value;
    }

    private static void SetEnum(SerializedObject serializedObject, string propertyName, int enumValueIndex)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
            property.enumValueIndex = enumValueIndex;
    }
}
