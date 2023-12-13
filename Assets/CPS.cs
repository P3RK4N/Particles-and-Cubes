using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Unity.VisualScripting;
using static UnityEngine.GraphicsBuffer;


#if UNITY_EDITOR
using UnityEditor;
#endif


public class CPS : MonoBehaviour
{

#region Enum Types

    public enum ScalarGeneratorType
    {
        Exact,
        Ranged,
        Gaussian
    };

    public enum FunctionGeneratorType
    {
        Point,
        Sphere,
        Plane,
        Cuboid,
    };

    public enum FalloffType
    {
        Root,
        Linear,
        Quadratic,
        Cuboid
    }

    public enum SimulationSpaceType
    {
        Local,
        Global
    }

    public enum ParticleRenderType
    {
        Billboard,
        Point,
        Mesh,
    }

    public enum SimulatorKernelType : int
    {
        MockInit    = 0,
        MockTick    = 1,
        MockEmit    = 2
    }

#endregion

#region Structs

    /// <summary>
    /// Type and parameters of scalar (exact, ranged, gaussian)
    /// </summary>
    /// <typeparam name="Scalar">1-4 Dimensional Value</typeparam>
    [Serializable]
    public struct ScalarGenerator<Scalar>
    {
        [SerializeField] public ScalarGeneratorType Type;

        // Exact
        [SerializeField] public Scalar ExactScalar;
        
        // Ranged
        [SerializeField] public bool Uniform;
        [SerializeField] public Scalar BottomScalar;
        [SerializeField] public Scalar TopScalar;

        // TODO: Expand
    }

    /// <summary>
    /// Type and parameters of implicit shape (dot, plane, sphere, cube)
    /// </summary>
    [Serializable]
    public struct FunctionGenerator
    {
        [SerializeField] public FunctionGeneratorType Type;

        // Point && Plane && Sphere && Cube
        [SerializeField] public Vector3 CenterOffset;
        
        // Sphere
        [SerializeField] public float Radius;

        // TODO: Expand
    }

    /// <summary>
    /// Type of force falloff (eg. linear), Force intensity and function/implicit shape where distance is equal to 0
    /// </summary>
    [Serializable]
    public struct ForceFieldDescriptor
    {
        [SerializeField] public FalloffType Type;
        [SerializeField] public float Intensity;
        [SerializeField] public Vector3 Coefficients;
        [SerializeField] FunctionGenerator Generator;
    }


#endregion 

#region Template Fields

    /// <summary>
    /// Compute shader which will simulate particles
    /// </summary>
    [SerializeField] public ComputeShader SimulatorTemplate;

    /// <summary>
    /// Material for rendering points and billboards
    /// </summary>
    [SerializeField] public Material BillboardMaterialTemplate;

    /// <summary>
    /// Material for rendering meshes
    /// </summary>
    [SerializeField] public Material MeshMaterialTemplate;

#endregion

#region Setting Fields

    /// <summary>
    /// Seed for random generation
    /// </summary>
    [SerializeField] public int                         Seed;

    /// <summary>
    /// Decides whether CPS moves with root or not
    /// </summary>
    [SerializeField] public SimulationSpaceType         SimulationSpace;

    /// <summary>
    /// Type of rendering
    /// </summary>
    [SerializeField] public ParticleRenderType          RenderType;

    /// <summary>
    /// Whether to draw helper gui
    /// </summary>
    [SerializeField] public bool                        DrawGUI;

    #endregion

#region Render Stuff

    /// <summary>
    /// Texture used for billboard particles
    /// </summary>
    [SerializeField] public Texture2D                   BillboardTexture;

    /// <summary>
    /// Mesh used for non billboard particles
    /// </summary>
    [SerializeField] public Mesh                        ParticleMesh;

#endregion

#region Properties

    /// <summary>
    /// Initial position stuff (Shape in which particles generate)
    /// </summary>
    [SerializeField] public FunctionGenerator           StartPositionGenerator;

    /// <summary>
    /// Initial lifetime stuff
    /// </summary>
    [SerializeField] public ScalarGenerator<float>      StartLifetimeGenerator;

    /// <summary>
    /// Initial velocity stuff
    /// </summary>
    [SerializeField] public ScalarGenerator<Vector3>    StartVelocityGenerator;

    /// <summary>
    /// Initial size stuff
    /// </summary>
    [SerializeField] public ScalarGenerator<Vector3>    StartScaleGenerator;
    [SerializeField] public bool                        UseEndScale;
    [SerializeField] public ScalarGenerator<Vector3>    EndScaleGenerator;

    /// <summary>
    /// Initial rotation stuff
    /// </summary>
    [SerializeField] public ScalarGenerator<Vector3>    StartRotationGenerator;
    [SerializeField] public ScalarGenerator<Vector3>    RotationOverTimeGenerator;

    /// <summary>
    /// Initial colour stuff
    /// </summary>
    [SerializeField] public ScalarGenerator<Vector3>    StartColourGenerator;
    [SerializeField] public bool                        UseEndColour;
    [SerializeField] public ScalarGenerator<Vector3>    EndColourGenerator;

#endregion

#region SimulationFields

    /// <summary>
    /// Max count of concurrent particles
    /// </summary>
    [SerializeField] public int                         MaximumParticleCount;
    
    /// <summary>
    /// Used for tracking current number of concurrent particles
    /// </summary>
    public int                                          CurrentParticleCount;

