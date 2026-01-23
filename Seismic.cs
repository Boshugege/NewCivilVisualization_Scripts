using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.Assertions;
using Debug = UnityEngine.Debug;

public class Seismic : MonoBehaviour
{
    private static CommonData commonData = CommonData.Instance;
    public static string dataFolderPath = $"{Application.streamingAssetsPath}/Python/seismic_data/json/";
    public List<MeshDeformer> meshDeformers = new List<MeshDeformer>();

    // public volatile bool isSolving = false;
    // public volatile bool resultReady = false;
    // public int solvedCount = 0;
    // public Stopwatch stopwatch = new();

    public string dataFile = "hebei_acc_data.json";
    public Solver1D.Config config;

    public class SeismicData
    {
        public int nt;  // number of time steps
        public float dt;    // time step
        public List<float> north;   // north acceleration
        public List<float> east;    // east acceleration

        public static SeismicData FromJson(string path)
        {
            string jsonString = System.IO.File.ReadAllText(path);
            return JsonUtility.FromJson<SeismicData>(jsonString);
        }

        public void ToJson(string fileName/*, string path = dataFolderPath*/)
        {
            string jsonString = JsonUtility.ToJson(this);
            System.IO.File.WriteAllText(dataFolderPath + fileName, jsonString);
        }
    }

    public SeismicData data;
    public float[] north;
    public float[] east;
    public float duration;


    public float currentTime = 0f;
    public bool isPlaying = false;

    private ConfigMenu configMenu;
    // private WaveInfo waveInfo;
    private Sensor sensor;
    private GameObject buildings;

    void Awake()
    {
        duration = 1f;
        configMenu = GameObject.Find("ConfigMenu").GetComponent<ConfigMenu>();
        Assert.IsNotNull(configMenu);
        // waveInfo = GameObject.Find("UICanvas").GetComponent<WaveInfo>();
        // Assert.IsNotNull(waveInfo);
        sensor = GameObject.Find("Sensor").GetComponent<Sensor>();
        Assert.IsNotNull(sensor);
        buildings = GameObject.Find("Buildings");
        Assert.IsNotNull(buildings);

        int vertexCount = 0;
        foreach (Transform child in buildings.transform)
        {
            var meshDeformer = child.gameObject.AddComponent<MeshDeformer>();
            meshDeformers.Add(meshDeformer);
            vertexCount += meshDeformer.vertexCount;
        }

        Debug.Log("Seismic.cs - Awake, vertex count: " + vertexCount);

        config = new Solver1D.Config()
        {
            xWidth = 40f,
            zWidth = 40f,
            mode = ShakeMode.Response,
            modelType = ModelType.Shear,
            // modelType = ModelType.Torsion,
            coloringType = ColoringType.Displacement,
        };
    }

    public void ApplyConfig()
    {
        data = SeismicData.FromJson(dataFolderPath + dataFile);
        north = data.north.ToArray();
        east = data.east.ToArray();
        duration = data.nt * data.dt;
        // Debug.Log("north max: " + north.MaximumAbsolute());
        // Debug.Log("east max: " + east.MaximumAbsolute());

        // ChartPainter.GeneratePlots(new float[][] { north, east });

        // float[] xAcc;
        // float[] zAcc;
        // CommonData.DirectionToAxis(north, east, out xAcc, out zAcc);

        foreach (Transform child in buildings.transform)
        {
            var meshDeformer = child.gameObject.GetComponent<MeshDeformer>();

            // meshDeformer.xConfig = new Solver1D.Config()
            // {
            //     levels = meshDeformer.numLevels,
            //     mode = ShakeMode.Response,
            //     modelType = ModelType.Shear,
            //     nt = data.nt,
            //     dt = data.dt,
            //     acc = xAcc,
            //     coloringType = ColoringType.Displacement,
            // };

            // meshDeformer.zConfig = new Solver1D.Config()
            // {
            //     levels = meshDeformer.numLevels,
            //     mode = ShakeMode.Response,
            //     modelType = ModelType.Shear,
            //     nt = data.nt,
            //     dt = data.dt,
            //     acc = zAcc,
            //     coloringType = ColoringType.Displacement,
            // };

            // meshDeformer.xConfig = new Solver1D.Config(config);
            // meshDeformer.xConfig.levels = meshDeformer.numLevels;
            // meshDeformer.xConfig.nt = data.nt;
            // meshDeformer.xConfig.dt = data.dt;
            // meshDeformer.xConfig.acc = xAcc;

            // meshDeformer.zConfig = new Solver1D.Config(config);
            // meshDeformer.zConfig.levels = meshDeformer.numLevels;
            // meshDeformer.zConfig.nt = data.nt;
            // meshDeformer.zConfig.dt = data.dt;
            // meshDeformer.zConfig.acc = zAcc;

            // meshDeformer.xSolver = new Solver1D(meshDeformer.xConfig);
            // meshDeformer.zSolver = new Solver1D(meshDeformer.zConfig);

            meshDeformer.northConfig = new Solver1D.Config(config);
            meshDeformer.northConfig.levels = meshDeformer.numLevels;
            meshDeformer.northConfig.nt = data.nt;
            meshDeformer.northConfig.dt = data.dt;
            meshDeformer.northConfig.acc = north;

            meshDeformer.eastConfig = new Solver1D.Config(config);
            meshDeformer.eastConfig.levels = meshDeformer.numLevels;
            meshDeformer.eastConfig.nt = data.nt;
            meshDeformer.eastConfig.dt = data.dt;
            meshDeformer.eastConfig.acc = east;

            meshDeformer.northSolver = new Solver1D(meshDeformer.northConfig);
            meshDeformer.eastSolver = new Solver1D(meshDeformer.eastConfig);
        }
    }

