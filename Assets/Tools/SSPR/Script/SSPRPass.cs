using System;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Experiemntal.Rendering.Universal
{
    // 定义一个继承自ScriptableRenderPass的类SSPRPass，用于实现屏幕空间平面反射（SSPR）后处理效果。
    public class SSPRPass : ScriptableRenderPass
    {
        // 成员变量声明
        private SSPRSettings m_SSPRSettings; // 存储SSPR设置

        private ComputeShader m_ComputeShader; // 计算着色器引用
        private Material m_BlurMaterial;

        private const int GroupThreadX = 8;
        private const int GroupThreadY = 8;
        private int GroupX;
        private int GroupY;
        private int m_SSPRKernelID; // SSPR计算着色器内核ID
        private int m_FillHoleKernelID; // FillHole计算着色器内核ID
        private const string m_SSPRKernelName = "SSPR"; // SSPR内核名称
        private const string m_FillHoleKernelName = "FillHole"; // FillHole内核名称

        private readonly ProfilingSampler m_ProfilingSampler = new ProfilingSampler("SSPR"); // 性能分析采样器
        private static readonly int m_ReflectPlaneHeightID = Shader.PropertyToID("_ReflectPlaneHeight"); // 反射平面高度属性ID
        private static readonly int m_WorldPosFadeOutHeightID = Shader.PropertyToID("_WorldPosFadeOutHeight");
        private static readonly int m_EnableWorldPosFadeOutID = Shader.PropertyToID("_EnableWorldPosFadeOut");
        private static readonly int m_StretchThresholdID = Shader.PropertyToID("_Threshold");
        private static readonly int m_StretchIntensityID = Shader.PropertyToID("_Intensity");
        private static readonly int m_RTSizeID = Shader.PropertyToID("_SSPRReflectionSize"); // 反射纹理尺寸属性ID
        private static readonly int m_SSPRReflectionTextureID = Shader.PropertyToID("_SSPRReflectionTexture");
        private static readonly int m_CameraColorTextureID = Shader.PropertyToID("_CameraColorTexture"); // 相机颜色纹理属性ID
        private static readonly int m_CameraDepthTextureID = Shader.PropertyToID("_CameraDepthTexture"); // 相机深度纹理属性ID
        private static readonly int m_SSPRHeightBufferID = Shader.PropertyToID("_SSPRHeightBuffer"); // 高度缓冲区属性ID
        private static readonly int m_CameraDirectionID = Shader.PropertyToID("_CameraDirection"); // 相机方向属性ID
        private static readonly int m_EdgeFadeOutMaxValueID = Shader.PropertyToID("_EdgeFadeOutMaxValue"); // 边缘渐出最大值属性ID
        private static readonly int m_BlurRadiusID = Shader.PropertyToID("_BlurRadius");
        private static readonly int m_BlurHeightScaleID = Shader.PropertyToID("_BlurHeightScale");

        private const string m_SSPRReflectionTextureName = "_SSPRReflectionTexture"; // 反射纹理名称
        private const string m_SSPRHeightTextureName = "_SSPRHeightBufferTexture";
        private const string m_TemporaryTextureDownName = "_SSPRDownRTexture";
        private const string m_TemporaryTextureUpName = "_SSPRUpRTexture";

        private RenderTextureDescriptor m_RenderTextureDescriptor; // 渲染纹理描述符
        private RTHandle m_CameraColorTexture; // 相机颜色纹理句柄
        private RTHandle m_CameraDepthTexture; // 相机深度纹理句柄
        private RTHandle m_SSPRHeightTexture; // 高度缓冲区纹理句柄
        private RTHandle m_SSPRReflectionTexture;

        private RTHandle[] m_TemporaryTextureDown = Array.Empty<RTHandle>();
        private RTHandle[] m_TemporaryTextureUp = Array.Empty<RTHandle>();

        // 构造函数初始化
        public SSPRPass(SSPRSettings settings)
        {
            UpdateSettings(settings);
            // 在通道入队前声明输入，让渲染器提前准备颜色与深度。
            ConfigureInput(ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth);
        }

        public void UpdateSettings(SSPRSettings settings)
        {
            // 编辑器反序列化可能替换设置对象，不能只持有旧引用。
            m_SSPRSettings = settings;
            renderPassEvent = settings.evt;
            m_ComputeShader = settings.m_ComputeShader;
            // 只有模糊着色器变化时才重建材质，纹理继续复用。
            if (m_BlurMaterial == null || m_BlurMaterial.shader != settings.m_SSPRShader)
            {
                CoreUtils.Destroy(m_BlurMaterial);
                m_BlurMaterial = CoreUtils.CreateEngineMaterial(settings.m_SSPRShader);
            }
        }

        // 设置目标和计算着色器
        public void SetTarget(RenderingData renderingData)
        {
            //获取RenderFeature传递的相机颜色和深度纹理句柄
            ScriptableRenderer renderer = renderingData.cameraData.renderer;
            m_CameraColorTexture = renderer.cameraColorTargetHandle;
            m_CameraDepthTexture = renderer.cameraDepthTargetHandle;

            // 在获取相机目标后计算线程组数量。
            float aspect = (float)Screen.height / (float)Screen.width; // 计算宽高比
            GroupY = Mathf.RoundToInt(Mathf.Max(2, (float)m_SSPRSettings.RTSize / (float)GroupThreadY));
            GroupX = Mathf.RoundToInt(Mathf.Max(2, (float)GroupY / aspect));
        }

        // 执行计算着色器
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!RenderRequire(renderingData.cameraData))
            {
                return;
            }

            if (m_ComputeShader == null)
            {
                Debug.LogWarning("SSPR missing compute shader ");
                return;
            }

            if (m_BlurMaterial == null)
            {
                Debug.LogWarning("SSPR missing shader");
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            try
            {
                using (new ProfilingScope(cmd, m_ProfilingSampler))
                {
                    SetupSSPR(cmd, ref renderingData);
                    SetupBlur(cmd);
                    SetupGlobal(cmd);
                }

                context.ExecuteCommandBuffer(cmd);
            }
            finally
            {
                cmd.Clear();
                CommandBufferPool.Release(cmd);
            }
        }

        // 设置计算着色器并分发线程组
        private void SetupSSPR(CommandBuffer cmd, ref RenderingData renderingData)
        {
            // 分配RTHandle
            m_RenderTextureDescriptor = new RenderTextureDescriptor(GroupThreadX * GroupX, GroupThreadY * GroupY, RenderTextureFormat.ARGB32, 0, 0);
            m_RenderTextureDescriptor.enableRandomWrite = true; // 开启UAV随机读写
            RenderingUtils.ReAllocateIfNeeded(ref m_SSPRReflectionTexture, m_RenderTextureDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: m_SSPRReflectionTextureName);

            // 高度缓冲只需要一个整数通道。
            m_RenderTextureDescriptor.colorFormat = RenderTextureFormat.RInt;
            RenderingUtils.ReAllocateIfNeeded(ref m_SSPRHeightTexture, m_RenderTextureDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: m_SSPRHeightTextureName);

            // 设置ComputeShader属性
            m_SSPRKernelID = m_ComputeShader.FindKernel(m_SSPRKernelName);
            m_ComputeShader.SetFloat(m_ReflectPlaneHeightID, m_SSPRSettings.ReflectHeight);
            m_ComputeShader.SetFloat(m_WorldPosFadeOutHeightID, m_SSPRSettings.m_WorldPosFadeOutHeight);
            m_ComputeShader.SetFloat(m_EnableWorldPosFadeOutID, m_SSPRSettings.m_EnableWorldPosFadeOut ? 1.0f : 0.0f);
            m_ComputeShader.SetVector(m_RTSizeID, new Vector4(m_RenderTextureDescriptor.width, m_RenderTextureDescriptor.height, 1.0f / (float)m_RenderTextureDescriptor.width, 1.0f / (float)m_RenderTextureDescriptor.height));
            m_ComputeShader.SetTexture(m_SSPRKernelID, m_SSPRReflectionTextureID, m_SSPRReflectionTexture);
            m_ComputeShader.SetTexture(m_SSPRKernelID, m_SSPRHeightBufferID, m_SSPRHeightTexture);
            m_ComputeShader.SetTexture(m_SSPRKernelID, m_CameraColorTextureID, m_CameraColorTexture);
            m_ComputeShader.SetTexture(m_SSPRKernelID, m_CameraDepthTextureID, m_CameraDepthTexture);
            m_ComputeShader.SetVector(m_CameraDirectionID, renderingData.cameraData.camera.transform.forward);
            m_ComputeShader.SetFloat(m_StretchThresholdID, m_SSPRSettings.m_StretchThreshold);
            m_ComputeShader.SetFloat(m_StretchIntensityID, m_SSPRSettings.m_StretchIntensity);
            m_ComputeShader.SetFloat(m_EdgeFadeOutMaxValueID, m_SSPRSettings.m_EdgeFadeOutMaxValue);

            m_FillHoleKernelID = m_ComputeShader.FindKernel(m_FillHoleKernelName);
            m_ComputeShader.SetTexture(m_FillHoleKernelID, m_SSPRReflectionTextureID, m_SSPRReflectionTexture);
            m_ComputeShader.SetTexture(m_FillHoleKernelID, m_SSPRHeightBufferID, m_SSPRHeightTexture);

            // 单独清理，避免反射写入与其他线程的清理操作互相覆盖。
            int clearKernelID = m_ComputeShader.FindKernel("Clear");
            m_ComputeShader.SetTexture(clearKernelID, m_SSPRReflectionTextureID, m_SSPRReflectionTexture);
            m_ComputeShader.SetTexture(clearKernelID, m_SSPRHeightBufferID, m_SSPRHeightTexture);
            cmd.DispatchCompute(m_ComputeShader, clearKernelID, GroupX, GroupY, 1);

            //开始计算
            cmd.DispatchCompute(m_ComputeShader, m_SSPRKernelID, GroupX, GroupY, 1);
            cmd.DispatchCompute(m_ComputeShader, m_FillHoleKernelID, GroupX / 2, GroupY / 2, 1);
        }

        private void SetupBlur(CommandBuffer cmd)
        {
            // 同步运行时模糊层级，缩减时立即释放多余纹理。
            ResizeBlurTextures(Mathf.Max(0, m_SSPRSettings.m_Iteration));
            if (m_SSPRSettings.m_Iteration < 1)
            {
                return;
            }

            //开始模糊
            m_BlurMaterial.SetFloat(m_BlurRadiusID, m_SSPRSettings.m_BlurRadius);
            m_BlurMaterial.SetFloat(m_BlurHeightScaleID, m_SSPRSettings.m_BlurHeightScale);
            // 两次模糊共用原始高度图，避免高度随颜色一起被模糊。
            m_BlurMaterial.SetTexture(m_SSPRHeightBufferID, m_SSPRHeightTexture.rt);
            m_BlurMaterial.SetFloat(m_ReflectPlaneHeightID, m_SSPRSettings.ReflectHeight);

            RenderTextureDescriptor desc = new RenderTextureDescriptor(GroupThreadX * GroupX, GroupThreadY * GroupY, RenderTextureFormat.BGRA32, 0, 0);

            for (int i = 0; i < m_SSPRSettings.m_Iteration; i++)
            {
                RenderingUtils.ReAllocateIfNeeded(ref m_TemporaryTextureUp[i], desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: m_TemporaryTextureUpName + i);

                desc.width = Mathf.Max(1, desc.width >> 1);
                desc.height = Mathf.Max(1, desc.height >> 1);

                RenderingUtils.ReAllocateIfNeeded(ref m_TemporaryTextureDown[i], desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: m_TemporaryTextureDownName + i);
            }

            //创建RTHandle作为临时指针
            var temporaryTexture = m_SSPRReflectionTexture;
            for (int i = 0; i < m_SSPRSettings.m_Iteration; i++)
            {
                Blitter.BlitCameraTexture(cmd, temporaryTexture, m_TemporaryTextureDown[i], m_BlurMaterial, 0);
                temporaryTexture = m_TemporaryTextureDown[i];
            }

            for (int i = m_SSPRSettings.m_Iteration - 1; i >= 0; i--)
            {
                Blitter.BlitCameraTexture(cmd, temporaryTexture, m_TemporaryTextureUp[i], m_BlurMaterial, 1);
                temporaryTexture = m_TemporaryTextureUp[i];
            }

            Blitter.BlitCameraTexture(cmd, temporaryTexture, m_SSPRReflectionTexture);
        }

        private void SetupGlobal(CommandBuffer cmd)
        {
            cmd.SetGlobalTexture(m_SSPRReflectionTextureID, m_SSPRReflectionTexture);
        }

        private void ResizeBlurTextures(int size)
        {
            if (m_TemporaryTextureDown.Length == size)
            {
                return;
            }

            for (int i = size; i < m_TemporaryTextureDown.Length; i++)
            {
                m_TemporaryTextureDown[i]?.Release();
                m_TemporaryTextureUp[i]?.Release();
            }

            Array.Resize(ref m_TemporaryTextureDown, size);
            Array.Resize(ref m_TemporaryTextureUp, size);
        }

        // 仅清除仍指向当前通道资源的全局绑定，避免影响其他实例。
        private static void ClearGlobalTexture(int propertyID, RTHandle handle)
        {
            if (handle != null && handle.rt != null && Shader.GetGlobalTexture(propertyID) == handle.rt)
            {
                Shader.SetGlobalTexture(propertyID, Texture2D.blackTexture);
            }
        }

        // 释放资源
        public void Dispose()
        {
            CoreUtils.Destroy(m_BlurMaterial);
            m_BlurMaterial = null;

            ClearGlobalTexture(m_SSPRReflectionTextureID, m_SSPRReflectionTexture);
            ResizeBlurTextures(0);
            m_SSPRReflectionTexture?.Release();
            m_SSPRHeightTexture?.Release();
            m_SSPRReflectionTexture = null;
            m_SSPRHeightTexture = null;

            m_CameraColorTexture = null;
            m_CameraDepthTexture = null;
        }

        private bool RenderRequire(CameraData cameraData)
        {
            return cameraData.camera != null && (cameraData.cameraType == CameraType.SceneView || cameraData.cameraType == CameraType.Game);
        }
    }
}