    /// <summary>
    /// Amount of particles emitted per second
    /// </summary>
    [SerializeField] public float                       EmissionRate;
    
    /// <summary>
    /// Tracks emission amount for current frame
    /// </summary>
    public float                                        EmissionAmount;

    /// <summary>
    /// Gravity intensity
    /// </summary>
    [SerializeField] public float                       Gravity;

    /// <summary>
    /// Drag intensity - slows down particles
    /// </summary>
    [SerializeField] public float                       Drag;

    /// <summary>
    /// Environmental ForceField stuff (walls, attractors, repulsors)
    /// </summary>
    [SerializeField] public List<ForceFieldDescriptor> ForceFields;

#endregion


    public static readonly int  HARD_LIMIT              = 32 * 32 * 32 * 32 - 1;
    public static int           GlobalStateSizeInFloat  = 96;
    public static float         MinimalParticleLifetime = 0.1f;

    /// <summary>
    /// CPSSimulator instance
    /// </summary>
    ComputeShader               Simulator;

    /// <summary>
    /// Rendering context
    /// </summary>
    RenderParams                BillboardRenderParams;
    RenderParams                MeshRenderParams;

    /// <summary>
    /// Defines number of vertices and instances drawn in billboard render mode
    /// </summary>
    GraphicsBuffer              BillboardCommandBuffer;

    /// <summary>
    /// Buffer containing simulation state
    /// </summary>
    /// Structure:
    /// 
    ///     float: PosX
    ///     float: PosY
    ///     float: PosZ
    ///     
    GraphicsBuffer              SimulationStateBuffer;

    /// <summary>
    /// Buffer containing mostly readonly data
    /// </summary>
    /// Structure:
    /// 
    GraphicsBuffer              GlobalStateBuffer;

    /// <summary>
    /// Counter buffers
    /// </summary>
    ComputeBuffer               EmissionAmountCounterBuffer;
    ComputeBuffer               CurrentParticleCountBuffer;
    ComputeBuffer               CopyCounterBuffer;

    Transform                   tf;

    void Awake()
    {
        Debug.Assert(SimulatorTemplate != null, "SimulatorTemplate should not be null!");

        tf = transform;
        ParticleMesh = ParticleMesh == null ? Resources.GetBuiltinResource<Mesh>("Cube.fbx") : ParticleMesh;

        CreateBuffers();
        InitializeCompute();
        InitializeRenderContext();
    }

    void Start()
    {
        RefreshGlobalStateBuffer();
        Simulator.Dispatch((int)SimulatorKernelType.MockInit, GetDispatchNum(), GetDispatchNum(), 1);    
    }

    void OnDestroy()
    {
        DeleteBuffers();
    }
    
    void Update()
    {
        UpdateLocalShaderVariables();
        SynchronizeCounters();

        // Emit
        Simulator.Dispatch((int)SimulatorKernelType.MockEmit, GetDispatchNum(), GetDispatchNum(), 1);

        // Simulate
        Simulator.Dispatch((int)SimulatorKernelType.MockTick, GetDispatchNum(), GetDispatchNum(), 1);
        
        // Render
        if(RenderType == ParticleRenderType.Billboard || RenderType == ParticleRenderType.Point)
        {
            Graphics.RenderPrimitivesIndirect(BillboardRenderParams, MeshTopology.Points, BillboardCommandBuffer);
        }
        else // ParticleRenderType.Mesh
        {
            Graphics.RenderMeshPrimitives(MeshRenderParams, ParticleMesh, 0, MaximumParticleCount);
        }
    }

    void OnValidate()
    {
        if
        (
            Application.isPlaying &&
            tf != null
        )
        {
            RefreshGlobalStateBuffer();
        }
    }

    void OnDrawGizmos()
    {
        if(!DrawGUI) return;

        DrawStartPosition();
    }
    
