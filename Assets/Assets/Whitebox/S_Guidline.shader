Shader "AZUMI/RenderFeature/SSPR/Example/Guideline"
{
    Properties
    {
        _LineTex("Line Texture", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (.5, .5, .5, 1)
        _LineColor("Line Color", Color) = (1, 1, 1, 1)
    }

    HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        struct Attributes {
            float4 positionOS : POSITION; // 本地坐标位置
            float2 uv : TEXCOORD0; // 纹理坐标坐标
        };

        struct Varyings {
            float4 positionCS : SV_POSITION; // 剪裁空间位置
            float2 uv : TEXCOORD0; // 纹理坐标坐标
        };

        TEXTURE2D(_LineTex);SAMPLER(sampler_LineTex);

        CBUFFER_START(UnityPerMaterial)
        float4 _LineTex_ST;
        float4 _BaseColor;
        float4 _LineColor;
        CBUFFER_END

        // 顶点着色器
        Varyings GuidelinePassVertex(Attributes input) {
            Varyings output;
            VertexPositionInputs vertexInputs = GetVertexPositionInputs(input.positionOS.xyz);
            output.positionCS = vertexInputs.positionCS;
            output.uv = TRANSFORM_TEX(input.uv, _LineTex);
            return output;
        }

        // 片段着色器
        float4 GuidelinePassFragment(Varyings input) : SV_Target {
            float2 uv = input.uv;
            float mask = SAMPLE_TEXTURE2D(_LineTex, sampler_LineTex, uv).a;
            float3 finalCol = lerp(_BaseColor.xyz, _LineColor.xyz, mask);
            return float4(finalCol,1.0);
        }

    ENDHLSL

    SubShader {

        Tags {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass {
            Name "Guideline Pass"

            HLSLPROGRAM
            #pragma vertex GuidelinePassVertex
            #pragma fragment GuidelinePassFragment
            ENDHLSL
        }
    }
}
