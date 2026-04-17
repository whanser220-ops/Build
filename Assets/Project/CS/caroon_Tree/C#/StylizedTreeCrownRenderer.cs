using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

[StructLayout(LayoutKind.Sequential)]
public struct StylizedCrownClusterGPU
{
    public static readonly int Stride = Marshal.SizeOf<StylizedCrownClusterGPU>();

    // 与 Compute/HLSL 中的 Cluster 结构严格对齐。
    // 这里描述的是“叶团”级别的数据，而不是单张叶片。
    public Vector3 centerWS;
    public float radius;
    public Vector3 axisXWS;
    public float density;
    public Vector3 axisYWS;
    public float layer;
    public Vector3 axisZWS;
    public float depthInCrown;
    public Vector3 extents;
    public float ambientOccl;
    public uint startCardIndex;
    public uint cardCount;
    public float directOcclBias;
    public float directionalOcclusion;
    public float directionalTransmittance;
    public float upFacingBias;
    public float shellBias;
    public float leafSizeScale;
    public float padding;
}

[StructLayout(LayoutKind.Sequential)]
public struct StylizedLeafCardGPU
{
    public static readonly int Stride = Marshal.SizeOf<StylizedLeafCardGPU>();

    // 单张叶片面片实例的数据。顶点着色器会根据它在世界空间展开 quad。
    public Vector3 positionWS;
    public float size;
    public Vector3 localPosN;
    public float rotation;
    public Vector3 outwardNormalWS;
    public float shell;
    public float random01;
    public float hueVariation;
    public float ao;
    public float windPhase;
    public uint clusterId;
    public Vector3 padding;
}

[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("caroon_Tree/风格化树冠/树冠渲染器")]
public sealed class StylizedTreeCrownRenderer : MonoBehaviour
{
    // Compute kernel 使用的线程组大小需要与 .compute 文件中的 numthreads 保持一致。
    private const int ThreadGroupSize = 64;

    private static readonly int ClustersId = Shader.PropertyToID("_Clusters");
    private static readonly int AllLeafCardsId = Shader.PropertyToID("_AllLeafCards");
    private static readonly int VisibleLeafCardsId = Shader.PropertyToID("_VisibleLeafCards");
    private static readonly int VisibleCardCounterId = Shader.PropertyToID("_VisibleCardCounter");
    private static readonly int ClusterCountId = Shader.PropertyToID("_ClusterCount");
    private static readonly int TotalCardCountId = Shader.PropertyToID("_TotalCardCount");
    private static readonly int LeafSizeRangeId = Shader.PropertyToID("_LeafSizeRange");
    private static readonly int GlobalRandomSeedId = Shader.PropertyToID("_GlobalRandomSeed");
    private static readonly int GlobalShellBiasId = Shader.PropertyToID("_GlobalShellBias");
    private static readonly int CenterVoidId = Shader.PropertyToID("_CenterVoid");
    private static readonly int GlobalUpBiasId = Shader.PropertyToID("_GlobalUpBias");
    private static readonly int LightDirectionWsId = Shader.PropertyToID("_LightDirectionWS");
    private static readonly int ClusterOcclusionSigmaId = Shader.PropertyToID("_ClusterOcclusionSigma");
    private static readonly int ClusterOcclusionDistanceFadeId = Shader.PropertyToID("_ClusterOcclusionDistanceFade");
    private static readonly int CameraPositionWsId = Shader.PropertyToID("_CameraPositionWS");
    private static readonly int LodNearDistanceId = Shader.PropertyToID("_LodNearDistance");
    private static readonly int LodFarDistanceId = Shader.PropertyToID("_LodFarDistance");
    private static readonly int LodFarKeepRatioId = Shader.PropertyToID("_LodFarKeepRatio");
    private static readonly int LodFarScaleId = Shader.PropertyToID("_LodFarScale");
    private static readonly int FrustumPlanesId = Shader.PropertyToID("_FrustumPlanes");
    private static readonly int FrustumCullingEnabledId = Shader.PropertyToID("_FrustumCullingEnabled");
    private static readonly int FrustumCullPaddingId = Shader.PropertyToID("_FrustumCullPadding");
    private static readonly int IndirectArgsId = Shader.PropertyToID("_IndirectArgs");