    void RefreshGlobalStateBuffer()
    {
        // Set Settings Stuff
        Simulator.SetInt    ("Seed",                        (int)Seed                               );
        Simulator.SetInt    ("SimulationSpace",             (int)SimulationSpace                    );
        Simulator.SetInt    ("RenderType",                  (int)RenderType                         );
        
        // Set kernel-related values
        Simulator.SetInt    ("DISPATCH_NUM",                GetDispatchNum()                        );
        Simulator.SetInt    ("MAX_PARTICLE_COUNT",          MaximumParticleCount                    );

        // Set time-related values
        Simulator.SetFloat  ("DeltaTime",                   Time.deltaTime                          );
        Simulator.SetFloat  ("Time",                        Time.time                               );

        // Set environment-related values
        Simulator.SetVector ("EmitterPositionWS",           tf.position                             );
        Simulator.SetFloat  ("GravityForce",                Gravity                                 );
        Simulator.SetFloat  ("DragForce",                   Drag                                    );                      

        // Set lifetime-related values
        Simulator.SetInt    ("LifetimeScalarType",          (int)StartLifetimeGenerator.Type        );
        Simulator.SetFloat  ("ExactLifetime",               StartLifetimeGenerator.ExactScalar      );
        Simulator.SetFloat  ("BottomLifetime",              StartLifetimeGenerator.BottomScalar     );
        Simulator.SetFloat  ("TopLifetime",                 StartLifetimeGenerator.TopScalar        );

        // Set position-related values
        Simulator.SetInt    ("PositionFunctionType",        (int)StartPositionGenerator.Type        );
        Simulator.SetVector ("CenterOffset",                StartPositionGenerator.CenterOffset     );
        Simulator.SetFloat  ("Radius",                      StartPositionGenerator.Radius           );

        // Set velocity-related values
        Simulator.SetInt    ("VelocityScalarType",          (int)StartVelocityGenerator.Type        );
        Simulator.SetVector ("ExactVelocity",               StartVelocityGenerator.ExactScalar      );
        Simulator.SetVector ("BottomVelocity",              StartVelocityGenerator.BottomScalar     );
        Simulator.SetVector ("TopVelocity",                 StartVelocityGenerator.TopScalar        );

        // Set scale-related values
        Simulator.SetInt    ("StartScaleScalarType",        (int)StartScaleGenerator.Type           );
        Simulator.SetInt    ("UniformStartScale",           StartScaleGenerator.Uniform ? 1 : 0     );
        Simulator.SetVector ("ExactStartScale",             StartScaleGenerator.ExactScalar         );
        Simulator.SetVector ("BottomStartScale",            StartScaleGenerator.BottomScalar        );
        Simulator.SetVector ("TopStartScale",               StartScaleGenerator.TopScalar           );
        Simulator.SetInt    ("UseEndScale",                 UseEndScale ? 1 : 0                     );

        Simulator.SetInt    ("EndScaleScalarType",          (int)EndScaleGenerator.Type             );
        Simulator.SetInt    ("UniformEndScale",             EndScaleGenerator.Uniform ? 1 : 0       );
        Simulator.SetVector ("ExactEndScale",               EndScaleGenerator.ExactScalar           );
        Simulator.SetVector ("BottomEndScale",              EndScaleGenerator.BottomScalar          );
        Simulator.SetVector ("TopEndScale",                 EndScaleGenerator.TopScalar             );

        // Set rotation-related values
        Simulator.SetInt    ("RotationScalarType",          (int)StartRotationGenerator.Type        );
        Simulator.SetVector ("ExactRotation",               StartRotationGenerator.ExactScalar      );
        Simulator.SetVector ("BottomRotation",              StartRotationGenerator.BottomScalar     );
        Simulator.SetVector ("TopRotation",                 StartRotationGenerator.TopScalar        );
        Simulator.SetInt    ("RotationOverTimeScalarType",  (int)RotationOverTimeGenerator.Type     );
        Simulator.SetVector ("ExactRotationOverTime",       RotationOverTimeGenerator.ExactScalar   );
        Simulator.SetVector ("BottomRotationOverTime",      RotationOverTimeGenerator.BottomScalar  );
        Simulator.SetVector ("TopRotationOverTime",         RotationOverTimeGenerator.TopScalar     );

        // Set colour-related values
        Simulator.SetInt    ("StartColourScalarType",       (int)StartColourGenerator.Type          );
        Simulator.SetVector ("ExactStartColour",            StartColourGenerator.ExactScalar        );
        Simulator.SetVector ("BottomStartColour",           StartColourGenerator.BottomScalar       );
        Simulator.SetVector ("TopStartColour",              StartColourGenerator.TopScalar          );
        Simulator.SetInt    ("UseEndColour",                UseEndColour ? 1 : 0                    );
        Simulator.SetInt    ("EndColourScalarType",         (int)EndColourGenerator.Type            );
        Simulator.SetVector ("ExactEndColour",              EndColourGenerator.ExactScalar          );
        Simulator.SetVector ("BottomEndColour",             EndColourGenerator.BottomScalar         );
        Simulator.SetVector ("TopEndColour",                EndColourGenerator.TopScalar            );
        
        // SHADER VALUES
        BillboardRenderParams.matProps.SetInteger ("UseEndScale",  UseEndScale ? 1 : 0          );
        MeshRenderParams.matProps.SetInteger      ("UseEndScale",  UseEndScale ? 1 : 0          );
        BillboardRenderParams.matProps.SetInteger ("UseEndColour", UseEndColour ? 1 : 0         );
        MeshRenderParams.matProps.SetInteger      ("UseEndColour", UseEndColour ? 1 : 0         );
    }

    void SynchronizeCounters()
    {
        int[] counterValue = new int[1];

        // Synchronise current particle count
        ComputeBuffer.CopyCount(CurrentParticleCountBuffer, CopyCounterBuffer, 0);
        CopyCounterBuffer.GetData(counterValue); 
        CurrentParticleCount = counterValue[0];

        // Update allowed emission amount for this frame
        ComputeBuffer.CopyCount(EmissionAmountCounterBuffer, CopyCounterBuffer, 0);
        CopyCounterBuffer.GetData(counterValue);
        EmissionAmount += EmissionRate * Time.deltaTime;
        counterValue[0] = Mathf.FloorToInt(EmissionAmount);
        EmissionAmount = EmissionAmount - counterValue[0];
        EmissionAmountCounterBuffer.SetCounterValue((uint)counterValue[0]);
    }

    void UpdateLocalShaderVariables()
    {
        Simulator.SetVector ( "EmitterPositionWS", tf.position    );
        Simulator.SetFloat  ( "DeltaTime",         Time.deltaTime );
        Simulator.SetFloat  ( "Time",              Time.time      );

        if(RenderType == ParticleRenderType.Billboard || RenderType == ParticleRenderType.Point)
        {
            BillboardRenderParams.matProps  .SetMatrix("ObjectToWorld", tf.localToWorldMatrix);
        }
        else // ParticleRenderType.Mesh
        {
            MeshRenderParams.matProps       .SetMatrix("ObjectToWorld", tf.localToWorldMatrix);
        }
    }

