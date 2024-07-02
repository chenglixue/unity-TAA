using System;
using Unity.VisualScripting;

namespace UnityEngine.Rendering.Universal
{
    [Serializable, VolumeComponentMenuForRenderPipeline("Elysia/Elysia TAA", typeof(UniversalRenderPipeline))]
    public class TAA_Volume : VolumeComponent, IPostProcessComponent
    {
        public BoolParameter enable = new BoolParameter(true);
        
        public ClampedFloatParameter m_jitterIntensity = new ClampedFloatParameter(1f, 0f, 1f);
        
        public ClampedFloatParameter m_frameBlend = new ClampedFloatParameter(1f, 0f, 1f);
        
        public ClampedFloatParameter m_sharpness = new ClampedFloatParameter(0.3f, 0f, 0.5f);
        
        
        
        
        public bool IsTileCompatible() => false;
        public bool IsActive() => enable == true; 
    }
}
