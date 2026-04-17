using UnityEngine;
using System.Runtime.InteropServices;
public class Boid : MonoBehaviour
{
    public ComputeShader shader;
    public Material material;
    public Mesh mesh;
    public int meshNumber;
    public int spawnRadius;
    

    private int GroudSizeX;
    private int KernelHanle;
    
    private Bounds bounds;

    public struct Boids
    {
        public Vector3 position;
        public Vector3 Dirction;
        public float noise_offset;

        public Boids(Vector3 pos, Vector3 dir, float noise_offset1)
        {
            position = pos;
            Dirction = dir;
            noise_offset = noise_offset1;
        }
        
    };

    private Boids[] boids; 
    private ComputeBuffer BoidsBuffer;
    private ComputeBuffer argsBuffer;
    uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
    
    void Start()
    {
        bounds = new Bounds(Vector3.zero, Vector3.one * 1000);
        initialCompute();
        
    }

    void initialCompute()
    {
        KernelHanle =  shader.FindKernel("CSMain");
        uint threadGroupSizeX = 0;
        shader.GetKernelThreadGroupSizes(KernelHanle,out threadGroupSizeX,out _,out _);
        
        GroudSizeX = Mathf.CeilToInt(meshNumber/threadGroupSizeX);

        boids = new Boids[meshNumber];
        for (int i = 0; i < meshNumber; i++)
        {
            Vector3 pos = transform.position + Random.insideUnitSphere * spawnRadius;
            Quaternion rot = Quaternion.Slerp(transform.rotation, Random.rotation, 0.3f);
            float offset = Random.value * 1000.0f;
            boids[i] = new Boids(pos, rot.eulerAngles, offset);
        }
        
        BoidsBuffer = new ComputeBuffer(boids.Length, Marshal.SizeOf(typeof(Boids)));
        BoidsBuffer.SetData(boids);
        shader.SetBuffer(KernelHanle, "BoidsBuffer", BoidsBuffer);
        
        argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        args[0] = mesh.GetIndexCount(0);
        args[1] = (uint)meshNumber;
        argsBuffer.SetData(args);
        
        
        material.SetBuffer("BoidsBuffer", BoidsBuffer);
    }
    
    void Update()
    {
        shader.Dispatch(KernelHanle, GroudSizeX, 1, 1);
        
        shader.SetFloat("time",Time.time);
        shader.SetFloat("deltaTime", Time.deltaTime);
        Graphics.DrawMeshInstancedIndirect(mesh,0,material,bounds,argsBuffer);
    }
}
