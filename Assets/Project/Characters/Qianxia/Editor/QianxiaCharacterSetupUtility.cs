using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.InputSystem;

public static class QianxiaCharacterSetupUtility
{
    private const string ExternalModelPath = @"D:\mouxin\mhy\qianxia\Qianxia_Rokoko_BlenderClean.fbx";
    private const string ExternalWalkPath = @"D:\mouxin\donghua\Walking.fbx";
    private const string SourceModelsFolder = "Assets/Project/Characters/Qianxia/SourceModels";
    private const string SourceAnimationsFolder = "Assets/Project/Characters/Qianxia/SourceAnimations";
    private const string SourceWalkFolder = "Assets/Project/Characters/Qianxia/SourceAnimations/Walk";
    private const string GeneratedFolder = "Assets/Project/Characters/Qianxia/Generated";
    private const string AnimatorControllerPath = GeneratedFolder + "/QianxiaLocomotion.controller";
    private const string PrefabPath = GeneratedFolder + "/QianxiaThirdPerson.prefab";
    private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";
    private const string InstallRequestFile = ".workspace/artifacts/qianxia-install.request";
    private const string InstallResultFile = ".workspace/artifacts/qianxia-install.result.txt";

    [MenuItem("Tools/Qianxia/Install Walk Character From External FBX")]
    public static void InstallWalkCharacterFromExternalFbx()
    {
        EnsureFolder(SourceModelsFolder);
        EnsureFolder(SourceAnimationsFolder);
        EnsureFolder(SourceWalkFolder);
        EnsureFolder(GeneratedFolder);

        CopyExternalAsset(ExternalModelPath, QianxiaFbxImportConfigurator.ModelAssetPath);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ApplyCharacterModelImporterOverrides();

        CopyExternalAsset(ExternalWalkPath, QianxiaFbxImportConfigurator.WalkAssetPath);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ApplyWalkAnimationImporterOverrides();

        AnimatorController controller = CreateOrReplaceAnimatorController();
        GameObject prefab = CreateOrReplacePrefab(controller);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        EditorGUIUtility.PingObject(prefab);

        Debug.Log($"Qianxia walk character install complete: {PrefabPath}");
    }

    public static string GetInstallRequestPath()
    {
        return Path.Combine(GetProjectRoot(), InstallRequestFile);
    }

    public static string GetInstallResultPath()
    {
        return Path.Combine(GetProjectRoot(), InstallResultFile);
    }

    [MenuItem("Tools/Qianxia/Instantiate Third Person Prefab In Current Scene")]
    public static void InstantiateThirdPersonPrefabInCurrentScene()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
            throw new InvalidOperationException($"Prefab not found at path: {PrefabPath}");

