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
            CGPROGRAM

            #pragma target 5.0

            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag

            #include "UnityCG.cginc"
            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawArgs
            #include "UnityIndirect.cginc"

            struct SimulationState
            {
                float3 OffsetWS;
                float3 Scale;
            };

            struct GEOM_IN
            {
                uint ID           : TEXCOORD0;
            };

            struct FRAG_IN
            {
                float4 PositionCS   : SV_POSITION;
                float2 UV           : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            StructuredBuffer<SimulationState> SimulationStateBuffer;

            CBUFFER_START(UnityPerMaterial)
                float4      ObjectPoints[4];
                float4x4    ObjectToWorld;
                float4      GlobalColor;
            CBUFFER_END

            GEOM_IN vert (uint VertexID : SV_VertexID)
            {
                InitIndirectDrawArgs(0); 
                GEOM_IN OUT = (GEOM_IN)0;
                OUT.ID = GetIndirectVertexID(VertexID);
                return OUT;
            }

            [maxvertexcount(4)]
            void geom(point GEOM_IN input[1], inout TriangleStream<FRAG_IN> output)
            {
                FRAG_IN OUT = (FRAG_IN)0;

                uint id = input[0].ID;
                float4 offsetWS = float4(SimulationStateBuffer[id].OffsetWS, 1);
                float4 scale    = float4(SimulationStateBuffer[id].Scale, 1);

                [unroll]
                for(int i = 0; i < 4; i++)
                {
                    float4 posWS = mul(ObjectToWorld, ObjectPoints[i] * scale);
                    OUT.PositionCS = UnityWorldToClipPos(posWS + offsetWS);
                    output.Append(OUT);
                }
                
                output.RestartStrip();
            }

            half4 frag (FRAG_IN i) : SV_Target
            {
                // fixed4 col = tex2D(_MainTex, i.uv);
                return float4(1,1,1,1);
            }
            
            ENDCG
        }
    }
}
