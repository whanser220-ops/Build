using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("caroon_Tree/风格化树冠/叶团控制点")]
public sealed class StylizedTreeCrownControlPoint : MonoBehaviour
{
    // 单个控制点代表一个叶团。位置与旋转决定叶团中心和局部坐标轴，
    // 半径、密度和艺术参数会被上传到 GPU 用于生成叶片实例。
    [Header("叶团形状")]
    [Tooltip("叶团在局部 X/Y/Z 方向上的椭球半径。旋转控制叶团朝向，位置控制叶团中心。")]
    [SerializeField] private Vector3 _ellipsoidRadii = new Vector3(1.4f, 1.0f, 1.2f);

    [Header("叶片生成")]
    [Tooltip("固定生成的叶片卡片数量。大于 0 时直接使用该值；等于 0 时会根据体积和密度自动估算。")]
    [SerializeField] [Min(0)] private int _cardCount = 320;
    [Tooltip("叶团密度。会同时影响自动数量估算、厚度遮挡和团间方向遮挡。")]
    [SerializeField] [Min(0.05f)] private float _density = 1.0f;
    [Tooltip("当前叶团的叶片尺寸倍率。顶部小团可以适当调小，主团可略大。")]
    [SerializeField] [Min(0.1f)] private float _leafSizeScale = 1.0f;
    [Tooltip("预留的随机种子。当前最小可跑版本尚未单独使用该值，可作为后续扩展入口。")]
    [SerializeField] private uint _seed = 1u;

    [Header("艺术控制")]
    [Tooltip("叶团在整棵树冠中的层级。通常顶部更小更亮，中层更厚，下层更暗。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _layer = 0.5f;
    [Tooltip("叶团位于树冠内部的深度。值越大，越容易被当作更厚、更靠里的部分。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _depthInCrown = 0.45f;
    [Tooltip("该叶团的环境遮挡强度。值越高，团内和下部环境光越暗。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _ambientOccl = 0.5f;
    [Tooltip("该叶团阻挡其他叶团直射光的偏置强度。下层或厚重团块可以适当调高。")]
    [SerializeField] [Range(0.0f, 2.0f)] private float _directOcclBias = 1.0f;
    [Tooltip("让叶片更倾向朝向叶团的局部上方向。顶部团块调高会更容易获得明亮顶面。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _upFacingBias = 0.25f;
    [Tooltip("让叶片分布更偏外壳。值越高，越蓬松、越不容易显得实心。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _shellBias = 0.8f;

    public Vector3 EllipsoidRadii => _ellipsoidRadii;
    public int CardCount => _cardCount;
    public float Density => _density;
    public float LeafSizeScale => _leafSizeScale;
    public uint Seed => _seed;
    public float Layer => _layer;
    public float DepthInCrown => _depthInCrown;
    public float AmbientOccl => _ambientOccl;
    public float DirectOcclBias => _directOcclBias;
    public float UpFacingBias => _upFacingBias;
    public float ShellBias => _shellBias;

    private void OnValidate()
    {
        // 保证 Inspector 中的参数始终落在可用范围内，避免 Compute 里出现退化椭球。
        _ellipsoidRadii.x = Mathf.Max(0.05f, _ellipsoidRadii.x);
        _ellipsoidRadii.y = Mathf.Max(0.05f, _ellipsoidRadii.y);
        _ellipsoidRadii.z = Mathf.Max(0.05f, _ellipsoidRadii.z);
        _cardCount = Mathf.Max(0, _cardCount);
        _density = Mathf.Max(0.05f, _density);
        _leafSizeScale = Mathf.Max(0.1f, _leafSizeScale);
        _layer = Mathf.Clamp01(_layer);
        _depthInCrown = Mathf.Clamp01(_depthInCrown);
        _ambientOccl = Mathf.Clamp01(_ambientOccl);
        _directOcclBias = Mathf.Clamp(_directOcclBias, 0.0f, 2.0f);
        _upFacingBias = Mathf.Clamp01(_upFacingBias);
        _shellBias = Mathf.Clamp01(_shellBias);
    }

    private void OnDrawGizmosSelected()
    {
        // 选中时用线框球近似显示该叶团的椭球范围，方便在场景里摆放团块层次。
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;

        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, _ellipsoidRadii * 2.0f);
        Gizmos.color = Color.Lerp(new Color(0.25f, 0.75f, 0.25f, 1.0f), new Color(1.0f, 0.85f, 0.25f, 1.0f), 1.0f - _layer);
        Gizmos.DrawWireSphere(Vector3.zero, 0.5f);

        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }
}


