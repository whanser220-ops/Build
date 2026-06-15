using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class QianxiaMeadowSceneMigrationUtility
{
    public const string MeadowSummerScenePath = "Assets/GameAssets/Worlds/Meadow/Scenes/Scene_MeadowEnvironment_01_Summer.unity";
    public const string CharacterPrefabPath = "Assets/Project/Characters/Qianxia/Generated/QianxiaThirdPerson.prefab";
    public const string CharacterObjectName = "QianxiaThirdPerson";

    private const float SpawnDistanceInFrontOfCamera = 4.0f;
    private const float GroundRaycastHeight = 80.0f;
    private const float GroundRaycastDistance = 180.0f;
    private static readonly Vector3 FallbackSpawnPosition = new Vector3(-5.8f, 1.23f, -10.1f);

    [MenuItem("Tools/Qianxia/Migrate Third Person To Current Build Scene")]
    public static void MigrateThirdPersonToCurrentBuildScene()
    {
        MigrateThirdPersonToScene(ResolveCurrentBuildScenePath());
    }

    [MenuItem("Tools/Qianxia/Migrate Third Person To Meadow Summer Scene")]
    public static void MigrateThirdPersonToMeadowSummerScene()
    {
        MigrateThirdPersonToScene(MeadowSummerScenePath);
    }

    public static void MigrateThirdPersonToCurrentBuildSceneFromCommandLine()
    {
        MigrateThirdPersonToCurrentBuildScene();
    }

    public static void MigrateThirdPersonToMeadowSummerSceneFromCommandLine()
    {
        MigrateThirdPersonToMeadowSummerScene();
    }

    public static void MigrateThirdPersonToScene(string scenePath)
    {
        if (string.IsNullOrWhiteSpace(scenePath))
            throw new ArgumentException("Scene path is required.", nameof(scenePath));

        SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
        if (sceneAsset == null)
            throw new InvalidOperationException("Scene asset not found: " + scenePath);

        GameObject characterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPrefabPath);
        if (characterPrefab == null)
            throw new InvalidOperationException("Qianxia character prefab not found: " + CharacterPrefabPath);

        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        Camera sceneCamera = ResolveSceneCamera();
        GameObject character = EnsureQianxiaCharacter(characterPrefab, sceneCamera);

        ConfigureCameraForQianxia(sceneCamera);
        ConfigureCharacterControllers(character, sceneCamera);

        EditorUtility.SetDirty(character);
        if (sceneCamera != null)
            EditorUtility.SetDirty(sceneCamera.gameObject);

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new InvalidOperationException("Failed to save scene after Qianxia migration: " + scenePath);

        Debug.Log("Qianxia third-person character migrated into scene: " + scenePath);
    }

    private static string ResolveCurrentBuildScenePath()
    {
        EditorBuildSettingsScene enabledScene = EditorBuildSettings.scenes
            .FirstOrDefault(item => item != null && item.enabled && !string.IsNullOrWhiteSpace(item.path));

        return enabledScene != null ? enabledScene.path : MeadowSummerScenePath;
    }

    private static Camera ResolveSceneCamera()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
            return mainCamera;

        return UnityEngine.Object.FindFirstObjectByType<Camera>();
    }

    private static GameObject EnsureQianxiaCharacter(GameObject characterPrefab, Camera sceneCamera)
    {
        GameObject existingCharacter = GameObject.Find(CharacterObjectName);
        if (existingCharacter != null)
        {
            existingCharacter.SetActive(true);
            return existingCharacter;
        }

        GameObject instance = PrefabUtility.InstantiatePrefab(characterPrefab) as GameObject;
        if (instance == null)
            throw new InvalidOperationException("Failed to instantiate Qianxia character prefab.");

        instance.name = CharacterObjectName;
        instance.transform.SetPositionAndRotation(
            ResolveSpawnPosition(sceneCamera, instance.GetComponent<CharacterController>()),
            ResolveSpawnRotation(sceneCamera));

        return instance;
    }

    private static Vector3 ResolveSpawnPosition(Camera sceneCamera, CharacterController characterController)
    {
        Vector3 position = FallbackSpawnPosition;
        if (sceneCamera != null)
        {
            Vector3 forward = Vector3.ProjectOnPlane(sceneCamera.transform.forward, Vector3.up);
            if (forward.sqrMagnitude > 0.0001f)
                position = sceneCamera.transform.position + forward.normalized * SpawnDistanceInFrontOfCamera;
        }

        float feetOffset = 0.0f;
        if (characterController != null)
            feetOffset = characterController.center.y - characterController.height * 0.5f;

        Physics.SyncTransforms();
        Vector3 rayOrigin = new Vector3(position.x, position.y + GroundRaycastHeight, position.z);
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, GroundRaycastDistance, ~0, QueryTriggerInteraction.Ignore))
            position.y = hit.point.y - feetOffset;

        return position;
    }

    private static Quaternion ResolveSpawnRotation(Camera sceneCamera)
    {
        if (sceneCamera == null)
            return Quaternion.identity;

        Vector3 forward = Vector3.ProjectOnPlane(sceneCamera.transform.forward, Vector3.up);
        if (forward.sqrMagnitude <= 0.0001f)
            return Quaternion.identity;

        return Quaternion.LookRotation(forward.normalized, Vector3.up);
    }

    private static void ConfigureCameraForQianxia(Camera sceneCamera)
    {
        if (sceneCamera == null)
            return;

        try
        {
            sceneCamera.gameObject.tag = "MainCamera";
        }
        catch (UnityException exception)
        {
            Debug.LogWarning("Unable to tag scene camera as MainCamera: " + exception.Message, sceneCamera);
        }

        MonoBehaviour[] behaviours = sceneCamera.GetComponents<MonoBehaviour>();
        for (int index = 0; index < behaviours.Length; index++)
        {
            MonoBehaviour behaviour = behaviours[index];
            if (behaviour == null || behaviour is UniversalAdditionalCameraData)
                continue;

            SerializedObject serializedObject = new SerializedObject(behaviour);
            SerializedProperty enabledProperty = serializedObject.FindProperty("m_Enabled");
            if (enabledProperty == null || !enabledProperty.boolValue)
                continue;

            enabledProperty.boolValue = false;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(behaviour);
        }
    }

    private static void ConfigureCharacterControllers(GameObject character, Camera sceneCamera)
    {
        if (character == null)
            throw new ArgumentNullException(nameof(character));

        QianxiaGenshinCharacterController movementController = character.GetComponent<QianxiaGenshinCharacterController>();
        if (movementController == null)
            throw new InvalidOperationException("Qianxia character is missing QianxiaGenshinCharacterController.");

        QianxiaGenshinCameraController cameraController = character.GetComponent<QianxiaGenshinCameraController>();
        if (cameraController == null)
            throw new InvalidOperationException("Qianxia character is missing QianxiaGenshinCameraController.");

        if (sceneCamera != null)
        {
            SerializedObject movementObject = new SerializedObject(movementController);
            SetObjectReference(movementObject, "_movementReferenceCamera", sceneCamera.transform);
            movementObject.ApplyModifiedPropertiesWithoutUndo();
        }

        SerializedObject cameraObject = new SerializedObject(cameraController);
        SetObjectReference(cameraObject, "_followTarget", character.transform);
        cameraObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetObjectReference(SerializedObject serializedObject, string propertyName, UnityEngine.Object value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null)
            throw new InvalidOperationException("Serialized property not found: " + propertyName);

        property.objectReferenceValue = value;
    }
}