    void CreateBuffers()
    {
        // Simulation state buffer
        SimulationStateBuffer = new GraphicsBuffer
        (
            Target.Structured,
            MaximumParticleCount,
            (
                + 3 /* Position          */
                + 3 /* StartScale        */
                + 3 /* EndScale          */
                + 3 /* Rotation          */
                + 3 /* RotationOverTime  */
                + 3 /* Velocity          */
                + 3 /* OuterVelocity     */
                + 3 /* StartColour       */
                + 3 /* EndColour         */
                + 2 /* Current_Max_Life  */
                + 2 /* SimSpace_RendType */
            ) /*Floats*/
            * 4 /*Bytes*/
        );

        // Global state buffer
        GlobalStateBuffer = new GraphicsBuffer
        (
            Target.Constant, 
            GlobalStateSizeInFloat,
            4
        );

        // Counters && CopyCount buffer
        EmissionAmountCounterBuffer  = new ComputeBuffer(1, 4, ComputeBufferType.Counter);
        CurrentParticleCountBuffer   = new ComputeBuffer(1, 4, ComputeBufferType.Counter);
        EmissionAmountCounterBuffer  .SetCounterValue(0);
        CurrentParticleCountBuffer   .SetCounterValue(0);
        CopyCounterBuffer            = new ComputeBuffer(1, 4, ComputeBufferType.Raw);

        // Billboard command buffer
        BillboardCommandBuffer       = new GraphicsBuffer(Target.IndirectArguments, 1, IndirectDrawArgs.size);
        BillboardCommandBuffer       .SetData(new IndirectDrawArgs[]{ new IndirectDrawArgs
        {
            vertexCountPerInstance   = (uint)MaximumParticleCount,
            instanceCount            = 1
        }});
    }

    void DeleteBuffers()
    {
        SimulationStateBuffer       ?.Release();
        GlobalStateBuffer           ?.Release();
        BillboardCommandBuffer      ?.Release();
        CurrentParticleCountBuffer  ?.Release();
        EmissionAmountCounterBuffer ?.Release();
        CopyCounterBuffer           ?.Release();
    }

    void InitializeCompute()
    {
        Simulator = Instantiate(SimulatorTemplate);

        Simulator.SetConstantBuffer("GlobalStateBuffer", GlobalStateBuffer, 0, GlobalStateSizeInFloat * sizeof(float));
        foreach(SimulatorKernelType kernel in Enum.GetValues(typeof(SimulatorKernelType)))
        {
            Simulator.SetBuffer((int)kernel, "SimulationStateBuffer",       SimulationStateBuffer       );
            Simulator.SetBuffer((int)kernel, "CurrentParticleCountBuffer",  CurrentParticleCountBuffer  );
            Simulator.SetBuffer((int)kernel, "EmissionAmountCounterBuffer", EmissionAmountCounterBuffer );
        }
    }

    void InitializeRenderContext()
    {
        BillboardRenderParams                            = new RenderParams(Instantiate(BillboardMaterialTemplate));
        BillboardRenderParams.worldBounds                = new Bounds(Vector3.zero, 10000*Vector3.one);
        BillboardRenderParams.matProps                   = new MaterialPropertyBlock();

        BillboardRenderParams.matProps.SetBuffer         ("SimulationStateBuffer", SimulationStateBuffer                                        );
        BillboardRenderParams.matProps.SetConstantBuffer ("GlobalStateBuffer",     GlobalStateBuffer, 0, GlobalStateSizeInFloat * sizeof(float) );
        BillboardRenderParams.matProps.SetTexture        ("_BillboardTexture",     BillboardTexture                                             );
        
        MeshRenderParams                                 = new RenderParams(Instantiate(MeshMaterialTemplate)                                   );
        MeshRenderParams.worldBounds                     = new Bounds(Vector3.zero, 10000*Vector3.one                                           );
        MeshRenderParams.material.enableInstancing       = true;
        MeshRenderParams.matProps                        = new MaterialPropertyBlock();

        MeshRenderParams.matProps.SetBuffer              ("SimulationStateBuffer", SimulationStateBuffer                                        );
        MeshRenderParams.matProps.SetConstantBuffer      ("GlobalStateBuffer",     GlobalStateBuffer, 0, GlobalStateSizeInFloat * sizeof(float) );
        MeshRenderParams.matProps.SetTexture             ("_BillboardTexture",     BillboardTexture                                             );
    }

    int _dispatchNum = -1;
    int GetDispatchNum()
    {
        if(_dispatchNum > 0 ) return _dispatchNum;

        for(int i = 2; i <= 32; i*=2)
        {
            if(MaximumParticleCount <= i*i*i*i)
            {
                _dispatchNum = i;
                return _dispatchNum;
            }
        }
        throw new Exception("Not happening!");
    }
    
    void DrawStartPosition()
    {
        switch(StartPositionGenerator.Type)
        {
            case FunctionGeneratorType.Point:
            {
                DebugExtension.DrawPoint(transform.position + StartPositionGenerator.CenterOffset, 0.3f); break;
            }
            case FunctionGeneratorType.Sphere:
            {
                Gizmos.DrawWireSphere(transform.position + StartPositionGenerator.CenterOffset, StartPositionGenerator.Radius); break;
            }
            case FunctionGeneratorType.Cuboid:
            case FunctionGeneratorType.Plane:
            default:
            {
                break;
            }
        }
    }

