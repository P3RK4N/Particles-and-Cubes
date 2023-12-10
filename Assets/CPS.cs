using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor.TerrainTools;
using System.Reflection.Emit;
using System;
using Unity.VisualScripting;
using UnityEditor.UIElements;
using static UnityEngine.GraphicsBuffer;
using Unity.Mathematics;
using UnityEngine.UIElements;
using System.Reflection;

using System.Runtime.InteropServices;




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
        Point,
        Billboard,
        Mesh
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

    //[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    static int GlobalStateDTOSize = 49;

#endregion 

#region Script Fields

    /// <summary>
    /// Template of shader which will simulate particles
    /// </summary>
    [SerializeField] public ComputeShader SimulatorTemplate;

#endregion

#region Setting Fields

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

#region Start Fields

    /// <summary>
    /// Initial velocity stuff
    /// </summary>
    [SerializeField] public ScalarGenerator<Vector3>    StartVelocityGenerator;

    /// <summary>
    /// Initial lifetime stuff
    /// </summary>
    [SerializeField] public ScalarGenerator<float>      StartLifetimeGenerator;

    /// <summary>
    /// Initial size stuff
    /// </summary>
    [SerializeField] public ScalarGenerator<Vector3>    StartScaleGenerator;

    /// <summary>
    /// Initial rotation stuff
    /// </summary>
    [SerializeField] public ScalarGenerator<Vector3>    StartRotationGenerator;

    /// <summary>
    /// Initial position stuff (Shape in which particles generate)
    /// </summary>
    [SerializeField] public FunctionGenerator           StartPositionGenerator;

#endregion

#region SimulationFields

    [SerializeField] public int MaximumParticleCount; public int CurrentParticleCount;
    [SerializeField] public int EmissionRate;         public float EmissionAmount;

    [SerializeField] public bool UseGravity;

    /// <summary>
    /// Environmental ForceField stuff (walls, attractors, repulsors)
    /// </summary>
    [SerializeField] public List<ForceFieldDescriptor> ForceFields;

    #endregion

#region Other

    [SerializeField] public Texture2D                   MainTexture;

