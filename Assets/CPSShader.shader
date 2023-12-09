Shader "Unlit/ParticleShader"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite On

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
                float3 Position;
                float3 Scale;
                float3 Velocity;
                float3 ExternalVelocity;
                float3 Colour;
                float2 Current_Max_Life;
            };
            StructuredBuffer<SimulationState> SimulationStateBuffer;
            cbuffer GlobalState
            {
                // Kernel Stuff
                int DISPATCH_NUM;
                int MAX_PARTICLE_COUNT;

                // Time Stuff
                float DeltaTime;
                float Time;

                // Environment Stuff
                float GravityForce;
    
                // TODO: Expand existing ones
 
                // Lifetime
                int LifetimeScalarType;
                float ExactLifetime;
                float BottomLifetime;
                float TopLifetime;

                // Position
                int PositionFunctionType;
                float3 CenterOffset;
                float Radius;
    
                // Velocity
                int VelocityScalarType;
                float3 ExactVelocity;
                float3 BottomVelocity;
                float3 TopVelocity;
    
                // Scale
                int ScaleScalarType;
                float3 ExactScale;
                float3 BottomScale;
                float3 TopScale;
    
                // Rotation
                int RotationScalarType;
                float3 ExactRotation;
                float3 BottomRotation;
                float3 TopRotation;
    
                // TODO: Expand new ones
            };

            struct GEOM_IN
            {
                uint ID           : TEXCOORD0;
            };

            struct FRAG_IN
            {
                float4 PositionCS   : SV_POSITION;
                half4  Colour       : COLOR;
                float2 UV           : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;


            CBUFFER_START(UnityPerMaterial)
                float4      ObjectPoints[4];
                float4x4    ObjectToWorld;
            CBUFFER_END

            GEOM_IN vert (uint VertexID : SV_VertexID)
            {
                InitIndirectDrawArgs(0); 
                GEOM_IN OUT = (GEOM_IN)0;
                OUT.ID = GetIndirectVertexID(VertexID);
                return OUT;
            }

            static float2 UVs[4] =
            {
                float2(0, 0),
                float2(0, 1),
                float2(1, 0),
                float2(1, 1)
            };

            [maxvertexcount(4)]
            void geom(point GEOM_IN input[1], inout TriangleStream<FRAG_IN> output)
            {
                FRAG_IN OUT = (FRAG_IN)0;

                uint id = input[0].ID;
                if(SimulationStateBuffer[id].Current_Max_Life.x <= 0) { return; }

                float4 offsetWS = float4(SimulationStateBuffer[id].Position, 1);
                float4 scale    = float4(SimulationStateBuffer[id].Scale,    1);
                OUT.Colour      = half4(SimulationStateBuffer[id].Colour,    1);

                [unroll]
                for(int i = 0; i < 4; i++)
                {
                    float4 posWS = mul(ObjectToWorld, ObjectPoints[i] * scale);
                    OUT.PositionCS = UnityWorldToClipPos(posWS + offsetWS);
                    OUT.UV = UVs[i];
                    output.Append(OUT);
                }
                
                output.RestartStrip();
            }

            half4 frag (FRAG_IN IN) : SV_Target
            {
                half4 texCol = tex2D(_MainTex, IN.UV);
                if(texCol.a < 0.6) discard;
                return IN.Colour * texCol;
            }
            
            ENDCG
        }
    }
}