    static void DrawCuboid(Vector3 bottom, Vector3 top)
    {
        Gizmos.color = Color.white;

        Vector3[] corners = new Vector3[8];
        corners[0] = new Vector3(bottom.x, bottom.y, bottom.z);
        corners[1] = new Vector3(top.x, bottom.y, bottom.z);
        corners[2] = new Vector3(bottom.x, top.y, bottom.z);
        corners[3] = new Vector3(top.x, top.y, bottom.z);
        corners[4] = new Vector3(bottom.x, bottom.y, top.z);
        corners[5] = new Vector3(top.x, bottom.y, top.z);
        corners[6] = new Vector3(bottom.x, top.y, top.z);
        corners[7] = new Vector3(top.x, top.y, top.z);

        // Draw bottom edges
        Gizmos.DrawLine(corners[0], corners[1]);
        Gizmos.DrawLine(corners[1], corners[3]);
        Gizmos.DrawLine(corners[3], corners[2]);
        Gizmos.DrawLine(corners[2], corners[0]);

        // Draw top edges
        Gizmos.DrawLine(corners[4], corners[5]);
        Gizmos.DrawLine(corners[5], corners[7]);
        Gizmos.DrawLine(corners[7], corners[6]);
        Gizmos.DrawLine(corners[6], corners[4]);

        // Draw vertical edges connecting top and bottom
        Gizmos.DrawLine(corners[0], corners[4]);
        Gizmos.DrawLine(corners[1], corners[5]);
        Gizmos.DrawLine(corners[2], corners[6]);
        Gizmos.DrawLine(corners[3], corners[7]);
    }
}

#if UNITY_EDITOR

[CustomEditor(typeof(CPS))]
public class CPSEditor : Editor
{

#region Statics

    static GUIStyle TitleStyle;
    static GUIStyle SubMenuStyle;

    static Color ScriptsColor       = new Color(0.95f,0.95f,0.95f, 0.3f);
    static Color SettingsColor      = new Color(0.95f,0.95f,0.3f, 0.3f);
    static Color StartColor         = new Color(0.8f,0.15f,0.15f, 0.4f);
    static Color SimulationColor    = new Color(0.15f,0.8f,0.15f, 0.4f);
    static Color EndColor           = new Color(0.15f,0.15f,0.8f, 0.4f);

    static GUIStyle GetTitleStyle()
    {
        if(TitleStyle == null)
        {
            TitleStyle           = new GUIStyle(EditorStyles.boldLabel);
            TitleStyle.alignment = TextAnchor.MiddleCenter;
            TitleStyle.fontSize  = 18;
        }

        return TitleStyle;
    }

    static GUIStyle GetSubMenuStyle(Color bgColor)
    {
        if(SubMenuStyle == null)
        {
            SubMenuStyle            = new GUIStyle(EditorStyles.helpBox);
            SubMenuStyle.margin     = new RectOffset(0, 0, 0, 0);
            SubMenuStyle.padding    = new RectOffset(0, 0, 0, 0);
            SubMenuStyle.border     = new RectOffset(0, 0, 0, 0);
        }
        if(bgColor != null) SubMenuStyle.normal.background = MakeTex(1, 1, bgColor);

        return SubMenuStyle;
    }

    static Texture2D MakeTex(int width, int height, Color color)
    {
        Color[] pix = new Color[width * height];
        for (int i = 0; i < pix.Length; ++i)
        {
            pix[i] = color;
        }
        Texture2D result = new Texture2D(width, height);
        result.SetPixels(pix);
        result.Apply();
        return result;
    }

    static void HorizontalSeparator(float spaceInPixels = 5.0f)
    {
        GUILayout.Space(spaceInPixels);

        GUIStyle separatorStyle = new GUIStyle(GUI.skin.box);
        separatorStyle.margin = new RectOffset(0, 0, 4, 4);
        separatorStyle.fixedHeight = 2f;
        separatorStyle.stretchWidth = true;
        separatorStyle.normal.background = EditorGUIUtility.whiteTexture;

        GUILayout.Box(GUIContent.none, separatorStyle);

        GUILayout.Space(spaceInPixels);
    }

    static void Indented(Action indentedPart)
    {
        EditorGUI.indentLevel++;
        indentedPart();
        EditorGUI.indentLevel--;
    }

    static void Disabled(bool disabled, Action disabledPart)
    {
        EditorGUI.BeginDisabledGroup(disabled);
            disabledPart();
        EditorGUI.EndDisabledGroup();
    }

    static bool ChangedPropertyField(SerializedProperty prop, GUIContent content = null, params GUILayoutOption[] options)
    {
        EditorGUI.BeginChangeCheck();
        if(content != null) EditorGUILayout.PropertyField(prop, content, options);
        else                EditorGUILayout.PropertyField(prop, options);
        return EditorGUI.EndChangeCheck();
    }

    static void DisabledPropertyField(bool disabled, SerializedProperty prop, GUIContent content = null, params GUILayoutOption[] options)
    {
        EditorGUI.BeginDisabledGroup(disabled);
        if(content != null) EditorGUILayout.PropertyField(prop, content, options);
        else                EditorGUILayout.PropertyField(prop, options);
        EditorGUI.EndDisabledGroup();
    }

