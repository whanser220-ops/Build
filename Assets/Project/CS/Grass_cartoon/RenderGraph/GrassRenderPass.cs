using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Matrix4x4 = UnityEngine.Matrix4x4;
using Vector3 = UnityEngine.Vector3;
using Vector4 = UnityEngine.Vector4;

public class GrassRenderPass : ScriptableRenderPass
{
    private readonly GrassFeature m_Feature;
    private readonly GrassFeature.Settings m_Settings;
    private readonly GraphicsBuffer m_CullingBuffer;
    private readonly GraphicsBuffer m_ArgsBuffer;
    private readonly GraphicsBuffer m_SingleGrassBuffer;
    private readonly GraphicsBuffer m_ClumpBuffer;

    private Texture2D m_HeightTexture;
    private Terrain m_CurrentTerrain;

    private RTHandle m_HeightRTHandle;
    private RTHandle m_WindRTHandle;

    private readonly MaterialPropertyBlock m_PropertyBlock;

    public GrassRenderPass(
        GrassFeature feature,
        GrassFeature.Settings settings,
        GraphicsBuffer argsBuffer,
        GraphicsBuffer cullingBuffer,
        GraphicsBuffer singleGrassBuffer,
        GraphicsBuffer clumpBuffer)
    {
        m_Feature = feature;
        m_Settings = settings;
        m_ArgsBuffer = argsBuffer;
        m_CullingBuffer = cullingBuffer;
        m_SingleGrassBuffer = singleGrassBuffer;
        m_ClumpBuffer = clumpBuffer;
        m_PropertyBlock = new MaterialPropertyBlock();
    }

    public void Dispose()
    {
        m_HeightRTHandle?.Release();
        m_HeightRTHandle = null;
        m_WindRTHandle?.Release();
        m_WindRTHandle = null;
    }

    public void SetupTerrainData(Texture2D height, Terrain terrain)
    {
        m_HeightTexture = height;
        m_CurrentTerrain = terrain;

        UpdateOrCreateRTHandle(ref m_HeightRTHandle, height, "Grass_TerrainHeightMap");
    }

    private void UpdateOrCreateRTHandle(ref RTHandle rtHandle, Texture texture, string name)
    {
        if (texture == null)
        {
            rtHandle?.Release();
            rtHandle = null;
            return;
        }

        if (rtHandle == null || rtHandle.rt != texture)
        {
            rtHandle?.Release();
            rtHandle = RTHandles.Alloc(texture, name: name);
        }
    }

    private static TextureHandle ImportTextureFromRTHandle(RenderGraph renderGraph, RTHandle rtHandle, Texture texture)
    {
        if (rtHandle == null || texture == null)
            return TextureHandle.nullHandle;

        RenderTargetInfo info = new RenderTargetInfo
        {
            width = texture.width,
            height = texture.height,
            volumeDepth = 1,
            msaaSamples = 1,
            format = texture.graphicsFormat,
            bindMS = false
        };

        return renderGraph.ImportTexture(rtHandle, info);
    }

    private class GrassPassData
    {
        public ComputeShader cullingShader;
        public int kernelHandle;

        public BufferHandle argsBuffer;
        public BufferHandle cullingBuffer;

        public TextureHandle heightmap;
        public TextureHandle windTexture;

        public Vector3 terrainPosition;
        public Vector3 terrainSize;

        public Matrix4x4 vpMatrix;
        public Vector3 cameraPositionWS;
        public float cullPadding;

        public bool hasWind;
        public float windScale;
        public float windSpeed;
        public float windRotateAmount;
        public float windStrength;
    }

    private class DrawPassData
    {
        public Material material;
        public BufferHandle argsBuffer;
        public GraphicsBuffer cullingBuffer;
        public GraphicsBuffer singleGrassBuffer;
        public int grassNum;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (m_CurrentTerrain == null || m_HeightTexture == null)
            return;

        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

        m_CullingBuffer.name = "Grass_CullingOutput_Buffer";
        m_ArgsBuffer.name = "Grass_Args_Buffer";