        PrefabUtility.InstantiatePrefab(prefab);
    }

    private static AnimatorController CreateOrReplaceAnimatorController()
    {
        if (AssetDatabase.DeleteAsset(AnimatorControllerPath))
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(AnimatorControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
        controller.AddParameter("HasMove", AnimatorControllerParameterType.Bool);

        AnimationClip walkClip = LoadWalkClip();
        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;

        AnimatorState idleState = stateMachine.AddState("Idle", new Vector3(240.0f, 120.0f, 0.0f));
        idleState.motion = walkClip;
        idleState.speed = 0.0f;
        idleState.writeDefaultValues = true;

        AnimatorState walkState = stateMachine.AddState("Walk", new Vector3(520.0f, 120.0f, 0.0f));
        walkState.motion = walkClip;
        walkState.speed = 1.0f;
        walkState.writeDefaultValues = true;

        stateMachine.defaultState = idleState;

        AnimatorStateTransition idleToWalk = idleState.AddTransition(walkState);
        idleToWalk.hasExitTime = false;
        idleToWalk.duration = 0.12f;
        idleToWalk.AddCondition(AnimatorConditionMode.Greater, 0.05f, "Speed");

        AnimatorStateTransition walkToIdle = walkState.AddTransition(idleState);
        walkToIdle.hasExitTime = false;
        walkToIdle.duration = 0.08f;
        walkToIdle.AddCondition(AnimatorConditionMode.Less, 0.05f, "Speed");

        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static GameObject CreateOrReplacePrefab(AnimatorController controller)
    {
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(QianxiaFbxImportConfigurator.ModelAssetPath);
        if (modelAsset == null)
            throw new InvalidOperationException($"Model asset not found at path: {QianxiaFbxImportConfigurator.ModelAssetPath}");

        Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(QianxiaFbxImportConfigurator.ModelAssetPath)
            .OfType<Avatar>()
            .FirstOrDefault(item => item != null && item.isValid && item.isHuman);

        if (avatar == null)
            throw new InvalidOperationException("Unable to load a valid humanoid avatar from the character model.");

        InputActionAsset inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
        if (inputActions == null)
            throw new InvalidOperationException($"Input actions asset not found at path: {InputActionsPath}");

        GameObject root = new GameObject("QianxiaThirdPerson");
        GameObject modelInstance = PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
        if (modelInstance == null)
            throw new InvalidOperationException("Unable to instantiate the imported model prefab.");

        modelInstance.transform.SetParent(root.transform, false);
        modelInstance.transform.localPosition = Vector3.zero;
        modelInstance.transform.localRotation = Quaternion.identity;
        modelInstance.transform.localScale = Vector3.one;

        AlignModelToGround(modelInstance.transform);
        Bounds modelBounds = CalculateCombinedBounds(modelInstance);

        CharacterController characterController = root.AddComponent<CharacterController>();
        ConfigureCharacterController(characterController, modelBounds);

        Animator animator = modelInstance.GetComponent<Animator>();
        if (animator == null)
            animator = modelInstance.AddComponent<Animator>();

        animator.runtimeAnimatorController = controller;
        animator.avatar = avatar;
        animator.applyRootMotion = false;
        animator.updateMode = AnimatorUpdateMode.Normal;

        QianxiaGenshinCharacterController characterMotor = root.AddComponent<QianxiaGenshinCharacterController>();
        QianxiaGenshinCameraController cameraController = root.AddComponent<QianxiaGenshinCameraController>();
        QianxiaRigidBodyProxySetup proxySetup = root.AddComponent<QianxiaRigidBodyProxySetup>();

        ConfigureCharacterMotor(characterMotor, inputActions, modelInstance.transform, animator);
        ConfigureCameraController(cameraController, inputActions, root.transform);
        proxySetup.Apply();

        if (AssetDatabase.DeleteAsset(PrefabPath))
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        UnityEngine.Object.DestroyImmediate(root);

        if (prefab == null)
            throw new InvalidOperationException($"Failed to save prefab at path: {PrefabPath}");

        return prefab;
    }

    private static void ConfigureCharacterMotor(QianxiaGenshinCharacterController motor, InputActionAsset inputActions, Transform visualRoot, Animator animator)
    {
        SerializedObject serializedObject = new SerializedObject(motor);
        serializedObject.FindProperty("_inputActions").objectReferenceValue = inputActions;
        serializedObject.FindProperty("_visualRoot").objectReferenceValue = visualRoot;
        serializedObject.FindProperty("_animator").objectReferenceValue = animator;
        serializedObject.FindProperty("_sprintSpeed").floatValue = 2.6f;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ConfigureCameraController(QianxiaGenshinCameraController controller, InputActionAsset inputActions, Transform followTarget)
    {
        SerializedObject serializedObject = new SerializedObject(controller);
        serializedObject.FindProperty("_inputActions").objectReferenceValue = inputActions;
        serializedObject.FindProperty("_followTarget").objectReferenceValue = followTarget;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static AnimationClip LoadWalkClip()
    {
        AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(QianxiaFbxImportConfigurator.WalkAssetPath)
            .OfType<AnimationClip>()
            .FirstOrDefault(item => !item.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase));

        if (clip == null)
            throw new InvalidOperationException("Unable to locate the imported walk animation clip.");

        return clip;
    }

    private static void ConfigureCharacterController(CharacterController controller, Bounds bounds)
    {
        float height = Mathf.Max(1.4f, bounds.size.y * 0.92f);
        float radius = Mathf.Clamp(Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.35f, 0.18f, height * 0.4f);
        controller.height = height;
        controller.radius = radius;
        controller.center = new Vector3(bounds.center.x, height * 0.5f, bounds.center.z);
        controller.slopeLimit = 50.0f;
        controller.stepOffset = Mathf.Min(0.4f, height * 0.18f);
        controller.skinWidth = Mathf.Max(0.02f, radius * 0.1f);
        controller.minMoveDistance = 0.0f;
    }

    private static void AlignModelToGround(Transform modelRoot)
    {
        Bounds bounds = CalculateCombinedBounds(modelRoot.gameObject);
        modelRoot.localPosition = new Vector3(0.0f, -bounds.min.y, 0.0f);
    }

    private static Bounds CalculateCombinedBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return new Bounds(Vector3.zero, new Vector3(1.0f, 1.8f, 1.0f));

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        return bounds;
    }

    private static void EnsureFolder(string assetPath)
    {
        if (AssetDatabase.IsValidFolder(assetPath))
            return;

        string[] segments = assetPath.Split('/');
        string current = segments[0];
        for (int i = 1; i < segments.Length; i++)
        {
            string next = $"{current}/{segments[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, segments[i]);

            current = next;
        }
    }

    private static void CopyExternalAsset(string externalPath, string assetPath)
    {
        if (!File.Exists(externalPath))
            throw new FileNotFoundException("External source file not found.", externalPath);

        string projectRoot = GetProjectRoot();
        string destinationPath = Path.Combine(projectRoot, assetPath);
        string destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrEmpty(destinationDirectory))
            throw new InvalidOperationException($"Unable to resolve destination directory for asset path: {assetPath}");

        Directory.CreateDirectory(destinationDirectory);
        File.Copy(externalPath, destinationPath, true);
    }

    private static string GetProjectRoot()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }

    private static void ApplyCharacterModelImporterOverrides()
    {
        ModelImporter importer = AssetImporter.GetAtPath(QianxiaFbxImportConfigurator.ModelAssetPath) as ModelImporter;
        if (importer == null)
            throw new InvalidOperationException("Unable to load model importer for the Qianxia character model.");

        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = true;
        importer.SaveAndReimport();
    }

    private static void ApplyWalkAnimationImporterOverrides()
    {
        ModelImporter importer = AssetImporter.GetAtPath(QianxiaFbxImportConfigurator.WalkAssetPath) as ModelImporter;
        if (importer == null)
            throw new InvalidOperationException("Unable to load model importer for the walk animation.");

        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = true;

        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
        if (clips != null && clips.Length > 0)
        {
            for (int i = 0; i < clips.Length; i++)
            {
                ModelImporterClipAnimation clip = clips[i];
                clip.name = "Qianxia_Walk_Slow";
                clip.loopTime = true;
                clip.loopPose = true;
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = false;
                clip.keepOriginalPositionY = false;
                clip.keepOriginalPositionXZ = false;
                clip.heightFromFeet = true;
                clips[i] = clip;
            }

            importer.clipAnimations = clips;
        }

        importer.SaveAndReimport();
    }
}

