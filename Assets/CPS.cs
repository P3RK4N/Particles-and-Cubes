using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor.TerrainTools;
using System.Reflection.Emit;
using System;
using Unity.VisualScripting;
using UnityEditor.UIElements;
using static UnityEngine.GraphicsBuffer;
using UnityEngine.Rendering;
using Unity.Collections;






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
    [SerializeField] public ScalarGenerator<Vector3>    StartSizeGenerator;

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

    [SerializeField] public int MaximumParticleCount;
    [SerializeField] public int CurrentParticleCount = 0;

    /// <summary>
    /// Environmental ForceField stuff (walls, attractors, repulsors)
    /// </summary>
    [SerializeField] public List<ForceFieldDescriptor> ForceFields;

#endregion

    public static CPS Singleton;

    /// <summary>
    /// CPSSimulator instance
    /// </summary>
    ComputeShader Simulator;

    /// <summary>
    /// Buffer containing simulation state
    /// </summary>
    /// Structure:
    /// 
    ///     float: PosX
    ///     float: PosY
    ///     float: PosZ
    ///     
    ComputeBuffer SimulationStateBuffer;

    /// <summary>
    /// Buffer containing mostly readonly data
    /// </summary>
    /// Structure:
    /// 
    ///     vec3: CameraX
    ///     vec3: CameraY
    ///     
    ComputeBuffer GlobalStateBuffer;

    Transform           tf;
    Mesh                mesh;
    MeshFilter          filter;
    Material            material;  
    new MeshRenderer    renderer;

    void Awake()
    {
        if(Singleton == null) Singleton = this;

        Debug.Assert(SimulatorTemplate != null, "SimulatorTemplate should not be null!");

        tf = transform;
        mesh = CreateMesh();
        filter = GetComponent<MeshFilter>();
        filter.mesh = mesh;
        renderer = GetComponent<MeshRenderer>();
        material = renderer.sharedMaterial; // TODO: Change in playmode

        Simulator = Instantiate(SimulatorTemplate);

        CreateBuffers();
    }

    void OnDestroy()
    {
        DeleteBuffers();
    }
    
    void Update()
    {
        if(this == Singleton)
        {
            material.SetVector("CameraX", Camera.main.transform.right);
            material.SetVector("CameraY", Camera.main.transform.up);
        }
    }

    void OnPostRender()
    {
    }

    void OnDrawGizmos()
    {
        if(!DrawGUI) return;

        DrawStartPosition();
    }

    void CreateBuffers()
    {
        //Debug.Assert(MaximumParticleCount > 1, "Mock position error!");

        NativeArray<Vector3> MockPositions = new NativeArray<Vector3>(MaximumParticleCount, Allocator.Temp);
        MockPositions[0] = new Vector3(0, 0, 0);
        //MockPositions[1] = new Vector3(1, 1, 1);

        SimulationStateBuffer = new ComputeBuffer(MaximumParticleCount, 3 /*Floats*/ * 4 /*Bytes*/, ComputeBufferType.Structured, ComputeBufferMode.Dynamic);
        SimulationStateBuffer.SetData(MockPositions);
        material.SetBuffer("SimulationStateBuffer", SimulationStateBuffer);

        GlobalStateBuffer = new ComputeBuffer(1, 2 /*Vector3*/ * 3 /*Floats*/ * 4 /*Bytes*/, ComputeBufferType.Constant);
        material.SetBuffer("GlobaStateBuffer", GlobalStateBuffer);
    }

    void DeleteBuffers()
    {
        SimulationStateBuffer?.Dispose();
        GlobalStateBuffer?.Dispose();
    }

    Mesh CreateMesh()
    {
        Mesh m = new Mesh();

        NativeArray<Vector3>    vertices = new NativeArray<Vector3> (1,                    Allocator.Temp);
        NativeArray<uint>       indices  = new NativeArray<uint>    (MaximumParticleCount, Allocator.Temp);

        m.SetVertices(vertices);
        m.SetIndices(indices, MeshTopology.Points, 0);
        m.bounds = new Bounds(Vector3.zero, new Vector3(5, 5, 5)); // TODO: Make dependent on maximum Geometry Shader output size?

        return m;
    }

    private void DrawStartPosition()
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

    void DrawCuboid(Vector3 bottom, Vector3 top)
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
    SerializedProperty  _StartSizeGenerator;
    SerializedProperty  _StartRotationGenerator;
    SerializedProperty  _StartLifetimeGenerator;
    SerializedProperty  _StartPositionGenerator;

    // Simulation properties
    SerializedProperty _MaximumParticleCount;
    SerializedProperty _CurrentParticleCount;

    private void OnEnable()
    {
        // NOTE: Can be automatized with reflection

        _CPS = new SerializedObject(target);

        _SimulatorTemplate      = _CPS.FindProperty("SimulatorTemplate");

        _SimulationSpace        = _CPS.FindProperty("SimulationSpace");
        _RenderType             = _CPS.FindProperty("RenderType");
        _DrawGUI                = _CPS.FindProperty("DrawGUI");

        _StartVelocityGenerator = _CPS.FindProperty("StartVelocityGenerator");
        _StartSizeGenerator     = _CPS.FindProperty("StartSizeGenerator");
        _StartRotationGenerator = _CPS.FindProperty("StartRotationGenerator");
        _StartLifetimeGenerator = _CPS.FindProperty("StartLifetimeGenerator");
        _StartPositionGenerator = _CPS.FindProperty("StartPositionGenerator");

        _MaximumParticleCount   = _CPS.FindProperty("MaximumParticleCount");
        _CurrentParticleCount   = _CPS.FindProperty("CurrentParticleCount");
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
        ManipulateScalarGenerator3D(_StartSizeGenerator,      "Start Size"      );
        HorizontalSeparator();
        Disabled(Target.RenderType == CPS.ParticleRenderType.Billboard, () => ManipulateScalarGenerator3D(_StartRotationGenerator, "Start Rotation"));
        HorizontalSeparator();
        ManipulateScalarGenerator3D(_StartVelocityGenerator,  "Start Velocity"  );
    }

    void SimulationSubMenu()
    {
        DisabledPropertyField(Application.isPlaying, _MaximumParticleCount);
        DisabledPropertyField(true, _CurrentParticleCount);

        //HorizontalSeparator();
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

        _CPS.ApplyModifiedProperties();
    }
}

#endif