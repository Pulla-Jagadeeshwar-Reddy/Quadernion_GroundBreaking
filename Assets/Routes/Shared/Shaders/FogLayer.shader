// Drifting mist under the floor: a large flat layer whose alpha is scrolling world-space noise,
// fading out near the camera so a player looking down never sees a hard plane. Several layers
// stacked at different depths (FloorPolish.PlaceFogBelow) read as an endless foggy drop.
// With _Opaque = 1 it is a plain fogged base colour (the bottom of the stack).
Shader "GroundBreaking/FogLayer"
{
    Properties
    {
        _Color ("Mist colour", Color) = (0.09, 0.10, 0.13, 1)
        _Density ("Density", Range(0, 1)) = 0.55
        _Scale ("Noise scale (m)", Float) = 14
        _Speed ("Drift speed", Vector) = (0.25, 0.12, 0, 0)
        _NearFade ("Fade-in distance (m)", Float) = 3
        _Opaque ("Opaque base", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-50" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color, _Speed;
                float _Density, _Scale, _NearFade, _Opaque;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float fogFactor : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x),
                            lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }

            float Fbm(float2 p)
            {
                float sum = 0, amp = 0.5;
                for (int octave = 0; octave < 4; octave++)
                {
                    sum += Noise(p) * amp;
                    p = p * 2.03 + 17.1;
                    amp *= 0.5;
                }
                return sum;
            }

            half4 frag(Varyings input) : SV_Target
            {
                if (_Opaque > 0.5)
                    return half4(MixFog(_Color.rgb, input.fogFactor), 1);

                float2 p = input.positionWS.xz / max(_Scale, 0.01) + _Time.y * _Speed.xy * 0.1;
                float n = Fbm(p);
                float alpha = saturate((n - 0.25) * 1.6) * _Density;
                float distance = length(input.positionWS - GetCameraPositionWS());
                alpha *= saturate(distance / max(_NearFade, 0.01));
                return half4(MixFog(_Color.rgb * (0.8 + 0.4 * n), input.fogFactor), alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