    static void SubMenuContext(Action subMenu, string title, Color bgColor, int space = 15)
    {
        EditorGUILayout.BeginVertical(GetSubMenuStyle(bgColor));
        {
            EditorGUILayout.LabelField(title, GetTitleStyle());
            subMenu();
        }
        EditorGUILayout.EndVertical();
    }

    static void ManipulateScalarGenerator3D(/*CPS.ScalarGenerator<Vector3>*/ SerializedProperty scalarGen, string msg, Vector2? clamp = null)
    {
        var type            = scalarGen.FindPropertyRelativeOrFail("Type");
        var exactScalar     = scalarGen.FindPropertyRelativeOrFail("ExactScalar");
        var uniform         = scalarGen.FindPropertyRelativeOrFail("Uniform");
        var bottomScalar    = scalarGen.FindPropertyRelativeOrFail("BottomScalar");
        var topScalar       = scalarGen.FindPropertyRelativeOrFail("TopScalar");


        GUIContent typeLabel    = new GUIContent{ text = $"{msg} Type"   };
        GUIContent exactLabel   = new GUIContent{ text = $"Exact {msg}"  };
        GUIContent bottomLabel  = new GUIContent{ text = $"Bottom {msg}" };
        GUIContent topLabel     = new GUIContent{ text = $"Top {msg}"    };
        GUIContent uniformLabel = new GUIContent{ text = $"Uniform"      };


        EditorGUILayout.PropertyField(type, typeLabel);
        Indented(() =>
        {
            switch((CPS.ScalarGeneratorType)type.enumValueIndex)
            {
                case CPS.ScalarGeneratorType.Exact:
                {
                    EditorGUILayout.PropertyField(uniform);
                    EditorGUILayout.PropertyField(exactScalar, exactLabel);

                    if(uniform.boolValue)
                    {
                        var v = exactScalar.vector3Value;

                        if(v.x == v.z)      { v.x = v.y; v.z = v.y; }
                        else if(v.y == v.z) { v.y = v.x; v.z = v.x; }
                        else                { v.y = v.z; v.x = v.z; }

                        exactScalar.vector3Value = v;
                    }

                    break;
                }
                case CPS.ScalarGeneratorType.Ranged:
                {
                    var becameUniform = ChangedPropertyField(uniform, uniformLabel);

                    if(becameUniform || ChangedPropertyField(bottomScalar, bottomLabel))
                    {
                        if(uniform.boolValue)
                        {
                            var v = bottomScalar.vector3Value;

                            if(v.x == v.z)      { v.x = v.y; v.z = v.y; }
                            else if(v.y == v.z) { v.y = v.x; v.z = v.x; }
                            else                { v.y = v.z; v.x = v.z; }
                            
                            bottomScalar.vector3Value = v;
                        }

                        topScalar.vector3Value = new Vector3
                        (
                            Mathf.Max(bottomScalar.vector3Value.x, topScalar.vector3Value.x),
                            Mathf.Max(bottomScalar.vector3Value.y, topScalar.vector3Value.y),
                            Mathf.Max(bottomScalar.vector3Value.z, topScalar.vector3Value.z)
                        );
                    }
                    if(becameUniform || ChangedPropertyField(topScalar, topLabel))
                    {
                        if(uniform.boolValue)
                        {
                            var v = topScalar.vector3Value;

                            if(v.x == v.z)      { v.x = v.y; v.z = v.y; }
                            else if(v.y == v.z) { v.y = v.x; v.z = v.x; }
                            else                { v.y = v.z; v.x = v.z; }

                            topScalar.vector3Value = v;
                        }

                        bottomScalar.vector3Value = new Vector3
                        (
                            Mathf.Min(bottomScalar.vector3Value.x, topScalar.vector3Value.x),
                            Mathf.Min(bottomScalar.vector3Value.y, topScalar.vector3Value.y),
                            Mathf.Min(bottomScalar.vector3Value.z, topScalar.vector3Value.z)
                        );
                    }
                    break;
                }
                case CPS.ScalarGeneratorType.Gaussian:
                {
                    EditorGUILayout.LabelField("Not supported yet!");
                    break;
                }
            }
        });

        if(clamp.HasValue)
        {
            exactScalar.vector3Value = Clamp(exactScalar.vector3Value, clamp.Value);
            bottomScalar.vector3Value = Clamp(bottomScalar.vector3Value, clamp.Value);
            topScalar.vector3Value = Clamp(topScalar.vector3Value, clamp.Value);
        }
    }

    static Vector3 Clamp(Vector3 vector, Vector2 clamp)
    {
        return new Vector3(
            Math.Clamp(vector.x, clamp.x, clamp.y),
            Math.Clamp(vector.y, clamp.x, clamp.y),
            Math.Clamp(vector.z, clamp.x, clamp.y)
        );
    }

