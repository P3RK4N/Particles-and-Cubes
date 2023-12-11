Shader "Unlit/CPSMeshShader"
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

            static const int LOCAL_SPACE        = 0;
            static const int GLOBAL_SPACE       = 1;

            static const int RENDER_BILLBOARD   = 0;
            static const int RENDER_POINT       = 1;
            static const int RENDER_MESH        = 2;

            static const int SCALAR_EXACT       = 0;
            static const int SCALAR_RANGED      = 1;
            static const int SCALAR_GAUSSIAN    = 2;

            static const int FUNCTION_POINT     = 0;
            static const int FUNCTION_SPHERE    = 1;
            static const int FUNCTION_PLANE     = 2;
            static const int FUNCTION_CUBOID    = 3;

            static const int FALLOFF_ROOT       = 0;
            static const int FALLOFF_LINEAR     = 1;
            static const int FALLOFF_QUADRATIC  = 2;
            static const int FALLOFF_CUBOID     = 3;

            #define DEG2RAD (3.1415926535897932384626433832795 / 180.0)

            struct SimulationState
            {
                float3  Position;
                float3  Scale;
                float3  Rotation;
                float3  Velocity;
                float3  ExternalVelocity;
                float3  Colour;
                float2  Current_Max_Life;
                int2    SimSpace_RendType;
            };

            StructuredBuffer<SimulationState> SimulationStateBuffer;
            
            cbuffer GlobalState
            {
                // Settings Stuff
                int Seed;
                int SimulationSpace;
                int RenderType;

                // Kernel Stuff
                int DISPATCH_NUM;
                int MAX_PARTICLE_COUNT;

                // Time Stuff
                float DeltaTime;
                float Time;

                // Environment Stuff
                float3 EmitterPositionWS;
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
                int UniformScale;
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
                float4      BillboardPoints[4];
                float4x4    ObjectToWorld;
                float4x4    ObjectToWorldNoRot;
            CBUFFER_END

            static float2 UVs[4] =
            {
                float2(0, 0),
                float2(0, 1),
                float2(1, 0),
                float2(1, 1)
            };

            static float4 NonBillboardPoints[4] =
            {
                float4(-0.5, -0.5, 0, 1),
                float4(-0.5, +0.5, 0, 1),
                float4(+0.5, -0.5, 0, 1),
                float4(+0.5, +0.5, 0, 1)
            };

            float4x4 EulerRotation(float3 xyz)
            {
                xyz *= DEG2RAD;

                float3 c, s;
                sincos(xyz, s, c);

                float sXsY = s.x * s.y;
                float cXsY = c.x * s.y;

                return float4x4
                (
                    c.y * c.z,      sXsY * c.z - c.x * s.z,     cXsY * c.z + s.x * s.z,     0,
                    c.y * s.z,      sXsY * s.z + c.x * c.z,     cXsY * s.z - s.x * c.z,     0,
                    - s.y,          s.x * c.y,                  c.x * c.y,                  0,
                    0,              0,                          0,                          1
                );
            }

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
                if(SimulationStateBuffer[id].Current_Max_Life.x <= 0) { return; }

                OUT.Colour      = half4(SimulationStateBuffer[id].Colour, 1);
                int space       = SimulationStateBuffer[id].SimSpace_RendType[0];
                int rendType    = SimulationStateBuffer[id].SimSpace_RendType[1];
                float3 pos      = SimulationStateBuffer[id].Position;
                float3 rot      = SimulationStateBuffer[id].Rotation;
                float3 scale    = SimulationStateBuffer[id].Scale;
                
                float4x4 localTS = float4x4
                (
                    scale.x,    0,          0,          pos.x,
                    0,          scale.y,    0,          pos.y,
                    0,          0,          scale.z,    pos.z,
                    0,          0,          0,          1
                );

                /*

                    * DO THIS IN SEQUENCE *

                    if LOCAL_SPACE:
                        - Position is LocalPosition
                        - Rotation is LocalRotation
                        - Scale is LocalScale
                        +
                        - Multiply all by Emitter's ObjectToWorld

                    if WORLD_SPACE:
                        - Start Position is EmitterPosition + OffsetWS
                        - Rotation is Rotation (We do not want to add to EmitterRotation)
                        - Scale is Scale (We do not want to multiply with EmitterScale)

                    if BILLBOARD:
                        - Override Rotation

                */
                // TODO FIX: Emitter rotation should be included in billboarding (moving center point before GeometryShader)

                float4x4 transform;

                if(space == LOCAL_SPACE && rendType == RENDER_BILLBOARD)
                // Local and Global transform without rotation
                {
                    transform = mul(ObjectToWorldNoRot, localTS);
                }
                else if(space == LOCAL_SPACE && rendType != RENDER_BILLBOARD)
                // Local and global transform with rotation
                {
                    transform = mul(ObjectToWorld, mul(localTS, EulerRotation(rot)));
                }
                else if(space == GLOBAL_SPACE && rendType == RENDER_BILLBOARD)
                // Local transform without rotation
                {
                    transform = localTS;
                }
                else // GLOBAL SPACE && !RENDER_BILLBOARD
                // Local transform with rotation
                {
                    transform = mul(localTS, EulerRotation(rot));
                }

                // Billboard points used
                if(rendType == RENDER_BILLBOARD)
                {
                    [unroll]
                    for(int i = 0; i < 4; i++)
                    {
                        float4 posWS = mul(transform, BillboardPoints[i]);
                        OUT.PositionCS = UnityWorldToClipPos(posWS);
                        OUT.UV = UVs[i];
                        output.Append(OUT);
                    }
                }
                // Non billboard points used
                else
                {
                    [unroll]
                    for(int i = 0; i < 4; i++)
                    {
                        float4 posWS = mul(transform, NonBillboardPoints[i]);
                        OUT.PositionCS = UnityWorldToClipPos(posWS);
                        OUT.UV = UVs[i];
                        output.Append(OUT);
                    }
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
