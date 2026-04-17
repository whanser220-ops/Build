using UnityEngine;

[DisallowMultipleComponent]
public sealed class BottlePileVolume : MonoBehaviour
{
    [Tooltip("本瓶堆生成的瓶子数量。")]
    [SerializeField] [Min(1)] private int _bottleCount = 512;
    [Tooltip("瓶堆生成区域尺寸，决定初始排布体积。")]
    [SerializeField] private Vector3 _pileSize = new Vector3(2.8f, 2.4f, 2.8f);
    [Tooltip("生成随机种子。相同参数下可复现排布。")]
    [SerializeField] private int _randomSeed = 12345;
    [Tooltip("水平方向随机扰动比例，用于打散过于整齐的排布。")]
    [SerializeField] [Range(0.0f, 0.5f)] private float _horizontalJitterFraction = 0.12f;
    [Tooltip("初始最大倾斜角度，给瓶堆一点自然噪声。")]
    [SerializeField] [Min(0.0f)] private float _maxTiltDegrees = 6.0f;

    public int BottleCount => _bottleCount;
    public Vector3 PileSize => _pileSize;
    public int RandomSeed => _randomSeed;
    public float HorizontalJitterFraction => _horizontalJitterFraction;
    public float MaxTiltDegrees => _maxTiltDegrees;

    private void OnValidate()
    {
        _bottleCount = Mathf.Max(1, _bottleCount);
        _pileSize.x = Mathf.Max(0.1f, _pileSize.x);
        _pileSize.y = Mathf.Max(0.1f, _pileSize.y);
        _pileSize.z = Mathf.Max(0.1f, _pileSize.z);
        _horizontalJitterFraction = Mathf.Clamp(_horizontalJitterFraction, 0.0f, 0.5f);
        _maxTiltDegrees = Mathf.Max(0.0f, _maxTiltDegrees);
    }
}
