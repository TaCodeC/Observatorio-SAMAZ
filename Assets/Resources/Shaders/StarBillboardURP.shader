Shader "Observatorio/StarBillboardURP"
{
    Properties
    {
        _GlobalIntensity ("Global Intensity", Float) = 1.35
        _PointSoftness ("Point Softness", Float) = 1.9
        _TwinkleStrength ("Twinkle Strength", Range(0, 1)) = 0.18
        _TwinkleSpeed ("Twinkle Speed", Float) = 1.1
        _SizeMultiplier ("Size Multiplier", Float) = 1.25
        _SkyCenter ("Sky Center", Vector) = (0, 0, 0, 0)
        _HorizonClipEnabled ("Horizon Clip Enabled", Float) = 0
        _HorizonClipHeight ("Horizon Clip Height", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "StarBillboard"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _GlobalIntensity;
                float _PointSoftness;
                float _TwinkleStrength;
                float _TwinkleSpeed;
                float _SizeMultiplier;
                float4 _SkyCenter;
                float _HorizonClipEnabled;
                float _HorizonClipHeight;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(StarProps)
                UNITY_DEFINE_INSTANCED_PROP(float4, _StarColor)
                UNITY_DEFINE_INSTANCED_PROP(float4, _StarTwinkle)
            UNITY_INSTANCING_BUFFER_END(StarProps)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float4 twinkle : TEXCOORD1;
                float3 centerOS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                float twinkleSeed : TEXCOORD1;
                float horizonDistance : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

#if defined(UNITY_INSTANCING_ENABLED)
                float3 positionOS = input.positionOS.xyz * _SizeMultiplier;
#else
                float3 positionOS = input.centerOS + (input.positionOS.xyz - input.centerOS) * _SizeMultiplier;
#endif

                float3 worldPosition = TransformObjectToWorld(positionOS);
                output.horizonDistance = worldPosition.y - _HorizonClipHeight;
                output.positionHCS = TransformWorldToHClip(worldPosition + _SkyCenter.xyz);
                output.uv = input.uv;
#if defined(UNITY_INSTANCING_ENABLED)
                output.color = UNITY_ACCESS_INSTANCED_PROP(StarProps, _StarColor);
                output.twinkleSeed = UNITY_ACCESS_INSTANCED_PROP(StarProps, _StarTwinkle).x;
#else
                output.color = input.color;
                output.twinkleSeed = input.twinkle.x;
#endif
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (_HorizonClipEnabled > 0.5)
                {
                    clip(input.horizonDistance);
                }

                float2 centeredUv = input.uv * 2.0 - 1.0;
                float radiusSquared = dot(centeredUv, centeredUv);
                float disk = saturate(1.0 - radiusSquared);
                float core = pow(disk, max(0.25, _PointSoftness));
                clip(core - 0.003);

                float phase = sin(_Time.y * _TwinkleSpeed + input.twinkleSeed * 6.2831853);
                float twinkle = 1.0 + phase * _TwinkleStrength * input.color.a;
                half3 rgb = input.color.rgb * _GlobalIntensity * core * twinkle;
                return half4(rgb, core);
            }
            ENDHLSL
        }
    }
}
