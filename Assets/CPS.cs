using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor.TerrainTools;
using System.Reflection.Emit;
using System;

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
        Cube,
    };

    public enum FallofType
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

    public enum CPSRenderType
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
    public struct ScalarGenerator<Scalar>
    {
        public ScalarGeneratorType Type;

        // Exact
        public Scalar ExactScalar;
        
        // TODO: Expand
    }

    /// <summary>
    /// Type and parameters of implicit shape (dot, plane, sphere, cube)
    /// </summary>
    public struct FunctionGenerator
    {
        public FunctionGeneratorType Type;

        // Identity
        public Vector3 Identity;
        
        // TODO: Expand
    }

    /// <summary>
    /// Type of force falloff (eg. linear), Force intensity and function/implicit shape where distance is equal to 0
    /// </summary>
    public struct ForceFieldDescriptor
    {
        public FallofType Type;
        public float Intensity;
        public Vector3 Coefficients;
        FunctionGenerator Generator;
    }

#endregion 

#region Script Fields

    /// <summary>
    /// Template of shader which will simulate particles
    /// </summary>
    public ComputeShader CPSSimulatorTemplate { get; set; }

#endregion

#region Setting Fields

    /// <summary>
    /// Decides whether CPS moves with root or not
    /// </summary>
    public SimulationSpaceType SimulationSpace { get; set; }

    /// <summary>
    /// Type of rendering
    /// </summary>
    public CPSRenderType RenderType { get; set; }

#endregion

#region Start Fields

    /// <summary>
    /// Initial velocity stuff
    /// </summary>
    [NonSerialized] public ScalarGenerator<Vector3> StartVelocityGenerator;

    /// <summary>
    /// Initial lifetime stuff
    /// </summary>
    [NonSerialized] public ScalarGenerator<float> StartLifetimeGenerator;

    /// <summary>
    /// Initial size stuff
    /// </summary>
    [NonSerialized] public ScalarGenerator<Vector3> StartSizeGenerator;

    /// <summary>
    /// Initial rotation stuff
    /// </summary>
    [NonSerialized] public ScalarGenerator<Vector3> StartRotationGenerator;

    /// <summary>
    /// Initial position stuff (Shape in which particles generate)
    /// </summary>
    [NonSerialized] public FunctionGenerator StartPositionGenerator;

#endregion

#region SimulationFields

    /// <summary>
    /// Environmental ForceField stuff (walls, attractors, repulsors)
    /// </summary>
    [NonSerialized] public List<ForceFieldDescriptor> ForceFields;