        BufferHandle cullHandle = renderGraph.ImportBuffer(m_CullingBuffer);
        BufferHandle argsHandle = renderGraph.ImportBuffer(m_ArgsBuffer);

        TerrainData terrainData = m_CurrentTerrain.terrainData;
        Vector3 terrainPos = m_CurrentTerrain.transform.position;
        Vector3 terrainSize = terrainData.size;

        if (!m_Feature.ClustersInitialized)
            m_Feature.InitializeClusters(terrainSize, terrainPos);

        Camera camera = Camera.main;
        if (camera != null)
            m_Feature.UpdateVisibleClusters(camera);

        TextureHandle heightHandle = ImportTextureFromRTHandle(renderGraph, m_HeightRTHandle, m_HeightTexture);

        var windSettings = m_Settings.windSettings;
        bool hasWind = windSettings != null && windSettings.windTexture != null;
        TextureHandle windHandle = TextureHandle.nullHandle;
        if (hasWind)
        {
            UpdateOrCreateRTHandle(ref m_WindRTHandle, windSettings.windTexture, "Grass_WindTexture");
            windHandle = ImportTextureFromRTHandle(renderGraph, m_WindRTHandle, windSettings.windTexture);
        }

        using (var builder = renderGraph.AddComputePass<GrassPassData>("Generate/Cull Grass", out GrassPassData data))
        {
            data.cullingShader = m_Settings.cullingCompute;
            data.kernelHandle = m_Settings.cullingCompute.FindKernel("CSMain");
            data.argsBuffer = argsHandle;
            data.cullingBuffer = cullHandle;
            data.heightmap = heightHandle;
            data.windTexture = windHandle;

            Matrix4x4 proj = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(), false);
            Matrix4x4 view = cameraData.GetViewMatrix();
            data.vpMatrix = proj * view;
            data.cameraPositionWS = cameraData.worldSpaceCameraPos;
            data.cullPadding = 0.5f;

            data.terrainPosition = terrainPos;
            data.terrainSize = terrainSize;

            data.hasWind = hasWind;
            if (hasWind)
            {
                data.windScale = windSettings.windScale;
                data.windSpeed = windSettings.windSpeed;
                data.windRotateAmount = windSettings.windRotateAmount;
                data.windStrength = windSettings.windStrength;
            }

            builder.UseTexture(data.heightmap, AccessFlags.Read);
            if (hasWind)
                builder.UseTexture(data.windTexture, AccessFlags.Read);

            builder.UseBuffer(data.argsBuffer, AccessFlags.Write);
            builder.UseBuffer(data.cullingBuffer, AccessFlags.Write);
            builder.SetRenderFunc((GrassPassData passData, ComputeGraphContext context) => ExecuteGrassPassData(passData, context));
        }

