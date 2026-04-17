using System;
using UnityEngine;

[Serializable]
public struct ClumpParameters
{
    [Tooltip("控制草叶向该草簇中心聚拢的程度。0 表示保持原分布，1 表示尽量收向簇中心。")]
    [Range(0.0f, 1.0f)]
    public float pullToCentre;

    [Tooltip("控制同一草簇内部草叶朝向的一致性。数值越高，簇内草叶越朝向同一个方向。")]
    [Range(0.0f, 1.0f)]
    public float pointInSameDirection;

    [Tooltip("该草簇类型的基础草高。")]
    public float baseHeight;

    [Tooltip("草高的随机变化范围。最终高度会在基础草高附近波动。")]
    public float heightRandom;

    [Tooltip("该草簇类型的基础草叶宽度。")]
    public float baseWidth;

    [Tooltip("草叶宽度的随机变化范围。")]
    public float widthRandom;

    [Tooltip("基础倾斜量，控制草尖偏离竖直方向的程度。")]
    public float baseTilt;

    [Tooltip("倾斜量的随机变化范围。")]
    public float tiltRandom;

    [Tooltip("基础弯曲量，决定草叶主曲线的弯折程度。")]
    public float baseBend;

    [Tooltip("弯曲量的随机变化范围。")]
    public float bendRandom;

    [Tooltip("该草簇类型的基础簇半径。数值越大，草会在更大范围内扩散。")]
    public float clusterRadius;

    [Tooltip("该草簇类型每个簇默认生成的草叶数量。")]
    public int grassPerCluster;

    public static ClumpParameters Default => new ClumpParameters
    {
        pullToCentre = 0.15f,
        pointInSameDirection = 0.5f,
        baseHeight = 1.0f,
        heightRandom = 0.2f,
        baseWidth = 0.08f,
        widthRandom = 0.02f,
        baseTilt = 0.8f,
        tiltRandom = 0.1f,
        baseBend = 0.35f,
        bendRandom = 0.15f,
        clusterRadius = 3.0f,
        grassPerCluster = 20
    };
}
