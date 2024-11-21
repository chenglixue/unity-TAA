#pragma once
#include_with_pragmas "Assets/Resources/Library/Common.hlsl"

float4 _ReprojectTexSize;
float4 _CurrFrameTex_TexelSize;
float _CurrFrameWeight;
float _StaticFrameWeight;
float _DynamiceFrameWeight;
float _Sharpness;
int _MaskRefValue;

Texture2D<float2> _MotionVectorTex;
Texture2D<half> _TAAMaskTex;
Texture2D<half4> _HistoryFrameTex;
Texture2D<half4> _CurrFrameTex;
Texture2D<half4> _MainTex;
Texture2D<half> _CameraDepthTexture;

float3 TransformRGB2YCoCg(float3 c)
{
    // Y  = R/4 + G/2 + B/4
    // Co = R/2 - B/2
    // Cg = -R/4 + G/2 - B/4
    return float3(
         c.x / 4.0 + c.y / 2.0 + c.z / 4.0,
         c.x / 2.0 - c.z / 2.0,
        -c.x / 4.0 + c.y / 2.0 - c.z / 4.0
    );
}
float3 TransformYCoCg2RGB(float3 c)
{
    // R = Y + Co - Cg
    // G = Y + Cg
    // B = Y - Co - Cg
    return saturate(float3(
        c.x + c.y - c.z,
        c.x + c.z,
        c.x - c.y - c.z
    ));
}
half3 MappingColor(half3 color)
{
    return TransformRGB2YCoCg(color * rcp(1.0 + Luminance(color)));
}
half3 ResolveColor(half3 color)
{
    half3 rgb = TransformYCoCg2RGB(color);
    return rgb * rcp(1.0 - Luminance(rgb));
}

half3 Unsharp(Texture2D<half4> colorTex, float2 uv, const float2 offsetUV[5])
{
    half3 o = 0;

    o += colorTex.SampleLevel(Smp_ClampU_ClampV_Linear, uv + offsetUV[0], 0).rgb * -1;
    o += colorTex.SampleLevel(Smp_ClampU_ClampV_Linear, uv + offsetUV[1], 0).rgb * -1;
    o += colorTex.SampleLevel(Smp_ClampU_ClampV_Linear, uv + offsetUV[2], 0).rgb * 5;
    o += colorTex.SampleLevel(Smp_ClampU_ClampV_Linear, uv + offsetUV[3], 0).rgb * -1;
    o += colorTex.SampleLevel(Smp_ClampU_ClampV_Linear, uv + offsetUV[4], 0).rgb * -1;
    o *= _Sharpness;

    return o;
}

void SampleDepth3x3(Texture2D<half> depthTex, float2 uv, float2 duv,
    out half depths[9])
{
    float du = duv.x;
    float dv = duv.y;

    const float2 offsetUV[9] =
    {
        {-du, dv}, {0, dv}, {du, dv},
        {-du, 0}, {0, 0}, {du, 0},
        {-du, -dv}, {0, -dv}, {du, -dv}
    };

    [unroll]
    for(int i = 0; i < 9; ++i)
    {
        half depth = depthTex.SampleLevel(Smp_ClampU_ClampV_Linear, uv + offsetUV[i], 0);
        depths[i] = depth;
    }
}
void SampleDepthCross(Texture2D<half> depthTex, float2 uv, float2 duv,
    out half depths[5])
{
    float du = duv.x;
    float dv = duv.y;

    const float2 offsetUV[5] =
    {
                    {0, dv}, 
        {-du, 0}, {0, 0}, {du, 0},
                    {0, -dv}, 
    };

    [unroll]
    for(int i = 0; i < 5; ++i)
    {
        half depth = depthTex.SampleLevel(Smp_ClampU_ClampV_Linear, uv + offsetUV[i], 0);
        depths[i] = depth;
    }
}
void SampleColor3x3(Texture2D<half4> colorTex, float2 uv, float2 duv,
    out half3 colors[9])
{
    float du = duv.x;
    float dv = duv.y;

    const float2 offsetUV[9] =
    {
        {-du, dv}, {0, dv}, {du, dv},
        {-du, 0}, {0, 0}, {du, 0},
        {-du, -dv}, {0, -dv}, {du, -dv}
    };

    [unroll]
    for(int i = 0; i < 9; ++i)
    {
        half3 color = colorTex.SampleLevel(Smp_ClampU_ClampV_Linear, uv + offsetUV[i], 0);
        colors[i] = color;
    }
}
void SampleColorCross(Texture2D<half4> colorTex, float2 uv, float2 duv,
    out half3 colors[5])
{
    float du = duv.x;
    float dv = duv.y;

    const float2 offsetUV[5] =
    {
                    {0, dv}, 
        {-du, 0}, {0, 0}, {du, 0},
                    {0, -dv},
    };

    [unroll]
    for(int i = 0; i < 5; ++i)
    {
        half3 color = colorTex.SampleLevel(Smp_ClampU_ClampV_Linear, uv + offsetUV[i], 0);
        colors[i] = color;
    }
}

