Shader "Observatorio/ConstellationLineURP"
{
    Properties
    {
        _LineColor ("Line Color", Color) = (0.2, 0.8, 1, 0.75)
        _EmissionStrength ("Emission Strength", Float) = 1.45
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
            Name "ConstellationLine"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _LineColor;
                float _EmissionStrength;
                float4 _SkyCenter;
                float _HorizonClipEnabled;
                float _HorizonClipHeight;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float horizonDistance : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                // Geometry is local to a runtime object centered on the observer. Keep the
                // horizon calculation relative to that center, matching the star dome.
                float3 worldPosition = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(worldPosition);
                output.horizonDistance = worldPosition.y - _SkyCenter.y - _HorizonClipHeight;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (_HorizonClipEnabled > 0.5)
                {
                    clip(input.horizonDistance);
                }

                // Additive blending expects premultiplied intensity. Respect the inspector
                // alpha so designers can soften the tracing without changing its hue.
                return half4(_LineColor.rgb * _LineColor.a * _EmissionStrength, _LineColor.a);
            }
            ENDHLSL
        }
    }
}
