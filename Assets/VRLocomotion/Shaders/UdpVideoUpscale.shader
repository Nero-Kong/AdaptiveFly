Shader "Hidden/UdpVideo/BicubicSharpen"
{
    Properties
    {
        _MainTex("Source", 2D) = "white" {}
        _Sharpness("Sharpness", Range(0, 1)) = 0.3
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Overlay" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "BicubicSharpen"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            Texture2D _MainTex;
            SamplerState sampler_MainTex;
            float4 _MainTex_TexelSize;
            float _Sharpness;

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

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float4 upscaled = SampleBicubic(uv);

                float2 texel = _MainTex_TexelSize.xy;
                float3 blur =
                    _MainTex.SampleLevel(sampler_MainTex, uv + float2(texel.x, 0.0), 0).rgb +
                    _MainTex.SampleLevel(sampler_MainTex, uv - float2(texel.x, 0.0), 0).rgb +
                    _MainTex.SampleLevel(sampler_MainTex, uv + float2(0.0, texel.y), 0).rgb +
                    _MainTex.SampleLevel(sampler_MainTex, uv - float2(0.0, texel.y), 0).rgb;
                blur *= 0.25;

                float3 sharpened = upscaled.rgb + (upscaled.rgb - blur) * _Sharpness;
                return float4(saturate(sharpened), upscaled.a);
            }
            ENDHLSL
        }
    }
}
