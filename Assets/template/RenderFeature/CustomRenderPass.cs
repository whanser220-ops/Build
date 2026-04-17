using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class CustomRenderPass : ScriptableRenderPass
{
    //添加设置、材质

    
    private RenderTextureDescriptor CustomTextureDescriptor;//您指定渲染纹理的属性，例如宽度、高度和格式。
    
    //使用以上设置和材质的RenderPass的构造函数。用this
    public CustomRenderPass()
    {
        
    }
    
    //添加用于和着色器属性交互的变量。 Shader.PropertyToID("_HorizontalBlur");private const string;
    
    
    
    //渲染图系统可以在渲染代码执行期间访问此数据结构
    private class  CustomPassData
    {
        
    }

    
    //更新着色器值的 UpdateBlurSettings 方法。
    private void UpdateCustomSettings()
    {
        //if (material == null) return;

        //material.SetFloat(horizontalBlurId, defaultSettings.horizontalBlur);
        //material.SetFloat(verticalBlurId, defaultSettings.verticalBlur);
    }

    //此方法清理任何手动管理的资源（例如，Material实例）
    
    //用RenderGraph的最重要的是告诉它我们要用哪些资源，这样RenderGraph才能为我们进行资源生命周期管理。
    //此过程包括声明渲染通道输入和输出，但不包括将命令添加到命令缓冲区。Unity 每帧调用此方法，每个摄像机调用一次。
    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        //base.RecordRenderGraph(renderGraph, frameData);这行代码会让Unity警告，最好不要添加。
        //UniversalResourceData 包含 URP 使用的所有纹理引用，包括摄像机的活动颜色和深度纹理。
        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
        //添加用于存储 UniversalCameraData 数据的变量
        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
        //声明 TextureHandle 字段以存储对输入和输出纹理的引用
        
        //添加持续更新材质中设置的函数。
        UpdateCustomSettings();
        using (var builder=renderGraph.AddRasterRenderPass<CustomPassData>(passName,out CustomPassData data))
        {
            //给passdata的数据填充  类成员变量的赋值
            
            //声明  只是声明，会在后续的渲染中使用到。
            //read   读 其实就是声明资源
            
            //write  写 其实就是设置渲染目标
            
            //process 
            builder.SetRenderFunc<CustomPassData>((CustomPassData data,RasterGraphContext context)=> ExecutePass(data,context));
        }
    }
    
    //通过分析效果所需要的资源。
    //把这些资源放在data中   赋值data。
    //用到的纹理和Buffer数据进行声明，以便RenderGraph管理生命周期，尤其是Texture。
    private void ExecutePass(CustomPassData data, RasterGraphContext context)
    {
        
    }
}
