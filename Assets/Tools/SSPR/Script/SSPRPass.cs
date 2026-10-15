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

        private int GroupThreadX = 8;
        private int GroupThreadY = 8;
        private int GroupX;
        private int GroupY;
        private int m_SSPRKernelID; // SSPR计算着色器内核ID
        private int m_FillHoleKernelID; // FillHole计算着色器内核ID
        private string m_SSPRKernelName = "SSPR"; // SSPR内核名称
        private string m_FillHoleKernelName = "FillHole"; // FillHole内核名称

        private ProfilingSampler m_ProfilingSampler = new ProfilingSampler("SSPR"); // 性能分析采样器
        private static readonly int m_ReflectPlaneHeightID = Shader.PropertyToID("_ReflectPlaneHeight"); // 反射平面高度属性ID
        private static readonly int m__WorldPosFadeOutHeightID = Shader.PropertyToID("_WorldPosFadeOutHeight");
        private static readonly int m_StretchThresholdID = Shader.PropertyToID("_Threshold");
        private static readonly int m_StretchIntensityID = Shader.PropertyToID("_Intensity");
        private static readonly int m_RTSizeID = Shader.PropertyToID("_SSPRReflectionSize"); // 反射纹理尺寸属性ID
        private static readonly int m_SSPRReflectionTextureID = Shader.PropertyToID("_SSPRReflectionTexture"); // 反射纹理属性ID
        private static readonly int m_SSPRReflectionBlurTextureID = Shader.PropertyToID("_SSPRReflectionBlurTexture"); // 反射纹理属性ID
        private static readonly int m_SSPRReflectionBlurmIteration = Shader.PropertyToID("_SSPRReflectionBlurIteration"); // 反射纹理属性ID
        private static readonly int m_CameraColorTextureID = Shader.PropertyToID("_CameraColorTexture"); // 相机颜色纹理属性ID
        private static readonly int m_CameraDepthTextureID = Shader.PropertyToID("_CameraDepthTexture"); // 相机深度纹理属性ID
        private static readonly int m_SSPRHeightBufferID = Shader.PropertyToID("_SSPRHeightBuffer"); // 高度缓冲区属性ID
        private static readonly int m_CameraDirectionID = Shader.PropertyToID("_CameraDirection"); // 相机方向属性ID
        private static readonly int m_EdgeFadeOutMaxValueID = Shader.PropertyToID("_EdgeFadeOutMaxValue"); // 边缘渐出最大值属性ID
        private static int[] m_TemporaryTextureDownID; // 临时降采样纹理属性ID
        private static int[] m_TemporaryTextureUpID; // 临时升采样纹理属性ID
        private static int m_BlurRadiusID = Shader.PropertyToID("_BlurRadius");

        private const string m_SSPRReflectionTextureName = "_SSPRReflectionTexture"; // 反射纹理名称
        private const string m_SSPRHeightTextureName = "_SSPRHeightBufferTexture"; // 高度缓冲区纹理名称
        private const string m_TemporaryTextureDownName = "_SSPRDownRTexture"; // 高度缓冲区纹理名称
        private const string m_TemporaryTextureUpName = "_SSPRUpRTexture"; // 高度缓冲区纹理名称


        private RenderTextureDescriptor m_RenderTextureDescriptor; // 渲染纹理描述符
        private RTHandle m_CameraColorTexture; // 相机颜色纹理句柄
        private RTHandle m_CameraDepthTexture; // 相机深度纹理句柄
        private RTHandle m_SSPRHeightTexture; // 高度缓冲区纹理句柄
        private RTHandle m_SSPRReflectionTexture; // 反射纹理句柄

        private RTHandle[] m_TemporaryTextureDown; // 反射纹理句柄
        private RTHandle[] m_TemporaryTextureUp; // 反射纹理句柄


        // 构造函数初始化
        public SSPRPass(ref SSPRSettings sSPRSettings)
        {
            // DebugTool.Log("SSPR Pass SSPRPass");
            //获取RenderFeature的数据
            m_SSPRSettings = sSPRSettings;

            //设置computeShader与blurShader
            m_ComputeShader = m_SSPRSettings.m_ComputeShader;
            m_BlurMaterial = CoreUtils.CreateEngineMaterial(m_SSPRSettings.m_SSPRShader);

            //设置渲染顺序
            renderPassEvent = m_SSPRSettings.evt;


            //设置SSPR临时RT
            m_SSPRHeightTexture = RTHandles.Alloc(m_SSPRHeightBufferID, name: m_SSPRHeightTextureName);
            m_SSPRReflectionTexture = RTHandles.Alloc(m_SSPRReflectionTextureID, name: m_SSPRReflectionTextureName);

            //设置blur shader临时RT
            int size = m_SSPRSettings.m_Iteration;

            m_TemporaryTextureDownID = new int[size];
            m_TemporaryTextureUpID = new int[size];

            m_TemporaryTextureDown = new RTHandle[size];
            m_TemporaryTextureUp = new RTHandle[size];
            for (int i = 0; i < size; i++)
            {
                m_TemporaryTextureDownID[i] = Shader.PropertyToID(m_TemporaryTextureDownName + i);
                m_TemporaryTextureUpID[i] = Shader.PropertyToID(m_TemporaryTextureUpName + i);

                m_TemporaryTextureDown[i] = RTHandles.Alloc(m_TemporaryTextureDownID[i], name: m_TemporaryTextureDownName + i);
                m_TemporaryTextureUp[i] = RTHandles.Alloc(m_TemporaryTextureUpID[i], name: m_TemporaryTextureUpName + i);
            }
        }

        // 设置目标和计算着色器
        public void SetTarget(RenderingData renderingData)
        {
            // DebugTool.Log("SSPR Pass SetTarget");
            //获取RenderFeature传递的相机颜色和深度纹理句柄
            ScriptableRenderer renderer = renderingData.cameraData.renderer;
            m_CameraColorTexture = renderer.cameraColorTargetHandle;
            m_CameraDepthTexture = renderer.cameraDepthTargetHandle;

            //如果在构造函数中计算这个，会导致计算出错，可能是Screen获取宽高在第一帧出错的问题
            //设置compute shader线程参数
            GroupThreadX = 8;
            GroupThreadY = 8;
            //计算Compute Shader所需的线程数量
            float aspect = (float)Screen.height / (float)Screen.width; // 计算宽高比
            GroupY = Mathf.RoundToInt(Mathf.Max(2, (float)m_SSPRSettings.RTSize / (float)GroupThreadY));
            GroupX = Mathf.RoundToInt(Mathf.Max(2, (float)GroupY / aspect));
        }

        // // 在摄像机设置时配置目标和清除
        // public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        // {
        //     // DebugTool.Log("SSPR Pass OnCameraSetup");
        //     ConfigureTarget(renderingData.cameraData.renderer.cameraColorTargetHandle); // 配置目标为相机颜色纹理
        //     ConfigureClear(ClearFlag.None, Color.white); // 不清除任何内容，背景为白色
        // }

        // 执行计算着色器
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // DebugTool.Log("SSPR Pass Execute");
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
            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                SetupSSPR(cmd, ref renderingData);
                SetupBlur(cmd, ref renderingData);
                SetupGlobal(cmd, ref renderingData);
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        // 设置计算着色器并分发线程组
        private void SetupSSPR(CommandBuffer cmd, ref RenderingData renderingData)
        {
            // DebugTool.Log("SSPR Pass SetupSSPR");
            // 分配RTHandle
            m_RenderTextureDescriptor = new RenderTextureDescriptor(GroupThreadX * GroupX, GroupThreadY * GroupY, RenderTextureFormat.BGRA32, 0, 0);
            m_RenderTextureDescriptor.enableRandomWrite = true; // 开启UAV随机读写
            RenderingUtils.ReAllocateIfNeeded(ref m_SSPRReflectionTexture, m_RenderTextureDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: m_SSPRReflectionTexture.name);

            // 只要r channel
            m_RenderTextureDescriptor.colorFormat = RenderTextureFormat.RFloat;
            RenderingUtils.ReAllocateIfNeeded(ref m_SSPRHeightTexture, m_RenderTextureDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: m_SSPRHeightTexture.name);

            // 设置ComputeShader属性
            m_SSPRKernelID = m_ComputeShader.FindKernel(m_SSPRKernelName);
            m_ComputeShader.SetFloat(m_ReflectPlaneHeightID, m_SSPRSettings.ReflectHeight);
            m_ComputeShader.SetFloat(m__WorldPosFadeOutHeightID, m_SSPRSettings.m_WorldPosFadeOutHeight);
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


            //开始计算
            cmd.DispatchCompute(m_ComputeShader, m_SSPRKernelID, GroupX, GroupY, 1);
            cmd.DispatchCompute(m_ComputeShader, m_FillHoleKernelID, GroupX / 2, GroupY / 2, 1);
        }

        private void SetupBlur(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (m_SSPRSettings.m_Iteration < 1)
            {
                return;
            }

            //开始模糊
            m_BlurMaterial.SetFloat(m_BlurRadiusID, m_SSPRSettings.m_BlurRadius);

            RenderTextureDescriptor desc = new RenderTextureDescriptor(GroupThreadX * GroupX, GroupThreadY * GroupY, RenderTextureFormat.BGRA32, 0, 0);

            for (int i = 0; i < m_SSPRSettings.m_Iteration; i++)
            {
                RenderingUtils.ReAllocateIfNeeded(ref m_TemporaryTextureUp[i], desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: m_TemporaryTextureUp[i].name);

                desc.width = Mathf.Max(1, desc.width >> 1);
                desc.height = Mathf.Max(1, desc.height >> 1);

                RenderingUtils.ReAllocateIfNeeded(ref m_TemporaryTextureDown[i], desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: m_TemporaryTextureDown[i].name);

            }

            //创建RTHandle作为临时指针
            var m_TemporaryTexture = m_SSPRReflectionTexture;
            for (int i = 0; i < m_SSPRSettings.m_Iteration; i++)
            {
                Blitter.BlitCameraTexture(cmd, m_TemporaryTexture, m_TemporaryTextureDown[i], m_BlurMaterial, 0);
                m_TemporaryTexture = m_TemporaryTextureDown[i];
            }

            for (int i = m_SSPRSettings.m_Iteration - 1; i >= 0; i--)
            {
                Blitter.BlitCameraTexture(cmd, m_TemporaryTexture, m_TemporaryTextureUp[i], m_BlurMaterial, 1);
                m_TemporaryTexture = m_TemporaryTextureUp[i];
            }

            // Blitter.BlitCameraTexture(cmd, m_TemporaryTexture, m_SSPRReflectionTexture);
            cmd.SetGlobalTexture(m_SSPRReflectionBlurTextureID, m_TemporaryTexture);
        }

        private void SetupGlobal(CommandBuffer cmd, ref RenderingData renderingData)
        {
            cmd.SetGlobalFloat(m_SSPRReflectionBlurmIteration, m_SSPRSettings.m_Iteration);
            cmd.SetGlobalTexture(m_SSPRReflectionTextureID, m_SSPRReflectionTexture);
            cmd.SetGlobalVector(m_RTSizeID, new Vector4(m_RenderTextureDescriptor.width, m_RenderTextureDescriptor.height, 1.0f / (float)m_RenderTextureDescriptor.width, 1.0f / (float)m_RenderTextureDescriptor.height));
        }

        // // 清理摄像机资源
        // public override void OnCameraCleanup(CommandBuffer cmd)
        // {

        // }

        // 释放资源
        public void Dispose()
        {
            // DebugTool.Log("SSPR Pass Dispose");
            CoreUtils.Destroy(m_BlurMaterial);

            foreach (var handle in m_TemporaryTextureDown)
            {
                handle.Release();
            }
            foreach (var handle in m_TemporaryTextureUp)
            {
                handle.Release();
            }


            m_SSPRReflectionTexture.Release();
            m_SSPRHeightTexture.Release();

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