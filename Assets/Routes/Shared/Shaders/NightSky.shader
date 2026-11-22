// Night sky for the school: dark gradient, hashed stars, a pale moon. Used as the scene skybox.
Shader "GroundBreaking/NightSky"
{
    Properties
    {
        _TopColor ("Top", Color) = (0.015, 0.02, 0.05, 1)
        _HorizonColor ("Horizon", Color) = (0.06, 0.07, 0.11, 1)
        _GroundColor ("Ground", Color) = (0.01, 0.01, 0.015, 1)
        _StarDensity ("Star density", Range(50, 600)) = 260
        _StarBrightness ("Star brightness", Range(0, 3)) = 1.2
        _MoonDirection ("Moon direction", Vector) = (-0.45, 0.55, 0.7, 0)
        _MoonColor ("Moon", Color) = (0.85, 0.9, 1, 1)
        _MoonSize ("Moon size", Range(0.005, 0.08)) = 0.03
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _TopColor, _HorizonColor, _GroundColor, _MoonDirection, _MoonColor;
                float _StarDensity, _StarBrightness, _MoonSize;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 d = normalize(input.direction);
                float up = d.y;
                float3 color = up >= 0
                    ? lerp(_HorizonColor.rgb, _TopColor.rgb, pow(saturate(up), 0.6))
                    : lerp(_HorizonColor.rgb, _GroundColor.rgb, saturate(-up * 4));

                // Stars: one candidate per cell of a direction grid, only above the horizon.
                float3 cell = floor(d * _StarDensity);
                float h = Hash(cell);
                float3 centre = (cell + 0.5) / _StarDensity;
                float spot = saturate(1 - length(d - normalize(centre)) * _StarDensity * 1.6);
                float twinkle = 0.7 + 0.3 * sin(_Time.y * (1.5 + h * 4) + h * 40);
                color += step(0.985, h) * spot * spot * _StarBrightness * twinkle * saturate(up * 6);

                // Moon with a soft halo.
                float3 moonDir = normalize(_MoonDirection.xyz);
                float angle = acos(saturate(dot(d, moonDir)));
                color += _MoonColor.rgb * smoothstep(_MoonSize, _MoonSize * 0.85, angle);
                color += _MoonColor.rgb * 0.08 * exp(-angle * 9);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