    private static readonly int CrownCenterWsId = Shader.PropertyToID("_CrownCenterWS");
    private static readonly int CrownExtentsWsId = Shader.PropertyToID("_CrownExtentsWS");
    private static readonly int CrownMinYId = Shader.PropertyToID("_CrownMinY");
    private static readonly int CrownMaxYId = Shader.PropertyToID("_CrownMaxY");
    private static readonly int BillboardBlendId = Shader.PropertyToID("_BillboardBlend");
    private static readonly int NormalBendId = Shader.PropertyToID("_NormalBend");
    private static readonly int CutoffId = Shader.PropertyToID("_Cutoff");
    private static readonly int ThicknessSigmaId = Shader.PropertyToID("_ThicknessSigma");
    private static readonly int InnerDirectScaleId = Shader.PropertyToID("_InnerDirectScale");
    private static readonly int InnerAmbientScaleId = Shader.PropertyToID("_InnerAmbientScale");
    private static readonly int TopLightBoostId = Shader.PropertyToID("_TopLightBoost");
    private static readonly int TopLightExponentId = Shader.PropertyToID("_TopLightExponent");
    private static readonly int BottomDarknessId = Shader.PropertyToID("_BottomDarkness");
    private static readonly int CrownCoreDarknessId = Shader.PropertyToID("_CrownCoreDarkness");
    private static readonly int LayerDarknessId = Shader.PropertyToID("_LayerDarkness");
    private static readonly int DiffuseWrapId = Shader.PropertyToID("_DiffuseWrap");
    private static readonly int SkyAmbientColorId = Shader.PropertyToID("_SkyAmbientColor");
    private static readonly int GroundAmbientColorId = Shader.PropertyToID("_GroundAmbientColor");
    private static readonly int TopTintId = Shader.PropertyToID("_TopTint");
    private static readonly int MidTintId = Shader.PropertyToID("_MidTint");
    private static readonly int BottomTintId = Shader.PropertyToID("_BottomTint");
    private static readonly int HuePositiveTintId = Shader.PropertyToID("_HuePositiveTint");
    private static readonly int HueNegativeTintId = Shader.PropertyToID("_HueNegativeTint");
    private static readonly int HueVariationStrengthId = Shader.PropertyToID("_HueVariationStrength");
    private static readonly int RimColorId = Shader.PropertyToID("_RimColor");
    private static readonly int RimStrengthId = Shader.PropertyToID("_RimStrength");
    private static readonly int RimPowerId = Shader.PropertyToID("_RimPower");
    private static readonly int WindAmplitudeId = Shader.PropertyToID("_WindAmplitude");
    private static readonly int WindFlutterId = Shader.PropertyToID("_WindFlutter");
    private static readonly int WindSpeedId = Shader.PropertyToID("_WindSpeed");
    private static readonly int WindSurfaceLockId = Shader.PropertyToID("_WindSurfaceLock");
    private static readonly int FrontShellDepthBiasId = Shader.PropertyToID("_FrontShellDepthBias");
    private static readonly int WindSpatialFrequencyId = Shader.PropertyToID("_WindSpatialFrequency");
    private static readonly int WindDirectionWsShaderId = Shader.PropertyToID("_WindDirectionWS");
    private static readonly int TimeValueId = Shader.PropertyToID("_TimeValue");

    [Header("资源")]
    [Tooltip("负责生成叶片、计算团间遮挡和可见性剔除的 Compute Shader。")]
    [SerializeField] private ComputeShader _crownCompute;
    [Tooltip("用于渲染叶片卡片的材质，通常使用 StylizedTreeCrownIndirect.shader。")]
    [SerializeField] private Material _leafMaterial;
    [Tooltip("主方向光。若为空，会尝试自动查找场景中的太阳光或平行光。")]
    [SerializeField] private Light _mainDirectionalLight;
    [Tooltip("用于 Compute 剔除的相机。为空时会自动选择主相机或 SceneView 相机。")]
    [SerializeField] private Camera _cullingCamera;

