Shader "Unlit/CPSMeshShader"
{
    Properties
    {
        _BillboardTexture("Texture", 2D) = "white" {}
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
            #pragma fragment frag

            #include "UnityCG.cginc"

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


            struct SimulationState
            {
                float3  Position;
                float3  StartScale;
                float3  EndScale;
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
                int StartScaleScalarType;
                int UniformStartScale;
                float3 ExactStartScale;
                float3 BottomStartScale;
                float3 TopStartScale;
                int UseEndScale;
                int EndScaleScalarType;
                int UniformEndScale;
                float3 ExactEndScale;
                float3 BottomEndScale;
                float3 TopEndScale;
    
    
                // Rotation
                int RotationScalarType;
                float3 ExactRotation;
                float3 BottomRotation;
                float3 TopRotation;
    
                // Colour
                int ColourScalarType;
                float3 ExactColour;
                float3 BottomColour;
                float3 TopColour;
    
                // TODO: Expand new ones
            }; 

            struct VERT_IN
            {
                float4 PositionOS   : POSITION;
                float2 UV           : TEXCOORD0;
                float3 Normal       : NORMAL0;
            };

            struct FRAG_IN
            {
                nointerpolation int Drop : TEXCOORD2;
                float4 PositionCS        : SV_POSITION;
                half4  Colour            : COLOR;
                float2 UV                : TEXCOORD1;
                float3 Normal            : NORMAL1;
                float3 PositionWS        : TEXCOORD3;
            };

            sampler2D _BillboardTexture;
            float4 _BillboardTexture_ST;

            CBUFFER_START(UnityPerMaterial)
                float4x4    ObjectToWorld;
            CBUFFER_END

#ifndef CPS_HELPERS
#define CPS_HELPERS

            static float2 UVs[4] =
            {
                float2(0, 0),
                float2(0, 1),
                float2(1, 0),
                float2(1, 1)
            };

            static int2 BillboardMultipliers[4] =
            {
                int2(-1, -1),
                int2(-1, +1),
                int2(+1, -1),
                int2(+1, +1)
            };

            static float4x4 Identity = float4x4
            (
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                0, 0, 0, 1
            );

            float4x4 EulerRotation(float3 xyz)
            {
                #define DEG2RAD (3.1415926535897932384626433832795 / 180.0)
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

            float3 CameraRight()
            {
                return mul((float3x3)unity_CameraToWorld, float3(0.5,0,0));
            }

            float3 CameraUp()
            {
                return mul((float3x3)unity_CameraToWorld, float3(0,0.5,0));
            }

#endif // CPS_HELPERS


            FRAG_IN vert (VERT_IN IN, uint InstanceID: SV_InstanceID)
            {
                FRAG_IN OUT = (FRAG_IN)0;
                uint id = InstanceID;
                OUT.Drop = 0;

                float2 Current_Max_Life = SimulationStateBuffer[id].Current_Max_Life;

                if(Current_Max_Life.x <= 0)
                {
                    OUT.Drop = 1;
                    return OUT;
                }

                float p         = (Current_Max_Life.y - Current_Max_Life.x) / Current_Max_Life.y;
                int space       = SimulationStateBuffer[id].SimSpace_RendType[0];
                float3 pos      = SimulationStateBuffer[id].Position;
                float3 rot      = SimulationStateBuffer[id].Rotation;
                float3 scale    = UseEndScale ? lerp(SimulationStateBuffer[id].StartScale, SimulationStateBuffer[id].EndScale, p) : SimulationStateBuffer[id].StartScale;
                // float3 scale    = UseEndScale == 1 ? SimulationStateBuffer[id].EndScale : SimulationStateBuffer[id].StartScale;


                // NOTE: Should particles stretch with the size of Emitter when they are in LOCAL_SPACE? Or should they just
                // transform their positions? Stretching doesnt seem right (eg. Particles at the extreme positions will have
                // vastly different scale compared to the ones in the center of local space)
                // FOR NOW: They just transform via Emitter position when in LOCAL_SPACE
                
                float4x4 ToObject = float4x4
                (
                    scale.x,    0,          0,          0,
                    0,          scale.y,    0,          0,
                    0,          0,          scale.z,    0,
                    0,          0,          0,          1
                );

                float4 offsetOS = mul(ToObject, mul(EulerRotation(rot), IN.PositionOS));
                float4 offsetWS = mul(space == LOCAL_SPACE ? ObjectToWorld : Identity, float4(pos, 1));

                float4 positionWS = offsetWS + offsetOS;
                positionWS.w = 1;

                OUT.PositionCS  = UnityWorldToClipPos(positionWS);
                OUT.UV          = IN.UV;
                OUT.Colour      = half4(SimulationStateBuffer[id].Colour, 1);
                OUT.Normal      = IN.Normal;
                OUT.PositionWS  = positionWS.xyz;

                return OUT;
            }

            half4 frag (FRAG_IN IN) : SV_Target
            {
                if(IN.Drop) discard;

                float3 lightDir = UnityWorldSpaceLightDir(IN.PositionWS);
                float val = dot(lightDir, IN.Normal) * 0.5 + 0.5;
                return half4(val,val,val,1) * IN.Colour;
            }
            
            ENDCG
        }
    }
}