[InitializeOnLoad]
public static class QianxiaCharacterSetupBootstrap
{
    private static double _nextCheckTime;

    static QianxiaCharacterSetupBootstrap()
    {
        EditorApplication.delayCall += TryRunQueuedInstall;
        EditorApplication.update += PollQueuedInstall;
    }

    private static void PollQueuedInstall()
    {
        if (EditorApplication.timeSinceStartup < _nextCheckTime)
            return;

        _nextCheckTime = EditorApplication.timeSinceStartup + 2.0d;
        TryRunQueuedInstall();
    }

    private static void TryRunQueuedInstall()
    {
        string requestPath = QianxiaCharacterSetupUtility.GetInstallRequestPath();
        if (!File.Exists(requestPath))
            return;

        string resultPath = QianxiaCharacterSetupUtility.GetInstallResultPath();

        try
        {
            File.Delete(requestPath);
            QianxiaCharacterSetupUtility.InstallWalkCharacterFromExternalFbx();
            File.WriteAllText(resultPath, $"SUCCESS{Environment.NewLine}{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{QianxiaFbxImportConfigurator.ModelAssetPath}{Environment.NewLine}{QianxiaFbxImportConfigurator.WalkAssetPath}");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
        catch (Exception exception)
        {
            File.WriteAllText(resultPath, $"FAILED{Environment.NewLine}{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{exception}");
            Debug.LogException(exception);
        }
    }
}
