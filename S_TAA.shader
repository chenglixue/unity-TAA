Shader "Elysia_TAA"
{
    Properties
    {
        _MainTex("Main Tex", 2D) = "white" {}
    }
    
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
        }
        Cull Off
        ZWrite Off
        ZTest Always
        
        HLSLINCLUDE
        #include "TAA.hlsl"
        ENDHLSL

        Pass
        {
            Name "Elysia TAA Pass"
            
            HLSLPROGRAM
            #pragma vertex TAAVS
            #pragma fragment TAAPS
            ENDHLSL
        }
    }
}