    static void ManipulateScalarGenerator1D(/*CPS.ScalarGenerator<float>*/ SerializedProperty scalarGen, string msg, Vector2? clamp = null)
    {
        var type            = scalarGen.FindPropertyRelativeOrFail("Type");
        var exactScalar     = scalarGen.FindPropertyRelativeOrFail("ExactScalar");
        var bottomScalar    = scalarGen.FindPropertyRelativeOrFail("BottomScalar");
        var topScalar       = scalarGen.FindPropertyRelativeOrFail("TopScalar");


        GUIContent typeLabel    = new GUIContent{ text = $"{msg} Type"   };
        GUIContent exactLabel   = new GUIContent{ text = $"Exact {msg}"  };
        GUIContent bottomLabel  = new GUIContent{ text = $"Bottom {msg}" };
        GUIContent topLabel     = new GUIContent{ text = $"Top {msg}"    };


        EditorGUILayout.PropertyField(type, typeLabel);
        Indented(() =>
        {
            switch((CPS.ScalarGeneratorType)type.enumValueIndex)
            {
                case CPS.ScalarGeneratorType.Exact:
                {
                    EditorGUILayout.PropertyField(exactScalar, exactLabel);
                    break;
                }
                case CPS.ScalarGeneratorType.Ranged:
                {
                    if(ChangedPropertyField(bottomScalar, bottomLabel))
                    {
                        topScalar.floatValue = Mathf.Max(bottomScalar.floatValue, topScalar.floatValue);
                    }
                    else if(ChangedPropertyField(topScalar, topLabel))
                    {
                        bottomScalar.floatValue = Mathf.Min(bottomScalar.floatValue, topScalar.floatValue);
                    }
                    break;
                }
                case CPS.ScalarGeneratorType.Gaussian:
                {
                    EditorGUILayout.LabelField("Not supported yet!");
                    break;
                }
            }
        });

        if(clamp.HasValue)
        {
            exactScalar.floatValue  = Mathf.Clamp(exactScalar.floatValue, clamp.Value.x, clamp.Value.y);
            topScalar.floatValue    = Mathf.Clamp(topScalar.floatValue, clamp.Value.x, clamp.Value.y);
            bottomScalar.floatValue = Mathf.Clamp(bottomScalar.floatValue, clamp.Value.x, clamp.Value.y);
        }
    }

    static void ManipulateFunctionGenerator(/*CPS.FunctionGenerator*/ SerializedProperty funcGen, string msg)
    {
        var type                = funcGen.FindPropertyRelativeOrFail("Type");
        var centerOffset        = funcGen.FindPropertyRelativeOrFail("CenterOffset");
        var radius              = funcGen.FindPropertyRelativeOrFail("Radius");

        GUIContent typeLabel    = new GUIContent{ text = $"{msg} Type"   };


        EditorGUILayout.PropertyField(type, typeLabel);
        Indented(() =>
        {
            switch((CPS.FunctionGeneratorType)type.enumValueIndex)
            {
                case CPS.FunctionGeneratorType.Point:
                {
                    EditorGUILayout.PropertyField(centerOffset);
                    break;
                }
                case CPS.FunctionGeneratorType.Sphere:
                {
                    EditorGUILayout.PropertyField(centerOffset);
                    EditorGUILayout.PropertyField(radius);
                    radius.floatValue = Mathf.Max(0, radius.floatValue);
                    break;
                }
                case CPS.FunctionGeneratorType.Plane:
                case CPS.FunctionGeneratorType.Cuboid:
                {
                    EditorGUILayout.LabelField("Not supported yet!");
                    break;
                }
            }
        });
    }

#endregion

    // Object
    SerializedObject    _CPS;

    // Script properties
    SerializedProperty  _SimulatorTemplate;
    SerializedProperty  _BillboardMaterialTemplate;
    SerializedProperty  _MeshMaterialTemplate;

    // Setting properties
    SerializedProperty  _Seed;
    SerializedProperty  _SimulationSpace;
    SerializedProperty  _RenderType;
    SerializedProperty  _DrawGUI;

    // Render stuff
    SerializedProperty _BillboardTexture;
    SerializedProperty _ParticleMesh;

    // Properties
    SerializedProperty  _StartVelocityGenerator;

    SerializedProperty  _StartScaleGenerator;
    SerializedProperty  _UseEndScale;
    SerializedProperty  _EndScaleGenerator;

    SerializedProperty  _StartRotationGenerator;
    SerializedProperty  _RotationOverTimeGenerator;

    SerializedProperty  _StartLifetimeGenerator;

    SerializedProperty  _StartPositionGenerator;

    SerializedProperty  _StartColourGenerator;
    SerializedProperty  _UseEndColour;
    SerializedProperty  _EndColourGenerator;

    // Simulation properties
    SerializedProperty _MaximumParticleCount;
    SerializedProperty _EmissionRate;
    SerializedProperty _Gravity;
    SerializedProperty _Drag;

    void OnEnable()
    {
        // NOTE: Can be automatized with reflection

        _CPS = new SerializedObject(target);

        _SimulatorTemplate          = _CPS.FindProperty("SimulatorTemplate");
        _BillboardMaterialTemplate  = _CPS.FindProperty("BillboardMaterialTemplate");
        _MeshMaterialTemplate       = _CPS.FindProperty("MeshMaterialTemplate");

        _Seed                       = _CPS.FindProperty("Seed");
        _SimulationSpace            = _CPS.FindProperty("SimulationSpace");
        _RenderType                 = _CPS.FindProperty("RenderType");
        _DrawGUI                    = _CPS.FindProperty("DrawGUI");

        _BillboardTexture           = _CPS.FindProperty("BillboardTexture");
        _ParticleMesh               = _CPS.FindProperty("ParticleMesh");

        _StartVelocityGenerator     = _CPS.FindProperty("StartVelocityGenerator");

        _StartScaleGenerator        = _CPS.FindProperty("StartScaleGenerator");
        _UseEndScale                = _CPS.FindProperty("UseEndScale");
        _EndScaleGenerator          = _CPS.FindProperty("EndScaleGenerator");

        _StartRotationGenerator     = _CPS.FindProperty("StartRotationGenerator");
        _RotationOverTimeGenerator  = _CPS.FindProperty("RotationOverTimeGenerator");

        _StartLifetimeGenerator     = _CPS.FindProperty("StartLifetimeGenerator");

        _StartPositionGenerator     = _CPS.FindProperty("StartPositionGenerator");

        _StartColourGenerator       = _CPS.FindProperty("StartColourGenerator");
        _UseEndColour               = _CPS.FindProperty("UseEndColour");
        _EndColourGenerator         = _CPS.FindProperty("EndColourGenerator");

        _MaximumParticleCount       = _CPS.FindProperty("MaximumParticleCount");
        _EmissionRate               = _CPS.FindProperty("EmissionRate");
        _Gravity                    = _CPS.FindProperty("Gravity");
        _Drag                       = _CPS.FindProperty("Drag");
    }