    public IEnumerator SolveCoroutine()
    {
        Stopwatch stopwatch = new Stopwatch();
        stopwatch.Start();
        configMenu.solveButton.interactable = false;
        configMenu.solveText.text = "Solving...";

        int solvedCount = 0;

        var progress = new Progress<int>(count =>
        {
            configMenu.solveProgressText.text = $"Solving {count}/{meshDeformers.Count}";
        });

        List<Task> tasks = new List<Task>();
        foreach (var meshDeformer in meshDeformers)
        {
            tasks.Add(Task.Run(() =>
            {
                meshDeformer.Solve();
                (progress as IProgress<int>).Report(Interlocked.Increment(ref solvedCount));
            }));
        }

        var allTasks = Task.WhenAll(tasks);
        while (!allTasks.IsCompleted)
        {
            yield return null;
        }

        yield return null;
        configMenu.solveButton.interactable = true;
        configMenu.solveText.text = "Solve";
        configMenu.solveProgressText.text = $"Solved({meshDeformers.Count})";
        sensor.SetInputAcceleration(north, east, data.dt);
        stopwatch.Stop();
        Debug.Log($"Seismic.cs - Solve used {stopwatch.ElapsedMilliseconds}ms");

        Debug.Log($"Seismic.cs - m0_east_maxDisp: {meshDeformers[0].eastSolver.maxDisplacement}");
        Debug.Log($"Seismic.cs - m0_north_maxDisp: {meshDeformers[0].northSolver.maxDisplacement}");
        Debug.Log($"Seismic.cs - m0_east_maxAcc: {meshDeformers[0].eastSolver.maxAcceleration}");
        Debug.Log($"Seismic.cs - m0_north_maxAcc: {meshDeformers[0].northSolver.maxAcceleration}");
    }

    // public void Solve()
    // {
    //     stopwatch.Restart();
    //     configMenu.solveButton.interactable = false;
    //     configMenu.solveText.text = "Solving...";
    //     configMenu.updateSolveProgress = true;
    //     isSolving = true;
    //     solvedCount = 0;
    //     _ = StartSolving(true);
    // }

    // private async Task StartSolving(bool useThreadPool)
    // {
    //     if (useThreadPool) await SolveWithThreadPool(); // 使用线程池加速求解
    //     else await SolveSingleThread(); // 单线程求解
    // }

    // private async Task SolveWithThreadPool()
    // {
    //     List<Task> tasks = new List<Task>();
    //     foreach (var meshDeformer in meshDeformers)
    //     {
    //         tasks.Add(Task.Run(() => SolveTask(meshDeformer)));
    //     }
    //     await Task.WhenAll(tasks);
    // }

    // private async Task SolveSingleThread()
    // {
    //     foreach (var meshDeformer in meshDeformers)
    //     {
    //         await Task.Run(() => SolveTask(meshDeformer));
    //     }
    // }

    // private void SolveTask(MeshDeformer meshDeformer)
    // {
    //     meshDeformer.Solve();
    //     // Interlocked.Increment(ref solvedCount);
    //     if (Interlocked.Increment(ref solvedCount) == meshDeformers.Count)
    //     {
    //         isSolving = false;
    //     }
    // }