        using (var builder = renderGraph.AddRasterRenderPass<DrawPassData>("Draw Grass", out DrawPassData data))
        {
            data.argsBuffer = argsHandle;
            data.material = m_Settings.grassMaterial;
            data.grassNum = m_Settings.grassCount;
            data.cullingBuffer = m_CullingBuffer;
            data.singleGrassBuffer = m_SingleGrassBuffer;

            builder.AllowGlobalStateModification(true);
            builder.UseBuffer(cullHandle, AccessFlags.Read);
            builder.UseBuffer(argsHandle, AccessFlags.Read);
            builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
            builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.ReadWrite);
            builder.SetRenderFunc((DrawPassData passData, RasterGraphContext context) => ExecuteDrawPassData(passData, context));
        }
    }

    private void ExecuteGrassPassData(GrassPassData passData, ComputeGraphContext context)
    {
        var cmd = context.cmd;
        ComputeShader cs = passData.cullingShader;
        int kernel = passData.kernelHandle;

        int dispatchClusterCount = m_Feature.GetDispatchClusterCount();
        if (dispatchClusterCount <= 0)
        {
            cmd.SetBufferCounterValue(passData.cullingBuffer, 0);
            cmd.CopyCounterValue(passData.cullingBuffer, passData.argsBuffer, 4);
            return;
        }

        GraphicsBuffer dispatchClustersBuffer = m_Feature.GetDispatchClustersBuffer();
        if (dispatchClustersBuffer == null)
            return;

        cmd.SetComputeBufferParam(cs, kernel, "_ClumpParams", m_ClumpBuffer);
        cmd.SetComputeIntParam(cs, "_ClumpCount", m_ClumpBuffer.count);

        cmd.SetComputeBufferParam(cs, kernel, "_DispatchClusters", dispatchClustersBuffer);
        cmd.SetComputeIntParam(cs, "_DispatchClusterCount", dispatchClusterCount);

        cmd.SetBufferCounterValue(passData.cullingBuffer, 0);

        cmd.SetComputeTextureParam(cs, kernel, "_TerrainHeightMap", passData.heightmap);

        cmd.SetComputeBufferParam(cs, kernel, "cullingOutputBuffer", passData.cullingBuffer);
        cmd.SetComputeVectorParam(cs, "_TerrainPosition", passData.terrainPosition);
        cmd.SetComputeVectorParam(cs, "_TerrainSize", passData.terrainSize);
        cmd.SetComputeMatrixParam(cs, "_VPMatrix", passData.vpMatrix);
        cmd.SetComputeVectorParam(cs, "_CameraPositionWS", passData.cameraPositionWS);
        cmd.SetComputeFloatParam(cs, "_CullPadding", passData.cullPadding);

        cmd.SetComputeFloatParam(cs, "_DensityFadeStart", m_Settings.densityFadeStart);
        cmd.SetComputeFloatParam(cs, "_DensityFadeEnd", m_Settings.densityFadeEnd);
        cmd.SetComputeFloatParam(cs, "_MinDensity", m_Settings.minDensity);

        // Wind settings
        if (passData.hasWind)
        {
            cmd.SetComputeFloatParam(cs, "_LocalWindScale", passData.windScale);
            cmd.SetComputeFloatParam(cs, "_LocalWindSpeed", passData.windSpeed);
            cmd.SetComputeFloatParam(cs, "_LocalWindRotateAmount", passData.windRotateAmount);
            cmd.SetComputeFloatParam(cs, "_LocalWindStrength", passData.windStrength);
            cmd.SetComputeVectorParam(cs, "_Time", Shader.GetGlobalVector("_Time"));
            cmd.SetComputeTextureParam(cs, kernel, "_LocalWindTex", passData.windTexture);
        }

        cmd.DispatchCompute(cs, kernel, dispatchClusterCount, 1, 1);
        cmd.CopyCounterValue(passData.cullingBuffer, passData.argsBuffer, 4);
    }

    private void ExecuteDrawPassData(DrawPassData passData, RasterGraphContext context)
    {
        var cmd = context.cmd;
        m_PropertyBlock.Clear();

        if (passData.cullingBuffer != null)
            m_PropertyBlock.SetBuffer("cullingOutputBuffer", passData.cullingBuffer);

        if (passData.singleGrassBuffer != null)
            m_PropertyBlock.SetBuffer("SingleGrassBuffer", passData.singleGrassBuffer);

        m_PropertyBlock.SetInt("grassNum", passData.grassNum);

        // 弯曲控制参数
        m_PropertyBlock.SetFloat("_P1Weight", m_Settings.p1Weight);
        m_PropertyBlock.SetFloat("_P2Weight", m_Settings.p2Weight);
        m_PropertyBlock.SetFloat("_P3Weight", m_Settings.p3Weight);
        m_PropertyBlock.SetFloat("_StiffnessCurve", m_Settings.stiffnessCurve);
        m_PropertyBlock.SetFloat("_TipBias", m_Settings.tipBias);
        m_PropertyBlock.SetFloat("_SwayFrequency", m_Settings.swayFrequency);

        cmd.DrawProceduralIndirect(
            Matrix4x4.identity,
            passData.material,
            0,
            MeshTopology.Triangles,
            passData.argsBuffer,
            0,
            m_PropertyBlock);
    }
}
