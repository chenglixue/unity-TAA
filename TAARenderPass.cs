using System.Collections.Generic;

namespace UnityEngine.Rendering.Universal
{
    class JitterRenderPass : ScriptableRenderPass
    {
        #region Variable
        private TAA_RenderFeature.TAASettings m_taaSetting;
        private TAA_RenderFeature.TAAData m_taaData;
        #endregion
        
        #region Setup
        public JitterRenderPass(TAA_RenderFeature.TAASettings taaSetting)
        {
            m_taaSetting = taaSetting;
            renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
        }

        public void Setup(TAA_RenderFeature.TAAData taaData)
        {
            m_taaData = taaData;
        }
        #endregion
        
        #region Execute
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, new ProfilingSampler(m_taaSetting.m_profilerTags)))
            {
                cmd.SetViewProjectionMatrices(renderingData.cameraData.GetViewMatrix(), m_taaData.m_projJitter);
            }
            
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            
        }
        #endregion
    }
    
    class TAARenderPass : ScriptableRenderPass
    {
        #region Variable
        private TAA_RenderFeature.TAASettings m_taaSetting = null;
        private TAA_RenderFeature.TAAData m_taaData = null;
        private TAA_Volume m_taaVolume;
        private Material m_material = null;

        private RenderTextureDescriptor m_descriptor;
        private RenderTargetIdentifier m_cameraColorIden;
        private RenderTexture[] m_historyRTs;
        private int m_writeIndex = 0;
        #endregion
        
        #region Setup
        public TAARenderPass(TAA_RenderFeature.TAASettings taaSettings)
        {
            this.m_taaSetting = taaSettings;
        }

        public void Setup(TAA_RenderFeature.TAAData taaData, TAA_Volume taaVolume)
        {
            renderPassEvent = m_taaSetting.m_passEvent;
            
            if (m_taaSetting.m_shader == null)
            {
                Debug.LogError("Custom: Shader not found.");
                m_material = CoreUtils.CreateEngineMaterial("Elysia_TAA");
            }
            else
            {
                m_material = new Material(m_taaSetting.m_shader);
            }

            m_taaData = taaData;
            m_taaVolume = taaVolume;
        }
        
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var cameraData = renderingData.cameraData;
            m_cameraColorIden = cameraData.renderer.cameraColorTarget;
            m_descriptor = cameraData.cameraTargetDescriptor;
        }
        #endregion
        
        #region Execute
        Vector4[] GetClampuvOffset()
        {
            Vector4[] result = new Vector4[9];
            
            result[0] = new Vector2(-1, 1) ;
            result[1] = new Vector2(0, 1) ;
            result[2] = new Vector2(1, 1) ;
            result[3] = new Vector2(-1, 0) ;
            result[4] = new Vector2(0, 0) ;
            result[5] = new Vector2(1, 0) ;
            result[6] = new Vector2(-1, -1) ;
            result[7] = new Vector2(0, -1) ;
            result[8] = new Vector2(1, -1) ;

            return result;
        }
        Vector4 GetTexSize(float width, float height)
        {
            return new Vector4(width, height, 1f / width, 1f / height);
        }
        
        void InitArray<T>(ref T[] arrays, int size, T initValue = default(T))
        {
            if (arrays == null || arrays.Length != size)
            {
                arrays = new T[size];
                for (int i = 0; i < size; ++i)
                {
                    arrays[i] = initValue;
                }
            }
        }
        bool InitRT(ref RenderTexture RT, int width, int height, RenderTextureFormat texFormat, FilterMode filterMode, int depthBits = 0, int antiAliasing = 1)
        {
            if (RT != null && (RT.width != width || RT.height != height || RT.format != texFormat ||
                               RT.filterMode != filterMode || RT.antiAliasing != antiAliasing))
            {
                RenderTexture.ReleaseTemporary(RT);
                RT = null;
            }

            if (RT == null)
            {
                RT = RenderTexture.GetTemporary(width, height, depthBits, texFormat, RenderTextureReadWrite.Default, antiAliasing);
                RT.filterMode = filterMode;
                RT.wrapMode = TextureWrapMode.Clamp;
                return true;
            }

            return false;
        }

        void DoTAA(CommandBuffer cmd, CameraData cameraData)
        {
            var camera = cameraData.camera;
            var descriptor = new RenderTextureDescriptor(camera.scaledPixelWidth, camera.scaledPixelHeight, RenderTextureFormat.DefaultHDR, 16);
            
            InitArray(ref m_historyRTs, 2);
            InitRT(ref m_historyRTs[0], descriptor.width, descriptor.height, descriptor.colorFormat, FilterMode.Bilinear);
            InitRT(ref m_historyRTs[1], descriptor.width, descriptor.height, descriptor.colorFormat, FilterMode.Bilinear);

            int readIndex = m_writeIndex;
            m_writeIndex = (++m_writeIndex) % 2;
            
            var viewProjPre = m_taaData.m_projPre * m_taaData.m_viewPre;
            m_material.SetMatrix("_ViewProjPre", viewProjPre);
            m_material.SetVector("_JitterUVOffset", new Vector4(m_taaData.m_offset.x, m_taaData.m_offset.y, m_taaData.m_lastOffset.x, m_taaData.m_lastOffset.y));
            m_material.SetVector("_sourceTexSize", GetTexSize(m_descriptor.width, m_descriptor.height));
            m_material.SetVectorArray("clampUvOffset", GetClampuvOffset());
            m_material.SetFloat("_FrameBlend", m_taaVolume.m_frameBlend.value);
            m_material.SetFloat("_Sharpness", m_taaVolume.m_sharpness.value);
            m_material.SetTexture("_HistoryRT", m_historyRTs[readIndex]);
            CoreUtils.SetKeyword(cmd, "LOW_QUALITY", m_taaSetting.m_taaQuality == AntialiasingQuality.Low);
            CoreUtils.SetKeyword(cmd, "MIDDLE_QUALITY", m_taaSetting.m_taaQuality == AntialiasingQuality.Medium);
            CoreUtils.SetKeyword(cmd, "HIGH_QUALITY", m_taaSetting.m_taaQuality == AntialiasingQuality.High);

            cmd.Blit(m_cameraColorIden, m_historyRTs[m_writeIndex], m_material, 0);
            cmd.Blit(m_historyRTs[m_writeIndex], m_cameraColorIden);
        }
        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, new ProfilingSampler(m_taaSetting.m_profilerTags)))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                
                DoTAA(cmd, renderingData.cameraData);
            }
            
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
        
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            
        }
        #endregion
    }
}
