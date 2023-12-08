Shader "Unlit/ParticleShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            HLSLPROGRAM

            #pragma target 5.0

            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct SimulationState
            {
                float3 Position;
            };

            struct GlobalState
            {
                float3 padding;
            };

            struct VERT_IN
            {
                float4 PositionOS   : POSITION;
                uint   ID           : SV_VERTEXID;
            };

            struct GEOM_IN
            {
                float4 PositionWS   : POSITION;
            };

            struct FRAG_IN
            {
                float4 PositionCS   : SV_POSITION;
                float2 UV           : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            float3 CameraX;
            float3 CameraY;

            RWStructuredBuffer<SimulationState> SimulationStateBuffer;
            RWStructuredBuffer<GlobalState>     GlobalStateBuffer;

            CBUFFER_START(UnityPerMaterial)
            CBUFFER_END

            GEOM_IN vert (VERT_IN IN)
            {
                GEOM_IN OUT = (GEOM_IN)0;
                OUT.PositionWS = float4(TransformObjectToWorld(IN.PositionOS.xyz) + SimulationStateBuffer[IN.ID].Position, 0);
                return OUT;
            }

            [maxvertexcount(4)]
            void geom(point GEOM_IN input[1], inout TriangleStream<FRAG_IN> output)
            {
                FRAG_IN OUT = (FRAG_IN)0;

                OUT.PositionCS = TransformWorldToHClip(input[0].PositionWS - CameraX - CameraY);
                output.Append(OUT);
                OUT.PositionCS = TransformWorldToHClip(input[0].PositionWS - CameraX + CameraY);
                output.Append(OUT);
                OUT.PositionCS = TransformWorldToHClip(input[0].PositionWS + CameraX - CameraY);
                output.Append(OUT);
                OUT.PositionCS = TransformWorldToHClip(input[0].PositionWS + CameraX + CameraY);
                output.Append(OUT);

                output.RestartStrip();
            }

            half4 frag (FRAG_IN i) : SV_Target
            {
                // fixed4 col = tex2D(_MainTex, i.uv);
                return half4(1,1,1,1);
            }
            
            ENDHLSL
        }
    }
}
