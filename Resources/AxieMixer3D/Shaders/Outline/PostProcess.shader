Shader "Axie Mixer 3D/Outline/PostProcess"
{
    Properties
    {
        _MainTex ("Main Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100

        // Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "OutlinePostProcess"

            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            // Outline parameters
            float _Thickness;
            float3 _Color;
            float _DepthScale;
            float _DepthBias;
            float _NormalScale;
            float _NormalBias;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = mul(UNITY_MATRIX_MVP, input.positionOS);
                output.uv = ComputeScreenPos(output.positionCS).xy;
                return output;
            }

            float LinearizeDepth(float z) {
                // Orthographic Projection
                if (unity_OrthoParams.x > 0.5) {
#if UNITY_REVERSED_Z
                    return 1.0 - z;
#else
                    return z;
#endif
                } else {
                    return Linear01Depth(z, _ZBufferParams);
                }
            }

            float SampleLinearDepth(float2 uv) {
                return LinearizeDepth(SampleSceneDepth(uv));
            }

            float SobelDepth(float2 uv, float4x2 adjacentUVs) {
                float dc = SampleLinearDepth(uv);
                float4 d = float4(
                    SampleLinearDepth(adjacentUVs[0]),
                    SampleLinearDepth(adjacentUVs[1]),
                    SampleLinearDepth(adjacentUVs[2]),
                    SampleLinearDepth(adjacentUVs[3])
                );
                return pow(length(d - dc) * _DepthScale, _DepthBias);
            }

            float SobelNormal(float2 uv, float4x2 adjacentUVs) {
                float3 nc = SampleSceneNormals(uv);
                float3 nt = SampleSceneNormals(adjacentUVs[0]);
                float3 nb = SampleSceneNormals(adjacentUVs[1]);
                float3 nr = SampleSceneNormals(adjacentUVs[2]);
                float3 nl = SampleSceneNormals(adjacentUVs[3]);
                nt -= nc;
                nb -= nc;
                nr -= nc;
                nl -= nc;
                float n = sqrt(dot(nt, nt) + dot(nb, nb) + dot(nr, nr) + dot(nl, nl));
                return pow(n * _NormalScale, _NormalBias);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float3 offset = float3(_Thickness / _ScreenParams.xy, 0.0) * _Thickness;
                float4x2 adjacentUVs = float4x2(
                    input.uv + offset.xz,
                    input.uv - offset.xz,
                    input.uv + offset.zy,
                    input.uv - offset.zy
                );
                float sobelDepth = SobelDepth(input.uv, adjacentUVs);
                float sobelNormal = SobelNormal(input.uv, adjacentUVs);
                float sobelAlpha = saturate(max(sobelDepth, sobelNormal));
                return float4(_Color, sobelAlpha);
            }
            ENDHLSL
        }
    }
}