    CPS Target { get => target as CPS; }

    void TemplatesMenu()
    { 
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.ObjectField("Script:", MonoScript.FromMonoBehaviour(target as CPS), typeof(CPS), true);
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.PropertyField(_SimulatorTemplate,         true);
        EditorGUILayout.PropertyField(_BillboardMaterialTemplate, true);
        EditorGUILayout.PropertyField(_MeshMaterialTemplate,      true);
    }

    void SettingsSubMenu()
    {
        EditorGUILayout.PropertyField(_Seed,            true);
        EditorGUILayout.PropertyField(_SimulationSpace, true);
        EditorGUILayout.PropertyField(_RenderType,      true);

        Indented(() =>
        {
            if((CPS.ParticleRenderType)_RenderType.enumValueIndex == CPS.ParticleRenderType.Mesh)
            {
                EditorGUILayout.PropertyField(_ParticleMesh);
            }
            else
            {
                EditorGUILayout.PropertyField(_BillboardTexture);
            }
        });

        EditorGUILayout.PropertyField(_DrawGUI,         true);
    }

    void PropertiesSubMenu()
    {
        ManipulateScalarGenerator1D(_StartLifetimeGenerator,  "Lifetime",       new Vector2(CPS.MinimalParticleLifetime, float.MaxValue)             );

    HorizontalSeparator();

        ManipulateFunctionGenerator(_StartPositionGenerator,  "Start Position"                                                                       );

    HorizontalSeparator();

        ManipulateScalarGenerator3D(_StartScaleGenerator,     "Start Scale",    new Vector2(float.Epsilon, float.MaxValue)                           );
        EditorGUILayout.PropertyField(_UseEndScale, true);
        if(_UseEndScale.boolValue)
        {
            ManipulateScalarGenerator3D(_EndScaleGenerator,   "End Scale",      new Vector2(float.Epsilon, float.MaxValue)                           );
        }

    HorizontalSeparator();

        Disabled((CPS.ParticleRenderType)_RenderType.enumValueIndex != CPS.ParticleRenderType.Mesh, () =>
        {
            ManipulateScalarGenerator3D(_StartRotationGenerator, "Start Rotation"                                                                    );
        });
        Disabled((CPS.ParticleRenderType)_RenderType.enumValueIndex != CPS.ParticleRenderType.Mesh, () =>
        {
            ManipulateScalarGenerator3D(_RotationOverTimeGenerator, "Rotation Over Time"                                                             );
        });

    HorizontalSeparator();

        ManipulateScalarGenerator3D(_StartVelocityGenerator,  "Start Velocity", null                                                                 );

    HorizontalSeparator();

        ManipulateScalarGenerator3D(_StartColourGenerator,    "Start Colour",   new Vector2(0.0f, 1.0f)                                              );
        EditorGUILayout.PropertyField(_UseEndColour, true);
        if(_UseEndColour.boolValue)
        {
            ManipulateScalarGenerator3D(_EndColourGenerator,   "End Colour",     new Vector2(0.0f, 1.0f)                                             );
        }
    }

    void SimulationSubMenu()
    {
        DisabledPropertyField(Application.isPlaying, _MaximumParticleCount);
        _MaximumParticleCount.intValue = Mathf.Clamp(_MaximumParticleCount.intValue, 0, CPS.HARD_LIMIT);
        
        Disabled(true, () =>
        {
            EditorGUILayout.IntField("Current Particle Count", Target.CurrentParticleCount);
        });

        EditorGUILayout.PropertyField(_EmissionRate);
        _EmissionRate.floatValue = Mathf.Clamp(_EmissionRate.floatValue, 0, CPS.HARD_LIMIT);

    HorizontalSeparator();
        
        EditorGUILayout.PropertyField(_Gravity);

        EditorGUILayout.PropertyField(_Drag);
        _Drag.floatValue = Mathf.Clamp(_Drag.floatValue, 0.0f, 1.0f);
    }

    public override void OnInspectorGUI()
    {
        _CPS.Update();


        SubMenuContext(TemplatesMenu,       "Templates",    ScriptsColor);
        EditorGUILayout.Space();
        SubMenuContext(SettingsSubMenu,     "Settings",     SettingsColor);
        EditorGUILayout.Space();
        SubMenuContext(SimulationSubMenu,   "Simulation",   SimulationColor);   
        EditorGUILayout.Space();
        SubMenuContext(PropertiesSubMenu,   "Properties",   StartColor);   

        _CPS.ApplyModifiedProperties();
    }
}

#endif