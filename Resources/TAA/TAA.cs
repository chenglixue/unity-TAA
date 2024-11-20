using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine.Assertions;

namespace Elysia
{
    public enum TAAQuality : int
    {
        Low = 0,
        Middle = 1,
        High = 2
    }
    public class TAA : ScriptableRendererFeature
    {
        MotionVectorPass m_motionVectorPass;
        ReprojectionPass m_reprojectionPass;
        public TAASetting m_TAASetting = new TAASetting();
        #if UNITY_EDITOR
        public TAADebugSetting m_debugSetting = new TAADebugSetting();
        #endif
        
        private RenderTargetIdentifier[] m_historyFrameRTIs = new RenderTargetIdentifier[2];
        
    
        public override void Create()
        {
            Shader motionVectorShader = Shader.Find("TAA/S_MotionVector");
            Shader reprojectionShader = Shader.Find("TAA/S_Reprojection");

            if (motionVectorShader == null)
            {
                Debug.LogError("can not find motion Vector Shader");
            }
            if (reprojectionShader == null)
            {
                Debug.LogError("can not find reprojection Shader");
            }

            Material motionVectorMaterial = CoreUtils.CreateEngineMaterial(motionVectorShader);
            Material reprojectionMaterial = CoreUtils.CreateEngineMaterial(reprojectionShader);
            
            m_motionVectorPass = new MotionVectorPass(motionVectorMaterial);
            m_reprojectionPass = new ReprojectionPass(reprojectionMaterial);
        }
    
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            m_motionVectorPass.Setup(RenderPassEvent.BeforeRenderingPostProcessing,  m_TAASetting
                #if UNITY_EDITOR
                    ,m_debugSetting
                #endif
                );
            m_motionVectorPass.ConfigureInput(ScriptableRenderPassInput.Motion | ScriptableRenderPassInput.Depth);
            renderer.EnqueuePass(m_motionVectorPass);
            
            m_reprojectionPass.Setup(RenderPassEvent.BeforeRenderingPostProcessing, m_TAASetting
#if UNITY_EDITOR
                ,m_debugSetting
#endif
            );
            renderer.EnqueuePass(m_reprojectionPass);
        }
    }

    [System.Serializable]
    public class TAASetting
    {
        public TAAQuality m_TAAQuality = TAAQuality.High;
        
        public Jitter.Type m_jitterType = Jitter.Type.Halton23X8;
        public LayerMask m_motionVectorLayerMask;
        public int m_motionVectorRefValue;

        [Range(0, 1)] 
        public float m_jitterIntensity = 1;
        [Range(0, 1)] 
        public float m_currFrameWeight = 0.5f;

        [Range(0, 1)]
        public float m_staticFrameWeight = 0.5f;

        [Range(0, 1)] 
        public float m_dynamiceFrameWeight = 0.5f;

        [Range(0, 2)]
        public float m_sharpness = 1f;
    }
    
    [System.Serializable]
    public class TAADebugSetting
    {
        public bool enableDebug = false;

        [Range(0.0f, 100.0f)]
        public float intensity = 1.0f;
    }
    
    
}


