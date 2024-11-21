using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine.Assertions;

namespace Elysia
{
    class MotionVectorPass : ScriptableRenderPass
    {
        #region Declaration
        private enum MotionVectorPassType : int
        {
            DynamicObjects = 0,
            StaticObjects = 1
        }
        private TAASetting m_TAASetting;
        #if UNITY_EDITOR
        private TAADebugSetting m_TAADebugSetting;
        #endif
        private Material m_motionVectorMaterial;

        private RenderTargetIdentifier m_motionVectorRTI;
        private RenderTargetIdentifier m_TAAMASKRTI;
        private RenderTextureDescriptor m_descriptor;

        static class ShaderIDs
        {
            public static readonly int m_preMatrixVPId = Shader.PropertyToID("_Pre_M_VP");
            public static readonly int m_preMatrixInvVPId = Shader.PropertyToID("_Pre_I_M_VP");
            public static readonly int m_currMatrixVPId = Shader.PropertyToID("_Curr_M_VP");
            public static readonly int m_currMatrixInvVPId = Shader.PropertyToID("_Curr_I_M_VP");
            public static readonly int m_jitterUVId = Shader.PropertyToID("_JitterUV");
            public static readonly int m_motionVectorTex = Shader.PropertyToID("_MotionVectorTex");
            public static readonly int m_TAAMaskTex = Shader.PropertyToID("_TAAMaskTex");
        }
        
        private Matrix4x4 m_preMatrixVP;
        private Matrix4x4 m_preMatrixInvVP;
        private Matrix4x4 m_currMatrixVP;
        private Matrix4x4 m_currMatrixInvVP;
        private Matrix4x4 m_nonJitterMatrixProj;
        
        private Vector2 m_jitterUV = Vector2.zero;
        private Vector2 m_nextJitterUV = Vector2.zero;
        #endregion

        public MotionVectorPass(Material material)
        {
            m_motionVectorMaterial = material;
        }
        public void Setup(RenderPassEvent passEvent, TAASetting taaSetting
        #if UNITY_EDITOR
            , TAADebugSetting debugSetting
        #endif
        )
        {
            this.renderPassEvent = passEvent;
            
            m_TAASetting = taaSetting;
            #if UNITY_EDITOR
            m_TAADebugSetting = debugSetting;
            #endif
        }
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.camera.cameraType == CameraType.Preview)
            {
                return;
            }
            var camera = renderingData.cameraData.camera;
            
            // jitter
            renderingData.cameraData.camera.ResetProjectionMatrix();
            Matrix4x4 projMatrix = renderingData.cameraData.camera.projectionMatrix;
            m_nonJitterMatrixProj = projMatrix;
            m_jitterUV = m_nextJitterUV;
            m_nextJitterUV = Jitter.SampleJitterUV(m_TAASetting.m_jitterType);
            projMatrix.m02 = m_nextJitterUV.x * 2.0f / camera.scaledPixelWidth * m_TAASetting.m_jitterIntensity;
            projMatrix.m12 = m_nextJitterUV.y * 2.0f / camera.scaledPixelHeight * m_TAASetting.m_jitterIntensity;
            renderingData.cameraData.camera.projectionMatrix = projMatrix;
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            m_descriptor = cameraTextureDescriptor;
            m_descriptor.depthBufferBits = 0;

