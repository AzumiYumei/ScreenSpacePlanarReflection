using System;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace UnityEngine.Experiemntal.Rendering.Universal
{
    /// <summary>
    /// 屏幕空间平面反射后处理参数结构体
    /// </summary>
    [Serializable]
    public class SSPRSettings
    {
        // [SerializeField]
        // [Tooltip("是否在编辑器中启用")]
        // internal bool EditorActive = false; // 是否在编辑器中启用
        [SerializeField]
        [Tooltip("设置渲染顺序")]
        internal RenderPassEvent evt = RenderPassEvent.AfterRenderingOpaques;
        [Header("屏幕空间平面反射")]
        [SerializeField]
        [Tooltip("反射图像纹理尺寸，与反射清晰度成正比")]
        internal int RTSize = 512; // 反射纹理尺寸
        [SerializeField]
        [Tooltip("平面反射的起始高度")]
        internal float ReflectHeight = 0.2f; // 反射平面高度
        [SerializeField]
        [Tooltip("SSPR屏幕边缘反射淡出效果阈值，数值越大淡出效果越明显")]
        [Range(0.0f, 5.0f)]
        internal float m_EdgeFadeOutMaxValue = 0.6f; // 边缘渐出最大值
        [SerializeField]
        [Tooltip("SSPR根据世界空间Y轴高度的阈值来控制渐出效果")]
        [Range(0.0f, 4.0f)]
        internal float m_WorldPosFadeOutHeight = 1.0f; // 边缘渐出最大值


        [Header("UV拉伸")]
        [SerializeField]
        [Tooltip("SSPR屏幕边缘拉伸阈值")]
        [Range(0.0f, 1.0f)]
        internal float m_StretchThreshold = 1.0f;
        [SerializeField]
        [Tooltip("SSPR屏幕边缘拉伸强度")]
        [Range(0.0f, 1.0f)]
        internal float m_StretchIntensity = 1.0f;


        [Header("模糊")]
        [SerializeField]
        [Tooltip("模糊迭代次数")]
        [Range(0, 5)]
        internal int m_Iteration = 1;
        [SerializeField]
        [Tooltip("模糊半径")]
        internal float m_BlurRadius = 1;
        [SerializeField]
        [Tooltip("SSPR使用的ComputeShader")]
        internal ComputeShader m_ComputeShader;
        [SerializeField]
        [Tooltip("模糊使用的Shader")]
        internal Shader m_SSPRShader;
    }

    /// <summary>
    /// 屏幕空间平面反射后处理
    /// </summary>
    [DisallowMultipleRendererFeature("Azumi SSPR Renderer Feature")]
    public class SSPRRendererFeature : ScriptableRendererFeature
    {
        [SerializeField]
        private SSPRSettings m_SSPRSettings = new SSPRSettings(); // SSPR设置
        private SSPRPass m_SSPRPass; // SSPR渲染通道

        // 创建渲染特征
        public override void Create()
        {
            if (this.isActive)
            {
                // DebugTool.Log("SSPR RenderFeature Create");
                if (m_SSPRPass != null)
                {
                    m_SSPRPass.Dispose();
                }

                if (m_SSPRSettings.m_SSPRShader == null)
                {
                    m_SSPRSettings.m_SSPRShader = Shader.Find("AZUMI/RenderFeature/SSPR/GaussianBlur99");
                }

                if (m_SSPRSettings.m_ComputeShader == null)
                {
                    Debug.LogWarning("SSPR missing compute shader ");
                    return;
                }

                m_SSPRPass = new SSPRPass(ref m_SSPRSettings);

            }
            else//在这里设置一个Dispose，防止关闭feature内存泄漏
            {
                m_SSPRPass?.Dispose();
                m_SSPRPass = null;
            }
        }

        // 添加渲染通道
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (RenderRequire(renderingData.cameraData))
            {
                renderer.EnqueuePass(m_SSPRPass);
            }
        }

        // 设置渲染通道
        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            if (RenderRequire(renderingData.cameraData))
            {
                m_SSPRPass.ConfigureInput(ScriptableRenderPassInput.Color);
                m_SSPRPass.SetTarget(renderingData);
            }
        }

        // 释放资源
        protected override void Dispose(bool disposing)
        {
            m_SSPRPass?.Dispose();
            m_SSPRPass = null;
        }


        private bool RenderRequire(CameraData cameraData)
        {
            return this.isActive/* && cameraData.camera != null && (cameraData.cameraType == CameraType.SceneView || cameraData.cameraType == CameraType.Game)*/;
        }
    }
}