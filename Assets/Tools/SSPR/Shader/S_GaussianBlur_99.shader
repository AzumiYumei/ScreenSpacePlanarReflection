Shader "AZUMI/RenderFeature/SSPR/GaussianBlur99"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    // The Blit.hlsl file provides the vertex shader (Vert),
    // the input structure (Attributes), and the output structure (Varyings)
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

    float _BlurRadius;
    float _BlurHeightScale;
    float _ReflectPlaneHeight;
    Texture2D<int> _SSPRHeightBuffer;

    float4 _BlitTexture_TexelSize;

    float GetBlurRadius(float2 uv)
    {
        uint width, height;
        _SSPRHeightBuffer.GetDimensions(width, height);
        uint2 pixel = min((uint2)(saturate(uv) * float2(width, height)), uint2(width, height) - 1);
        int heightValue = _SSPRHeightBuffer.Load(int3(pixel, 0));
        if (heightValue == 2147483647) return _BlurRadius;

        // 还原放大一百万倍的世界空间高度，物体离反射平面越高，半径越大。
        float heightAbovePlane = max(0.0, heightValue * 1e-6 - _ReflectPlaneHeight);
        return _BlurRadius * (1.0 + heightAbovePlane * max(0.0, _BlurHeightScale));
    }

    float4 BlurVertical(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);
        float texelSize = GetBlurRadius(uv) * _BlitTexture_TexelSize.x;

        // 9 - tap gaussian blur on the downsampled source
        float4 c0 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(texelSize * 4.0, 0.0));
        float4 c1 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(texelSize * 3.0, 0.0));
        float4 c2 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(texelSize * 2.0, 0.0));
        float4 c3 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(texelSize * 1.0, 0.0));
        float4 c4 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
        float4 c5 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(texelSize * 1.0, 0.0));
        float4 c6 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(texelSize * 2.0, 0.0));
        float4 c7 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(texelSize * 3.0, 0.0));
        float4 c8 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(texelSize * 4.0, 0.0));

        float4 color = c0 * 0.01621622 + c1 * 0.05405405 + c2 * 0.12162162 + c3 * 0.19459459
        + c4 * 0.22702703
        + c5 * 0.19459459 + c6 * 0.12162162 + c7 * 0.05405405 + c8 * 0.01621622;
        return color;
    }

    float4 BlurHorizontal(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);
        float texelSize = GetBlurRadius(uv) * _BlitTexture_TexelSize.y;

        // 9 - tap gaussian blur on the downsampled source
        float4 c0 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(0.0, texelSize * 4.0));
        float4 c1 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(0.0, texelSize * 3.0));
        float4 c2 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(0.0, texelSize * 2.0));
        float4 c3 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(0.0, texelSize * 1.0));
        float4 c4 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
        float4 c5 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0.0, texelSize * 1.0));
        float4 c6 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0.0, texelSize * 2.0));
        float4 c7 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0.0, texelSize * 3.0));
        float4 c8 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0.0, texelSize * 4.0));

        float4 color = c0 * 0.01621622 + c1 * 0.05405405 + c2 * 0.12162162 + c3 * 0.19459459
        + c4 * 0.22702703
        + c5 * 0.19459459 + c6 * 0.12162162 + c7 * 0.05405405 + c8 * 0.01621622;
        return color;
    }
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100
        ZWrite Off Cull Off
        Pass
        {
            Name "BlurPassVertical"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment BlurVertical
            ENDHLSL
        }

        Pass
        {
            Name "BlurPassHorizontal"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment BlurHorizontal
            ENDHLSL
        }
    }
}
