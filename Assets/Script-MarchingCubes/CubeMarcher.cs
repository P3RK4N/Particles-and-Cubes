using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;

public class CubeMarcher : MonoBehaviour
{
    [SerializeField] ComputeShader  MarcherTemplate         = null;
    [SerializeField] Material       MeshMaterial            = null;
    [SerializeField] float          MeshSize                = 1.0f;
    [SerializeField]
    [Range(1, 64)]   int            MeshResolutionPerDim    = 32;

    static readonly int             TriangleStride          = (3 + 3 + 3) * 4;
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

    void InitializeBuffers()
    {
        MeshBuffer      = new GraphicsBuffer(GraphicsBuffer.Target.Append, GetMaxTriangles(), TriangleStride);
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
    }

    void InitializeRenderContext()
    {
        MeshRenderParams = new RenderParams(Instantiate(MeshMaterial));
        MeshRenderParams.matProps = new MaterialPropertyBlock();
        MeshRenderParams.matProps.SetBuffer("MeshBuffer", MeshBuffer);
        MeshRenderParams.matProps.SetInteger("MeshResolutionPerDim", MeshResolutionPerDim);
    }

    void GenerateMesh()
    {
        float   step                    = MeshSize / MeshResolutionPerDim;
        float   halfStep                = step / 2.0f;
        int     halfResolutionPerDim    = MeshResolutionPerDim / 2;
        int     numGroups               = Mathf.CeilToInt(MeshResolutionPerDim / 32.0f);

        Marcher.SetInt      ("MeshResolutionPerDim", MeshResolutionPerDim                               );
        Marcher.SetFloat    ("MarchStep",            step                                               );
        Marcher.SetVector   ("StartPosition",        Ones * (- step * halfResolutionPerDim + halfStep)  );
        
        /* 
         _ _ _ _
        |       |
        |       |
        |       |
        |_ _ _ _|

        */

        Marcher.Dispatch(0, numGroups, numGroups, numGroups);
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
