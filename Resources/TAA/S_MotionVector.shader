Shader "TAA/S_MotionVector"
{
    SubShader
    {
        HLSLINCLUDE
        #include_with_pragmas "Assets/Resources/TAA/MotionVector.hlsl"
        ENDHLSL
        Pass
        {
            Name "object motion vector"
            Stencil
			{
				Ref [_Ref]
				WriteMask [_Ref]
				Pass Replace
			}
            Cull Back
            ZWrite Off
            ZTest LEqual
            
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex VS
            #pragma fragment ObjectMotionVector
            #pragma shader_feature_local _TAA_DEBUG

            struct VSInput
            {
                float3 positionOS       : POSITION;
				float3 prePositionOS    : TEXCOORD4;
            };
            struct PSInput
            {
                float4 positionCS       : SV_POSITION;
				float4 currPositionNDC  : TEXCOORD0;
				float4 prePositionNDC   : TEXCOORD1;
            };
            struct PSOutput
            {
                float4 color : SV_TARGET;
            };

            PSInput VS(VSInput i)
            {
                PSInput o = (PSInput)0;

                o.positionCS = mul(UNITY_MATRIX_MVP, float4(i.positionOS, 1.f));
                float4 currPositionCS = mul(_Curr_M_VP, mul(unity_ObjectToWorld, float4(i.positionOS, 1.f)));
                float4 prePositionCS = mul(_Pre_M_VP, mul(unity_MatrixPreviousM,
                    float4(step(0.5, unity_MotionVectorsParams.x) ? i.prePositionOS : i.positionOS, 1.f)));
				#if UNITY_REVERSED_Z
                    o.positionCS.z -= unity_MotionVectorsParams.z * o.positionCS.w;
                #else
                    o.positionCS.z += unity_MotionVectorsParams.z * o.positionCS.w;
                #endif
            	
                o.currPositionNDC = GetVertexPositionNDC(currPositionCS);
                o.prePositionNDC = GetVertexPositionNDC(prePositionCS);
                return o;
            }

            void ObjectMotionVector(PSInput i, out PSOutput o)
            {
                float2 currUV = i.currPositionNDC.xy / i.currPositionNDC.w;
				float2 preUV = i.prePositionNDC.xy / i.prePositionNDC.w;

                o.color = float4(currUV - preUV, 0, 0);
            	#if defined(_TAA_DEBUG)
					o.color *= _DebugIntensity;
				#endif
            }
            ENDHLSL
        }

        Pass
        {
            Name "Camera Motion Vector"
            Cull Off
			ZWrite Off
			ZTest Always
			Stencil
			{
				Ref [_Ref]
				Comp NotEqual
			}
			
			HLSLPROGRAM
			#include_with_pragmas "Assets/Resources/Library/Common.hlsl"
			#pragma target 4.5
            #pragma vertex VS
            #pragma fragment CameraMotionVector
			 #pragma shader_feature_local _TAA_DEBUG

			struct VSInput
            {
                float4 positionOS	: POSITION;
            	float2 uv			: TEXCOORD0;
            };
            struct PSInput
            {
                float4 positionCS : SV_POSITION;
            	float2 uv			: TEXCOORD0;
            };
			struct PSOutput
			{
			    float4 color : SV_TARGET;
			};
			Texture2D<float> _CameraDepthTexture;

			PSInput VS(VSInput i)
			{
			    PSInput o = (PSInput)0;

			    o.positionCS = mul(UNITY_MATRIX_MVP, i.positionOS);

				o.uv = i.uv - _JitterUV / _ScaledScreenParams.xy;

			    return o;
			}

			void CameraMotionVector(PSInput i, out PSOutput o)
			{
				float rawDepth = _CameraDepthTexture.SampleLevel(Smp_ClampU_ClampV_Linear, i.uv, 0);
				float3 positionWS = ComputeWorldSpacePosition(i.uv, rawDepth, _Curr_I_M_VP);

				float2 preUV = ComputeNormalizedDeviceCoordinates(positionWS, _Pre_M_VP);

				o.color = float4(i.uv - preUV, 0, 0);
				#if defined(_TAA_DEBUG)
					o.color *= _DebugIntensity;
				#endif
			}
			ENDHLSL
        }

		Pass
		{
			Cull Off
			ZWrite Off
			ZTest Always
			Stencil
			{
				Ref [_MaskRef]
				Comp Equal
                pass Keep
			}
			
			HLSLPROGRAM
			#pragma target 4.5
            #pragma vertex VS
            #pragma fragment TAAMask
			
			struct VSInput
            {
                float4 positionOS	: POSITION;
            	float2 uv			: TEXCOORD0;
            };
            struct PSInput
            {
                float4 positionCS : SV_POSITION;
            	float2 uv			: TEXCOORD0;
            };
			struct PSOutput
			{
			    float4 color : SV_TARGET;
			};
			

			PSInput VS(VSInput i)
			{
			    PSInput o = (PSInput)0;

			    o.positionCS = mul(UNITY_MATRIX_MVP, float4(i.positionOS.xyz, 1.f));

				o.uv = i.uv;

			    return o;
			}
			void TAAMask(PSInput i, out PSOutput o)
			{
				o.color.r = 1;
			}
			ENDHLSL
		}
    }
}
