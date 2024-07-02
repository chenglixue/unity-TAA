#pragma region Marco
#pragma once
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#pragma multi_compile LOW_QUALITY MIDDLE_QUALITY HIGH_QUALITY
#pragma endregion 

#pragma region Variable
float _FrameBlend;
float _Sharpness;
half4 _MainTex_TexelSize;
float4 _sourceTexSize;
int2 clampUvOffset[9];
float4x4 _I_View_Cur;
float4x4 _I_Proj_Cur;
float4x4 _ViewProjPre;
float4 _JitterUVOffset;

TEXTURE2D(_MainTex);
TEXTURE2D(_HistoryRT);
SamplerState sampler_LinearClamp;
SamplerState sampler_PointClamp;


struct VSInput
{
    float4 positionOS   : POSITION;
    float2 uv           : TEXCOORD0;
};

struct PSInput
{
    float4 positionCS   : SV_POSITION;
    float2 uv           : TEXCOORD0;
};
#pragma endregion 

#pragma region VS
PSInput TAAVS(VSInput i)
{
    PSInput o;

    VertexPositionInputs vertexPosData = GetVertexPositionInputs(i.positionOS);
    o.positionCS = vertexPosData.positionCS;

    o.uv = i.uv;
    #if defined(UNITY_UV_STARTS_AT_TOP)
    if(_MainTex_TexelSize.y < 0.h) o.uv.y = 1.h - i.uv.y;
    #endif

    return o;
}


#pragma endregion 

#pragma region PS
half3 GetSourceColor(float2 uv)
{
    return _MainTex.SampleLevel(sampler_LinearClamp, uv, 0).rgb;
}
half3 GetHistoryColor(float2 uv)
{
    return _HistoryRT.SampleLevel(sampler_LinearClamp, uv, 0);
}

void ClampAABB(float2 uv, inout half3 colorMin, inout half3 colorMax, inout half3 colorAvg)
{
    colorMax = 1.h;
    colorMin = 0.h;
    colorAvg = 0.h;

    UNITY_UNROLL
    for(int i = 0; i < 9; ++i)
    {
        half3 currColor = RGBToYCoCg(GetSourceColor(uv + clampUvOffset[i].xy * _sourceTexSize.zw));
        colorAvg += currColor;
        colorMin = min(colorMin, currColor);
        colorMax = max(colorMax, currColor);
    }

    colorAvg /= 9.f;
}

half3 ClipAABB(half3 historyColor, half3 colorMin, half3 colorMax)
{
    historyColor = RGBToYCoCg(historyColor);
    half3 filtered = (colorMin + colorMax) * 0.5f;
    half3 origin = historyColor;
    float3 dir = filtered - historyColor;
    dir = abs(dir) < 1.f * rcp(65536.f) ? 1.f * rcp(65536.f) : dir;
    float3 invDir = rcp(dir);

    float3 minIntersect = (colorMin - origin) * invDir;
    float3 maxIntersect = (colorMax - origin) * invDir;
    float3 intersect = min(minIntersect, maxIntersect);
    float clipBlend = max(intersect.x, max(intersect.y, intersect.z));
    clipBlend = saturate(clipBlend);

    float3 intersectTCoCg = lerp(historyColor, filtered, clipBlend);
    return YCoCgToRGB(intersectTCoCg);
}

half3 VarianceClipAABB(half3 colorMin, half3 colorMax, half3 colorAvg)
{
    float3 p_clip = 0.5 * (colorMax + colorMin);
    float3 e_clip = 0.5 * (colorMax - colorMin) + FLT_EPS;
    float3 v_clip = colorMax - p_clip;
    float3 v_unit = v_clip / e_clip;
    float3 a_unit = abs(v_unit);
    float ma_unit = max(a_unit.x, max(a_unit.y, a_unit.z));

    if (ma_unit > 1.0)
        return p_clip + v_clip / ma_unit;
    else
        return colorAvg;
}

float2 GetHistoryUV(float2 un_jittred_uv, float2 jittered_uv)
{
    float depth = _CameraDepthTexture.SampleLevel(sampler_PointClamp, un_jittred_uv, 0).r;
    float4 historyNDC = float4(jittered_uv * 2.f - 1.f, depth, 1.f);
    #if UNITY_UV_STARTS_AT_TOP
    historyNDC.y = -historyNDC.y;
    #endif

    float4 historyWS = mul(UNITY_MATRIX_I_VP, historyNDC);
    historyWS *= rcp(historyWS.w);
    float4 historyCS = mul(_ViewProjPre, historyWS);
    float2 historyUV = historyCS.xy / historyCS.w;
    historyUV = historyUV * 0.5f + 0.5f;
    
    return historyUV;
}

half4 TAAPS(PSInput i) : SV_TARGET
{
    float3 result = 0.f;

    float2 currOffset = _JitterUVOffset.xy;
    
    float2 uv_jittered = i.uv;
    float2 uv_unJittered = uv_jittered - currOffset * 0.5f;
    float2 historyUV = GetHistoryUV(uv_unJittered, uv_jittered);
    
    half3 sourceColor = GetSourceColor(uv_unJittered);
    half3 sourceColorTL = GetSourceColor(uv_unJittered - _sourceTexSize.zw * 0.5f);
    half3 sourceColorBR = GetSourceColor(uv_unJittered + _sourceTexSize.zw * 0.5f);
    half3 corners = 4.f * (sourceColorTL + sourceColorBR) - 2.f * sourceColor;
    sourceColor += (sourceColor - (corners * 0.1666667f)) * 2.718282 * _Sharpness;
    half3 historyColor = GetHistoryColor(historyUV);

    #if defined(LOW_QUALITY)
    half3 colorMin, colorMax, colorAvg;
    ClampAABB(i.uv, colorMin, colorMax, colorAvg); 
    historyColor = clamp(RGBToYCoCg(historyColor), colorMin, colorMax);
    historyColor = YCoCgToRGB(historyColor);
    #elif defined(MIDDLE_QUALITY)
    half3 colorMin, colorMax, colorAvg;
    ClampAABB(uv_unJittered, colorMin, colorMax, colorAvg);
    historyColor = ClipAABB(historyColor, colorMin, colorMax);
    #elif defined(HIGH_QUALITY)
    half3 colorMin, colorMax, colorAvg;
    ClampAABB(uv_unJittered, colorMin, colorMax, colorAvg);
    historyColor = YCoCgToRGB(VarianceClipAABB(colorMin, colorMax, colorAvg));
    sourceColor = min(sourceColor, YCoCgToRGB(colorAvg) * 1.25f);
    #endif

    result += historyColor * _FrameBlend + sourceColor * (1.f - _FrameBlend);

    return half4(result, 1.f);
}
#pragma endregion 