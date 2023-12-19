using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;

public class CubeMarcher : MonoBehaviour
{
    [SerializeField]            ComputeShader  MarcherTemplate         = null;
    [SerializeField]            Material       MeshMaterial            = null;
    [SerializeField]            float          MeshSize                = 1.0f;
    [SerializeField]
    [Range(0,1)]                float          ValueBorder             = 0.5f;
    [SerializeField]
    [Range(1, 64)]              int            MeshResolutionPerDim    = 32;
    [Space(10)]
    [Header("Perlin Noise")]
    [SerializeField]
    [Range(0,8)]                int            Octaves                 = 3;
    [SerializeField]
    [Range(0,10)]               float          Lacunarity              = 2.0f;
    [SerializeField]
    [Range(0,10)]               float          Frequency               = 0.01f;


    static readonly int             MaxTrianglesPerCube     = 5;
    static readonly Vector3         Ones                    = new Vector3(1, 1, 1);

    ComputeShader                   Marcher;
    GraphicsBuffer                  MeshBuffer;
    GraphicsBuffer                  CommandBuffer;
    RenderParams                    MeshRenderParams;  

    void Awake()
    {
        InitializeBuffers();
        InitializeCompute();
        InitializeRenderContext();
        GenerateMesh();
    }

    void OnDestroy()
    {
        MeshBuffer.Release();    
    }

    void Update()
    {
        Graphics.RenderPrimitivesIndirect(MeshRenderParams, MeshTopology.Triangles, CommandBuffer);
    }

    void OnDrawGizmos()
    {
        DebugExtension.DrawBounds(new Bounds{center = Vector3.zero, size = Ones * MeshSize});

        float   step                    = MeshSize / MeshResolutionPerDim;
        bool    even                    = MeshResolutionPerDim % 2 == 0;
        float   halfStep                = step / 2.0f;
        int     halfResolutionPerDim    = MeshResolutionPerDim / 2;
        int     numGroups               = Mathf.CeilToInt(MeshResolutionPerDim / 8.0f);
        Vector3 startPos                = Ones * (- step * halfResolutionPerDim + (even ? halfStep : 0));

        DebugExtension.DrawBounds(new Bounds{ center = startPos, size = Ones * step });
    }

    void OnValidate()
    {
        if(Marcher == null) return; 

        MeshRenderParams.matProps.SetInteger("MaxVertices", GetMaxTriangles() * 3);
        GenerateMesh();
    }

    void InitializeBuffers()
    {
        MeshBuffer      = new GraphicsBuffer(Target.Structured, GetMaxTriangles() * 3 /*Points*/, sizeof(float) * 4 /*float4*/);
        CommandBuffer   = new GraphicsBuffer(Target.IndirectArguments, 1, IndirectDrawArgs.size);
        CommandBuffer.SetData(new IndirectDrawArgs[]{ new IndirectDrawArgs
        {
            vertexCountPerInstance   = (uint)(GetMaxTriangles() * 3),
            instanceCount            = 1
        }});
    }
    
    void InitializeCompute()
    {
        Marcher = Instantiate(MarcherTemplate);

        Marcher.SetBuffer(0, "MeshBuffer", MeshBuffer);
        Marcher.SetBuffer(1, "MeshBuffer", MeshBuffer);
    }

    void InitializeRenderContext()
    {
        MeshRenderParams = new RenderParams(Instantiate(MeshMaterial));
        MeshRenderParams.matProps = new MaterialPropertyBlock();
        MeshRenderParams.worldBounds = new Bounds(Vector3.zero, Ones * 100000);
        MeshRenderParams.matProps.SetBuffer("MeshBuffer", MeshBuffer);
        MeshRenderParams.matProps.SetInteger("MaxVertices", GetMaxTriangles() * 3);
    }

    void GenerateMesh()
    {
        float   step                    = MeshSize / MeshResolutionPerDim;
        bool    even                    = MeshResolutionPerDim % 2 == 0;
        float   halfStep                = step / 2.0f;
        int     halfResolutionPerDim    = MeshResolutionPerDim / 2;
        int     numGroups               = Mathf.CeilToInt(MeshResolutionPerDim / 8.0f);
        Vector3 startPos                = Ones * (- step * halfResolutionPerDim + (even ? halfStep : 0));

        Marcher.SetInt      ("MeshResolutionPerDim", MeshResolutionPerDim   );
        Marcher.SetFloat    ("MarchStep",            step                   );
        Marcher.SetFloat    ("ValueBorder",          ValueBorder            );        
        Marcher.SetVector   ("StartPosition",        startPos               );
        
        Marcher.SetInt      ("Octaves",              Octaves                );            
        Marcher.SetFloat    ("Lacunarity",           Lacunarity             );            
        Marcher.SetFloat    ("Frequency",            Frequency              );            

        /* 
         _ _ _ _
        |       |
        |_ _    |
        |_  |   |
        |_|_|_ _|

        */

        //Marcher.Dispatch(0, numGroups, numGroups, numGroups);
        Marcher.Dispatch(1, numGroups, numGroups, numGroups);
    }

    int GetMaxTriangles()
    {
        return MaxTrianglesPerCube * GetMaxCubes();
    }

    int GetMaxCubes()
    {
        return MeshResolutionPerDim * MeshResolutionPerDim * MeshResolutionPerDim;
    }
}
