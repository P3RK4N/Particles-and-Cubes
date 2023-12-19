Shader "Unlit/MarchShader"
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
                nointerpolation int    Drop         : TEXCOORD1;
                                // float  IsFrontFace  : SV_ISFRONTFACE;
            };

            StructuredBuffer<float4> MeshBuffer;

            uniform int     MaxVertices;
            
            FRAG_IN vert (uint VertexID : SV_VertexID)
            {
                InitIndirectDrawArgs(0); 
                
                uint id         = GetIndirectVertexID(VertexID);
                
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

                return OUT;
            }

            half4 frag (FRAG_IN IN, bool isFront : SV_ISFRONTFACE) : SV_Target
            {
                if(IN.Drop == 1) discard;

                float3 lightDir = UnityWorldSpaceLightDir(IN.PositionWS);
                // float val = dot(lightDir, IN.Normal) * 0.5 + 0.5;

                // return half4(val, val, val, 1.0);
                if(!isFront) return half4(1,1,1,1);
                else         return half4(0,0,0,1);
            }
            
            ENDCG
        }
    }
}
