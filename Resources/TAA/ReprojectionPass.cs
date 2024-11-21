using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;
using AmplifyShaderEditor;
using Unity.Mathematics;
using UnityEngine.Assertions;

namespace Elysia
{
    class ReprojectionPass : ScriptableRenderPass
    {
        private TAASetting m_TAASetting;
        #if UNITY_EDITOR
            private TAADebugSetting m_TAADebugSetting;
        #endif
        private Material m_reprojectionMaterial;
        
        private RenderTextureDescriptor m_descriptor;
        private RenderTargetIdentifier[] m_reprojectionRTIs = new RenderTargetIdentifier[2];
        
        static class ShaderIDs
        {
            public static readonly int m_historyFrameTexAId = Shader.PropertyToID("_HistoryFrameTexA");
            public static readonly int m_historyFrameTexBId = Shader.PropertyToID("_HistoryFrameTexB");
            public static readonly int m_historyFrameTexId = Shader.PropertyToID("_HistoryFrameTex");
            public static readonly int m_currFrameTexId = Shader.PropertyToID("_CurrFrameTex");
            
            public static readonly int m_reprojectTexSizeId = Shader.PropertyToID("_ReprojectTexSize");
            public static readonly int m_currFrameWeightId = Shader.PropertyToID("_CurrFrameWeight");
            public static readonly int m_staticFrameWeightId = Shader.PropertyToID("_StaticFrameWeight");
            public static readonly int m_dynamiceFrameWeightId = Shader.PropertyToID("_DynamiceFrameWeight");
            public static readonly int m_sharpnessId = Shader.PropertyToID("_Sharpness");
        }
        private int m_writeIndex = 0;

        public ReprojectionPass(Material material)
        {
            m_reprojectionMaterial = material;
        }
        public void Setup(RenderPassEvent passEvent, TAASetting taaSetting
        #if UNITY_EDITOR
                    , TAADebugSetting debugSetting
        #endif
        )
        {
            this.m_TAASetting = taaSetting;
            #if UNITY_EDITOR
            this.m_TAADebugSetting = debugSetting;
            #endif
            this.renderPassEvent = passEvent;
        }
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            m_descriptor = cameraTextureDescriptor;
            m_descriptor.depthBufferBits = 0;

            InitRTI(ref m_reprojectionRTIs[0], ShaderIDs.m_historyFrameTexAId, m_descriptor, cmd,
                1, 1, RenderTextureFormat.DefaultHDR, 0,
                true, true, FilterMode.Point);
            
            InitRTI(ref m_reprojectionRTIs[1], ShaderIDs.m_historyFrameTexBId, m_descriptor, cmd,
                1, 1, RenderTextureFormat.DefaultHDR, 0,
                true, true, FilterMode.Point);
        }
        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get("Reprojection Pass");

            {
                var cameraData = renderingData.cameraData;

                SetMaterialProperty();
                
                int readIndex = m_writeIndex;
                m_writeIndex = (++m_writeIndex) % 2;
                cmd.SetGlobalTexture(ShaderIDs.m_historyFrameTexId, m_reprojectionRTIs[readIndex]);
                cmd.SetGlobalTexture(ShaderIDs.m_currFrameTexId, cameraData.renderer.cameraColorTarget);

                DoTAAGlobal(cmd, ref renderingData);
                DoTAAMask(cmd, ref renderingData);
                cmd.SetGlobalTexture("_MainTex", m_reprojectionRTIs[m_writeIndex]);
                cmd.Blit(m_reprojectionRTIs[m_writeIndex], cameraData.renderer.cameraColorTarget, m_reprojectionMaterial, 2);
                
                cmd.SetRenderTarget(cameraData.renderer.cameraColorTarget, cameraData.renderer.cameraDepthTarget);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
        
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            cmd.ReleaseTemporaryRT(ShaderIDs.m_historyFrameTexAId);
            cmd.ReleaseTemporaryRT(ShaderIDs.m_historyFrameTexBId);
        }
        
        void InitRTI(ref RenderTargetIdentifier RTI, int texID, RenderTextureDescriptor descriptor, CommandBuffer cmd,
            int downSampleWidth, int downSampleHeight, RenderTextureFormat colorFormat, 
            int depthBufferBits, bool isUseMipmap, bool isAutoGenerateMips,
            FilterMode filterMode)
        {
            descriptor.width           /= downSampleWidth;
            descriptor.height          /= downSampleHeight;
            descriptor.colorFormat      = colorFormat;
            descriptor.depthBufferBits  = depthBufferBits;
            descriptor.useMipMap        = isUseMipmap;
            descriptor.autoGenerateMips = isAutoGenerateMips;
            
            RTI = new RenderTargetIdentifier(texID);
            cmd.GetTemporaryRT(texID, descriptor, filterMode);
            cmd.SetGlobalTexture(texID, RTI);
        }
        
        void BlitSp(CommandBuffer cmd, RenderTargetIdentifier source, RenderTargetIdentifier dest,
            RenderTargetIdentifier depth, Material mat, int passIndex, MaterialPropertyBlock mpb = null)
        {
            cmd.SetRenderTarget(dest, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, 
                depth, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            cmd.ClearRenderTarget(false, false, Color.clear);
            cmd.SetViewProjectionMatrices(Matrix4x4.identity,Matrix4x4.identity);
            cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, mat, 0, passIndex, mpb);
        }
        
        void SetMaterialProperty()
        {
            m_reprojectionMaterial.SetVector(ShaderIDs.m_reprojectTexSizeId, new Vector4(m_descriptor.width, m_descriptor.height, 
                1.0f / m_descriptor.width, 1.0f / m_descriptor.height ));
            m_reprojectionMaterial.SetFloat(ShaderIDs.m_currFrameWeightId, m_TAASetting.m_currFrameWeight);
            m_reprojectionMaterial.SetFloat(ShaderIDs.m_staticFrameWeightId, m_TAASetting.m_staticFrameWeight);
            m_reprojectionMaterial.SetFloat(ShaderIDs.m_dynamiceFrameWeightId, m_TAASetting.m_dynamiceFrameWeight);
            m_reprojectionMaterial.SetFloat(ShaderIDs.m_sharpnessId, m_TAASetting.m_sharpness);
            m_reprojectionMaterial.SetInt("_MaskRefValue", m_TAASetting.m_maskRefValue);
            CoreUtils.SetKeyword(m_reprojectionMaterial, "_TAA_LOW", m_TAASetting.m_TAAQuality == TAAQuality.Low);
            CoreUtils.SetKeyword(m_reprojectionMaterial, "_TAA_MIDDLE", m_TAASetting.m_TAAQuality == TAAQuality.Middle);
            CoreUtils.SetKeyword(m_reprojectionMaterial, "_TAA_HIGH", m_TAASetting.m_TAAQuality == TAAQuality.High);
        }

        void DoTAAGlobal(CommandBuffer cmd, ref RenderingData renderingData)
        {
            BlitSp(cmd, renderingData.cameraData.renderer.cameraColorTarget, m_reprojectionRTIs[m_writeIndex],
                renderingData.cameraData.renderer.cameraDepthTarget,
                m_reprojectionMaterial, 0);
        }

        void DoTAAMask(CommandBuffer cmd, ref RenderingData renderingData)
        {
            BlitSp(cmd, renderingData.cameraData.renderer.cameraColorTarget, m_reprojectionRTIs[m_writeIndex],
                renderingData.cameraData.renderer.cameraDepthTarget,
                m_reprojectionMaterial, 1);
        }
    }   
}