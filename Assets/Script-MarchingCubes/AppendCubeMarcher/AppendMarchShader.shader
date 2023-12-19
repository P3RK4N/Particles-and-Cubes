Shader "Unlit/AppendMarchShader"
{
    Properties
    {
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Off

        Pass
        {
            CGPROGRAM

            #pragma target 5.0

            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawArgs
            #include "UnityIndirect.cginc"

            struct FRAG_IN
            {
                                float4 PositionCS   : SV_POSITION;
                                float4 PositionWS   : TEXCOORD0;
                                float3 NormalWS     : TEXCOORD1;
                nointerpolation int    Drop         : TEXCOORD2;
                                // float  IsFrontFace  : SV_ISFRONTFACE;
            };

            StructuredBuffer<float4> MeshBuffer;

            uniform int     MaxVertices;
            
            void GetNormalWS(in float3 a, in float3 b, in float3 c, inout float3 normal)
            {
                float3 vec1 = b - a;
                float3 vec2 = c - a;
                normal = normalize(cross(vec1, vec2));
            };

            FRAG_IN vert (uint VertexID : SV_VertexID)
            {
                InitIndirectDrawArgs(0); 
                
                uint id         = GetIndirectVertexID(VertexID);
                uint triId      = id - id % 3;

                FRAG_IN OUT = (FRAG_IN)0;
                OUT.Drop = 0;

                if
                (
                    id >= (uint)MaxVertices
                    || MeshBuffer[id].w == 0
                )
                {
                    OUT.Drop = 1;
                    return OUT;
                }

                OUT.PositionWS = float4(MeshBuffer[id].xyz, 1);
                OUT.PositionCS = UnityWorldToClipPos(OUT.PositionWS);
                GetNormalWS
                (
                    MeshBuffer[triId + 0].xyz,
                    MeshBuffer[triId + 1].xyz,
                    MeshBuffer[triId + 2].xyz,
                    OUT.NormalWS
                );

                return OUT;
            }

            half4 frag (FRAG_IN IN, bool isFront : SV_ISFRONTFACE) : SV_Target
            {
                if(IN.Drop == 1) discard;

                float3 lightDir = UnityWorldSpaceLightDir(IN.PositionWS);
                float ndotl = dot(lightDir, -IN.NormalWS) * 0.5 + 0.5;
                float3 diffuse = max(0.2, ndotl) * float3(0.2, 0.7, 0.3)/* unity_LightColor0.rgb */;

                if(!isFront) return half4(diffuse, 1);
                else         return half4(0,0,0,1);
            }
            
            ENDCG
        }
    }
}