#endregion

    public static CPS Singleton;
    public static readonly int HARD_LIMIT = 32 * 32 * 32 * 32 - 1;

    /// <summary>
    /// CPSSimulator instance
    /// </summary>
    ComputeShader Simulator;

    /// <summary>
    /// Rendering context
    /// </summary>
    RenderParams renderParams;

    /// <summary>
    /// Defines number of vertices and instances per graphics buffer 
    /// </summary>
    GraphicsBuffer CommandBuffer;

    /// <summary>
    /// Buffer containing simulation state
    /// </summary>
    /// Structure:
    /// 
    ///     float: PosX
    ///     float: PosY
    ///     float: PosZ
    ///     
    GraphicsBuffer SimulationStateBuffer;

    /// <summary>
    /// Buffer containing mostly readonly data
    /// </summary>
    /// Structure:
    /// 
    GraphicsBuffer GlobalStateBuffer;

    /// <summary>
    /// Counter buffers
    /// </summary>
    ComputeBuffer EmissionAmountCounterBuffer;
    ComputeBuffer CurrentParticleCountBuffer;
    ComputeBuffer CopyCounterBuffer;

    Transform           tf;
    Material            material;
    Material            sharedMaterial;
    new MeshRenderer    renderer;

    void Awake()
    {
        if(Singleton == null) Singleton = this;

        Debug.Assert(SimulatorTemplate != null, "SimulatorTemplate should not be null!");

        tf              = transform;
        renderer        = GetComponent<MeshRenderer>();
        material        = renderer.material;
        sharedMaterial  = renderer.sharedMaterial;

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
        if(this == Singleton) Singleton = null;
        DeleteBuffers();
    }
    
    void Update()
    {
        if(this == Singleton) UpdateGlobalShaderVariables();
        UpdateLocalShaderVariables();
        SynchronizeCounters();

        // Emit
        Simulator.Dispatch((int)SimulatorKernelType.MockEmit, GetDispatchNum(), GetDispatchNum(), 1);

        // Simulate
        Simulator.Dispatch((int)SimulatorKernelType.MockTick, GetDispatchNum(), GetDispatchNum(), 1);
        
        // Render
        Graphics.RenderPrimitivesIndirect(renderParams, MeshTopology.Points, CommandBuffer);
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
        Simulator.SetInt("SimulationSpace", ((int)SimulationSpace));
        Simulator.SetInt("RenderType",      ((int)RenderType));
        
        // Set kernel-related values
        Simulator.SetInt("DISPATCH_NUM", GetDispatchNum());
        Simulator.SetInt("MAX_PARTICLE_COUNT", MaximumParticleCount);

        // Set time-related values
        Simulator.SetFloat("DeltaTime", Time.deltaTime);
        Simulator.SetFloat("Time", Time.time);

        // Set environment-related values
        Simulator.SetVector("EmitterPositionWS", tf.position);
        Simulator.SetFloat("GravityForce", UseGravity ? -9.81f : 0);

        // Set lifetime-related values
        Simulator.SetInt("LifetimeScalarType", ((int)StartLifetimeGenerator.Type));
        Simulator.SetFloat("ExactLifetime", StartLifetimeGenerator.ExactScalar);
        Simulator.SetFloat("BottomLifetime", StartLifetimeGenerator.BottomScalar);
        Simulator.SetFloat("TopLifetime", StartLifetimeGenerator.TopScalar);

        // Set position-related values
        Simulator.SetInt("PositionFunctionType", ((int)StartPositionGenerator.Type));
        Simulator.SetVector("CenterOffset", StartPositionGenerator.CenterOffset);
        Simulator.SetFloat("Radius", StartPositionGenerator.Radius);

        // Set velocity-related values
        Simulator.SetInt("VelocityScalarType", ((int)StartVelocityGenerator.Type));
        Simulator.SetVector("ExactVelocity", StartVelocityGenerator.ExactScalar);
        Simulator.SetVector("BottomVelocity", StartVelocityGenerator.BottomScalar);
        Simulator.SetVector("TopVelocity", StartVelocityGenerator.TopScalar);

        // Set scale-related values
        Simulator.SetInt("ScaleScalarType", ((int)StartScaleGenerator.Type));
        Simulator.SetVector("ExactScale", StartScaleGenerator.ExactScalar);
        Simulator.SetVector("BottomScale", StartScaleGenerator.BottomScalar);
        Simulator.SetVector("TopScale", StartScaleGenerator.TopScalar);

        // Set rotation-related values
        Simulator.SetInt("RotationScalarType", ((int)StartRotationGenerator.Type));
        Simulator.SetVector("ExactRotation", StartRotationGenerator.ExactScalar);
        Simulator.SetVector("BottomRotation", StartRotationGenerator.BottomScalar);
        Simulator.SetVector("TopRotation", StartRotationGenerator.TopScalar);
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
        Simulator.SetVector("EmitterPositionWS", tf.position);
        Simulator.SetFloat("DeltaTime", Time.deltaTime);
        Simulator.SetFloat("Time", Time.time);

        // TODO: Potentially include rotation and scale later
        //renderParams.matProps.SetMatrix("ObjectToWorld", Matrix4x4.TRS(tf.position, Quaternion.identity, new Vector3(1,1,1)));
        renderParams.matProps.SetMatrix("ObjectToWorld", tf.localToWorldMatrix);
    }

    void UpdateGlobalShaderVariables()
    {
        // TODO: Consider unique prefix for global vars
        UpdateBillboardPoints();
    }

    void UpdateBillboardPoints()
    {
        var rot = Matrix4x4.Rotate(Camera.main.transform.rotation);
        
        Vector4[] billboardPoints = new Vector4[]
        {
            rot * (- Vector3.right - Vector3.up),
            rot * (- Vector3.right + Vector3.up),
            rot * (  Vector3.right - Vector3.up),
            rot * (  Vector3.right + Vector3.up)
        };

        for(int i = 0; i < 4; i++) billboardPoints[i].w = 1.0f;

        Shader.SetGlobalVectorArray("BillboardPoints", billboardPoints);
        //renderParams.matProps.SetVectorArray("ObjectPoints", objectPoints);
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
                + 3 /* Scale             */
                + 3 /* Rotation          */
                + 3 /* Velocity          */
                + 3 /* OuterVelocity     */
                + 3 /* Colour            */
                + 2 /* Current_Max_Life  */
                + 2 /* SimSpace_RendType */
            ) /*Floats*/ * 4 /*Bytes*/
        );

        // Global state buffer
        GlobalStateBuffer = new GraphicsBuffer
        (
            Target.Constant, 
            GlobalStateDTOSize,
            4
        );

        // Counters && CopyCount buffer
        EmissionAmountCounterBuffer = new ComputeBuffer(1, 4, ComputeBufferType.Counter);
        CurrentParticleCountBuffer = new ComputeBuffer(1, 4, ComputeBufferType.Counter);
        EmissionAmountCounterBuffer.SetCounterValue(0);
        CurrentParticleCountBuffer.SetCounterValue(0);
        CopyCounterBuffer = new ComputeBuffer(1, 4, ComputeBufferType.Raw);

        // Command Buffer
        CommandBuffer = new GraphicsBuffer(Target.IndirectArguments, 1, IndirectDrawArgs.size);
        CommandBuffer.SetData(new IndirectDrawArgs[]{ new IndirectDrawArgs{ vertexCountPerInstance = (uint)MaximumParticleCount, instanceCount = 1 } });
    }

    void DeleteBuffers()
    {
        SimulationStateBuffer?.Release();
        GlobalStateBuffer?.Release();
        CommandBuffer?.Release();
        CurrentParticleCountBuffer?.Release();
        EmissionAmountCounterBuffer?.Release();
        CopyCounterBuffer?.Release();
    }

    void InitializeCompute()
    {
        Simulator = Instantiate(SimulatorTemplate);

        //Simulator.SetInt("DISPATCH_NUM", GetDispatchNum());
        //Simulator.SetInt("MAX_PARTICLE_COUNT", MaximumParticleCount);

        //Simulator.SetFloat("GravityForce", UseGravity ? -9.81f : 0.0f);

        foreach(SimulatorKernelType kernel in Enum.GetValues(typeof(SimulatorKernelType)))
        {
            Simulator.SetBuffer((int)kernel, "SimulationStateBuffer",       SimulationStateBuffer       );
            Simulator.SetBuffer((int)kernel, "GlobalStateBuffer",           GlobalStateBuffer           );
            Simulator.SetBuffer((int)kernel, "CurrentParticleCountBuffer",  CurrentParticleCountBuffer  );
            Simulator.SetBuffer((int)kernel, "EmissionAmountCounterBuffer", EmissionAmountCounterBuffer );
        }
    }

    void InitializeRenderContext()
    {
        renderParams             = new RenderParams(Instantiate(material)); // Consider sharedMat or mat
        renderParams.worldBounds = new Bounds(Vector3.zero, 10000*Vector3.one);
        renderParams.matProps    = new MaterialPropertyBlock();
        renderParams.matProps.SetBuffer ("SimulationStateBuffer", SimulationStateBuffer );
        renderParams.matProps.SetBuffer ("GlobalStateBuffer",     GlobalStateBuffer     );
        renderParams.matProps.SetTexture("_MainTex",              MainTexture           );
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

    static void ManipulateScalarGenerator3D(/*CPS.ScalarGenerator<Vector3>*/ SerializedProperty scalarGen, string msg)
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
    }

    static void ManipulateScalarGenerator1D(/*CPS.ScalarGenerator<float>*/ SerializedProperty scalarGen, string msg, bool nonNegative = true)
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

        if(nonNegative)
        {
            exactScalar.floatValue  = Mathf.Max(0, exactScalar.floatValue  );
            topScalar.floatValue    = Mathf.Max(0, topScalar.floatValue    );
            bottomScalar.floatValue = Mathf.Max(0, bottomScalar.floatValue );
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

    // Setting properties
    SerializedProperty  _SimulationSpace;
    SerializedProperty  _RenderType;
    SerializedProperty  _DrawGUI;

    // Start properties
    SerializedProperty  _StartVelocityGenerator;
    SerializedProperty  _StartScaleGenerator;
    SerializedProperty  _StartRotationGenerator;
    SerializedProperty  _StartLifetimeGenerator;
    SerializedProperty  _StartPositionGenerator;

    // Simulation properties
    SerializedProperty _MaximumParticleCount;
    SerializedProperty _EmissionRate;
    SerializedProperty _UseGravity;

    // Other
    SerializedProperty _MainTexture;

    void OnEnable()
    {
        // NOTE: Can be automatized with reflection

        _CPS = new SerializedObject(target);

        _SimulatorTemplate      = _CPS.FindProperty("SimulatorTemplate");

        _SimulationSpace        = _CPS.FindProperty("SimulationSpace");
        _RenderType             = _CPS.FindProperty("RenderType");
        _DrawGUI                = _CPS.FindProperty("DrawGUI");

        _StartVelocityGenerator = _CPS.FindProperty("StartVelocityGenerator");
        _StartScaleGenerator    = _CPS.FindProperty("StartScaleGenerator");
        _StartRotationGenerator = _CPS.FindProperty("StartRotationGenerator");
        _StartLifetimeGenerator = _CPS.FindProperty("StartLifetimeGenerator");
        _StartPositionGenerator = _CPS.FindProperty("StartPositionGenerator");

        _MaximumParticleCount   = _CPS.FindProperty("MaximumParticleCount");
        _EmissionRate           = _CPS.FindProperty("EmissionRate");
        _UseGravity             = _CPS.FindProperty("UseGravity");

        _MainTexture            = _CPS.FindProperty("MainTexture");
    }

    CPS Target { get => target as CPS; }

    void ScriptSubMenu()
    { 
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.ObjectField("Script:", MonoScript.FromMonoBehaviour(target as CPS), typeof(CPS), true);
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.PropertyField(_SimulatorTemplate, true);
    }

    void SettingsSubMenu()
    {
        EditorGUILayout.PropertyField(_SimulationSpace, true);
        EditorGUILayout.PropertyField(_RenderType,      true);
        EditorGUILayout.PropertyField(_DrawGUI,         true);
    }

    void StartSubMenu()
    {
        ManipulateScalarGenerator1D(_StartLifetimeGenerator,  "Lifetime"        );
        HorizontalSeparator();
        ManipulateFunctionGenerator(_StartPositionGenerator,  "Position"        );
        HorizontalSeparator();
        ManipulateScalarGenerator3D(_StartScaleGenerator,     "Start Scale"     );
        HorizontalSeparator();
        Disabled(Target.RenderType == CPS.ParticleRenderType.Billboard, () => ManipulateScalarGenerator3D(_StartRotationGenerator, "Start Rotation"));
        HorizontalSeparator();
        ManipulateScalarGenerator3D(_StartVelocityGenerator,  "Start Velocity"  );
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
        _EmissionRate.intValue = Mathf.Clamp(_EmissionRate.intValue, 0, CPS.HARD_LIMIT);

        HorizontalSeparator();
        
        EditorGUILayout.PropertyField(_UseGravity);
    }

    void EndSubMenu()
    {

    }

    public override void OnInspectorGUI()
    {
        _CPS.Update();


        SubMenuContext(ScriptSubMenu,       "Scripts",      ScriptsColor);
        EditorGUILayout.Space();
        SubMenuContext(SettingsSubMenu,     "Settings",     SettingsColor);
        EditorGUILayout.Space();
        SubMenuContext(StartSubMenu,        "Start",        StartColor);   
        EditorGUILayout.Space();
        SubMenuContext(SimulationSubMenu,   "Simulation",   SimulationColor);   
        EditorGUILayout.Space();
        SubMenuContext(EndSubMenu,          "End",          EndColor);   

        EditorGUILayout.PropertyField(_MainTexture);

        _CPS.ApplyModifiedProperties();
    }
}

#endif