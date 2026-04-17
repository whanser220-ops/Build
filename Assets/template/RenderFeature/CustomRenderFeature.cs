using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;


public class CustomRenderFeature : ScriptableRendererFeature
{
    //定义暴露在面板上的参数，如材质，着色器，和Shaded相关的强度，比如模糊强度，一般用序列化的public的类，如BlueSetting类
    
    
    //声明renderPass类和声明序列化材质和Shader和Setting类
    private CustomRenderPass renderPass;
    
    //在RenderFeature第一次创建时调用，此方法通常用于使用构造函数实例化自定义的ScriptableRenderPass和指定何时执行渲染通道
    public override void Create()
    {
        renderPass = new CustomRenderPass();
        renderPass.renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
    }
    
    //Unity每帧为每个摄像机调用此方法，把RenderPass插入到渲染通道中
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        SetupRenderPasses(renderer, renderingData);
        renderer.EnqueuePass(renderPass);
    }
    
    //该方法将销毁 Renderer Feature 创建的材质实例
    protected override void Dispose(bool disposing)
    {
        if (Application.isPlaying)
        {
            //Destroy(material);
        }
        else
        {
            //DestroyImmediate(material);
        }
    }
}
