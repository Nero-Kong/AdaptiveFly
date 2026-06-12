Shader "Hidden/UdpVideo/AiLumaReconstruct"
{
    Properties
    {
        _MainTex("Source", 2D) = "white" {}
        _AiLumaTex("AI Luma", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Overlay" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "AiLumaReconstruct"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            Texture2D _MainTex;
            SamplerState sampler_MainTex;
            float4 _MainTex_TexelSize;

            Texture2D _AiLumaTex;
            SamplerState sampler_AiLumaTex;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float2 uv = float2((input.vertexID << 1) & 2, input.vertexID & 2);
                output.uv = uv;
                output.positionCS = float4(uv * 2.0 - 1.0, 0.0, 1.0);
                output.positionCS.y *= -1.0;
                return output;
            }

            float CubicWeight(float x)
            {
                x = abs(x);
                float x2 = x * x;
                float x3 = x2 * x;

                if (x <= 1.0)
                {
                    return 1.5 * x3 - 2.5 * x2 + 1.0;
                }

                if (x < 2.0)
                {
                    return -0.5 * x3 + 2.5 * x2 - 4.0 * x + 2.0;
                }

                return 0.0;
            }

            float4 SampleBicubic(float2 uv)
            {
                float2 texSize = 1.0 / _MainTex_TexelSize.xy;
                float2 pixel = uv * texSize - 0.5;
                float2 basePixel = floor(pixel);
                float2 fraction = pixel - basePixel;

                float4 result = 0.0;
                float totalWeight = 0.0;

                [unroll]
                for (int y = -1; y <= 2; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 2; x++)
                    {
                        float2 offset = float2(x, y);
                        float2 samplePixel = basePixel + offset + 0.5;
                        float2 sampleUv = samplePixel / texSize;
                        float weight = CubicWeight(offset.x - fraction.x) * CubicWeight(offset.y - fraction.y);
                        result += _MainTex.SampleLevel(sampler_MainTex, sampleUv, 0) * weight;
                        totalWeight += weight;
                    }
                }

                return result / max(totalWeight, 1e-5);
            }

            float3 RGBToYCbCr(float3 rgb)
            {
                float y = dot(rgb, float3(0.299, 0.587, 0.114));
                float cb = (rgb.b - y) * 0.564 + 0.5;
                float cr = (rgb.r - y) * 0.713 + 0.5;
                return float3(y, cb, cr);
            }

            float3 YCbCrToRGB(float3 ycbcr)
            {
                float y = ycbcr.x;
                float cb = ycbcr.y - 0.5;
                float cr = ycbcr.z - 0.5;
                float r = y + 1.403 * cr;
                float g = y - 0.344 * cb - 0.714 * cr;
                float b = y + 1.773 * cb;
                return float3(r, g, b);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float3 sourceRgb = SampleBicubic(uv).rgb;
                float3 ycbcr = RGBToYCbCr(sourceRgb);
                float aiY = _AiLumaTex.SampleLevel(sampler_AiLumaTex, uv, 0).r;
                ycbcr.x = aiY;
                float3 reconstructed = saturate(YCbCrToRGB(ycbcr));
                return float4(reconstructed, 1.0);
            }
            ENDHLSL
        }
    }
}