    [Header("生成")]
    [Tooltip("叶片卡片的最小/最大尺寸范围。实际尺寸还会乘以各叶团的 Leaf Size Scale。")]
    [SerializeField] private Vector2 _leafSizeRange = new Vector2(0.18f, 0.34f);
    [Tooltip("整棵树冠允许生成的最大叶片数量上限。")]
    [SerializeField] [Min(1)] private int _maxGeneratedCards = 20000;
    [Tooltip("全局随机种子。修改它可以快速打散整棵树冠的叶片分布。")]
    [SerializeField] private int _globalRandomSeed = 1337;
    [Tooltip("全局外壳偏置。值越高，叶片越倾向分布在叶团外层。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _globalShellBias = 0.8f;
    [Tooltip("叶团中心空腔比例。值越高，中心越空、轮廓越蓬松。")]
    [SerializeField] [Range(0.0f, 0.95f)] private float _centerVoid = 0.48f;
    [Tooltip("全局朝上偏置。值越高，叶片更偏向抬头。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _globalUpBias = 0.22f;
    [Tooltip("绘制包围盒额外留白，避免风摆动或大叶片在边缘被整棵树提前裁掉。")]
    [SerializeField] [Min(0.0f)] private float _boundsPadding = 0.75f;

    [Header("团间遮挡")]
    [Tooltip("团间方向遮挡的整体强度。值越高，上层挡下层越明显。")]
    [SerializeField] [Min(0.0f)] private float _clusterOcclusionSigma = 1.25f;
    [Tooltip("团间挡光随距离衰减的速度。值越高，只有更近的叶团才会明显互相遮挡。")]
    [SerializeField] [Min(0.01f)] private float _clusterOcclusionDistanceFade = 0.65f;

    [Header("细节层级 LOD")]
    [Tooltip("近距离阈值。相机在此距离内基本保留全部叶片。")]
    [SerializeField] [Min(0.0f)] private float _lodNearDistance = 18.0f;
    [Tooltip("远距离阈值。超过后会按 LOD Far Keep Ratio 大幅减少叶片数量。")]
    [SerializeField] [Min(0.1f)] private float _lodFarDistance = 60.0f;
    [Tooltip("远距离最终保留比例。值越低，远景叶片越少。")]
    [SerializeField] [Range(0.01f, 1.0f)] private float _lodFarKeepRatio = 0.22f;
    [Tooltip("远距离幸存叶片的尺寸补偿倍率。")]
    [SerializeField] [Range(1.0f, 3.0f)] private float _lodFarScale = 1.35f;

    [Header("剔除")]
    [Tooltip("是否启用视锥剔除。关闭后只做 LOD，不做相机视锥筛选。")]
    [SerializeField] private bool _enableFrustumCulling = true;
    [Tooltip("编辑器模式下是否关闭视锥剔除。建议开启，避免 SceneView 和 GameView 相机切换导致整棵树消失。")]
    [SerializeField] private bool _disableFrustumCullingInEditMode = true;
    [Tooltip("给每张叶片包围球增加的额外半径，用于保守剔除。")]
    [SerializeField] [Min(0.0f)] private float _frustumCullPadding = 0.35f;

    [Header("着色")]
    [Tooltip("半 billboard 混合比例。0 更贴近叶团朝向，1 更贴近相机朝向。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _billboardBlend = 0.35f;
    [Tooltip("将卡片法线向叶团外壳法线弯折的比例。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _normalBend = 0.55f;
    [Tooltip("叶片贴图的 Alpha Clip 阈值。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _alphaCutoff = 0.45f;
    [Tooltip("团内厚度遮挡强度。值越高，内部越暗。")]
    [SerializeField] [Min(0.0f)] private float _thicknessSigma = 1.1f;
    [Tooltip("内部区域直射光下限。值越低，内部越难被点亮。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _innerDirectScale = 0.42f;
    [Tooltip("内部区域环境光下限。值越低，树冠核心越厚重。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _innerAmbientScale = 0.55f;
    [Tooltip("顶部额外提亮倍率。")]
    [SerializeField] [Min(1.0f)] private float _topLightBoost = 1.15f;
    [Tooltip("顶部提亮曲线指数。值越高，亮部越集中在顶部。")]
    [SerializeField] [Min(0.01f)] private float _topLightExponent = 1.35f;
    [Tooltip("整体底部压暗强度。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _bottomDarkness = 0.3f;
    [Tooltip("树冠核心压暗强度。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _crownCoreDarkness = 0.48f;
    [Tooltip("深层叶团的额外压暗强度。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _layerDarkness = 0.2f;
    [Tooltip("包裹漫反射程度，用于做更柔和的动画风亮面。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _diffuseWrap = 0.28f;
    [Tooltip("来自上方天空的环境光颜色。")]
    [SerializeField] private Color _skyAmbientColor = new Color(0.62f, 0.74f, 0.56f, 1.0f);
    [Tooltip("来自下方地面的环境光颜色。")]
    [SerializeField] private Color _groundAmbientColor = new Color(0.12f, 0.18f, 0.23f, 1.0f);
    [Tooltip("顶部颜色渐变。")]
    [SerializeField] private Color _topTint = new Color(0.84f, 1.0f, 0.72f, 1.0f);
    [Tooltip("中层主色。")]
    [SerializeField] private Color _midTint = new Color(0.43f, 0.78f, 0.32f, 1.0f);
    [Tooltip("底部颜色渐变。")]
    [SerializeField] private Color _bottomTint = new Color(0.17f, 0.38f, 0.18f, 1.0f);
    [Tooltip("正向色相漂移颜色。")]
    [SerializeField] private Color _huePositiveTint = new Color(0.96f, 1.02f, 0.88f, 1.0f);
    [Tooltip("负向色相漂移颜色。")]
    [SerializeField] private Color _hueNegativeTint = new Color(0.88f, 0.93f, 1.02f, 1.0f);
    [Tooltip("色相漂移强度。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _hueVariationStrength = 0.16f;
    [Tooltip("边缘光颜色。")]
    [SerializeField] private Color _rimColor = new Color(1.0f, 0.95f, 0.78f, 1.0f);
    [Tooltip("边缘光强度。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _rimStrength = 0.12f;
    [Tooltip("边缘光锐利程度。")]
    [SerializeField] [Min(0.01f)] private float _rimPower = 5.5f;

    [Header("风")]
    [Tooltip("风的世界方向。")]
    [SerializeField] private Vector3 _windDirection = new Vector3(1.0f, 0.0f, 0.25f);
    [Tooltip("整体摆动幅度。")]
    [SerializeField] [Min(0.0f)] private float _windAmplitude = 0.08f;
    [Tooltip("叶片局部抖动幅度。")]
    [SerializeField] [Min(0.0f)] private float _windFlutter = 0.04f;
    [Tooltip("风动画速度。")]
    [SerializeField] [Min(0.0f)] private float _windSpeed = 1.25f;
    [Tooltip("将风位移限制在叶团外壳切平面上的程度。1 表示完全贴壳层摆动，可显著减少亮卡和暗卡互相穿插。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _windSurfaceLock = 1.0f;
    [Tooltip("朝向相机的外壳层前移偏置，用于减少亮卡和暗卡在前景互相抢占。值应保持很小。")]
    [SerializeField] [Range(0.0f, 0.03f)] private float _frontShellDepthBias = 0.008f;
    [Tooltip("风噪声在世界空间中的频率。")]
    [SerializeField] private Vector3 _windSpatialFrequency = new Vector3(0.17f, 0.11f, 0.14f);

    [Header("调试")]
    [Tooltip("调试开关。开启后跳过 GPU 剔除，强制绘制全部叶片。")]
    [SerializeField] private bool _forceDrawAllCards = false;
    [Tooltip("当前参与生成的叶团数量。")]
    [SerializeField] private int _debugClusterCount;
    [Tooltip("当前生成的总叶片数量。")]
    [SerializeField] private int _debugGeneratedCardCount;
    [Tooltip("当前经过 LOD 和剔除后实际参与绘制的叶片数量。")]
    [SerializeField] private int _debugVisibleCardCount;

    private readonly uint[] _visibleCountReadback = new uint[1];

    // GPU 缓冲：
    // _clusterBuffer          -> 叶团控制数据
    // _allLeafCardsBuffer     -> 所有生成出的叶片实例
    // _visibleLeafCardsBuffer -> 经过剔除/LOD 后真正参与绘制的实例
    // _visibleCountBuffer     -> 可见实例计数器
    // _argsBuffer             -> DrawMeshInstancedIndirect 的参数
    private ComputeBuffer _clusterBuffer;
    private ComputeBuffer _allLeafCardsBuffer;
    private ComputeBuffer _visibleLeafCardsBuffer;
    private ComputeBuffer _visibleCountBuffer;
    private ComputeBuffer _argsBuffer;
    private MaterialPropertyBlock _propertyBlock;
    private Mesh _quadMesh;
    private readonly Vector4[] _frustumPlaneData = new Vector4[6];
    private readonly uint[] _args = new uint[5];

    private int _generateKernel = -1;
    private int _clusterOcclusionKernel = -1;
    private int _resetCounterKernel = -1;
    private int _cullKernel = -1;
    private int _buildArgsKernel = -1;
    private int _clusterCount;
    private int _totalCardCount;
    private Bounds _drawBounds = new Bounds(Vector3.zero, Vector3.one);
    private Vector3 _crownCenterWS;
    private Vector3 _crownExtentsWS = Vector3.one;
    private float _crownMinY;
    private float _crownMaxY;
    private bool _needsRebuild = true;

    private void OnEnable()
    {
        _needsRebuild = true;
        EnsureResources();
    }

    private void OnDisable()
    {
        ReleaseResources();
    }

    private void OnDestroy()
    {
        ReleaseResources();
    }

    private void OnValidate()
    {
        _leafSizeRange.x = Mathf.Max(0.01f, _leafSizeRange.x);
        _leafSizeRange.y = Mathf.Max(_leafSizeRange.x, _leafSizeRange.y);
        _maxGeneratedCards = Mathf.Max(1, _maxGeneratedCards);
        _centerVoid = Mathf.Clamp(_centerVoid, 0.0f, 0.95f);
        _globalShellBias = Mathf.Clamp01(_globalShellBias);
        _globalUpBias = Mathf.Clamp01(_globalUpBias);
        _boundsPadding = Mathf.Max(0.0f, _boundsPadding);
        _clusterOcclusionSigma = Mathf.Max(0.0f, _clusterOcclusionSigma);
        _clusterOcclusionDistanceFade = Mathf.Max(0.01f, _clusterOcclusionDistanceFade);
        _lodNearDistance = Mathf.Max(0.0f, _lodNearDistance);
        _lodFarDistance = Mathf.Max(_lodNearDistance + 0.1f, _lodFarDistance);
        _lodFarKeepRatio = Mathf.Clamp(_lodFarKeepRatio, 0.01f, 1.0f);
        _lodFarScale = Mathf.Clamp(_lodFarScale, 1.0f, 3.0f);
        _frustumCullPadding = Mathf.Max(0.0f, _frustumCullPadding);
        _billboardBlend = Mathf.Clamp01(_billboardBlend);
        _normalBend = Mathf.Clamp01(_normalBend);
        _alphaCutoff = Mathf.Clamp01(_alphaCutoff);
        _thicknessSigma = Mathf.Max(0.0f, _thicknessSigma);
        _innerDirectScale = Mathf.Clamp01(_innerDirectScale);
        _innerAmbientScale = Mathf.Clamp01(_innerAmbientScale);
        _topLightBoost = Mathf.Max(1.0f, _topLightBoost);
        _topLightExponent = Mathf.Max(0.01f, _topLightExponent);
        _bottomDarkness = Mathf.Clamp01(_bottomDarkness);
        _crownCoreDarkness = Mathf.Clamp01(_crownCoreDarkness);
        _layerDarkness = Mathf.Clamp01(_layerDarkness);
        _diffuseWrap = Mathf.Clamp01(_diffuseWrap);
        _hueVariationStrength = Mathf.Clamp01(_hueVariationStrength);
        _rimStrength = Mathf.Clamp01(_rimStrength);
        _rimPower = Mathf.Max(0.01f, _rimPower);
        _windAmplitude = Mathf.Max(0.0f, _windAmplitude);
        _windFlutter = Mathf.Max(0.0f, _windFlutter);
        _windSpeed = Mathf.Max(0.0f, _windSpeed);
        _windSurfaceLock = Mathf.Clamp01(_windSurfaceLock);
        _frontShellDepthBias = Mathf.Clamp(_frontShellDepthBias, 0.0f, 0.03f);
        _needsRebuild = true;
    }

    private void Update()
    {
        if (!EnsureResources())
            return;

        // 编辑器下为了所见即所得会频繁重建；运行时则只在 authoring 发生变化时重建。
        if (!Application.isPlaying || HaveAuthoringChanged())
            _needsRebuild = true;

        if (_needsRebuild)
            RebuildNow();

        _debugClusterCount = _clusterCount;
        _debugGeneratedCardCount = _totalCardCount;

        if (_clusterCount <= 0 || _totalCardCount <= 0)
        {
            _debugVisibleCardCount = 0;
            return;
        }

        Camera camera = ResolveCamera();
        Vector3 lightDirectionWS = ResolveMainLightDirection();
        DispatchClusterOcclusion(lightDirectionWS);

        // forceDrawAllCards 是排查“为什么没出叶子”时的保底路径。
        // 正常情况下应关闭，让 Compute 的剔除/LOD 真正生效。
        if (_forceDrawAllCards || camera == null)
        {
            SetupIndirectArgs();
            _args[1] = (uint)_totalCardCount;
            _argsBuffer.SetData(_args);
            _debugVisibleCardCount = _totalCardCount;
        }
        else
        {
            DispatchCullAndLod(camera);
            UpdateDebugCounters();
        }

        UpdateMaterialProperties(lightDirectionWS);

        Graphics.DrawMeshInstancedIndirect(
            _quadMesh,
            0,
            _leafMaterial,
            _drawBounds,
            _argsBuffer,
            0,
            _propertyBlock,
            ShadowCastingMode.Off,
            false,
            gameObject.layer,
            null,
            LightProbeUsage.Off,
            null);
    }

    [ContextMenu("立即重建树冠")]
    public void RebuildNow()
    {
        if (!EnsureResources())
            return;

        // CPU 只负责收集控制点和打包叶团数据，真正的叶片散布由 GenerateLeafCards 在 GPU 完成。
        StylizedTreeCrownControlPoint[] controlPoints = GetComponentsInChildren<StylizedTreeCrownControlPoint>(true);
        int activeClusterCount = 0;
        for (int i = 0; i < controlPoints.Length; i++)
        {
            StylizedTreeCrownControlPoint controlPoint = controlPoints[i];
            if (controlPoint != null && controlPoint.isActiveAndEnabled)
                activeClusterCount++;
        }

        if (activeClusterCount == 0)
        {
            _clusterCount = 0;
            _totalCardCount = 0;
            ZeroIndirectInstanceCount();
            ResetTransformChangeFlags(controlPoints);
            _needsRebuild = false;
            return;
        }

        StylizedCrownClusterGPU[] clusters = new StylizedCrownClusterGPU[activeClusterCount];
        int clusterWriteIndex = 0;
        int startCardIndex = 0;
        float maxLeafSize = 0.0f;
        Bounds bounds = default;
        bool hasBounds = false;

        for (int i = 0; i < controlPoints.Length; i++)
        {
            StylizedTreeCrownControlPoint controlPoint = controlPoints[i];
            if (controlPoint == null || !controlPoint.isActiveAndEnabled)
                continue;

            Vector3 radii = MaxVector(controlPoint.EllipsoidRadii, Vector3.one * 0.05f);
            int resolvedCardCount = ResolveCardCount(controlPoint, radii);
            if (resolvedCardCount <= 0)
                continue;

            if (startCardIndex + resolvedCardCount > _maxGeneratedCards)
                resolvedCardCount = Mathf.Max(0, _maxGeneratedCards - startCardIndex);

            if (resolvedCardCount <= 0)
                continue;

            Vector3 axisX = (controlPoint.transform.rotation * Vector3.right).normalized;
            Vector3 axisY = (controlPoint.transform.rotation * Vector3.up).normalized;
            Vector3 axisZ = (controlPoint.transform.rotation * Vector3.forward).normalized;
            float radius = Mathf.Max(radii.x, Mathf.Max(radii.y, radii.z));

            clusters[clusterWriteIndex++] = new StylizedCrownClusterGPU
            {
                centerWS = controlPoint.transform.position,
                radius = radius,
                axisXWS = axisX,
                density = controlPoint.Density,
                axisYWS = axisY,
                layer = controlPoint.Layer,
                axisZWS = axisZ,
                depthInCrown = controlPoint.DepthInCrown,
                extents = radii,
                ambientOccl = controlPoint.AmbientOccl,
                startCardIndex = (uint)startCardIndex,
                cardCount = (uint)resolvedCardCount,
                directOcclBias = controlPoint.DirectOcclBias,
                directionalOcclusion = 0.0f,
                directionalTransmittance = 1.0f,
                upFacingBias = controlPoint.UpFacingBias,
                shellBias = controlPoint.ShellBias,
                leafSizeScale = controlPoint.LeafSizeScale,
                padding = 0.0f
            };

            startCardIndex += resolvedCardCount;
            maxLeafSize = Mathf.Max(maxLeafSize, _leafSizeRange.y * controlPoint.LeafSizeScale);

            float boundRadius = radius + maxLeafSize * _lodFarScale + _windAmplitude + _boundsPadding;
            Bounds clusterBounds = new Bounds(controlPoint.transform.position, Vector3.one * boundRadius * 2.0f);
            if (!hasBounds)
            {
                bounds = clusterBounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(clusterBounds);
            }
        }

        if (clusterWriteIndex == 0 || startCardIndex == 0)
        {
            _clusterCount = 0;
            _totalCardCount = 0;
            ZeroIndirectInstanceCount();
            ResetTransformChangeFlags(controlPoints);
            _needsRebuild = false;
            return;
        }

        if (clusterWriteIndex != clusters.Length)
            System.Array.Resize(ref clusters, clusterWriteIndex);

        _clusterCount = clusters.Length;
        _totalCardCount = startCardIndex;
        EnsureBuffers(_clusterCount, _totalCardCount);
        _clusterBuffer.SetData(clusters);

        // 这里只生成所有卡片实例，不做可见性筛选。
        _crownCompute.SetInt(ClusterCountId, _clusterCount);
        _crownCompute.SetInt(TotalCardCountId, _totalCardCount);
        _crownCompute.SetVector(LeafSizeRangeId, _leafSizeRange);
        _crownCompute.SetInt(GlobalRandomSeedId, _globalRandomSeed);
        _crownCompute.SetFloat(GlobalShellBiasId, _globalShellBias);
        _crownCompute.SetFloat(CenterVoidId, _centerVoid);
        _crownCompute.SetFloat(GlobalUpBiasId, _globalUpBias);
        _crownCompute.SetBuffer(_generateKernel, ClustersId, _clusterBuffer);
        _crownCompute.SetBuffer(_generateKernel, AllLeafCardsId, _allLeafCardsBuffer);
        Dispatch1D(_generateKernel, _totalCardCount);

        SetupIndirectArgs();

        _drawBounds = hasBounds ? bounds : new Bounds(transform.position, Vector3.one * 10.0f);
        _crownCenterWS = _drawBounds.center;
        _crownExtentsWS = MaxVector(_drawBounds.extents, Vector3.one * 0.1f);
        _crownMinY = _drawBounds.min.y;
        _crownMaxY = _drawBounds.max.y;

        ResetTransformChangeFlags(controlPoints);
        _needsRebuild = false;
    }

    private bool EnsureResources()
    {
        if (_crownCompute == null || _leafMaterial == null)
            return false;

        if (_propertyBlock == null)
            _propertyBlock = new MaterialPropertyBlock();

        EnsureQuadMesh();
        // Indirect instancing 依赖材质显式打开 instancing。
        _leafMaterial.enableInstancing = true;

        if (_generateKernel < 0)
            _generateKernel = _crownCompute.FindKernel("GenerateLeafCards");
        if (_clusterOcclusionKernel < 0)
            _clusterOcclusionKernel = _crownCompute.FindKernel("EvaluateClusterOcclusion");
        if (_resetCounterKernel < 0)
            _resetCounterKernel = _crownCompute.FindKernel("ResetVisibleCounter");
        if (_cullKernel < 0)
            _cullKernel = _crownCompute.FindKernel("CullAndLod");
        if (_buildArgsKernel < 0)
            _buildArgsKernel = _crownCompute.FindKernel("BuildIndirectArgs");

        return true;
    }

    private void EnsureBuffers(int clusterCount, int cardCount)
    {
        // Buffer 只在容量变化时重建，避免每帧重新分配。
        if (_clusterBuffer == null || _clusterBuffer.count != clusterCount)
        {
            ReleaseBuffer(ref _clusterBuffer);
            _clusterBuffer = new ComputeBuffer(clusterCount, StylizedCrownClusterGPU.Stride, ComputeBufferType.Structured);
        }

        if (_allLeafCardsBuffer == null || _allLeafCardsBuffer.count != cardCount)
        {
            ReleaseBuffer(ref _allLeafCardsBuffer);
            _allLeafCardsBuffer = new ComputeBuffer(cardCount, StylizedLeafCardGPU.Stride, ComputeBufferType.Structured);
        }

        if (_visibleLeafCardsBuffer == null || _visibleLeafCardsBuffer.count != cardCount)
        {
            ReleaseBuffer(ref _visibleLeafCardsBuffer);
            _visibleLeafCardsBuffer = new ComputeBuffer(cardCount, StylizedLeafCardGPU.Stride, ComputeBufferType.Structured);
        }

        if (_visibleCountBuffer == null)
        {
            ReleaseBuffer(ref _visibleCountBuffer);
            _visibleCountBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Structured);
        }

        if (_argsBuffer == null)
        {
            ReleaseBuffer(ref _argsBuffer);
            _argsBuffer = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments | ComputeBufferType.Raw);
        }
    }

    private void SetupIndirectArgs()
    {
        // Indirect args 格式：
        // [indexCountPerInstance, instanceCount, startIndex, baseVertex, startInstance]
        _args[0] = _quadMesh != null ? _quadMesh.GetIndexCount(0) : 6u;
        _args[1] = 0u;
        _args[2] = _quadMesh != null ? _quadMesh.GetIndexStart(0) : 0u;
        _args[3] = _quadMesh != null ? (uint)_quadMesh.GetBaseVertex(0) : 0u;
        _args[4] = 0u;
        _argsBuffer.SetData(_args);
    }

    private void DispatchClusterOcclusion(Vector3 lightDirectionWS)
    {
        // 团间遮挡是 cluster 级别的 cheap occlusion，用来近似“上层挡下层”。
        _crownCompute.SetInt(ClusterCountId, _clusterCount);
        _crownCompute.SetVector(LightDirectionWsId, lightDirectionWS);
        _crownCompute.SetFloat(ClusterOcclusionSigmaId, _clusterOcclusionSigma);
        _crownCompute.SetFloat(ClusterOcclusionDistanceFadeId, _clusterOcclusionDistanceFade);
        _crownCompute.SetBuffer(_clusterOcclusionKernel, ClustersId, _clusterBuffer);
        Dispatch1D(_clusterOcclusionKernel, _clusterCount);
    }

    private void DispatchCullAndLod(Camera camera)
    {
        bool useFrustumCulling = ShouldUseFrustumCulling(camera);
        if (useFrustumCulling)
        {
            // 把 CPU 端视锥平面传给 Compute，逐卡片进行轻量球体剔除。
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
            for (int i = 0; i < 6; i++)
            {
                Plane plane = planes[i];
                _frustumPlaneData[i] = new Vector4(plane.normal.x, plane.normal.y, plane.normal.z, plane.distance);
            }
        }
        else
        {
            for (int i = 0; i < 6; i++)
                _frustumPlaneData[i] = Vector4.zero;
        }

        // 先重置可见数量，再写入新一帧的可见卡片。
        _crownCompute.SetBuffer(_resetCounterKernel, VisibleCardCounterId, _visibleCountBuffer);
        _crownCompute.Dispatch(_resetCounterKernel, 1, 1, 1);

        Vector3 cameraPositionWS = camera != null ? camera.transform.position : transform.position;
        _crownCompute.SetInt(TotalCardCountId, _totalCardCount);
        _crownCompute.SetVector(CameraPositionWsId, cameraPositionWS);
        _crownCompute.SetFloat(LodNearDistanceId, _lodNearDistance);
        _crownCompute.SetFloat(LodFarDistanceId, _lodFarDistance);
        _crownCompute.SetFloat(LodFarKeepRatioId, _lodFarKeepRatio);
        _crownCompute.SetFloat(LodFarScaleId, _lodFarScale);
        _crownCompute.SetInt(FrustumCullingEnabledId, useFrustumCulling ? 1 : 0);
        _crownCompute.SetFloat(FrustumCullPaddingId, _frustumCullPadding);
        _crownCompute.SetVectorArray(FrustumPlanesId, _frustumPlaneData);
        _crownCompute.SetBuffer(_cullKernel, AllLeafCardsId, _allLeafCardsBuffer);
        _crownCompute.SetBuffer(_cullKernel, VisibleLeafCardsId, _visibleLeafCardsBuffer);
        _crownCompute.SetBuffer(_cullKernel, VisibleCardCounterId, _visibleCountBuffer);
        Dispatch1D(_cullKernel, _totalCardCount);

        _crownCompute.SetBuffer(_buildArgsKernel, VisibleCardCounterId, _visibleCountBuffer);
        _crownCompute.SetBuffer(_buildArgsKernel, IndirectArgsId, _argsBuffer);
        _crownCompute.Dispatch(_buildArgsKernel, 1, 1, 1);
    }

    private bool ShouldUseFrustumCulling(Camera camera)
    {
        // 编辑器下 SceneView / GameView 相机切换频繁，默认禁用 frustum cull 更稳妥；
        // Play 模式再启用真正的视锥剔除。
        if (!_enableFrustumCulling || camera == null)
            return false;

#if UNITY_EDITOR
        if (!Application.isPlaying && _disableFrustumCullingInEditMode)
            return false;
#endif

        return true;
    }

    private void UpdateMaterialProperties(Vector3 lightDirectionWS)
    {
        Vector3 windDirectionWS = ResolveWindDirection(_windDirection);

        // 材质只读取 property block，不依赖 Material 自身被每帧实例化修改。
        _propertyBlock.Clear();
        _propertyBlock.SetBuffer(VisibleLeafCardsId, _forceDrawAllCards ? _allLeafCardsBuffer : _visibleLeafCardsBuffer);
        _propertyBlock.SetBuffer(ClustersId, _clusterBuffer);
        _propertyBlock.SetVector(CrownCenterWsId, _crownCenterWS);
        _propertyBlock.SetVector(CrownExtentsWsId, _crownExtentsWS);
        _propertyBlock.SetFloat(CrownMinYId, _crownMinY);
        _propertyBlock.SetFloat(CrownMaxYId, _crownMaxY);
        _propertyBlock.SetFloat(BillboardBlendId, _billboardBlend);
        _propertyBlock.SetFloat(NormalBendId, _normalBend);
        _propertyBlock.SetFloat(CutoffId, _alphaCutoff);
        _propertyBlock.SetFloat(ThicknessSigmaId, _thicknessSigma);
        _propertyBlock.SetFloat(InnerDirectScaleId, _innerDirectScale);
        _propertyBlock.SetFloat(InnerAmbientScaleId, _innerAmbientScale);
        _propertyBlock.SetFloat(TopLightBoostId, _topLightBoost);
        _propertyBlock.SetFloat(TopLightExponentId, _topLightExponent);
        _propertyBlock.SetFloat(BottomDarknessId, _bottomDarkness);
        _propertyBlock.SetFloat(CrownCoreDarknessId, _crownCoreDarkness);
        _propertyBlock.SetFloat(LayerDarknessId, _layerDarkness);
        _propertyBlock.SetFloat(DiffuseWrapId, _diffuseWrap);
        _propertyBlock.SetColor(SkyAmbientColorId, _skyAmbientColor);
        _propertyBlock.SetColor(GroundAmbientColorId, _groundAmbientColor);
        _propertyBlock.SetColor(TopTintId, _topTint);
        _propertyBlock.SetColor(MidTintId, _midTint);
        _propertyBlock.SetColor(BottomTintId, _bottomTint);
        _propertyBlock.SetColor(HuePositiveTintId, _huePositiveTint);
        _propertyBlock.SetColor(HueNegativeTintId, _hueNegativeTint);
        _propertyBlock.SetFloat(HueVariationStrengthId, _hueVariationStrength);
        _propertyBlock.SetColor(RimColorId, _rimColor);
        _propertyBlock.SetFloat(RimStrengthId, _rimStrength);
        _propertyBlock.SetFloat(RimPowerId, _rimPower);
        _propertyBlock.SetFloat(WindAmplitudeId, _windAmplitude);
        _propertyBlock.SetFloat(WindFlutterId, _windFlutter);
        _propertyBlock.SetFloat(WindSpeedId, _windSpeed);
        _propertyBlock.SetFloat(WindSurfaceLockId, _windSurfaceLock);
        _propertyBlock.SetFloat(FrontShellDepthBiasId, _frontShellDepthBias);
        _propertyBlock.SetVector(WindSpatialFrequencyId, _windSpatialFrequency);
        _propertyBlock.SetVector(WindDirectionWsShaderId, windDirectionWS);
        _propertyBlock.SetVector(LightDirectionWsId, lightDirectionWS);
        _propertyBlock.SetFloat(TimeValueId, GetTimeValue());
    }

    private void UpdateDebugCounters()
    {
        if (_visibleCountBuffer == null)
        {
            _debugVisibleCardCount = 0;
            return;
        }

        // 这是调试用的 GPU -> CPU 回读，正式优化时可考虑只在需要时读取。
        _visibleCountBuffer.GetData(_visibleCountReadback);
        _debugVisibleCardCount = (int)_visibleCountReadback[0];
    }

    private void ZeroIndirectInstanceCount()
    {
        if (_argsBuffer == null)
            return;

        SetupIndirectArgs();
    }

    private bool HaveAuthoringChanged()
    {
        // 这里只检测 Transform / 控制点数量变化。
        // 如果后续要更精确，可继续把序列化参数变化也纳入 dirty 判定。
        if (transform.hasChanged)
            return true;

        // CPU 只负责收集控制点和打包叶团数据，真正的叶片散布由 GenerateLeafCards 在 GPU 完成。
        StylizedTreeCrownControlPoint[] controlPoints = GetComponentsInChildren<StylizedTreeCrownControlPoint>(true);
        int activeCount = 0;
        for (int i = 0; i < controlPoints.Length; i++)
        {
            StylizedTreeCrownControlPoint controlPoint = controlPoints[i];
            if (controlPoint == null)
                return true;

            if (!controlPoint.isActiveAndEnabled)
                continue;

            activeCount++;
            if (controlPoint.transform.hasChanged)
                return true;
        }

        return activeCount != _clusterCount;
    }

    private void ResetTransformChangeFlags(StylizedTreeCrownControlPoint[] controlPoints)
    {
        transform.hasChanged = false;
        for (int i = 0; i < controlPoints.Length; i++)
        {
            if (controlPoints[i] != null)
                controlPoints[i].transform.hasChanged = false;
        }
    }

    private int ResolveCardCount(StylizedTreeCrownControlPoint controlPoint, Vector3 radii)
    {
        if (controlPoint.CardCount > 0)
            return controlPoint.CardCount;

        // 当 cardCount 为 0 时，按椭球体积和密度自动估算叶片数量。
        float volume = 4.0f / 3.0f * Mathf.PI * radii.x * radii.y * radii.z;
        return Mathf.Clamp(Mathf.CeilToInt(volume * controlPoint.Density * 96.0f), 0, _maxGeneratedCards);
    }

    private void EnsureQuadMesh()
    {
        if (_quadMesh != null)
            return;

        // 所有叶片共享一张基础 quad，实例数据决定它在世界空间中的位置和朝向。
        _quadMesh = new Mesh { name = "StylizedTreeLeafQuad" };
        _quadMesh.SetVertices(new[]
        {
            new Vector3(-0.5f, -0.5f, 0.0f),
            new Vector3(-0.5f, 0.5f, 0.0f),
            new Vector3(0.5f, 0.5f, 0.0f),
            new Vector3(0.5f, -0.5f, 0.0f)
        });
        _quadMesh.SetUVs(0, new[]
        {
            new Vector2(0.0f, 0.0f),
            new Vector2(0.0f, 1.0f),
            new Vector2(1.0f, 1.0f),
            new Vector2(1.0f, 0.0f)
        });
        _quadMesh.SetNormals(new[]
        {
            Vector3.forward,
            Vector3.forward,
            Vector3.forward,
            Vector3.forward
        });
        _quadMesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0, true);
        _quadMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2.0f);
        _quadMesh.UploadMeshData(true);
    }

    private Camera ResolveCamera()
    {
        if (_cullingCamera != null)
            return _cullingCamera;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            // 编辑器里优先使用当前 SceneView 相机，避免“拿错相机剔除”导致全灭。
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null && sceneView.camera != null)
                return sceneView.camera;
        }
#endif

        if (Camera.main != null)
            return Camera.main;

        return FindFirstObjectByType<Camera>();
    }

    private Vector3 ResolveMainLightDirection()
    {
        if (_mainDirectionalLight == null)
            _mainDirectionalLight = ResolveDirectionalLight();

        if (_mainDirectionalLight == null)
            return new Vector3(0.35f, 1.0f, 0.2f).normalized;

        return (-_mainDirectionalLight.transform.forward).normalized;
    }

    private Light ResolveDirectionalLight()
    {
        if (RenderSettings.sun != null)
            return RenderSettings.sun;

        Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] != null && lights[i].type == LightType.Directional)
                return lights[i];
        }

        return null;
    }

    public static Vector3 ResolveWindDirection(Vector3 direction)
    {
        if (direction.sqrMagnitude < 1e-6f)
            return new Vector3(1.0f, 0.0f, 0.35f).normalized;

        return direction.normalized;
    }

    private float GetTimeValue()
    {
        return Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
    }

    private void Dispatch1D(int kernel, int elementCount)
    {
        // 统一的 1D dispatch 包装，避免每个 kernel 重复计算 groupCount。
        int groupCount = Mathf.CeilToInt(elementCount / (float)ThreadGroupSize);
        _crownCompute.Dispatch(kernel, Mathf.Max(1, groupCount), 1, 1);
    }

    private static Vector3 MaxVector(Vector3 a, Vector3 b)
    {
        return new Vector3(Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y), Mathf.Max(a.z, b.z));
    }

    private static void ReleaseBuffer(ref ComputeBuffer buffer)
    {
        if (buffer == null)
            return;

        buffer.Release();
        buffer = null;
    }

    private void ReleaseResources()
    {
        ReleaseBuffer(ref _clusterBuffer);
        ReleaseBuffer(ref _allLeafCardsBuffer);
        ReleaseBuffer(ref _visibleLeafCardsBuffer);
        ReleaseBuffer(ref _visibleCountBuffer);
        ReleaseBuffer(ref _argsBuffer);

        if (_quadMesh != null)
        {
            if (Application.isPlaying)
                Destroy(_quadMesh);
            else
                DestroyImmediate(_quadMesh);

            _quadMesh = null;
        }
    }
}























