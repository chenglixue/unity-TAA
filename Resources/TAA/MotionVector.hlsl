#pragma once
#include "Assets/Resources/Library/Common.hlsl"

float4x4 _Pre_M_VP;
float4x4 _Pre_I_M_VP;
float4x4 _Curr_M_VP;
float4x4 _Curr_I_M_VP;
float2   _JitterUV;
int _Ref;
int _MaskRef;
half _DebugIntensity;
Texture2D<float4> _CameraColorTexture;

float4 GetVertexPositionNDC(float4 positionCS)
{
    float4 positionNDC;
    float4 ndc = positionCS * 0.5f;
    positionNDC.xy = float2(ndc.x, ndc.y * _ProjectionParams.x) + ndc.w;
    positionNDC.zw = positionCS.zw;

    return positionNDC;
}