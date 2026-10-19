Shader "AZUMI/RenderFeature/SSPR/Example/Guideline_Ref"
{
    Properties
    {
        _LineTex("Line Texture", 2D) = "black" {}
        _BaseColor("Base Color", Color) = (.5, .5, .5, 1)
        _LineColor("Line Color", Color) = (1, 1, 1, 1)
        _F0("F0 (* 0.001)", Float) = 4
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"

    struct Attributes {
        float4 positionOS : POSITION; // 本地坐标位置
        float3 normal : NORMAL; // 法线方向
        float2 uv : TEXCOORD0; // 纹理坐标
    };

    struct Varyings {
        float4 positionCS : SV_POSITION; // 剪裁空间位置
        float4 positionNDC : TEXCOORD0; // 归一化设备坐标
        float2 uv : TEXCOORD1; // 纹理坐标
        float3 normalWS : TEXCOORD2; // 法线方向
        float3 viewDirWS : TEXCOORD5; // 视图方向
    };

    TEXTURE2D(_LineTex);SAMPLER(sampler_LineTex);
    TEXTURE2D(_SSPRReflectionTexture);SAMPLER(sampler_SSPRReflectionTexture);

    CBUFFER_START(UnityPerMaterial)
        float4 _BaseColor;
        float4 _LineColor;
        float _F0;
    CBUFFER_END

    // 顶点着色器
    Varyings GuidelinePassVertex(Attributes input) {
        Varyings output;
        VertexPositionInputs vertexInputs = GetVertexPositionInputs(input.positionOS.xyz);
        output.positionCS = vertexInputs.positionCS;
        output.positionNDC = vertexInputs.positionNDC;
        output.normalWS = mul(input.normal,(float3x3)UNITY_MATRIX_I_M);
        output.viewDirWS = normalize(_WorldSpaceCameraPos.xyz - vertexInputs.positionWS.xyz);
        output.uv = input.uv;
        return output;
    }

    // 片段着色器
    float4 GuidelinePassFragment(Varyings input) : SV_Target {
        float2 uv = input.uv;
        float mask = SAMPLE_TEXTURE2D(_LineTex, sampler_LineTex, uv).a;
        float3 col = lerp(_BaseColor.xyz, _LineColor.xyz, mask);

        float fresnel = saturate(dot(input.normalWS, normalize(input.viewDirWS)));
        float f0 = _F0 * 0.001;
        fresnel = f0 + (1.0 - f0) * pow(1.0 - fresnel, 5.0);

        float2 suv = input.positionNDC.xy / input.positionNDC.w; // 计算屏幕 UV
        half4 refColor = SAMPLE_TEXTURE2D(_SSPRReflectionTexture, sampler_SSPRReflectionTexture, suv); // 采样反射纹理
        // 以水平面的反射方向采样环境，补全屏幕空间反射无法覆盖的天空。
        float3 reflectDir = reflect(-normalize(input.viewDirWS), float3(0.0, 1.0, 0.0));
        half4 environment = SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0, reflectDir, 0);
        half3 skyColor = DecodeHDREnvironment(environment, unity_SpecCube0_HDR);
        float3 reflectionColor = refColor.rgb + skyColor * (1.0 - saturate(refColor.a));


        float3 finalCol = lerp(col, reflectionColor, fresnel);

        return float4(finalCol,1.0);
    }

    ENDHLSL

    SubShader {

        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass {
            Name "Guideline Pass"

            HLSLPROGRAM
            #pragma vertex GuidelinePassVertex
            #pragma fragment GuidelinePassFragment
            ENDHLSL
        }
    }
}
