Shader "TAA/S_Reprojection"
{
    SubShader
    {
    	Cull Off
	    ZWrite Off
	    ZTest Always
	    
    	HLSLINCLUDE
    	struct VSInput
	    {
		    float4 positionOS	: POSITION;
		    float2 uv			: TEXCOORD0;
	    };
	    struct PSInput
	    {
		    float4 positionCS	: SV_POSITION;
		    float2 uv		: TEXCOORD0;
	    };
	    struct PSOutput
	    {
		    float4 color : SV_TARGET;
	    };
    	#include_with_pragmas "Reprojection.hlsl"

    	PSInput VS(VSInput i)
	    {
		    PSInput o = (PSInput)0;

		    o.positionCS = mul(UNITY_MATRIX_MVP, i.positionOS);
		    o.uv = i.uv;

		    return o;
	    }
    	ENDHLSL
        Pass
        {
		    Stencil
		    {
		    	Ref [_MaskRefValue]
		    	Comp NotEqual
		    }
		    
		    HLSLPROGRAM
		    #pragma target 4.5
		    #pragma vertex VS
			#pragma fragment Reprojection
			#pragma shader_feature_local _TAA_LOW _TAA_MIDDLE _TAA_HIGH
		    
		    void Reprojection(PSInput i, out PSOutput o)
		    {
			    float2 uv			= SampleClosestUVCross(_CameraDepthTexture, i.uv, _ReprojectTexSize.zw);
		    	float2 motionVector	= _MotionVectorTex.SampleLevel(Smp_ClampU_ClampV_Linear, i.uv, 0);
		    	float4 historyFrame = _HistoryFrameTex.SampleLevel(Smp_ClampU_ClampV_Linear, i.uv - motionVector, 0);
		    	float3 historyColor = historyFrame.rgb;
    		
		    	half3 minColor, maxColor, currColor;
    			#if defined(_TAA_LOW)
					SampleMinMaxCross(_CurrFrameTex, uv, _ReprojectTexSize.zw, minColor, maxColor, currColor);
    		
		    		historyColor = ClampBox(historyColor, minColor, maxColor);
    			#elif defined(_TAA_MIDDLE)
    				SampleMinMax3x3(_CurrFrameTex, uv, _ReprojectTexSize.zw, minColor, maxColor, currColor);
    				minColor  = MappingColor(minColor);
    				maxColor  = MappingColor(maxColor);
    				currColor = MappingColor(currColor);
    				historyColor = MappingColor(historyColor);
    		
    				ClipBox(historyColor, minColor, maxColor);
    			#else
    				SampleMinMax3x3(_CurrFrameTex, uv, _ReprojectTexSize.zw, minColor, maxColor, currColor);
    				minColor  = MappingColor(minColor);
    				maxColor  = MappingColor(maxColor);
    				currColor = MappingColor(currColor);
    				historyColor = MappingColor(historyColor);
    		
    				historyColor = VarianceClipBox(minColor, maxColor);
    			#endif
		    	
		    	half3 blendColor = lerp(historyColor, currColor, GetWeight(motionVector));
    			#if defined(_TAA_LOW)
    				o.color = float4(blendColor, 1.f);
    			#else
    				o.color = float4(ResolveColor(blendColor), 1.f);
    			#endif
    		
		    }
		    ENDHLSL
        }

		Pass
		{
			Stencil
		    {
		    	Ref [_MaskRefValue]
		    	Comp Equal
		    }
			
			HLSLPROGRAM
		    #pragma target 4.5
		    #pragma vertex VS
			#pragma fragment Reprojection
			#pragma shader_feature_local _TAA_LOW _TAA_MIDDLE _TAA_HIGH
		    
		    void Reprojection(PSInput i, out PSOutput o)
		    {
			    float2 uv			= SampleClosestUVCross(_CameraDepthTexture, i.uv, _ReprojectTexSize.zw);
		    	float2 motionVector	= _MotionVectorTex.SampleLevel(Smp_ClampU_ClampV_Linear, i.uv, 0) * _TAAMaskTex.SampleLevel(Smp_ClampU_ClampV_Linear, i.uv, 0);
		    	float4 historyFrame = _HistoryFrameTex.SampleLevel(Smp_ClampU_ClampV_Linear, i.uv - motionVector, 0);
		    	float3 historyColor = historyFrame.rgb;
    		
		    	half3 minColor, maxColor, currColor;
    			#if defined(_TAA_LOW)
					SampleMinMaxCross(_CurrFrameTex, uv, _ReprojectTexSize.zw, minColor, maxColor, currColor);
    		
		    		historyColor = ClampBox(historyColor, minColor, maxColor);
    			#elif defined(_TAA_MIDDLE)
    				SampleMinMax3x3(_CurrFrameTex, uv, _ReprojectTexSize.zw, minColor, maxColor, currColor);
    				minColor  = MappingColor(minColor);
    				maxColor  = MappingColor(maxColor);
    				currColor = MappingColor(currColor);
    				historyColor = MappingColor(historyColor);
    		
    				ClipBox(historyColor, minColor, maxColor);
    			#else
    				SampleMinMax3x3(_CurrFrameTex, uv, _ReprojectTexSize.zw, minColor, maxColor, currColor);
    				minColor  = MappingColor(minColor);
    				maxColor  = MappingColor(maxColor);
    				currColor = MappingColor(currColor);
    				historyColor = MappingColor(historyColor);
    		
    				historyColor = VarianceClipBox(minColor, maxColor);
    			#endif
		    	
		    	half3 blendColor = lerp(historyColor, currColor, GetWeight(motionVector));
    			#if defined(_TAA_LOW)
    				o.color = float4(blendColor, 1.f);
    			#else
    				o.color = float4(ResolveColor(blendColor), 1.f);
    			#endif
		    }
		    ENDHLSL
		}

		Pass
		{
		    HLSLPROGRAM
		    #pragma target 4.5
		    #pragma vertex VS
			#pragma fragment Unsharp

		    void Unsharp(PSInput i, out PSOutput o)
    		{
    			float du = _MainTex_TexelSize.x;
    			float dv = _MainTex_TexelSize.y;
    			const float2 offsetUV[5] =
			    {
			                    {0, dv}, 
			        {-du, 0}, {0, 0}, {du, 0},
			                    {0, -dv}, 
			    };
    			o.color = half4(Unsharp(_MainTex, i.uv, offsetUV), 1.f);
    		}
		    ENDHLSL
		}
    }
}
