Shader "Axie Mixer 3D/Outline/PostProcess"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100

        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "OutlinePostProcess"

            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            // Blit.hlsl supplies the fullscreen-triangle Vert/Varyings used by URP's
            // RenderGraph full-screen passes (procedural triangle from SV_VertexID).
            // Varyings.texcoord is the [0,1] screen UV.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            // Outline parameters
            float _Thickness;
            float3 _Color;
            float _DepthScale;
            float _DepthBias;
            float _NormalScale;
            float _NormalBias;

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
                float2 uv = input.texcoord;
                float3 offset = float3(_Thickness / _ScreenParams.xy, 0.0);
                float4x2 adjacentUVs = float4x2(
                    uv + offset.xz,
                    uv - offset.xz,
                    uv + offset.zy,
                    uv - offset.zy
                );
                float sobelDepth = SobelDepth(uv, adjacentUVs);
                float sobelNormal = SobelNormal(uv, adjacentUVs);
                float sobelAlpha = saturate(max(sobelDepth, sobelNormal));
                return float4(_Color, sobelAlpha);
            }
            ENDHLSL
        }
    }
}
