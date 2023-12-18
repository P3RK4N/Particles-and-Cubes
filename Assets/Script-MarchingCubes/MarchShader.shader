Shader "Unlit/MarchShader"
{
    Properties
    {
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

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
            };

            struct Triangle { float3 a,b,c; };
            AppendStructuredBuffer<Triangle> MeshBuffer;

            uniform int MeshResolutionPerDim;

            FRAG_IN vert (uint VertexID : SV_VertexID)
            {
                InitIndirectDrawArgs(0); 
                
                uint id = GetIndirectVertexID(VertexID);
                uint triangleID = id / 3;
                uint pointID    = id % 3;
                
                FRAG_IN OUT = (FRAG_IN)0;

                if(id >= MeshResolutionPerDim * MeshResolutionPerDim * MeshResolutionPerDim)
                {
                    OUT.Drop = 1;
                    return OUT;
                }

                switch(pointID)
                {
                    case 0: OUT.PositionWS = MeshBuffer[triangleID].a; break;
                    case 1: OUT.PositionWS = MeshBuffer[triangleID].b; break;
                    case 2: OUT.PositionWS = MeshBuffer[triangleID].c; break;
                }
                
                OUT.PositionCS = UnityWorldToClipPos(OUT.PositionWS);

                return OUT;
            }

            half4 frag (FRAG_IN IN) : SV_Target
            {
                if(IN.Drop == 1) discard;

                float3 lightDir = UnityWorldSpaceLightDir(IN.PositionWS);
                float val = dot(lightDir, IN.Normal) * 0.5 + 0.5;

                return half4(val, val, val, 1.0);
            }
            
            ENDCG
        }
    }
}
