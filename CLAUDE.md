# CLAUDE.md

本文件为 Claude Code (claude.ai/code) 提供在本仓库中工作的指导。

## 项目概述

这是一个面向 **PC 端**的**风格化卡通**渲染实验项目，核心功能是基于 GPU 实例化的草地渲染系统与交互效果。TA 工作的核心职责是维护 URP 渲染管线、开发自定义 RenderFeature 以及图形性能优化。

## 核心技术栈与环境

- **游戏引擎：** Unity 6000.0.46f1 (Unity 6)
- **渲染管线：** URP (Universal Render Pipeline) 17.0.4 + RenderGraph API
- **主要语言：** C# (Gameplay/渲染逻辑) / HLSL (Shader) / Compute Shader
- **关键包：**
  - `com.unity.render-pipelines.universal` 17.0.4
  - `com.unity.burst` 1.8.19
  - `com.unity.mathematics` 1.3.2
  - `com.unity.terrain-tools` 5.2.1

## 全局规范 (AI 必须严格遵守)

### 1. 渲染与 Shader 规范

- **必须使用 URP RenderGraph API**：所有自定义渲染功能必须使用 `ScriptableRendererFeature` + `ScriptableRenderPass` + `RecordRenderGraph()` 模式
- **禁止使用 Built-in 管线语法**：如 `OnRenderImage`、`Graphics.DrawMesh` 等传统方式
- **Shader 精度规范**：
  - 优先使用 `half` 精度（移动端友好）
  - 仅在世界空间坐标、深度计算或地形采样时使用 `float`
- **顶点着色器纹理采样**：必须使用 `SAMPLE_TEXTURE2D_LOD`，禁止使用 `SAMPLE_TEXTURE2D`（Vertex stage 需要显式 LOD）
- **纹理命名约定**：
  - 高度图：`_TerrainHeightMap`
  - 法线图：`_TerrainNormalMap`
  - 交互 RT：`_GrassInteractRT` (RGHalf 格式)
  - 风场遮罩：`_WindMask`

### 2. 代码风格规范

- **缩进与编码**：4 空格缩进，UTF-8 编码
- **命名规范**：
  - 类型/方法/属性：`PascalCase`
  - 局部变量/参数：`camelCase`
  - 私有序列化字段：`_camelCase`
  - RenderFeature 文件：`{FeatureName}Feature.cs` + `{FeatureName}Pass.cs`
- **缓冲区命名**：
  - AppendBuffer：`cullingOutputBuffer`
  - ArgsBuffer：`argsBuffer` / `m_ArgsBuffer`
  - StructuredBuffer：`m_SingleGrassBuffer` / `m_ClumpBuffer`

### 3. Compute Shader 规范

- **Kernel 命名**：`CSMain`
- **线程组大小**：默认 64 (即 `Dispatch(threadGroups, 1, 1)`，其中 `threadGroups = ceil(count / 64.0)`)
- **计数器重置**：必须在 Dispatch 前执行 `cmd.SetBufferCounterValue(buffer, 0)`
- **缓冲区类型**：
  - 剔除输出：`AppendStructuredBuffer<T>`
  - 绘制参数：`GraphicsBuffer.Target.IndirectArguments`
  - 结构化数据：`GraphicsBuffer.Target.Structured`

### 4. RenderGraph 标准模式

```csharp
// 1. 定义 PassData 类
class MyPassData {
    public TextureHandle input;
    public BufferHandle argsBuffer;
    public Material material;
}

// 2. RecordRenderGraph
using (var builder = renderGraph.AddComputePass<MyPassData>("Pass Name", out MyPassData data))
{
    data.material = settings.material;
    builder.UseTexture(inputHandle, AccessFlags.Read);
    builder.UseBuffer(bufferHandle, AccessFlags.Write);
    builder.SetRenderFunc<MyPassData>((data, ctx) => Execute(data, ctx));
}

// 3. 执行函数
void Execute(MyPassData data, ComputeGraphContext ctx)
{
    ctx.cmd.DispatchCompute(...);
}
```

### 5. 资产命名公约

| 资产类型 | 命名格式 | 示例 |
|---------|---------|------|
| 贴图 | `T_<Name>_<Type>` | `T_Grass_01_BaseColor` |
| 材质 | `M_<Name>` / `MI_<Name>` | `M_Grass_Cartoon` |
| 材质实例 | `MI_<Name>_<Variant>` | `MI_Grass_Dry` |
| Shader | `S_<Feature>_<Type>` | `S_Grass_Cartoon` |
| Compute | `CS_<Feature>` | `CS_Grass_Culling` |
| RenderFeature | `<Feature>Feature` / `<Feature>Pass` | `GrassFeature.cs` / `GrassRenderPass.cs` |
| 场景 | `Scene_<Name>` | `Scene_GrassDemo` |

## 草地渲染系统架构


## 常用开发命令

```bash
# 在 Unity Editor 中打开
Unity.exe -projectPath .

# 批处理构建 (Windows)
Unity.exe -batchmode -projectPath . -quit -buildTarget Win64 -logFile Logs/build.log

# 运行测试
Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode -testResults Logs/EditMode.xml -quit
```

## 重要文件位置

| 组件 | 位置 |
|-----|------|
| 草地主 Shader | `Assets/Project/CS/Grass_cartoon/Shader/Grass_cartoon_shader.shader` |
| 草地计算 Shader | `Assets/Project/CS/Grass_cartoon/Shader/Grass_cartoon.compute` |
| RenderFeature | `Assets/Project/CS/Grass_cartoon/RenderGraph/GrassFeature.cs` |
| RenderPass | `Assets/Project/CS/Grass_cartoon/RenderGraph/GrassRenderPass.cs` |
| 地形提供器 | `Assets/Project/CS/Grass_cartoon/C#/GrassTerrianProvider.cs` |
| 示例场景 | `Assets/Scenes/SampleScene.unity` |
| URP 全局设置 | `Assets/Settings/Default/` |
