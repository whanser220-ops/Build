using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

[StructLayout(LayoutKind.Sequential)]
public struct CrownControlPointGPU
{
    public Vector3 centerLS;
    public uint shapeType;
    public Quaternion rotationLS;
    public Vector3 scaleLS;
    public float density;
    public uint seed;
    public uint treeId;
    public uint startIndex;
    public uint leafCount;
    public float leafSizeScale;
    public float shellInner;
    public float shellExponent;
    public float noiseAmount;
    public float heightTintWeight;
    public float overlapAtten;
    public Vector2 padding;
}

[StructLayout(LayoutKind.Sequential)]
public struct LeafInstanceGPU
{
    public Vector3 positionLS;
    public float size;
    public Vector3 crownCenterLS;
    public float crownRadiusLS;
    public Vector3 crownExtentsLS;
    public float rotation;
    public float variation;
    public float radial01;
    public float localHeight01;
    public float heightTintWeight;
    public float shellWeight;
    public uint treeId;
    public Vector2 padding;
}

public enum NewTreeLeafDebugView
{
    [InspectorName("正常显示")]
    None = 0,
    [InspectorName("壳层权重")]
    ShellWeight = 1,
    [InspectorName("径向深度")]
    Radial01 = 2,
    [InspectorName("AO 观察")]
    AOOnly = 4,
    [InspectorName("宏观受光")]
    MacroLightOnly = 5,
}

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class NewTreeLeafRenderer : MonoBehaviour
{
    private const int KernelThreadGroupSize = 64;
    private const int VerticesPerLeaf = 6;
    private const float Epsilon = 1e-4f;
    private const int RampTextureWidth = 128;
    private const int BreakupTextureSize = 64;

    private static readonly int ControlPointsId = Shader.PropertyToID("_ControlPoints");
    private static readonly int PointCountId = Shader.PropertyToID("_PointCount");
    private static readonly int TotalLeafCountId = Shader.PropertyToID("_TotalLeafCount");
    private static readonly int LeafInstancesId = Shader.PropertyToID("_LeafInstances");
    private static readonly int LeafSizeRangeId = Shader.PropertyToID("_LeafSizeRange");
    private static readonly int RandomSeedId = Shader.PropertyToID("_RandomSeed");
    private static readonly int TreeLocalToWorldId = Shader.PropertyToID("_TreeLocalToWorld");
    private static readonly int TreeWorldToLocalId = Shader.PropertyToID("_TreeWorldToLocal");
    private static readonly int TreeUniformScaleId = Shader.PropertyToID("_TreeUniformScale");
    private static readonly int CrownCenterLSId = Shader.PropertyToID("_CrownCenterLS");
    private static readonly int CrownRadiusLSId = Shader.PropertyToID("_CrownRadiusLS");
    private static readonly int CrownExtentsLSId = Shader.PropertyToID("_CrownExtentsLS");
    private static readonly int TreeTopYId = Shader.PropertyToID("_TreeTopY");
    private static readonly int TreeBottomYId = Shader.PropertyToID("_TreeBottomY");
    private static readonly int AOStrengthId = Shader.PropertyToID("_AOStrength");
    private static readonly int AOStartId = Shader.PropertyToID("_AOStart");
    private static readonly int AOExponentId = Shader.PropertyToID("_AOExponent");
    private static readonly int AORadiusScaleId = Shader.PropertyToID("_AORadiusScale");
    private static readonly int TransColorId = Shader.PropertyToID("_TransColor");
    private static readonly int TransStrengthId = Shader.PropertyToID("_TransStrength");
    private static readonly int TransScatteringId = Shader.PropertyToID("_TransScattering");
    private static readonly int TransNormalDistortionId = Shader.PropertyToID("_TransNormalDistortion");
    private static readonly int TransAmbientId = Shader.PropertyToID("_TransAmbient");
    private static readonly int WindStrengthId = Shader.PropertyToID("_WindStrength");
    private static readonly int WindScaleId = Shader.PropertyToID("_WindScale");
    private static readonly int WindSpeedId = Shader.PropertyToID("_WindSpeed");
    private static readonly int WindTimeId = Shader.PropertyToID("_WindTime");
    private static readonly int BreakupNoiseTexId = Shader.PropertyToID("_BreakupNoiseTex");
    private static readonly int BreakupScaleId = Shader.PropertyToID("_BreakupScale");
    private static readonly int BreakupStrengthId = Shader.PropertyToID("_BreakupStrength");
    private static readonly int LocalLightingBlendId = Shader.PropertyToID("_LocalLightingBlend");
    private static readonly int VariationStrengthId = Shader.PropertyToID("_VariationStrength");
    private static readonly int RampWrapId = Shader.PropertyToID("_RampWrap");
    private static readonly int LightContrastId = Shader.PropertyToID("_LightContrast");
    private static readonly int ShadowFloorId = Shader.PropertyToID("_ShadowFloor");
    private static readonly int BaseMapBoostId = Shader.PropertyToID("_BaseMapBoost");
    private static readonly int TopTintId = Shader.PropertyToID("_TopTint");
    private static readonly int BottomTintId = Shader.PropertyToID("_BottomTint");
    private static readonly int HeightTintStrengthId = Shader.PropertyToID("_HeightTintStrength");
    private static readonly int EdgeScatterStrengthId = Shader.PropertyToID("_EdgeScatterStrength");
    private static readonly int DebugViewId = Shader.PropertyToID("_DebugView");
    private static readonly int CutoffId = Shader.PropertyToID("_Cutoff");
    private static readonly int RampTexId = Shader.PropertyToID("_RampTex");

    [Header("资源")]
    [InspectorName("散布 Compute")]
    [Tooltip("用于在 GPU 上批量生成整棵树叶片实例的 Compute Shader。")]
    [SerializeField] private ComputeShader _scatterCompute;
    [InspectorName("叶片材质")]
    [Tooltip("绘制叶片 billboard 的材质。")]
    [SerializeField] private Material _leafMaterial;

    [Header("生成")]
    [InspectorName("叶片尺寸范围")]
    [Tooltip("单片叶簇卡片的基础尺寸随机范围。")]
    [SerializeField] private Vector2 _leafSizeRange = new Vector2(0.12f, 0.24f);
    [InspectorName("随机种子")]
    [Tooltip("控制整棵树叶片分布随机性的全局种子。")]
    [SerializeField] private int _randomSeed = 12345;

    [Header("渲染")]
    [InspectorName("透明裁剪阈值")]
    [Tooltip("叶片贴图 alpha 低于这个值时会被裁掉。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _alphaCutoff = 0.4f;
    [InspectorName("包围盒留白")]
    [Tooltip("在整棵树包围盒外额外补一点留白，避免被裁切。")]
    [HideInInspector] [SerializeField] [Min(0.0f)] private float _boundsPadding = 0.25f;
    [InspectorName("视角AO强度")]
    [Tooltip("视角系 AO 的整体压暗强度。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _aoStrength = 0.28f;
    [InspectorName("视角AO起点")]
    [Tooltip("从树冠前后深度的哪个位置开始明显变暗。")]
    [SerializeField] [Range(0.0f, 0.95f)] private float _aoStart = 0.62f;
    [InspectorName("视角AO曲线")]
    [Tooltip("控制视角系 AO 的过渡曲线，越大暗部越集中。")]
    [HideInInspector] [SerializeField] [Min(0.01f)] private float _aoExponent = 1.6f;
    [InspectorName("视角AO范围")]
    [Tooltip("控制视角系 AO 使用多大的树冠半径范围。")]
    [HideInInspector] [SerializeField] [Min(0.1f)] private float _aoRadiusScale = 1.0f;
    [InspectorName("透射颜色")]
    [Tooltip("背光透射叠加使用的颜色。")]
    [SerializeField] private Color _transColor = new Color(1.0f, 0.92f, 0.55f, 1.0f);
    [InspectorName("透射强度")]
    [Tooltip("背光透射的整体强度。")]
    [SerializeField] [Min(0.0f)] private float _transStrength = 0.12f;
    [InspectorName("透射聚焦")]
    [Tooltip("背光高亮的聚焦程度，越大越集中。")]
    [HideInInspector] [SerializeField] [Min(0.01f)] private float _transScattering = 18.0f;
    [InspectorName("透射法线扭动")]
    [Tooltip("透射时对宏观法线方向的额外扭动。")]
    [HideInInspector] [SerializeField] [Min(0.0f)] private float _transNormalDistortion = 0.65f;
    [InspectorName("透射底光")]
    [Tooltip("背光透射的基础底亮，避免完全没有透感。")]
    [HideInInspector] [SerializeField] [Min(0.0f)] private float _transAmbient = 0.02f;
    [InspectorName("风强度")]
    [Tooltip("风动的整体幅度，设为 0 时叶片静止。")]
    [SerializeField] [Min(0.0f)] private float _windStrength = 0.8f;
    [InspectorName("风尺度")]
    [Tooltip("风场的空间尺度，越大变化越密。")]
    [SerializeField] [Min(0.01f)] private float _windScale = 1.0f;
    [InspectorName("风速度")]
    [Tooltip("风随时间变化的速度。")]
    [SerializeField] [Min(0.0f)] private float _windSpeed = 1.0f;
    [InspectorName("打散噪声贴图")]
    [Tooltip("用于打散大色块渐变的低频噪声贴图；留空会运行时自动生成。")]
    [SerializeField] private Texture2D _breakupNoiseTex;
    [InspectorName("打散尺度")]
    [Tooltip("打散噪声在树局部空间里的采样尺度。")]
    [SerializeField] [Min(0.01f)] private float _breakupScale = 0.18f;
    [InspectorName("打散强度")]
    [Tooltip("打散噪声对颜色分层的影响强度。")]
    [SerializeField] [Range(0.0f, 0.5f)] private float _breakupStrength = 0.08f;
    [InspectorName("局部受光混合")]
    [Tooltip("局部树冠受光参与 ramp 采样的混合比例。")]
    [SerializeField] [Range(0.0f, 0.75f)] private float _localLightingBlend = 0.32f;
    [InspectorName("实例差异强度")]
    [Tooltip("每片叶簇卡片的实例差异强度，用来避免颜色完全一致。")]
    [HideInInspector] [SerializeField] [Range(0.0f, 0.2f)] private float _variationStrength = 0.04f;
    [InspectorName("渐变包裹")]
    [Tooltip("ramp 采样前的包裹值，越大颜色过渡越柔和。")]
    [HideInInspector] [SerializeField] [Range(0.0f, 1.0f)] private float _rampWrap = 0.18f;
    [InspectorName("明暗对比")]
    [Tooltip("最终明暗强度的对比曲线，越大亮暗差越明显。")]
    [HideInInspector] [SerializeField] [Min(0.01f)] private float _lightContrast = 1.55f;
    [InspectorName("暗面底亮")]
    [Tooltip("暗面最低亮度，防止整片树冠被压成纯黑。")]
    [HideInInspector] [SerializeField] [Range(0.0f, 1.0f)] private float _shadowFloor = 0.22f;
    [InspectorName("底图提亮")]
    [Tooltip("单独提亮 BaseMap 的基础颜色，不影响透明裁剪。")]
    [SerializeField] [Min(0.0f)] private float _baseMapBoost = 1.2f;
    [InspectorName("顶部色调")]
    [Tooltip("树冠上部的偏暖偏亮色调。")]
    [SerializeField] private Color _topTint = new Color(1.12f, 1.05f, 0.86f, 1.0f);
    [InspectorName("底部色调")]
    [Tooltip("树冠下部的偏冷偏深色调。")]
    [SerializeField] private Color _bottomTint = new Color(0.68f, 0.84f, 0.98f, 1.0f);
    [InspectorName("高度调色强度")]
    [Tooltip("整棵树上暖下冷的调色强度。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _heightTintStrength = 0.82f;
    [InspectorName("边缘背光强度")]
    [Tooltip("只在外轮廓和外壳区域抬亮背光，避免内部泛白。")]
    [HideInInspector] [SerializeField] [Range(0.0f, 2.0f)] private float _edgeScatterStrength = 0.35f;
    [InspectorName("颜色渐变")]
    [Tooltip("用于生成运行时 ramp 纹理的颜色渐变。")]
    [SerializeField] [GradientUsage(false)] private Gradient _colorRamp;

    [Header("调试")]
    [InspectorName("显示 ControlPoint 体积")]
    [Tooltip("在场景视图里绘制所有控制点的体积范围，便于检查树冠大形。")]
    [SerializeField] private bool _showControlPointVolumes = true;
    [InspectorName("调试视图")]
    [Tooltip("切换查看壳层、径向深度、AO 和宏观受光等中间结果。")]
    [SerializeField] private NewTreeLeafDebugView _debugView = NewTreeLeafDebugView.None;

    private GraphicsBuffer _controlPointBuffer;
    private GraphicsBuffer _leafInstanceBuffer;
    private GraphicsBuffer _argsBuffer;
    private MaterialPropertyBlock _propertyBlock;
    private Texture2D _rampTexture;
    private Texture2D _generatedBreakupNoiseTexture;
    private ComputeShader _kernelOwner;
    private int _kernel = -1;
    private bool _needsRebuild = true;
    private bool _rampDirty = true;
    private int _currentLeafCount;
    private float _maxLeafSizeScale = 1.0f;
    private Bounds _localBounds;
    private bool _hasLocalBounds;

    private ComputeShader _lastScatterCompute;
    private Vector2 _lastLeafSizeRange;
    private int _lastRandomSeed;
    private bool _hasCachedGenerationSettings;

    private readonly List<ControlPointState> _workingStates = new List<ControlPointState>();
    private readonly List<ControlPointState> _cachedStates = new List<ControlPointState>();

    private struct ControlPointState
    {
        public int instanceId;
        public CrownShapeType shape;
        public float density;
        public uint seed;
        public uint treeId;
        public float leafSizeScale;
        public float shellInner;
        public float shellExponent;
        public float noiseAmount;
        public float heightTintWeight;
        public Vector3 centerLS;
        public Quaternion rotationLS;
        public Vector3 scaleLS;
        public float boundingRadius;
    }

    private void OnEnable()
    {
        _needsRebuild = true;
        EnsureColorRamp();
        CacheGenerationSettings();
        EnsureResources();
        RebuildNow();
    }

    private void OnValidate()
    {
        _leafSizeRange.x = Mathf.Max(0.001f, _leafSizeRange.x);
        _leafSizeRange.y = Mathf.Max(_leafSizeRange.x, _leafSizeRange.y);
        _alphaCutoff = Mathf.Clamp01(_alphaCutoff);
        _boundsPadding = Mathf.Max(0.0f, _boundsPadding);
        _aoStrength = Mathf.Clamp01(_aoStrength);
        _aoStart = Mathf.Clamp(_aoStart, 0.0f, 0.95f);
        _aoExponent = Mathf.Max(0.01f, _aoExponent);
        _aoRadiusScale = Mathf.Max(0.1f, _aoRadiusScale);
        _transStrength = Mathf.Max(0.0f, _transStrength);
        _transScattering = Mathf.Max(0.01f, _transScattering);
        _transNormalDistortion = Mathf.Max(0.0f, _transNormalDistortion);
        _transAmbient = Mathf.Max(0.0f, _transAmbient);
        _windStrength = Mathf.Max(0.0f, _windStrength);
        _windScale = Mathf.Max(0.01f, _windScale);
        _windSpeed = Mathf.Max(0.0f, _windSpeed);
        _breakupScale = Mathf.Max(0.01f, _breakupScale);
        _breakupStrength = Mathf.Clamp(_breakupStrength, 0.0f, 0.5f);
        _localLightingBlend = Mathf.Clamp(_localLightingBlend, 0.0f, 0.75f);
        _variationStrength = Mathf.Clamp(_variationStrength, 0.0f, 0.2f);
        _rampWrap = Mathf.Clamp01(_rampWrap);
        _lightContrast = Mathf.Max(0.01f, _lightContrast);
        _shadowFloor = Mathf.Clamp01(_shadowFloor);
        _baseMapBoost = Mathf.Max(0.0f, _baseMapBoost);
        _heightTintStrength = Mathf.Clamp01(_heightTintStrength);
        _edgeScatterStrength = Mathf.Clamp(_edgeScatterStrength, 0.0f, 2.0f);
        EnsureColorRamp();
        _rampDirty = true;

        if (HasGenerationSettingsChanged())
            _needsRebuild = true;

        if (isActiveAndEnabled)
        {
            EnsureResources();

            if (_needsRebuild)
                RebuildNow();
        }

        CacheGenerationSettings();
    }

    private void Update()
    {
        if (!EnsureResources())
            return;

        CollectControlPointStates(_workingStates);

        if (HaveControlPointsChanged(_workingStates))
            _needsRebuild = true;

        if (_needsRebuild)
            RebuildFromStates(_workingStates);

        DrawLeaves();
    }

    private void OnDisable()
    {
        ReleaseResources();
    }

    private void OnDestroy()
    {
        ReleaseResources();
    }

    private void OnDrawGizmosSelected()
    {
        if (!_showControlPointVolumes)
            return;

        NewTreeLeafControlPoint[] controlPoints = GetComponentsInChildren<NewTreeLeafControlPoint>(true);
        if (controlPoints == null || controlPoints.Length == 0)
            return;

        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;

        foreach (NewTreeLeafControlPoint controlPoint in controlPoints)
        {
            if (controlPoint == null || !controlPoint.isActiveAndEnabled)
                continue;

            DrawControlPointVolume(controlPoint);
        }

        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }

    [ContextMenu("立即重建")]
    public void RebuildNow()
    {
        CollectControlPointStates(_workingStates);
        RebuildFromStates(_workingStates);
    }

    private bool EnsureResources()
    {
        if (_scatterCompute == null || _leafMaterial == null)
            return false;

        EnsureColorRamp();

        if (_propertyBlock == null)
            _propertyBlock = new MaterialPropertyBlock();

        if (_kernel < 0 || _kernelOwner != _scatterCompute)
        {
            _kernel = _scatterCompute.FindKernel("Main");
            _kernelOwner = _scatterCompute;
        }

        if (_argsBuffer == null)
            _argsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawArgs.size);

        return true;
    }

    private void RebuildFromStates(List<ControlPointState> states)
    {
        if (!EnsureResources())
            return;

        // CPU 先为每个控制点分配固定写入区间，Compute 只负责往这些区间填叶片。
        BuildControlPointData(states, out CrownControlPointGPU[] pointData, out int totalLeafCount, out Bounds localBounds, out bool hasLocalBounds);

        EnsureStructuredBuffers(states.Count, totalLeafCount);

        if (states.Count > 0)
            _controlPointBuffer.SetData(pointData);

        if (states.Count > 0 && totalLeafCount > 0)
        {
            _scatterCompute.SetInt(PointCountId, states.Count);
            _scatterCompute.SetInt(TotalLeafCountId, totalLeafCount);
            _scatterCompute.SetVector(LeafSizeRangeId, _leafSizeRange);
            _scatterCompute.SetInt(RandomSeedId, _randomSeed);
            _scatterCompute.SetBuffer(_kernel, ControlPointsId, _controlPointBuffer);
            _scatterCompute.SetBuffer(_kernel, LeafInstancesId, _leafInstanceBuffer);

            int groupCount = Mathf.CeilToInt(totalLeafCount / (float)KernelThreadGroupSize);
            _scatterCompute.Dispatch(_kernel, groupCount, 1, 1);
        }

        GraphicsBuffer.IndirectDrawArgs[] args = new GraphicsBuffer.IndirectDrawArgs[1];
        args[0].vertexCountPerInstance = (uint)VerticesPerLeaf;
        args[0].instanceCount = (uint)totalLeafCount;
        args[0].startVertex = 0;
        args[0].startInstance = 0;
        _argsBuffer.SetData(args);

        _currentLeafCount = totalLeafCount;
        _localBounds = localBounds;
        _hasLocalBounds = hasLocalBounds;
        CacheGenerationSettings();
        CacheControlPointStates(states);
        _needsRebuild = false;
    }

    private static void DrawControlPointVolume(NewTreeLeafControlPoint controlPoint)
    {
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;

        Gizmos.color = new Color(0.29f, 0.92f, 0.48f, 0.95f);
        Gizmos.matrix = BuildControlPointGizmoMatrix(controlPoint);

        switch (controlPoint.Shape)
        {
            case CrownShapeType.Capsule:
                DrawWireCapsuleGizmo();
                break;
            default:
                Gizmos.DrawWireSphere(Vector3.zero, 0.5f);
                break;
        }

        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }

    private static Matrix4x4 BuildControlPointGizmoMatrix(NewTreeLeafControlPoint controlPoint)
    {
        Vector3 scale = controlPoint.DistributionRange;

        if (controlPoint.Shape == CrownShapeType.Sphere)
            scale = Vector3.one * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));

        return Matrix4x4.TRS(controlPoint.transform.position, controlPoint.transform.rotation, scale);
    }

    private static void DrawWireCapsuleGizmo()
    {
        const int segmentCount = 24;
        const float radius = 0.5f;
        const float halfHeight = 0.5f;
        float cylinderHalf = Mathf.Max(halfHeight - radius, 0.0f);

        Vector3 topCenter = Vector3.up * cylinderHalf;
        Vector3 bottomCenter = Vector3.down * cylinderHalf;

        for (int i = 0; i < segmentCount; i++)
        {
            float angleA = (i / (float)segmentCount) * Mathf.PI * 2.0f;
            float angleB = ((i + 1) / (float)segmentCount) * Mathf.PI * 2.0f;

            Vector3 ringA = new Vector3(Mathf.Cos(angleA) * radius, 0.0f, Mathf.Sin(angleA) * radius);
            Vector3 ringB = new Vector3(Mathf.Cos(angleB) * radius, 0.0f, Mathf.Sin(angleB) * radius);

            Gizmos.DrawLine(topCenter + ringA, topCenter + ringB);
            Gizmos.DrawLine(bottomCenter + ringA, bottomCenter + ringB);
            Gizmos.DrawLine(topCenter + ringA, bottomCenter + ringA);
        }

        DrawArcGizmo(Vector3.right, Vector3.up, topCenter, radius, segmentCount);
        DrawArcGizmo(Vector3.forward, Vector3.up, topCenter, radius, segmentCount);
        DrawArcGizmo(Vector3.right, Vector3.down, bottomCenter, radius, segmentCount);
        DrawArcGizmo(Vector3.forward, Vector3.down, bottomCenter, radius, segmentCount);
    }

    private static void DrawArcGizmo(Vector3 axisA, Vector3 axisB, Vector3 centerOffset, float radius, int segmentCount)
    {
        Vector3 previousPoint = centerOffset + axisA * radius;

        for (int i = 1; i <= segmentCount / 2; i++)
        {
            float angle = (i / (segmentCount / 2.0f)) * Mathf.PI;
            Vector3 nextPoint = centerOffset + (axisA * Mathf.Cos(angle) + axisB * Mathf.Sin(angle)) * radius;
            Gizmos.DrawLine(previousPoint, nextPoint);
            previousPoint = nextPoint;
        }
    }

    private void DrawLeaves()
    {
        if (_currentLeafCount <= 0 || _leafInstanceBuffer == null || _argsBuffer == null || _leafMaterial == null)
            return;

        // 绘制阶段只更新材质参数，不重建叶片实例；风、AO 和调色都走 MPB。
        _propertyBlock.Clear();
        _propertyBlock.SetBuffer(LeafInstancesId, _leafInstanceBuffer);
        _propertyBlock.SetMatrix(TreeLocalToWorldId, transform.localToWorldMatrix);
        _propertyBlock.SetMatrix(TreeWorldToLocalId, transform.worldToLocalMatrix);
        _propertyBlock.SetFloat(TreeUniformScaleId, GetTreeUniformScale());

        Vector3 crownCenterLS = _hasLocalBounds ? _localBounds.center : Vector3.zero;
        Vector3 crownExtentsLS = _hasLocalBounds ? MaxVector(_localBounds.extents, Vector3.one * Epsilon) : Vector3.one;
        float crownRadiusLS = Mathf.Max(MaxComponent(crownExtentsLS), Epsilon);

        _propertyBlock.SetVector(CrownCenterLSId, crownCenterLS);
        _propertyBlock.SetFloat(CrownRadiusLSId, crownRadiusLS);
        _propertyBlock.SetVector(CrownExtentsLSId, crownExtentsLS);
        _propertyBlock.SetFloat(TreeTopYId, _hasLocalBounds ? _localBounds.max.y : 0.5f);
        _propertyBlock.SetFloat(TreeBottomYId, _hasLocalBounds ? _localBounds.min.y : -0.5f);
        _propertyBlock.SetFloat(AOStrengthId, _aoStrength);
        _propertyBlock.SetFloat(AOStartId, _aoStart);
        _propertyBlock.SetFloat(AOExponentId, _aoExponent);
        _propertyBlock.SetFloat(AORadiusScaleId, _aoRadiusScale);
        _propertyBlock.SetColor(TransColorId, _transColor);
        _propertyBlock.SetFloat(TransStrengthId, _transStrength);
        _propertyBlock.SetFloat(TransScatteringId, _transScattering);
        _propertyBlock.SetFloat(TransNormalDistortionId, _transNormalDistortion);
        _propertyBlock.SetFloat(TransAmbientId, _transAmbient);
        _propertyBlock.SetFloat(WindStrengthId, _windStrength);
        _propertyBlock.SetFloat(WindScaleId, _windScale);
        _propertyBlock.SetFloat(WindSpeedId, _windSpeed);
        _propertyBlock.SetFloat(WindTimeId, GetWindTime());
        _propertyBlock.SetTexture(BreakupNoiseTexId, GetOrCreateBreakupNoiseTexture());
        _propertyBlock.SetFloat(BreakupScaleId, _breakupScale);
        _propertyBlock.SetFloat(BreakupStrengthId, _breakupStrength);
        _propertyBlock.SetFloat(LocalLightingBlendId, _localLightingBlend);
        _propertyBlock.SetFloat(VariationStrengthId, _variationStrength);
        _propertyBlock.SetFloat(RampWrapId, _rampWrap);
        _propertyBlock.SetFloat(LightContrastId, _lightContrast);
        _propertyBlock.SetFloat(ShadowFloorId, _shadowFloor);
        _propertyBlock.SetFloat(BaseMapBoostId, _baseMapBoost);
        _propertyBlock.SetColor(TopTintId, _topTint);
        _propertyBlock.SetColor(BottomTintId, _bottomTint);
        _propertyBlock.SetFloat(HeightTintStrengthId, _heightTintStrength);
        _propertyBlock.SetFloat(EdgeScatterStrengthId, _edgeScatterStrength);
        _propertyBlock.SetFloat(DebugViewId, (float)_debugView);
        _propertyBlock.SetFloat(CutoffId, _alphaCutoff);
        _propertyBlock.SetTexture(RampTexId, GetOrCreateRampTexture());

        Graphics.DrawProceduralIndirect(
            _leafMaterial,
            BuildWorldBounds(),
            MeshTopology.Triangles,
            _argsBuffer,
            0,
            null,
            _propertyBlock,
            ShadowCastingMode.Off,
            false,
            gameObject.layer);
    }

    private Bounds BuildWorldBounds()
    {
        if (!_hasLocalBounds)
            return new Bounds(transform.position, Vector3.one * 0.01f);

        Matrix4x4 localToWorld = transform.localToWorldMatrix;
        Vector3 worldCenter = localToWorld.MultiplyPoint3x4(_localBounds.center);
        Vector3 worldExtents = MultiplyExtents(localToWorld, _localBounds.extents);
        worldExtents += Vector3.one * (_leafSizeRange.y * _maxLeafSizeScale * GetTreeUniformScale() + _boundsPadding);
        return new Bounds(worldCenter, worldExtents * 2.0f);
    }

    private void CollectControlPointStates(List<ControlPointState> target)
    {
        target.Clear();

        NewTreeLeafControlPoint[] controlPoints = GetComponentsInChildren<NewTreeLeafControlPoint>(true);
        Matrix4x4 worldToLocal = transform.worldToLocalMatrix;

        foreach (NewTreeLeafControlPoint controlPoint in controlPoints)
        {
            if (!controlPoint.isActiveAndEnabled)
                continue;

            Vector3 scaleLS = MaxVector(controlPoint.DistributionRange, Vector3.one * Epsilon);
            Vector3 centerLS = worldToLocal.MultiplyPoint3x4(controlPoint.transform.position);
            Quaternion rotationLS = Quaternion.Inverse(transform.rotation) * controlPoint.transform.rotation;

            target.Add(new ControlPointState
            {
                instanceId = controlPoint.GetInstanceID(),
                shape = controlPoint.Shape,
                density = Mathf.Max(0.0f, controlPoint.Density),
                seed = controlPoint.Seed,
                treeId = controlPoint.TreeId,
                leafSizeScale = Mathf.Max(0.01f, controlPoint.LeafSizeScale),
                shellInner = Mathf.Clamp(controlPoint.ShellInner, 0.0f, 0.95f),
                shellExponent = Mathf.Max(0.01f, controlPoint.ShellExponent),
                noiseAmount = Mathf.Clamp(controlPoint.NoiseAmount, 0.0f, 0.5f),
                heightTintWeight = Mathf.Clamp01(controlPoint.HeightTintWeight),
                centerLS = centerLS,
                rotationLS = rotationLS,
                scaleLS = scaleLS,
                boundingRadius = ComputeBoundingRadius(controlPoint.Shape, scaleLS),
            });
        }
    }

    private bool HaveControlPointsChanged(List<ControlPointState> currentStates)
    {
        if (currentStates.Count != _cachedStates.Count)
            return true;

        for (int i = 0; i < currentStates.Count; i++)
        {
            ControlPointState current = currentStates[i];
            ControlPointState cached = _cachedStates[i];

            if (current.instanceId != cached.instanceId
                || current.shape != cached.shape
                || !Mathf.Approximately(current.density, cached.density)
                || current.seed != cached.seed
                || current.treeId != cached.treeId
                || !Mathf.Approximately(current.leafSizeScale, cached.leafSizeScale)
                || !Mathf.Approximately(current.shellInner, cached.shellInner)
                || !Mathf.Approximately(current.shellExponent, cached.shellExponent)
                || !Mathf.Approximately(current.noiseAmount, cached.noiseAmount)
                || !Mathf.Approximately(current.heightTintWeight, cached.heightTintWeight)
                || (current.centerLS - cached.centerLS).sqrMagnitude > 1e-6f
                || Mathf.Abs(Quaternion.Dot(current.rotationLS, cached.rotationLS)) < 0.99999f
                || (current.scaleLS - cached.scaleLS).sqrMagnitude > 1e-6f)
            {
                return true;
            }
        }

        return false;
    }

    private void CacheControlPointStates(List<ControlPointState> source)
    {
        _cachedStates.Clear();
        _cachedStates.AddRange(source);
    }

    private void EnsureStructuredBuffers(int pointCount, int leafCount)
    {
        int safePointCount = Mathf.Max(1, pointCount);
        int safeLeafCount = Mathf.Max(1, leafCount);

        int pointStride = Marshal.SizeOf<CrownControlPointGPU>();
        if (_controlPointBuffer == null || _controlPointBuffer.count != safePointCount || _controlPointBuffer.stride != pointStride)
        {
            ReleaseBuffer(ref _controlPointBuffer);
            _controlPointBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, safePointCount, pointStride);
        }

        int leafStride = Marshal.SizeOf<LeafInstanceGPU>();
        if (_leafInstanceBuffer == null || _leafInstanceBuffer.count != safeLeafCount || _leafInstanceBuffer.stride != leafStride)
        {
            ReleaseBuffer(ref _leafInstanceBuffer);
            _leafInstanceBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, safeLeafCount, leafStride);
        }
    }

    private void BuildControlPointData(
        List<ControlPointState> states,
        out CrownControlPointGPU[] pointData,
        out int totalLeafCount,
        out Bounds localBounds,
        out bool hasLocalBounds)
    {
        pointData = states.Count > 0 ? new CrownControlPointGPU[states.Count] : Array.Empty<CrownControlPointGPU>();
        totalLeafCount = 0;
        _maxLeafSizeScale = 1.0f;
        hasLocalBounds = false;
        Vector3 boundsMin = Vector3.zero;
        Vector3 boundsMax = Vector3.zero;

        if (states.Count == 0)
        {
            localBounds = new Bounds(Vector3.zero, Vector3.zero);
            return;
        }

        float[] overlapAttenuation = new float[states.Count];
        for (int i = 0; i < states.Count; i++)
            overlapAttenuation[i] = 1.0f;

        for (int i = 0; i < states.Count; i++)
        {
            float overlapSum = 0.0f;

            for (int j = 0; j < states.Count; j++)
            {
                if (i == j)
                    continue;

                float combinedRadius = states[i].boundingRadius + states[j].boundingRadius;
                if (combinedRadius <= Epsilon)
                    continue;

                float distance = Vector3.Distance(states[i].centerLS, states[j].centerLS);
                overlapSum += Mathf.Clamp01((combinedRadius - distance) / combinedRadius);
            }

            overlapAttenuation[i] = 1.0f / (1.0f + overlapSum * 0.75f);
        }

        for (int i = 0; i < states.Count; i++)
        {
            ControlPointState state = states[i];
            int rawCount = Mathf.Max(0, Mathf.RoundToInt(ComputeShapeVolume(state.shape, state.scaleLS) * state.density));
            int leafCount = Mathf.Max(0, Mathf.RoundToInt(rawCount * overlapAttenuation[i]));

            pointData[i] = new CrownControlPointGPU
            {
                centerLS = state.centerLS,
                shapeType = (uint)state.shape,
                rotationLS = state.rotationLS,
                scaleLS = MaxVector(state.scaleLS, Vector3.one * Epsilon),
                density = state.density,
                seed = state.seed,
                treeId = state.treeId,
                startIndex = (uint)totalLeafCount,
                leafCount = (uint)leafCount,
                leafSizeScale = state.leafSizeScale,
                shellInner = state.shellInner,
                shellExponent = state.shellExponent,
                noiseAmount = state.noiseAmount,
                heightTintWeight = state.heightTintWeight,
                overlapAtten = overlapAttenuation[i],
                padding = Vector2.zero,
            };

            totalLeafCount += leafCount;
            _maxLeafSizeScale = Mathf.Max(_maxLeafSizeScale, state.leafSizeScale);

            Vector3 extents = ComputeLocalExtents(state.shape, state.scaleLS, state.rotationLS) * (1.0f + state.noiseAmount);
            Vector3 min = state.centerLS - extents;
            Vector3 max = state.centerLS + extents;

            if (!hasLocalBounds)
            {
                boundsMin = min;
                boundsMax = max;
                hasLocalBounds = true;
            }
            else
            {
                boundsMin = Vector3.Min(boundsMin, min);
                boundsMax = Vector3.Max(boundsMax, max);
            }
        }

        localBounds = hasLocalBounds
            ? new Bounds((boundsMin + boundsMax) * 0.5f, boundsMax - boundsMin)
            : new Bounds(Vector3.zero, Vector3.zero);
    }

    private bool HasGenerationSettingsChanged()
    {
        if (!_hasCachedGenerationSettings)
            return true;

        return _scatterCompute != _lastScatterCompute
            || _leafSizeRange != _lastLeafSizeRange
            || _randomSeed != _lastRandomSeed;
    }

    private void CacheGenerationSettings()
    {
        _lastScatterCompute = _scatterCompute;
        _lastLeafSizeRange = _leafSizeRange;
        _lastRandomSeed = _randomSeed;
        _hasCachedGenerationSettings = true;
    }

    private float GetTreeUniformScale()
    {
        Vector3 lossyScale = transform.lossyScale;
        return Mathf.Max(Mathf.Abs(lossyScale.x), Mathf.Abs(lossyScale.y), Mathf.Abs(lossyScale.z));
    }

    private static float GetWindTime()
    {
        return Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
    }

    private void ReleaseResources()
    {
        ReleaseBuffer(ref _controlPointBuffer);
        ReleaseBuffer(ref _leafInstanceBuffer);
        ReleaseBuffer(ref _argsBuffer);
        ReleaseTexture(ref _rampTexture);
        ReleaseTexture(ref _generatedBreakupNoiseTexture);
        _kernel = -1;
        _kernelOwner = null;
        _currentLeafCount = 0;
        _maxLeafSizeScale = 1.0f;
        _hasLocalBounds = false;
    }

    private static void ReleaseBuffer(ref GraphicsBuffer buffer)
    {
        if (buffer == null)
            return;

        buffer.Release();
        buffer = null;
    }

    private Texture2D GetOrCreateRampTexture()
    {
        EnsureColorRamp();

        if (_rampTexture == null)
        {
            _rampTexture = new Texture2D(RampTextureWidth, 1, TextureFormat.RGBA32, false, true)
            {
                name = "NewTreeLeafRamp",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            _rampDirty = true;
        }

        if (_rampDirty)
            RebuildRampTexture();

        return _rampTexture;
    }

    private Texture2D GetOrCreateBreakupNoiseTexture()
    {
        if (_breakupNoiseTex != null)
            return _breakupNoiseTex;

        if (_generatedBreakupNoiseTexture == null)
        {
            _generatedBreakupNoiseTexture = new Texture2D(BreakupTextureSize, BreakupTextureSize, TextureFormat.RGBA32, false, true)
            {
                name = "NewTreeLeafBreakupNoise",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            RebuildBreakupNoiseTexture();
        }

        return _generatedBreakupNoiseTexture;
    }

    private void RebuildRampTexture()
    {
        if (_rampTexture == null)
            return;

        Color[] pixels = new Color[RampTextureWidth];
        for (int i = 0; i < RampTextureWidth; i++)
        {
            float t = i / (float)(RampTextureWidth - 1);
            pixels[i] = _colorRamp.Evaluate(t);
        }

        _rampTexture.SetPixels(pixels);
        _rampTexture.Apply(false, false);
        _rampDirty = false;
    }

    private void RebuildBreakupNoiseTexture()
    {
        if (_generatedBreakupNoiseTexture == null)
            return;

        Color[] pixels = new Color[BreakupTextureSize * BreakupTextureSize];
        for (int y = 0; y < BreakupTextureSize; y++)
        {
            for (int x = 0; x < BreakupTextureSize; x++)
            {
                float u = x / (float)BreakupTextureSize;
                float v = y / (float)BreakupTextureSize;
                float amplitude = 1.0f;
                float frequency = 1.35f;
                float value = 0.0f;
                float totalWeight = 0.0f;

                for (int octave = 0; octave < 3; octave++)
                {
                    value += Mathf.PerlinNoise(u * frequency + 17.31f, v * frequency + 41.72f) * amplitude;
                    totalWeight += amplitude;
                    amplitude *= 0.5f;
                    frequency *= 2.0f;
                }

                value /= Mathf.Max(totalWeight, Epsilon);
                pixels[y * BreakupTextureSize + x] = new Color(value, value, value, 1.0f);
            }
        }

        _generatedBreakupNoiseTexture.SetPixels(pixels);
        _generatedBreakupNoiseTexture.Apply(false, false);
    }

    private void EnsureColorRamp()
    {
        if (_colorRamp != null)
            return;

        _colorRamp = new Gradient();
        _colorRamp.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.14f, 0.31f, 0.16f), 0.0f),
                new GradientColorKey(new Color(0.38f, 0.68f, 0.31f), 0.45f),
                new GradientColorKey(new Color(0.92f, 0.98f, 0.62f), 1.0f),
            },
            new[]
            {
                new GradientAlphaKey(1.0f, 0.0f),
                new GradientAlphaKey(1.0f, 1.0f),
            });
        _rampDirty = true;
    }

    private static void ReleaseTexture(ref Texture2D texture)
    {
        if (texture == null)
            return;

        if (Application.isPlaying)
            Destroy(texture);
        else
            DestroyImmediate(texture);

        texture = null;
    }

    private static float ComputeBoundingRadius(CrownShapeType shape, Vector3 scaleLS)
    {
        switch (shape)
        {
            case CrownShapeType.Sphere:
                return MaxComponent(scaleLS) * 0.5f;
            case CrownShapeType.Capsule:
            {
                float radius = Mathf.Max(scaleLS.x, scaleLS.z) * 0.5f;
                float halfLine = Mathf.Max(scaleLS.y * 0.5f - radius, 0.0f);
                return halfLine + radius;
            }
            default:
                return MaxComponent(scaleLS) * 0.5f;
        }
    }

    private static float ComputeShapeVolume(CrownShapeType shape, Vector3 scaleLS)
    {
        switch (shape)
        {
            case CrownShapeType.Sphere:
            {
                float radius = MaxComponent(scaleLS) * 0.5f;
                return (4.0f / 3.0f) * Mathf.PI * radius * radius * radius;
            }
            case CrownShapeType.Ellipsoid:
            case CrownShapeType.OrientedEllipsoid:
            {
                Vector3 radii = scaleLS * 0.5f;
                return (4.0f / 3.0f) * Mathf.PI * radii.x * radii.y * radii.z;
            }
            case CrownShapeType.Capsule:
            {
                float radius = Mathf.Max(scaleLS.x, scaleLS.z) * 0.5f;
                float halfLine = Mathf.Max(scaleLS.y * 0.5f - radius, 0.0f);
                float cylinderVolume = Mathf.PI * radius * radius * (halfLine * 2.0f);
                float sphereVolume = (4.0f / 3.0f) * Mathf.PI * radius * radius * radius;
                return cylinderVolume + sphereVolume;
            }
            default:
                return 0.0f;
        }
    }

    private static Vector3 ComputeLocalExtents(CrownShapeType shape, Vector3 scaleLS, Quaternion rotationLS)
    {
        switch (shape)
        {
            case CrownShapeType.Sphere:
            {
                float radius = MaxComponent(scaleLS) * 0.5f;
                return Vector3.one * radius;
            }
            case CrownShapeType.Ellipsoid:
                return scaleLS * 0.5f;
            case CrownShapeType.OrientedEllipsoid:
                return MultiplyExtents(Matrix4x4.Rotate(rotationLS), scaleLS * 0.5f);
            case CrownShapeType.Capsule:
            {
                float radius = Mathf.Max(scaleLS.x, scaleLS.z) * 0.5f;
                float halfLine = Mathf.Max(scaleLS.y * 0.5f - radius, 0.0f);
                return MultiplyExtents(Matrix4x4.Rotate(rotationLS), new Vector3(radius, halfLine + radius, radius));
            }
            default:
                return Vector3.zero;
        }
    }

    private static Vector3 MultiplyExtents(Matrix4x4 matrix, Vector3 extents)
    {
        Vector4 column0 = matrix.GetColumn(0);
        Vector4 column1 = matrix.GetColumn(1);
        Vector4 column2 = matrix.GetColumn(2);

        return new Vector3(
            Mathf.Abs(column0.x) * extents.x + Mathf.Abs(column1.x) * extents.y + Mathf.Abs(column2.x) * extents.z,
            Mathf.Abs(column0.y) * extents.x + Mathf.Abs(column1.y) * extents.y + Mathf.Abs(column2.y) * extents.z,
            Mathf.Abs(column0.z) * extents.x + Mathf.Abs(column1.z) * extents.y + Mathf.Abs(column2.z) * extents.z);
    }

    private static float MaxComponent(Vector3 value)
    {
        return Mathf.Max(value.x, Mathf.Max(value.y, value.z));
    }

    private static Vector3 MaxVector(Vector3 value, Vector3 minValue)
    {
        return new Vector3(
            Mathf.Max(value.x, minValue.x),
            Mathf.Max(value.y, minValue.y),
            Mathf.Max(value.z, minValue.z));
    }
}