            InitRTI(ref m_motionVectorRTI, ShaderIDs.m_motionVectorTex, m_descriptor, cmd,
                1, 1, RenderTextureFormat.RGHalf, 0, true, true, FilterMode.Point);
            InitRTI(ref m_TAAMASKRTI, ShaderIDs.m_TAAMaskTex, m_descriptor, cmd,
                1, 1, RenderTextureFormat.R8, 0, true, true, FilterMode.Point);
        }
        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.camera.cameraType == CameraType.Preview)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get("Motion Vector Pass");
            {
                var cameraData = renderingData.cameraData;

                SetMaterialProperty(cameraData);

                DoCharacterMask(cmd, ref renderingData);
                DoDynamicMotionVector(cmd, context, ref renderingData);
                DoStaticMotionVector(cmd, ref renderingData);

                if (m_TAADebugSetting.enableDebug)
                {
                    cmd.Blit(ShaderIDs.m_motionVectorTex, cameraData.renderer.cameraColorTarget);
                }
                cmd.SetRenderTarget(renderingData.cameraData.renderer.cameraColorTarget, 
                    renderingData.cameraData.renderer.cameraDepthTarget);
                m_preMatrixVP = m_currMatrixVP;
                m_preMatrixInvVP = m_currMatrixInvVP;
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
        
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            cmd.ReleaseTemporaryRT(ShaderIDs.m_motionVectorTex);
            cmd.ReleaseTemporaryRT(ShaderIDs.m_TAAMaskTex);
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

        void SetMaterialProperty(CameraData cameraData)
        {
            Matrix4x4 nonJitterGPUProjection = GL.GetGPUProjectionMatrix(m_nonJitterMatrixProj, cameraData.IsCameraProjectionMatrixFlipped());
            m_currMatrixVP = nonJitterGPUProjection * cameraData.GetViewMatrix();
            m_currMatrixInvVP = m_currMatrixVP.inverse;
                
            m_motionVectorMaterial.SetMatrix(ShaderIDs.m_preMatrixVPId, m_preMatrixVP);
            m_motionVectorMaterial.SetMatrix(ShaderIDs.m_preMatrixInvVPId, m_preMatrixInvVP);
            m_motionVectorMaterial.SetMatrix(ShaderIDs.m_currMatrixVPId, m_currMatrixVP);
            m_motionVectorMaterial.SetMatrix(ShaderIDs.m_currMatrixInvVPId, m_currMatrixInvVP);
            m_motionVectorMaterial.SetVector(ShaderIDs.m_jitterUVId, m_jitterUV);
            m_motionVectorMaterial.SetInt("_Ref", m_TAASetting.m_motionVectorRefValue);
            m_motionVectorMaterial.SetInt("_MaskRef", m_TAASetting.m_maskRefValue);
            m_motionVectorMaterial.SetFloat("_DebugIntensity", m_TAADebugSetting.intensity);
            CoreUtils.SetKeyword(m_motionVectorMaterial, "_TAA_LOW", m_TAASetting.m_TAAQuality == TAAQuality.Low);
            CoreUtils.SetKeyword(m_motionVectorMaterial, "_TAA_MIDDLE", m_TAASetting.m_TAAQuality == TAAQuality.Middle);
            CoreUtils.SetKeyword(m_motionVectorMaterial, "_TAA_HIGH", m_TAASetting.m_TAAQuality == TAAQuality.High);
            CoreUtils.SetKeyword(m_motionVectorMaterial, "_TAA_DEBUG", m_TAADebugSetting.enableDebug == true);
        }
        
        void BlitSp(CommandBuffer cmd, RenderTargetIdentifier source, RenderTargetIdentifier dest,
            RenderTargetIdentifier depth, Material mat, int passIndex, MaterialPropertyBlock mpb = null)
        {
            cmd.SetRenderTarget(dest, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, 
                depth, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            cmd.ClearRenderTarget(false, true, Color.clear);
            cmd.SetViewProjectionMatrices(Matrix4x4.identity,Matrix4x4.identity);
            cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, mat, 0, passIndex, mpb);
        }
        
        void DoDynamicMotionVector(CommandBuffer cmd, ScriptableRenderContext context, ref RenderingData renderingData)
        {
            cmd.BeginSample("Object");
            cmd.SetRenderTarget(m_motionVectorRTI, renderingData.cameraData.renderer.cameraDepthTarget);
            cmd.ClearRenderTarget(false, true, Color.black);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            DrawingSettings drawingSettings = CreateDrawingSettings(
                new ShaderTagId("UniversalForward"),
                ref renderingData,
                renderingData.cameraData.defaultOpaqueSortFlags);
            drawingSettings.overrideMaterial = m_motionVectorMaterial;
            drawingSettings.overrideMaterialPassIndex = (int)MotionVectorPassType.DynamicObjects;
            drawingSettings.perObjectData = PerObjectData.MotionVectors;
            FilteringSettings filteringSettings = new FilteringSettings(RenderQueueRange.all, m_TAASetting.m_motionVectorLayerMask);
                
            context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings);
            cmd.EndSample("Object");
        }

        void DoStaticMotionVector(CommandBuffer cmd, ref RenderingData renderingData)
        {
            cmd.BeginSample("Camera");
            BlitSp(cmd, renderingData.cameraData.renderer.cameraColorTarget, m_motionVectorRTI,
                renderingData.cameraData.renderer.cameraDepthTarget,
                m_motionVectorMaterial, (int)MotionVectorPassType.StaticObjects);
            cmd.EndSample("Camera");
        }

        void DoCharacterMask(CommandBuffer cmd, ref RenderingData renderingData)
        {
            cmd.BeginSample("Mask");
            
            BlitSp(cmd, renderingData.cameraData.renderer.cameraColorTarget, m_TAAMASKRTI,
                renderingData.cameraData.renderer.cameraDepthTarget,
                m_motionVectorMaterial, 2);
            
            cmd.EndSample("Mask");
            
        }
    }   
}