float2 SampleClosestUV3x3(Texture2D<half> depthTex, float2 uv, float2 duv)
{
    half depths[9];
    SampleDepth3x3(depthTex, uv, duv, depths);

    float du = duv.x;
    float dv = duv.y;

    #if UNITY_REVERSED_Z
    half minDepth = HALF_MIN;
    #else
    half minDepth = HALF_MAX;
    #endif
    float2 minUV = uv;
    const float2 offsetUV[9] =
    {
        {-du, dv}, {0, dv}, {du, dv},
        {-du, 0}, {0, 0}, {du, 0},
        {-du, -dv}, {0, -dv}, {du, -dv}
    };

    [unroll]
    for(int i = 0; i < 9; ++i)
    {
        #if UNITY_REVERSED_Z
        const float lerpFactor = step(minDepth, depths[i].r);
        #else
        const float lerpFactor = step(depths[i].r, minDepth);
        #endif

        minDepth = lerp(minDepth, depths[i], lerpFactor);
        minUV = lerp(minUV, minUV + offsetUV[i], lerpFactor);
    }

    return minUV;
}
float2 SampleClosestUVCross(Texture2D<half> depthTex, float2 uv, float2 duv)
{
    half depths[5];
    SampleDepthCross(depthTex, uv, duv, depths);

    float du = duv.x;
    float dv = duv.y;

    #if UNITY_REVERSED_Z
        half minDepth = HALF_MIN;
    #else
        half minDepth = HALF_MAX;
    #endif
    float2 minUV = uv;
    const float2 offsetUV[5] =
    {
                    {0, dv}, 
        {-du, 0}, {0, 0}, {du, 0},
                    {0, -dv}, 
    };

    [unroll]
    for(int i = 0; i < 5; ++i)
    {
        #if UNITY_REVERSED_Z
            const float lerpFactor = step(minDepth, depths[i].r);
        #else
            const float lerpFactor = step(depths[i].r, minDepth);
        #endif

        minDepth = lerp(minDepth, depths[i], lerpFactor);
        minUV = lerp(minUV, minUV + offsetUV[i], lerpFactor);
    }

    return minUV;
}

void SampleMinMax3x3(Texture2D<half4> tex, float2 uv, float2 duv,
    out half3 minColor, out half3 maxColor, out half3 currColor)
{
    half3 colors[9];
    SampleColor3x3(tex, uv, duv, colors);

    minColor = maxColor = colors[0];
    [unroll]
    for(int i = 1; i < 9; ++i)
    {
        minColor = min(minColor, colors[i]);
        maxColor = max(maxColor, colors[i]);
    }
    currColor = colors[4];
}
void SampleMinMaxCross(Texture2D<half4> tex, float2 uv, float2 duv,
    out half3 minColor, out half3 maxColor, out half3 currColor)
{
    half3 colors[5];
    SampleColorCross(tex, uv, duv, colors);

    minColor = maxColor = colors[0];
    [unroll]
    for(int i = 1; i < 5; ++i)
    {
        minColor = min(minColor, colors[i]);
        maxColor = max(maxColor, colors[i]);
    }
    currColor = colors[2];
}

half3 ClampBox(half3 historyColor, half3 minColor, half3 maxColor)
{
    return clamp(historyColor, minColor, maxColor);
}
half3 ClipBox(half3 currColor, half3 minColor, half3 maxColor)
{
    half3 averageColor = (minColor + maxColor) * 0.5f;
    
    half3 toEdgeVec = (maxColor - minColor) * 0.5f;
    half3 toCurrVec = currColor - averageColor;
    half3 unitVec = abs(toCurrVec / max(toEdgeVec, HALF_EPS));
    float unit = max(unitVec.x, max(unitVec.y, max(unitVec.z, HALF_EPS)));
    
    half3 o = lerp(currColor, averageColor + toCurrVec / unit, step(1.f, unit));
    return o;
}
half3 VarianceClipBox(half3 colorMin, half3 colorMax)
{
    half3 averageColor = (colorMin + colorMax) * 0.5f;

    float3 p_clip = 0.5 * (colorMax + colorMin);
    float3 e_clip = 0.5 * (colorMax - colorMin) + FLT_EPS;
    float3 v_clip = colorMax - p_clip;
    float3 v_unit = v_clip / e_clip;
    float3 a_unit = abs(v_unit);
    float ma_unit = max(a_unit.x, max(a_unit.y, a_unit.z));

    if (ma_unit > 1.0)
        return p_clip + v_clip / ma_unit;
    else
        return averageColor;
}

float GetWeight(float2 motionVector)
{
    float weight = saturate(length(motionVector));
    
    float o = lerp(_StaticFrameWeight, _CurrFrameWeight, weight * _DynamiceFrameWeight);
    o = clamp(o, 0.04f, 1.f);
    return o;
}