    void Update()
    {
        if (isPlaying)
        {
            // currentTime += Time.deltaTime;
            SetTime(currentTime + Time.deltaTime * commonData.playSpeed);
        }
    }

    public void SetTime(float t)
    {
        if (t < 0 || t >= duration)
        {
            isPlaying = false;
            t = Mathf.Clamp(t, 0, duration);
        }

        currentTime = t;
        sensor.SetTime(t);

        foreach (var meshDeformer in meshDeformers)
        {
            meshDeformer.SetTime(t);
        }
    }

    public void Play()
    {
        isPlaying = true;
    }

    public void Pause()
    {
        isPlaying = false;
    }

    public void Stop()
    {
        isPlaying = false;
        SetTime(0f);
    }

    public void EnableColoring(bool enable)
    {
        Debug.Log("Seismic.cs - SetEnableColoring " + enable);
        foreach (var meshDeformer in meshDeformers)
        {
            meshDeformer.EnableColoring(enable);
        }
    }

    public void EnableInternalStructure(bool enable)
    {
        Debug.Log("Seismic.cs - SetInternalStructure " + enable);
        foreach (var meshDeformer in meshDeformers)
        {
            meshDeformer.EnableInternalStructure(enable);
        }
    }

    public void EnableExternalStructure(bool enable)
    {
        Debug.Log("Seismic.cs - SetExternalStructure " + enable);
        foreach (var meshDeformer in meshDeformers)
        {
            meshDeformer.EnableExternalStructure(enable);
        }
    }
}



// [CustomEditor(typeof(Seismic))]
// public class SeismicEditor : Editor
// {
//     enum SolvingState
//     {
//         NotStarted,
//         Solving,
//         Solved,
//     }
//     SolvingState solvingState = SolvingState.NotStarted;
//     int solvedCount = 0;
//     object lockObj = new();
//     System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();

//     public override void OnInspectorGUI()
//     {
//         base.OnInspectorGUI();
//         var t = (Seismic)target;

//         if (solvingState == SolvingState.NotStarted)
//         {
//             if (GUILayout.Button("Solve Directly"))
//             {
//                 Debug.Log("Solving, building count: " + t.meshDeformers.Count);
//                 stopwatch.Start();

//                 solvingState = SolvingState.Solving;

//                 foreach (var meshDeformer in t.meshDeformers)
//                 {
//                     ThreadPool.QueueUserWorkItem(state =>
//                     {
//                         meshDeformer.Solve();
//                         lock (lockObj)
//                         {
//                             ++solvedCount;
//                             if (solvedCount == t.meshDeformers.Count)
//                             {
//                                 stopwatch.Stop();
//                                 Debug.Log("Solve used " + stopwatch.ElapsedMilliseconds + "ms");
//                                 solvingState = SolvingState.Solved;
//                             }
//                         }
//                     });
//                 }
//             }
//         }
//         if (solvingState == SolvingState.Solved)
//         {
//             if (GUILayout.Button("Play"))
//             {
//                 foreach (var meshDeformer in t.meshDeformers)
//                 {
//                     meshDeformer.isPlaying = true;
//                 }
//             }
//             if (GUILayout.Button("Pause"))
//             {
//                 foreach (var meshDeformer in t.meshDeformers)
//                 {
//                     meshDeformer.isPlaying = false;
//                 }
//             }
//             if (GUILayout.Button("Stop"))
//             {
//                 foreach (var meshDeformer in t.meshDeformers)
//                 {
//                     meshDeformer.isStopped = true;
//                 }
//             }
//             if (GUILayout.Button("Enable Internal Structure"))
//             {
//                 foreach (var meshDeformer in t.meshDeformers)
//                 {
//                     meshDeformer.SetInternalStructure(true);
//                 }
//             }
//             if (GUILayout.Button("Disable Internal Structure"))
//             {
//                 foreach (var meshDeformer in t.meshDeformers)
//                 {
//                     meshDeformer.SetInternalStructure(false);
//                 }
//             }
//             if (GUILayout.Button("Enable Coloring"))
//             {
//                 foreach (var meshDeformer in t.meshDeformers)
//                 {
//                     meshDeformer.SetEnableColoring(true);
//                 }
//             }
//             if (GUILayout.Button("Disable Coloring"))
//             {
//                 foreach (var meshDeformer in t.meshDeformers)
//                 {
//                     meshDeformer.SetEnableColoring(false);
//                 }
//             }
//         }
//     }
// }