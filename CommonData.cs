using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Singleton class to store common data
public class CommonData
{
    private static CommonData _instance;
    private static readonly object _lock = new object();

    private CommonData() { }

    public static CommonData Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = new CommonData();
                    }
                }
            }
            return _instance;
        }
    }

    public enum Direction { North, South, East, West }
    public enum Axis { PosX, NegX, PosZ, NegZ }

    // +X -> West, -X -> East
    // +Z -> South, -Z -> North
    public const Direction PosX = Direction.West;
    // public Direction NegX = Direction.East;
    public const Direction PosZ = Direction.South;
    // public Direction NegZ = Direction.North;
    public const Axis North = Axis.NegZ;
    // public Axis South = Axis.PosZ;
    public const Axis East = Axis.NegX;
    // public const Axis West = Axis.PosX;

    public float flySpeed;
    public float mouseSensitivity;
    public float walkSpeed;
    public float jumpForce;
    public float gravity;

    public bool lightRotate = false;

    public float playSpeed = 1f;
    public float visualAmplitude = 8000;
    public const float lookXLimit = 90f;
    public const float FPSUpdateInterval = 1f;

    private Dictionary<string, CanvasGroup> _canvasGroups = new Dictionary<string, CanvasGroup>();

    public void RegisterUI(string name, GameObject ui)
    {
        if (_canvasGroups.ContainsKey(name))
        {
            Debug.LogWarning($"UI already registered: {name}");
            return;
        }

        Debug.Log($"Registering UI: {name} - {ui}");

        var canvasGroup = ui.GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = ui.AddComponent<CanvasGroup>();

        _canvasGroups[name] = canvasGroup;
        // EnableCanvasGroup(name, enable);
    }

    public void EnableUI(string name, bool enable)
    {
        var canvasGroup = _canvasGroups[name];
        if (canvasGroup == null)
        {
            Debug.LogWarning($"UI not registered: {name}");
            return;
        }
        Debug.Log($"Enabling UI: {name} - {enable}");
        canvasGroup.alpha = enable ? 1 : 0;
        canvasGroup.interactable = enable;
        canvasGroup.blocksRaycasts = enable;
    }

    public bool UIEnabled(string name)
    {
        var canvasGroup = _canvasGroups[name];
        return canvasGroup != null && canvasGroup.interactable;
    }

    public void ToggleUI(string name)
    {
        var canvasGroup = _canvasGroups[name];
        if (canvasGroup == null) return;
        bool enable = canvasGroup.interactable == false;
        EnableUI(name, enable);
    }

    //     public static void DirectionToAxis(in float[] north, in float[] east, out float[] x, out float[] z)
    //     {
    //         int length = north.Length;

    //         x = new float[length];
    //         z = new float[length];

    //         for (int i = 0; i < length; i++)
    //         {
    // #pragma warning disable CS0162
    //             if (PosX == Direction.North) x[i] = north[i];
    //             else if (PosX == Direction.South) x[i] = -north[i];
    //             else if (PosX == Direction.East) x[i] = east[i];
    //             else if (PosX == Direction.West) x[i] = -east[i];

    //             if (PosZ == Direction.North) z[i] = north[i];
    //             else if (PosZ == Direction.South) z[i] = -north[i];
    //             else if (PosZ == Direction.East) z[i] = east[i];
    //             else if (PosZ == Direction.West) z[i] = -east[i];
    // #pragma warning restore CS0162
    //         }
    //     }

    //     public static void AxisToDirection(in float[] x, in float[] z, out float[] north, out float[] east)
    //     {
    //         int length = x.Length;

    //         north = new float[length];
    //         east = new float[length];

    //         for (int i = 0; i < length; i++)
    //         {
    // #pragma warning disable CS0162
    //             if (North == Axis.PosX) north[i] = x[i];
    //             else if (North == Axis.NegX) north[i] = -x[i];
    //             else if (North == Axis.PosZ) north[i] = z[i];
    //             else if (North == Axis.NegZ) north[i] = -z[i];

    //             if (East == Axis.PosX) east[i] = x[i];
    //             else if (East == Axis.NegX) east[i] = -x[i];
    //             else if (East == Axis.PosZ) east[i] = z[i];
    //             else if (East == Axis.NegZ) east[i] = -z[i];
    // #pragma warning restore CS0162
    //         }
    //     }

    public static void DirectionToAxis(in Vector3 north, in Vector3 east, out Vector3 x, out Vector3 z)
    {
        // todo: torsion
        x = new Vector3(0, 0, 0);
        z = new Vector3(0, 0, 0);
#pragma warning disable CS0162
        if (PosX == Direction.North) x = north;
        else if (PosX == Direction.South) x = new Vector3(-north.x, north.y, north.z);
        else if (PosX == Direction.East) x = east;
        else if (PosX == Direction.West) x = new Vector3(-east.x, east.y, east.z);

        if (PosZ == Direction.North) z = north;
        else if (PosZ == Direction.South) z = new Vector3(-north.x, north.y, north.z);
        else if (PosZ == Direction.East) z = east;
        else if (PosZ == Direction.West) z = new Vector3(-east.x, east.y, east.z);
#pragma warning restore CS0162
    }

    public static void SimplifyData(float[] data, float dt, out float[] simplifiedData, out float simplifiedDt)
    {
        // Reduce the number of data points to avoid performance issues
        const int maxDataPoints = 500;

        if (data.Length <= maxDataPoints)
        {
            simplifiedData = data;
            simplifiedDt = dt;
            return;
        }

        int groupSize = (data.Length + maxDataPoints - 1) / maxDataPoints;
        float[] newData = new float[(data.Length + groupSize - 1) / groupSize];
        for (int i = 0; i < newData.Length; ++i)
        {
            int start = i * groupSize;
            int end = Mathf.Min(start + groupSize, data.Length);
            float sum = 0;
            for (int j = start; j < end; ++j)
            {
                sum += data[j];
            }
            newData[i] = sum / (end - start);
            // float value = 0;
            // for (int j = start; j < end; ++j)
            // {
            //     if (Mathf.Abs(data[j]) > Mathf.Abs(value))
            //     {
            //         value = data[j];
            //     }
            // }
            // newData[i] = value;
        }
        simplifiedData = newData;
        simplifiedDt = dt * groupSize;
    }

    public static GameObject FindInChild(GameObject gameObject, string name)
    {
        foreach (Transform child in gameObject.transform)
        {
            if (child.name == name) return child.gameObject;
            GameObject found = FindInChild(child.gameObject, name);
            if (found != null) return found;
        }
        return null;
    }

    // private bool pythonInitialized = false;
    // private static string envPath = "D:/Programs/anaconda3/envs/disaster";
    // private static string dllName = "python313.dll";
    // public void PythonInit()
    // {
    //     if (pythonInitialized) return;
    //     pythonInitialized = true;
    // }
}
