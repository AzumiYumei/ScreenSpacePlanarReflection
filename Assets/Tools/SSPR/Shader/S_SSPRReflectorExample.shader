Shader "AZUMI/RenderFeature/SSPR/Example/SSPRReflector" 
{
    HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        struct Attributes {
            float4 positionOS : POSITION; // 本地坐标位置
        };

        struct Varyings {
            float4 positionCS : SV_POSITION; // 剪裁空间位置
            float4 positionNDC : TEXCOORD0; // 归一化设备坐标
        };


        TEXTURE2D(_SSPRReflectionTexture);SAMPLER(sampler_SSPRReflectionTexture);

        // 顶点着色器
        Varyings SSPRReflectorPassVertex(Attributes input) {
            Varyings output;
            VertexPositionInputs vertexInputs = GetVertexPositionInputs(input.positionOS.xyz);
            output.positionCS = vertexInputs.positionCS;
            output.positionNDC = vertexInputs.positionNDC;
            return output;
        }

        // 片段着色器
        half4 SSPRReflectorPassFragment(Varyings input) : SV_Target {
            float2 suv = input.positionNDC.xy / input.positionNDC.w; // 计算屏幕UV
            half4 finalCol = SAMPLE_TEXTURE2D(_SSPRReflectionTexture, sampler_SSPRReflectionTexture, suv); // 采样反射纹理
            return half4(finalCol.xyz, 1.0); // 返回最终颜色
        }
    
    ENDHLSL


    SubShader {
        
        Tags {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass {
            Name "SSPR Reflector Pass"

            HLSLPROGRAM
            #pragma vertex SSPRReflectorPassVertex
            #pragma fragment SSPRReflectorPassFragment
            ENDHLSL
        }
    }
}