#endregion

    /// <summary>
    /// CPSSimulator instance
    /// </summary>
    ComputeShader CPSSimulator;

    /// <summary>
    /// Buffer containing simulation state
    /// </summary>
    ComputeBuffer CPSSimulationBuffer;

    void Awake()
    {
        CPSSimulator = Instantiate(CPSSimulatorTemplate);
        //CPSSimulationBuffer = new ComputeBuffer();
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
            TitleStyle = new GUIStyle(EditorStyles.boldLabel);
            TitleStyle.alignment = TextAnchor.MiddleCenter;
            TitleStyle.fontSize = 18;
        }

        return TitleStyle;
    }

    static GUIStyle GetSubMenuStyle(Color bgColor)
    {
        if(SubMenuStyle == null)
        {
            SubMenuStyle = new GUIStyle(EditorStyles.helpBox);
            SubMenuStyle.margin = new RectOffset(0, 0, 0, 0);
            SubMenuStyle.padding = new RectOffset(0, 0, 0, 0);
            SubMenuStyle.border = new RectOffset(0, 0, 0, 0);
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

    static void Indented(Action indentedPart)
    {
        EditorGUI.indentLevel++;
        indentedPart();
        EditorGUI.indentLevel--;
    }

    static void SubMenuContext(Action subMenu, string title, Color bgColor, int space = 15)
    {
        EditorGUILayout.Space(space);

        EditorGUILayout.BeginVertical(GetSubMenuStyle(bgColor));
        {
            EditorGUILayout.LabelField(title, GetTitleStyle());
            subMenu();
        }
        EditorGUILayout.EndVertical();
    }

    static void ManipulateScalarGenerator3D(ref CPS.ScalarGenerator<Vector3> scalarGen, string msg)
    {
        scalarGen.Type = (CPS.ScalarGeneratorType)EditorGUILayout.EnumPopup($"{msg}: ", scalarGen.Type);
        EditorGUI.indentLevel++;
            switch(scalarGen.Type) 
            {
                case CPS.ScalarGeneratorType.Exact:
                {
                    scalarGen.ExactScalar = EditorGUILayout.Vector3Field($"Exact {msg}: ", scalarGen.ExactScalar);
                    break;
                }
                case CPS.ScalarGeneratorType.Ranged:
                case CPS.ScalarGeneratorType.Gaussian:
                {
                    EditorGUILayout.LabelField("Not supported yet!");
                    break;
                }
            }
        EditorGUI.indentLevel--;
    }

    static void ManipulateScalarGenerator1D(ref CPS.ScalarGenerator<float> scalarGen, string msg, bool nonNegative = true)
    {
        scalarGen.Type = (CPS.ScalarGeneratorType)EditorGUILayout.EnumPopup($"{msg}: ", scalarGen.Type);
        EditorGUI.indentLevel++;
            switch(scalarGen.Type) 
            {
                case CPS.ScalarGeneratorType.Exact:
                {
                    scalarGen.ExactScalar = EditorGUILayout.FloatField($"Exact {msg}: ", scalarGen.ExactScalar);
                    if(nonNegative) scalarGen.ExactScalar = Mathf.Max(scalarGen.ExactScalar, 0);
                    break;
                }
                case CPS.ScalarGeneratorType.Ranged:
                case CPS.ScalarGeneratorType.Gaussian:
                {
                    EditorGUILayout.LabelField("Not supported yet!");
                    break;
                }
            }
        EditorGUI.indentLevel--;
    }

    static void ManipulateFunctionGenerator(ref CPS.FunctionGenerator funcGen, string msg)
    {
        funcGen.Type = (CPS.FunctionGeneratorType)EditorGUILayout.EnumPopup($"{msg}: ", funcGen.Type);
        EditorGUI.indentLevel++;
            switch(funcGen.Type) 
            {
                case CPS.FunctionGeneratorType.Point:
                {
                    funcGen.Identity = EditorGUILayout.Vector3Field($"Exact {msg}: ", funcGen.Identity);
                    break;
                }
                case CPS.FunctionGeneratorType.Sphere:
                case CPS.FunctionGeneratorType.Plane:
                case CPS.FunctionGeneratorType.Cube:
                {
                    EditorGUILayout.LabelField("Not supported yet!");
                    break;
                }
            }
        EditorGUI.indentLevel--;
    }

#endregion

    CPS Target
    {
        get => target as CPS;
    }

    void ScriptSubMenu()
    { 
        DrawDefaultInspector();
        Target.CPSSimulatorTemplate = (ComputeShader)EditorGUILayout.ObjectField("CPS Simulation Template: ", Target.CPSSimulatorTemplate as UnityEngine.Object, typeof(ComputeShader), true);
    }

    void SettingsSubMenu()
    {
        Target.SimulationSpace  = (CPS.SimulationSpaceType) EditorGUILayout.EnumPopup("Simulation space: ", Target.SimulationSpace);
        Target.RenderType       = (CPS.CPSRenderType)       EditorGUILayout.EnumPopup("Render type: ", Target.RenderType);
    }

    void StartSubMenu()
    {
        ManipulateScalarGenerator3D(ref Target.StartVelocityGenerator,  "Start Velocity");
        ManipulateScalarGenerator3D(ref Target.StartSizeGenerator,      "Start Size");
        ManipulateScalarGenerator3D(ref Target.StartRotationGenerator,  "Start Rotation");

        ManipulateScalarGenerator1D(ref Target.StartLifetimeGenerator,  "Lifetime");

        ManipulateFunctionGenerator(ref Target.StartPositionGenerator,  "Position");
    }

    void SimulationSubMenu()
    {

    }

    void EndSubMenu()
    {

    }

    public override void OnInspectorGUI()
    {
        SubMenuContext(ScriptSubMenu,       "Scripts",      ScriptsColor);
        SubMenuContext(SettingsSubMenu,     "Settings",     SettingsColor);
        SubMenuContext(StartSubMenu,        "Start",        StartColor);   
        SubMenuContext(SimulationSubMenu,   "Simulation",   SimulationColor);   
        SubMenuContext(EndSubMenu,          "End",          EndColor);   
    }
}

#endif