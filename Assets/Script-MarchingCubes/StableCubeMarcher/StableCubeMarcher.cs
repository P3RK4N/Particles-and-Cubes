using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;

public class StableCubeMarcher : MonoBehaviour
{
    [SerializeField]            ComputeShader  MarcherTemplate         = null;
    [SerializeField]            Material       MeshMaterial            = null;
    [SerializeField]            float          MeshSize                = 1.0f;
    [SerializeField]
    [Range(0,1)]                float          ValueBorder             = 0.5f;
    [SerializeField]
    [Range(1, 256)]              int           MeshResolutionPerDim    = 32;

    [Space(10)]

    [Header("Perlin Noise")]
    [SerializeField]
    [Range(0,8)]                int            Octaves                 = 3;
    [SerializeField]
    [Range(0,10)]               float          Lacunarity              = 2.0f;
    [SerializeField]
    [Range(0,10)]               float          Frequency               = 0.01f;
    [SerializeField]            Vector3        Offset                  = Vector3.zero;
    [SerializeField]            bool           Animate                 = false;


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
        if(Animate) AnimateMesh();
        if(Animate || transform.hasChanged) GenerateMesh();

        Graphics.RenderPrimitivesIndirect(MeshRenderParams, MeshTopology.Triangles, CommandBuffer);
    }

    void OnDrawGizmos()
    {
        DebugExtension.DrawBounds(new Bounds{center = transform.position, size = Ones * MeshSize * transform.localScale.x});

        float   step                    = MeshSize / MeshResolutionPerDim * transform.localScale.x;
        bool    even                    = MeshResolutionPerDim % 2 == 0;
        float   halfStep                = step / 2.0f;
        int     halfResolutionPerDim    = MeshResolutionPerDim / 2;
        int     numGroups               = Mathf.CeilToInt(MeshResolutionPerDim / 8.0f);
        Vector3 startPos                = Ones * (- step * halfResolutionPerDim + (even ? halfStep : 0)) + transform.position + Offset;

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
        float   step                    = MeshSize / MeshResolutionPerDim * transform.localScale.x;
        bool    even                    = MeshResolutionPerDim % 2 == 0;
        float   halfStep                = step / 2.0f;
        int     halfResolutionPerDim    = MeshResolutionPerDim / 2;
        int     numGroups               = Mathf.CeilToInt(MeshResolutionPerDim / 8.0f);
        Vector3 startPos                = Ones * (- step * halfResolutionPerDim + (even ? halfStep : 0)) + transform.position;

        Marcher.SetInt      ("MeshResolutionPerDim", MeshResolutionPerDim   );
        Marcher.SetFloat    ("MarchStep",            step                   );
        Marcher.SetFloat    ("ValueBorder",          ValueBorder            );        
        Marcher.SetVector   ("StartPosition",        startPos               );
        
        Marcher.SetInt      ("Octaves",              Octaves                );            
        Marcher.SetFloat    ("Lacunarity",           Lacunarity             );            
        Marcher.SetFloat    ("Frequency",            Frequency              );            
        Marcher.SetVector   ("Offset",               Offset                 );             
        
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

    void AnimateMesh()
    {
        // Frequency from 2 to 4
        float freqSpeed = 0.5f;
        float freq = Mathf.Sin(Time.time * freqSpeed) + 2;

        // Offset
        float offsetSpeed = 1.3f;
        Vector3 offsetDir = new Vector3(1.0f,0.33f, 0.7f).normalized * offsetSpeed * Time.deltaTime;

        // Lacunarity from 0 to 3
        float lacunaritySpeed = 1.0f;
        float lacunarity = Mathf.Sin(Time.time * lacunaritySpeed) * 1.5f + 1.5f;



        Frequency  = freq;
        Lacunarity = lacunarity;
        //Offset    += offsetDir;
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
