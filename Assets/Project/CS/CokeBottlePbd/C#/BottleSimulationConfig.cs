using UnityEngine;

[CreateAssetMenu(fileName = "BottleSimulationConfig", menuName = "CokeBottlePbd/Bottle Simulation Config")]
public sealed class BottleSimulationConfig : ScriptableObject
{
    public enum GridRefreshMode
    {
        PerSubstep = 0,
        PerIteration = 1
    }

    [Tooltip("每个 substep 内的求解迭代次数。越高越稳，但越贵。")]
    [SerializeField] [Min(1)] private int _solverIterations = 1;
    [Tooltip("每帧拆分的物理小步数。通常优先增加它，而不是盲目增加迭代。")]
    [SerializeField] [Min(1)] private int _substeps = 2;
    [Tooltip("邻域网格单元尺寸。过小会增加 cell 数，过大则会增加候选碰撞。")]
    [SerializeField] [Min(0.01f)] private float _cellSize = 0.09f;
    [Tooltip("形状匹配强度，用于把粒子拉回瓶体整体形状。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _shapeStiffness = 0.78f;
    [Tooltip("速度阻尼。越接近 1，运动保留越多。")]
    [SerializeField] [Range(0.9f, 1.0f)] private float _velocityDamping = 0.992f;
    [Tooltip("重力加速度。")]
    [SerializeField] private float _gravity = -9.81f;
    [Tooltip("接触容差，小于该值的穿插会被忽略。")]
    [SerializeField] [Min(0.0f)] private float _contactSlop = 0.0015f;
    [Tooltip("最大速度限制，防止极端帧把瓶子打飞。")]
    [SerializeField] [Min(0.1f)] private float _maxSpeed = 15.0f;
    [Tooltip("连续静止多少帧后，瓶子进入休眠。")]
    [SerializeField] [Min(1)] private int _sleepFrameThreshold = 18;
    [Tooltip("低于该速度阈值时可累计休眠计数。")]
    [SerializeField] [Min(0.0f)] private float _sleepSpeedThreshold = 0.05f;
    [Tooltip("角色靠近该半径内时，休眠瓶会被唤醒。")]
    [SerializeField] [Min(0.0f)] private float _sleepCharacterWakeRadius = 1.4f;
    [Tooltip("活跃瓶邻近传播的预唤醒半径，用于避免 active frontier 断层。")]
    [SerializeField] [Min(0.0f)] private float _sleepActiveNeighborWakeRadius = 0.4f;
    [Tooltip("局部 broadphase 窗口尺寸。未启用 Terrain 对齐时也作为完整模拟边界；启用后，Y 仍表示模拟高度，X/Z 用于局部网格窗口。")]
    [SerializeField] private Vector3 _simulationBoundsSize = new Vector3(14.0f, 8.0f, 14.0f);
    [Tooltip("邻域网格刷新频率。PerSubstep 更省，PerIteration 更稳。")]
    [SerializeField] private GridRefreshMode _gridRefreshMode = GridRefreshMode.PerSubstep;

    public int SolverIterations => _solverIterations;
    public int Substeps => _substeps;
    public float CellSize => _cellSize;
    public float ShapeStiffness => _shapeStiffness;
    public float VelocityDamping => _velocityDamping;
    public float Gravity => _gravity;
    public float ContactSlop => _contactSlop;
    public float MaxSpeed => _maxSpeed;
    public int SleepFrameThreshold => _sleepFrameThreshold;
    public float SleepSpeedThreshold => _sleepSpeedThreshold;
    public float SleepCharacterWakeRadius => _sleepCharacterWakeRadius;
    public float SleepActiveNeighborWakeRadius => _sleepActiveNeighborWakeRadius;
    public Vector3 SimulationBoundsSize => _simulationBoundsSize;
    public GridRefreshMode GridRefresh => _gridRefreshMode;

    private void OnValidate()
    {
        _solverIterations = Mathf.Max(1, _solverIterations);
        _substeps = Mathf.Max(1, _substeps);
        _cellSize = Mathf.Max(0.01f, _cellSize);
        _contactSlop = Mathf.Max(0.0f, _contactSlop);
        _maxSpeed = Mathf.Max(0.1f, _maxSpeed);
        _sleepFrameThreshold = Mathf.Max(1, _sleepFrameThreshold);
        _sleepSpeedThreshold = Mathf.Max(0.0f, _sleepSpeedThreshold);
        _sleepCharacterWakeRadius = Mathf.Max(0.0f, _sleepCharacterWakeRadius);
        _sleepActiveNeighborWakeRadius = Mathf.Max(0.0f, _sleepActiveNeighborWakeRadius);
        _simulationBoundsSize.x = Mathf.Max(0.1f, _simulationBoundsSize.x);
        _simulationBoundsSize.y = Mathf.Max(0.1f, _simulationBoundsSize.y);
        _simulationBoundsSize.z = Mathf.Max(0.1f, _simulationBoundsSize.z);
    }
}
