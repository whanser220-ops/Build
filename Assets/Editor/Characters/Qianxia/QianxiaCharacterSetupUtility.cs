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
    private const string SourceModelsFolder = "Assets/Game/Characters/Qianxia/Art/Meshs";
    private const string SourceAnimationsFolder = "Assets/Game/Characters/Qianxia/Art/SourceAnimations";
    private const string SourceWalkFolder = "Assets/Game/Characters/Qianxia/Art/SourceAnimations/Walk";
    private const string CharacterMaterialsFolder = "Assets/Game/Characters/Qianxia/Art/Materials";
    private const string GeneratedFolder = "Assets/Game/Characters/Qianxia/Runtime/Generated";
    private const string AnimatorControllerPath = GeneratedFolder + "/QianxiaLocomotion.controller";
    private const string PrefabPath = GeneratedFolder + "/QianxiaThirdPerson.prefab";
    private const string InputActionsPath = "Assets/Game/Core/Input/Runtime/InputSystem_Actions.inputactions";
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

    [MenuItem("Tools/Qianxia/Repair Third Person Prefab Materials")]
    public static void RepairThirdPersonPrefabMaterials()
    {
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            ApplyCharacterMaterials(prefabRoot);
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"Qianxia third-person prefab materials repaired: {PrefabPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
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

        ApplyCharacterMaterials(modelInstance);
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

    private static void ApplyCharacterMaterials(GameObject modelInstance)
    {
        Material[] materials = LoadCharacterMaterials();
        Renderer[] renderers = modelInstance.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
            renderer.sharedMaterials = materials;
    }

    private static Material[] LoadCharacterMaterials()
    {
        string[] materialGuids =
        {
            "b46f3eeedb7bf704b83510a043342a8c",
            "4b85c70482764ff49a2da8af82ecee21",
            "2d78d9e294998a846bc9d30b6c9d5896",
            "d9ecb26aa71b30e4b9ba38e5b3d8730b",
            "951b24c35b2ecb541a7c014851346347",
            "610d10a14a02c5948b5c60835eab067f",
            "b9d75e898eaa94b429c55c332e40c36c",
            "fcb948bc39845da4586134435870f8aa",
            "85218bbfe919ef94697fddcdd8aa5a3d",
            "2d5a9e1e500fa6347b218f61e3cf51e1",
            "56c6dad6a9778e14088f3d57aaf6aa5b",
            "7b5007af745a13e4c9b21a6b49f88f87",
            "797f7b2682e319b48bd7b97ef161a69c",
            "3e7e2e2540304fb48810c9716962fb4f",
            "ceff811f6bf8a9a49b5e689119575a62",
            "472fc137ba5200945b2ca6be4e7b6e42",
            "64ac40502423a2540a61e9a5e31a7c1c",
            "7774097e240e59e46967b792d8a433a3",
            "36d4d94b635356c4f8936e2a1bfd2d20",
            "ceb1397485c9c264ea3a5347f8862de2"
        };

        Material[] materials = new Material[materialGuids.Length];
        for (int i = 0; i < materialGuids.Length; i++)
        {
            string materialPath = AssetDatabase.GUIDToAssetPath(materialGuids[i]);
            materials[i] = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (materials[i] == null)
                throw new InvalidOperationException($"Qianxia material not found for GUID: {materialGuids[i]}");
        }

        return materials;
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
