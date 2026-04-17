using UnityEngine;

public enum CrownShapeType : uint
{
    [InspectorName("球体")]
    Sphere = 0,
    [InspectorName("椭球体")]
    Ellipsoid = 1,
    [InspectorName("定向椭球体")]
    OrientedEllipsoid = 2,
    [InspectorName("胶囊体")]
    Capsule = 3,
}

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class NewTreeLeafControlPoint : MonoBehaviour
{
    [Header("树冠控制点")]
    [Tooltip("控制这个树冠团块使用哪种基础体积形状。")]
    [InspectorName("形状")]
    [SerializeField] private CrownShapeType _shape = CrownShapeType.Sphere;

    [Tooltip("树冠团块在树根局部空间里的分布尺寸。")]
    [InspectorName("分布范围")]
    [SerializeField] private Vector3 _distributionRange = Vector3.one;

    [Tooltip("单位体积内生成多少叶片实例。")]
    [InspectorName("密度")]
    [SerializeField] [Min(0.0f)] private float _density = 128.0f;

    [Tooltip("控制当前树冠团块随机分布的独立种子。")]
    [InspectorName("随机种子")]
    [HideInInspector] [SerializeField] private uint _seed = 1u;

    [Tooltip("预留给多树或分组使用的树 ID。")]
    [InspectorName("树ID")]
    [HideInInspector] [SerializeField] private uint _treeId = 0u;

    [Tooltip("对这个控制点生成的叶片尺寸做额外缩放。")]
    [InspectorName("叶片尺寸倍率")]
    [SerializeField] [Min(0.01f)] private float _leafSizeScale = 1.0f;

    [Tooltip("控制树冠内部保留多少空腔，值越大越偏外层分布。")]
    [InspectorName("内层空腔")]
    [SerializeField] [Range(0.0f, 0.95f)] private float _shellInner = 0.72f;

    [Tooltip("控制外密内疏的分布曲线，越小越偏向外层。")]
    [InspectorName("壳层指数")]
    [HideInInspector] [SerializeField] [Min(0.01f)] private float _shellExponent = 0.35f;

    [Tooltip("对采样结果加入额外扰动，避免分布过于规整。")]
    [InspectorName("分布噪声")]
    [HideInInspector] [SerializeField] [Range(0.0f, 0.5f)] private float _noiseAmount = 0.1f;

    [Tooltip("当前团块受整棵树上下调色影响的权重。")]
    [InspectorName("高度调色权重")]
    [HideInInspector] [SerializeField] [Range(0.0f, 1.0f)] private float _heightTintWeight = 0.8f;

    [Tooltip("选中控制点时是否绘制辅助 Gizmo。")]
    [InspectorName("显示 Gizmo")]
    [HideInInspector] [SerializeField] private bool _drawGizmo = true;

    public CrownShapeType Shape => _shape;
    public Vector3 DistributionRange => _distributionRange;
    public float Density => _density;
    public uint Seed => _seed;
    public uint TreeId => _treeId;
    public float LeafSizeScale => _leafSizeScale;
    public float ShellInner => _shellInner;
    public float ShellExponent => _shellExponent;
    public float NoiseAmount => _noiseAmount;
    public float HeightTintWeight => _heightTintWeight;

    private void OnValidate()
    {
        _distributionRange.x = Mathf.Max(0.01f, _distributionRange.x);
        _distributionRange.y = Mathf.Max(0.01f, _distributionRange.y);
        _distributionRange.z = Mathf.Max(0.01f, _distributionRange.z);
        _density = Mathf.Max(0.0f, _density);
        _leafSizeScale = Mathf.Max(0.01f, _leafSizeScale);
        _shellInner = Mathf.Clamp(_shellInner, 0.0f, 0.95f);
        _shellExponent = Mathf.Max(0.01f, _shellExponent);
        _noiseAmount = Mathf.Clamp(_noiseAmount, 0.0f, 0.5f);
        _heightTintWeight = Mathf.Clamp01(_heightTintWeight);
    }

    private void OnDrawGizmosSelected()
    {
        if (!_drawGizmo)
            return;

        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;

        Gizmos.color = new Color(0.33f, 0.86f, 0.46f, 0.95f);
        Gizmos.matrix = BuildGizmoMatrix();

        switch (_shape)
        {
            case CrownShapeType.Capsule:
                DrawWireCapsule();
                break;
            default:
                Gizmos.DrawWireSphere(Vector3.zero, 0.5f);
                break;
        }

        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }

    private Matrix4x4 BuildGizmoMatrix()
    {
        Vector3 scale = _distributionRange;

        // 球体 gizmo 强制取最大轴，和运行时 Sphere 的包围语义保持一致。
        if (_shape == CrownShapeType.Sphere)
            scale = Vector3.one * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));

        return Matrix4x4.TRS(transform.position, transform.rotation, scale);
    }

    private static void DrawWireCapsule()
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

        DrawArc(Vector3.right, Vector3.up, topCenter, radius, segmentCount);
        DrawArc(Vector3.forward, Vector3.up, topCenter, radius, segmentCount);
        DrawArc(Vector3.right, Vector3.down, bottomCenter, radius, segmentCount);
        DrawArc(Vector3.forward, Vector3.down, bottomCenter, radius, segmentCount);
    }

    private static void DrawArc(Vector3 axisA, Vector3 axisB, Vector3 centerOffset, float radius, int segmentCount)
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
}
