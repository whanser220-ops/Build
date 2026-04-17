using UnityEngine;
using System.Runtime.InteropServices;
public class Star : MonoBehaviour
{
    public ComputeShader shader;
    public Material material;
    public Mesh mesh;
    public Camera MainCamera;
    public int StarNumber;
    public float _speed;
    public float _range = 10;
    public float maxViewDistance;

    private Bounds bounds;
    private int kernelHandle;
    private int GroupSizeX;
    private struct StarData
    {
        public Vector3 position;
        public Vector3 scale;
        public Vector3 rotation;
        public float seed;
    }
    uint[] args;
    
    private ComputeBuffer CullingOutputBuffer;
    private ComputeBuffer ArgsBuffer;
    void Start()
    {
        kernelHandle = shader.FindKernel("CSMain");
        InitialBuffer();
    }

    void Update()
    {
        SetUp();
        //将修改GPUBuffer的元素值。
        CullingOutputBuffer.SetCounterValue(0);
        
        bounds = new Bounds(transform.position, Vector3.one * _range);
        shader.Dispatch(kernelHandle,GroupSizeX,1,1);
        Graphics.DrawMeshInstancedIndirect(mesh,0,material,bounds,ArgsBuffer);
        
        ComputeBuffer.CopyCount(CullingOutputBuffer,ArgsBuffer,sizeof(uint));
    }
    
    void InitialBuffer()
    {

        CullingOutputBuffer = new ComputeBuffer(StarNumber, Marshal.SizeOf(typeof(StarData)), ComputeBufferType.Append);

        ArgsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        args =  new uint[5];
        args[0] = (uint)mesh.GetIndexCount(0);
        args[1] = (uint)StarNumber;
        ArgsBuffer.SetData(args);
    }

    void SetUp()
    {
        uint X = 0;
        shader.GetKernelThreadGroupSizes(kernelHandle, out X , out _, out _);
        GroupSizeX = Mathf.CeilToInt((float)StarNumber / X);
        var V = MainCamera.worldToCameraMatrix;
        var P = GL.GetGPUProjectionMatrix(MainCamera.projectionMatrix, false);
        var VP = P * V;
        
        shader.SetMatrix("_VP",VP);//透视投影矩阵
        shader.SetVector("cameraPos", MainCamera.transform.position);
        shader.SetVector("cameraDir", MainCamera.transform.forward);
        shader.SetFloat("cameraHalfFov", MainCamera.fieldOfView*0.5f);
        shader.SetFloat("maxViewDistance", maxViewDistance);
        shader.SetBuffer(kernelHandle, "CullingOutputBuffer", CullingOutputBuffer);
        shader.SetFloat("Time",Time.time);
        shader.SetInt("Count",StarNumber);
        shader.SetFloat("_speed",_speed);
        shader.SetFloat("_range",_range);
        
        material.SetBuffer("CullingOutputBuffer", CullingOutputBuffer);
        material.SetFloat("_StarCount",StarNumber);
        
    }
    void OnDestroy()
    {
        // 释放缓冲区
        if (CullingOutputBuffer != null)
        {
            CullingOutputBuffer.Release();
        }
    }
}
