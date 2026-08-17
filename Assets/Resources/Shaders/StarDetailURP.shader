Shader "Observatorio/StarDetailURP"
{
    Properties
    {
        _BaseColor ("Photosphere Color", Color) = (1, 0.78, 0.42, 1)
        _CellColor ("Cell Color", Color) = (0.72, 0.13, 0.02, 1)
        _RimColor ("Rim Color", Color) = (1, 0.94, 0.72, 1)
        _EmissionStrength ("Emission Strength", Float) = 4
        _GranulationScale ("Granulation Scale", Float) = 3.7
        _GranulationSpeed ("Granulation Speed", Float) = 0.48
        _Activity ("Activity", Range(0, 1)) = 0.7
        _TimeSeed ("Time Seed", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "StarDetail"
            Tags { "LightMode" = "UniversalForward" }

            Blend One Zero
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _CellColor;
                half4 _RimColor;
                float _EmissionStrength;
                float _GranulationScale;
                float _GranulationSpeed;
                float _Activity;
                float _TimeSeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            float Hash(float3 point)
            {
                point = frac(point * 0.3183099 + 0.1);
                point *= 17.0;
                return frac(point.x * point.y * point.z * (point.x + point.y + point.z));
            }

            float ValueNoise(float3 point)
            {
                float3 cell = floor(point);
                float3 local = frac(point);
                local = local * local * (3.0 - 2.0 * local);

                float n000 = Hash(cell + float3(0, 0, 0));
                float n100 = Hash(cell + float3(1, 0, 0));
                float n010 = Hash(cell + float3(0, 1, 0));
                float n110 = Hash(cell + float3(1, 1, 0));
                float n001 = Hash(cell + float3(0, 0, 1));
                float n101 = Hash(cell + float3(1, 0, 1));
                float n011 = Hash(cell + float3(0, 1, 1));
                float n111 = Hash(cell + float3(1, 1, 1));

                float nx00 = lerp(n000, n100, local.x);
                float nx10 = lerp(n010, n110, local.x);
                float nx01 = lerp(n001, n101, local.x);
                float nx11 = lerp(n011, n111, local.x);
                float nxy0 = lerp(nx00, nx10, local.y);
                float nxy1 = lerp(nx01, nx11, local.y);
                return lerp(nxy0, nxy1, local.z);
            }

            float Granulation(float3 point)
            {
                float total = 0.0;
                float amplitude = 0.58;
                float frequency = 1.0;

                [unroll]
                for (int octave = 0; octave < 3; octave++)
                {
                    total += ValueNoise(point * frequency) * amplitude;
                    frequency *= 2.15;
                    amplitude *= 0.5;
                }

                return saturate(total / 1.01);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.positionOS = input.positionOS.xyz;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 surfaceDirection = normalize(input.positionOS);
                float phase = _Time.y * _GranulationSpeed + _TimeSeed * 6.2831853;
                float3 motion = float3(phase, phase * 0.63, -phase * 0.38);
                float granulation = Granulation(surfaceDirection * _GranulationScale + motion);
                float filament = ValueNoise(surfaceDirection * (_GranulationScale * 5.2) - motion * 1.7);
                float cellMix = saturate(granulation * 0.78 + filament * 0.22);

                half3 photosphere = lerp(_CellColor.rgb, _BaseColor.rgb, cellMix);
                float3 viewDirection = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                float facing = saturate(dot(normalize(input.normalWS), viewDirection));
                float limb = lerp(0.46, 1.0, pow(facing, 0.45));
                float activityBand = saturate(sin((surfaceDirection.y * 10.0 + surfaceDirection.x * 6.0) + phase * 2.2));
                float activity = pow(activityBand, 12.0) * _Activity * 0.35;
                float rim = pow(1.0 - facing, 4.0) * 0.45;

                half3 emittedColor = photosphere * limb;
                emittedColor += _BaseColor.rgb * activity;
                emittedColor = lerp(emittedColor, _RimColor.rgb, rim);
                return half4(emittedColor * _EmissionStrength, 1.0);
            }
            ENDHLSL
        }
    }
}
