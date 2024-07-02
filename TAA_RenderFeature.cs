using System.Collections.Generic;

namespace UnityEngine.Rendering.Universal
{
    public class TAA_RenderFeature : ScriptableRendererFeature
    {
        #region Variable
        [System.Serializable]
        public class TAASettings
        {
            public string m_profilerTags = "Elysia TAA";
            public Shader m_shader = null;
            public RenderPassEvent m_passEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            public AntialiasingQuality m_taaQuality = AntialiasingQuality.Medium;
        }
        public class TAAData
        {
            public Matrix4x4 m_projJitter;
            public Matrix4x4 m_currView;
            public Matrix4x4 m_projPre;
            public Matrix4x4 m_viewPre;
            public Vector2 m_offset;
            public Vector2 m_lastOffset;
            
            public TAAData()
            {
                m_projJitter = Matrix4x4.identity;
                m_projPre = Matrix4x4.identity;
                m_viewPre = Matrix4x4.identity;
                m_currView = Matrix4x4.identity;

                m_offset = Vector2.zero;
                m_lastOffset = Vector2.zero;
            }
        }
    
        private TAARenderPass m_taaRenderPass;
        private JitterRenderPass m_jitterRenderPass;
        public TAASettings m_taaSetting = new TAASettings();
        private TAAData m_taaData = new TAAData();
        private Matrix4x4 m_projPre = Matrix4x4.identity;
        private Matrix4x4 m_viewPre = Matrix4x4.identity;
        private int m_haltonIndex = 0;
        private Vector2 m_lastOffset;
        #endregion
        
        #region Setup
        public override void Create()
        {
            m_jitterRenderPass = new JitterRenderPass(m_taaSetting);
            
            m_taaRenderPass = new TAARenderPass(m_taaSetting);
        }
        #endregion

        #region Execute
        static float GetHalton(int index, int prime) 
        {
            float r = 0.0f;
            float f = 1.0f;
            int i = index;
            while (i > 0)
            {
                f /= prime;
                r += f * (i % prime);
                i = (int)Mathf.Floor(i / (float)prime);
            }
            return r;
        }
        static Vector2 GenerateRandomOffset(int sampleIndex)
        {
            var offset = new Vector2(
                GetHalton((sampleIndex & 1023) + 1, 2) - 0.5f,
                GetHalton((sampleIndex & 1023) + 1, 3) - 0.5f
            );

            return offset;
        }
        static Matrix4x4 CalcJitterProjectionMatrix(ref Camera camera, Vector2 offset, float jitterIntensity = 1f)
        {
            Matrix4x4 result = camera.nonJitteredProjectionMatrix;
            
            Vector2 matrixOffset = offset * new Vector2(1f / camera.pixelWidth, 1f / camera.pixelHeight) * jitterIntensity;

            result[0, 2] = matrixOffset.x;
            result[1, 2] = matrixOffset.y;

            return result;
        }
        
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            TAA_Volume taaVolume = VolumeManager.instance.stack.GetComponent<TAA_Volume>();

            if (taaVolume != null && taaVolume.IsActive())
            {
                m_haltonIndex = (m_haltonIndex + 1) & 1023;
                var offset = GenerateRandomOffset(m_haltonIndex);

                m_lastOffset = m_taaData.m_offset;
                m_taaData.m_lastOffset = m_lastOffset;
                m_taaData.m_offset = new Vector2(offset.x / camera.pixelWidth, offset.y / camera.pixelHeight);
                m_taaData.m_projJitter = CalcJitterProjectionMatrix(ref camera, offset, taaVolume.m_jitterIntensity.value);
                m_taaData.m_projPre = m_projPre;
                m_taaData.m_viewPre = m_viewPre;
                m_projPre = camera.projectionMatrix;
                m_viewPre = camera.worldToCameraMatrix;
            
                m_jitterRenderPass.Setup(m_taaData);
                renderer.EnqueuePass(m_jitterRenderPass);
            
                m_taaRenderPass.Setup(m_taaData, taaVolume);
                renderer.EnqueuePass(m_taaRenderPass);   
            }
        }
        #endregion
    }   
}


