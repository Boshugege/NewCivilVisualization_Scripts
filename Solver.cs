#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Single;
using MathNet.Numerics.OdeSolvers;
using static UnityEngine.Mathf;
using System;
using System.Linq;
using UnityEngine.Assertions;
using System.Collections;
using System.IO.MemoryMappedFiles;
using System.Collections.Generic;
using System.Runtime.InteropServices;


public enum ShakeMode
{
    ModeShape,
    Response,
}

public enum ModelType
{
    Shear,
    FlexuralShear,
    Torsion,
}

public enum ColoringType
{
    None,
    Displacement,
    Velocity,
    Acceleration,
    Torsion, // todo
}

[StructLayout(LayoutKind.Sequential)]
public struct ResponseData
{
    public Vector3 displacement;
    public Vector3 rotation;
    public float red;

    static public int Size()
    {
        return sizeof(float) * 3 + sizeof(float) * 3 + sizeof(float);
    }
}

public class Solver
{
    public static void SolveModeShape(in Matrix<float> M, in Matrix<float> K, out Vector<float> omega, out Matrix<float> phi)
    {
        // [K - \omega^2M]\phi = 0
        // [M^{-1}K - \omega^2I]\phi = 0
        var eigen = (M.Inverse() * K).Evd();

        Vector<float> localOmega = eigen.EigenValues.Real().ToSingle().PointwiseSqrt();
        Matrix<float> localPhi = eigen.EigenVectors.NormalizeColumns(Infinity);

        // sort by omega
        var sorted = localOmega.Storage.EnumerateIndexed().OrderBy(x => x.Item2).ToArray();
        localOmega = Vector<float>.Build.DenseOfEnumerable(sorted.Select(x => x.Item2));
        localPhi = Matrix<float>.Build.DenseOfColumns(sorted.Select(x => localPhi.Column(x.Item1)));

        omega = localOmega;
        phi = localPhi;
    }

    public static void SolveResponse(in Matrix<float> M, in Matrix<float> K, in Vector<float> omega, in Matrix<float> phi, float rayleigh_alpha, float rayleigh_beta, float[] acc, float dt,
        out float[,] u, out float[,] uVel, out float[,] uAcc)
    {
        int ms = omega.Count;
        int nt = acc.Length;

        u = new float[nt, ms];
        uVel = new float[nt, ms];
        uAcc = new float[nt, ms];

        // for (int i = 0; i < ms; ++i)
        // {
        //     float sum = phi.Column(i).DotProduct(M * phi.Column(i));
        //     phi.SetColumn(i, phi.Column(i) / Sqrt(sum));
        // }

        // C = \alphaM + \betaK
        // \ksi_i'' + 2\omega_i\zeta\ksi_i' + \omega_i^2\ksi_i = -\alpha_iu_g(t)''
        // i = 1, 2, ..., n
        // for (int i = 0; i < Min(ms, 3); ++i)
        // {
        //     Vector<float> phi_i = phi.Column(i);

        //     float a = rayleigh_alpha + rayleigh_beta * omega[i] * omega[i];
        //     float b = omega[i] * omega[i];
        //     float c = -phi_i.DotProduct(M * Vector<float>.Build.Dense(ms, 1f)) / phi_i.DotProduct(M * phi_i);

        //     // \ksi_i'' + a\ksi_i' + b\ksi_i = cu_g(t)''
        //     // i = 1, 2, ..., n

        //     // y1 = \ksi
        //     // y2 = \ksi'
        //     //
        //     // y1' = y2
        //     // y2' = -ay2 - by1 + cu_g(t)''
        //     var xi = RungeKutta.FourthOrder(
        //         Vector<double>.Build.Dense(new double[] { 0, 0 }),
        //         0f,
        //         (nt - 1) * dt,
        //         nt,
        //         (t, y) =>
        //         {
        //             double y1 = y[0], y2 = y[1], u_g = acc[Clamp((int)(t / dt), 0, nt - 1)];
        //             return Vector<double>.Build.Dense(new double[] {
        //                 y2,
        //                 -a * y2 - b * y1 + c * u_g
        //             });
        //         }
        //     );

        //     for (int j = 0; j < nt; ++j)
        //         for (int k = 0; k < ms; ++k)
        //             u[j, k] += (float)(xi[j][0] * phi_i[k]);
        // }

        // for (int i = 0; i < ms; ++i)
        // {
        //     uVel[0, i] = (u[1, i] - u[0, i]) / dt;
        //     for (int j = 1; j < nt - 1; ++j)
        //         uVel[j, i] = (u[j + 1, i] - u[j - 1, i]) / (2 * dt);
        //     uVel[nt - 1, i] = (u[nt - 1, i] - u[nt - 2, i]) / dt;

        //     uAcc[0, i] = (uVel[1, i] - uVel[0, i]) / dt;
        //     for (int j = 1; j < nt - 1; ++j)
        //         uAcc[j, i] = (uVel[j + 1, i] - uVel[j - 1, i]) / (2 * dt);
        //     uAcc[nt - 1, i] = (uVel[nt - 1, i] - uVel[nt - 2, i]) / dt;
        // }

        // Newmark-beta method
        // https://zhuanlan.zhihu.com/p/17940378700
        float[] cu = new float[nt];
        float[] cv = new float[nt];
        float[] ca = new float[nt];
        for (int i = 0; i < Min(ms, 3); ++i)
        {
            Vector<float> phi_i = phi.Column(i);

            float a = rayleigh_alpha + rayleigh_beta * omega[i] * omega[i];
            float b = omega[i] * omega[i];
            float c = -phi_i.DotProduct(M * Vector<float>.Build.Dense(ms, 1f)) / phi_i.DotProduct(M * phi_i);
            // \ksi_i'' + a\ksi_i' + b\ksi_i = cu_g(t)''
            // mx'' + cx' + kx = p

            cu[0] = 0f;
            cv[0] = 0f;
            ca[0] = c * acc[0];

            const float gamma = 0.5f;
            const float beta = 0.25f;

            float m_inv = 1f / (1 + gamma * a * dt + beta * b * dt * dt);

            for (int j = 1; j < nt; ++j)
            {
                // prediction step
                float pu = cu[j - 1] + cv[j - 1] * dt + (0.5f - beta) * ca[j - 1] * dt * dt;
                float pv = cv[j - 1] + (1 - gamma) * ca[j - 1] * dt;

                ca[j] = m_inv * (c * acc[j] - a * pv - b * pu);
                cv[j] = pv + gamma * ca[j] * dt;
                cu[j] = pu + beta * ca[j] * dt * dt;
            }

            // 振型叠加
            for (int j = 0; j < nt; ++j)
                for (int k = 0; k < ms; ++k)
                {
                    u[j, k] += cu[j] * phi_i[k];
                    uVel[j, k] += cv[j] * phi_i[k];
                    uAcc[j, k] += ca[j] * phi_i[k];
                }
        }
    }

    // 经验公式计算地震传播衰减
    // distance: 距离（km
    public static void CalcSeismicAttenuation(float[] acc, int magnitude, float distance)
    {
        // GB 18306-2015
        // lg(Y) = A + B * M + C * lg(R + D * exp(EM))
        // Y: pga / pgv
        // M: magnitude
        // R: distance (km)
        float A, B, C, D, E;
        if (magnitude <= 6.5f)
        {
            A = 0.561f;
            B = 0.746f;
        }
        else
        {
            A = 2.501f;
            B = 0.448f;
        }
        C = -1.925f;
        D = 0.956f;
        E = 0.462f;
        Func<float, float> calcY = R => Pow(10f, A + B * magnitude + C * Log10(R + D * Exp(E * magnitude)));
        float ratio = calcY(distance) / calcY(0f);
        for (int i = 0; i < acc.Length; ++i) acc[i] *= ratio;
    }

    // public static void SaveBuildingData(List<MeshDeformer> meshDeformers)
    // {
    //     // 将每一个 meshDeformer 的 xSolver, zSolver 的 M, K, alpha, beta, acc, dt 等数据保存到json文件
    //     // [mesh1, mesh2, ...]
    //     // mesh1: {
    //     //     name: string,
    //     //     xSolver: {
    //     //         M: float[,],
    //     //         K: float[,],
    //     //         alpha: float,
    //     //         beta: float,
    //     //         acc: float[],
    //     //         dt: float
    //     //     },
    //     //     zSolver: ...
    //     // }

    //     const string path = $"{Application.streamingAssetsPath}/Python/seismic_data/building_data.json";
    //     var buildingData = new List<Dictionary<string, object>>();
    //     foreach (var meshDeformer in meshDeformers)
    //     {
    //         var data = new Dictionary<string, object>();
    //         data["name"] = meshDeformer.gameObject.name;

    //         int ms;
    //         Matrix<float> M, K;

    //         meshDeformer.xSolver.GetMatrix(out M, out K, out ms);
    //         data["xSolver"] = new Dictionary<string, object>
    //         {
    //             ["M"] = M.ToArray(),
    //             ["K"] = K.ToArray(),
    //             ["alpha"] = meshDeformer.xConfig.alpha,
    //             ["beta"] = meshDeformer.xConfig.beta,
    //             ["acc"] = meshDeformer.xConfig.acc,
    //             ["dt"] = meshDeformer.xConfig.dt,
    //         };

    //         meshDeformer.zSolver.GetMatrix(out M, out K, out ms);
    //         data["zSolver"] = new Dictionary<string, object>
    //         {
    //             ["M"] = M.ToArray(),
    //             ["K"] = K.ToArray(),
    //             ["alpha"] = meshDeformer.zConfig.alpha,
    //             ["beta"] = meshDeformer.zConfig.beta,
    //             ["acc"] = meshDeformer.zConfig.acc,
    //             ["dt"] = meshDeformer.zConfig.dt,
    //         };

    //         buildingData.Add(data);
    //         // break;
    //     }

    //     // newtonsoft.json
    //     string jsonString = Newtonsoft.Json.JsonConvert.SerializeObject(buildingData);
    //     System.IO.File.WriteAllText(path, jsonString, System.Text.Encoding.UTF8);
    //     // System.IO.File.WriteAllText(path, jsonString);
    // }

    // public static void LoadBuildingResponse(List<MeshDeformer> meshDeformers)
    // {
    //     const string path = $"{Application.streamingAssetsPath}/Python/seismic_data/building_response.json";

    //     string jsonString = System.IO.File.ReadAllText(path);

    //     // 读取 json 文件
    //     // [mesh1, mesh2, ...]
    //     // mesh1: {
    //     //     name: string,
    //     //     xSolver: {
    //     //         disp: float[,],
    //     //         vel: float[,],
    //     //         acc: float[,],
    //     //         maxDisp: float,
    //     //         maxVel: float,
    //     //         maxAcc: float
    //     //     },
    //     //     zSolver: ...
    //     // }

    //     var buildingResponse = Newtonsoft.Json.JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(jsonString);

    //     for (int i = 0; i < meshDeformers.Count; i++)
    //     {
    //         var meshDeformer = meshDeformers[i];
    //         var data = buildingResponse[i];

    //         var xSolver = (Dictionary<string, object>)data["xSolver"];
    //         var zSolver = (Dictionary<string, object>)data["zSolver"];

    //         meshDeformer.xSolver.u = ((Newtonsoft.Json.Linq.JArray)xSolver["disp"]).ToObject<float[,]>();
    //         meshDeformer.xSolver.uVel = ((Newtonsoft.Json.Linq.JArray)xSolver["vel"]).ToObject<float[,]>();
    //         meshDeformer.xSolver.uAcc = ((Newtonsoft.Json.Linq.JArray)xSolver["acc"]).ToObject<float[,]>();
    //         meshDeformer.xSolver.maxDisplacement = (float)xSolver["maxDisp"];
    //         meshDeformer.xSolver.maxVelocity = (float)xSolver["maxVel"];
    //         meshDeformer.xSolver.maxAcceleration = (float)xSolver["maxAcc"];

    //         meshDeformer.zSolver.u = ((Newtonsoft.Json.Linq.JArray)zSolver["disp"]).ToObject<float[,]>();
    //         meshDeformer.zSolver.uVel = ((Newtonsoft.Json.Linq.JArray)zSolver["vel"]).ToObject<float[,]>();
    //         meshDeformer.zSolver.uAcc = ((Newtonsoft.Json.Linq.JArray)zSolver["acc"]).ToObject<float[,]>();
    //         meshDeformer.zSolver.maxDisplacement = (float)zSolver["maxDisp"];
    //         meshDeformer.zSolver.maxVelocity = (float)zSolver["maxVel"];
    //         meshDeformer.zSolver.maxAcceleration = (float)zSolver["maxAcc"];

    //         if (i == 0) break;
    //     }
    // }
}
public class Solver1D
{
    public class Config
    {
        public int levels;
        public float levelHeight;
        public float xWidth;
        public float zWidth;
        public ShakeMode mode;
        public int modeShape;
        public ModelType modelType;
        public float alpha;
        public float beta;
        public float zCenterOffset;
        public float dt;
        public int nt;
        public float[] acc;
        public ColoringType coloringType;

        public Config()
        {
            levels = 10;
            levelHeight = 3.5f;
            xWidth = 10f;
            zWidth = 10f;
            mode = ShakeMode.Response;
            modeShape = 1;
            // modelType = ModelType.Torsion;
            modelType = ModelType.Shear;
            alpha = 0f;
            beta = 0f;
            zCenterOffset = 0f;
            dt = 0.01f;
            nt = 1;
            acc = new float[] { 0f };
            coloringType = ColoringType.None;
        }

        public Config(Config config)
        {
            levels = config.levels;
            levelHeight = config.levelHeight;
            xWidth = config.xWidth;
            zWidth = config.zWidth;
            mode = config.mode;
            modeShape = config.modeShape;
            modelType = config.modelType;
            alpha = config.alpha;
            beta = config.beta;
            zCenterOffset = config.zCenterOffset;
            dt = config.dt;
            nt = config.nt;
            // copy the array
            acc = new float[config.acc.Length];
            config.acc.CopyTo(acc, 0);
            coloringType = config.coloringType;
        }

#if UNITY_EDITOR
        // set the inspector GUI
        public void SetInspectorGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Config details");
            levels = EditorGUILayout.IntSlider("Levels", levels, 1, 200);
            levelHeight = EditorGUILayout.Slider("Level Height", levelHeight, 1f, 10f);
            xWidth = EditorGUILayout.Slider("X Width", xWidth, 1f, 25f);
            zWidth = EditorGUILayout.Slider("Z Width", zWidth, 1f, 25f);
            coloringType = (ColoringType)EditorGUILayout.EnumPopup("Coloring Type", coloringType);
            mode = (ShakeMode)EditorGUILayout.EnumPopup("Mode", mode);
            if (mode == ShakeMode.ModeShape)
            {
                modelType = (ModelType)EditorGUILayout.EnumPopup("Model Type", modelType);
                if (modelType == ModelType.Torsion)
                {
                    zCenterOffset = EditorGUILayout.Slider("Z Center Offset", zCenterOffset, -zWidth / 2, zWidth / 2);
                }
                modeShape = EditorGUILayout.IntSlider("Mode Shape", modeShape, 1, modelType == ModelType.Torsion ? levels * 2 : levels);
            }
            else if (mode == ShakeMode.Response)
            {
                modelType = (ModelType)EditorGUILayout.EnumPopup("Model Type", modelType);
                if (modelType == ModelType.Torsion)
                {
                    zCenterOffset = EditorGUILayout.Slider("Z Center Offset", zCenterOffset, -zWidth / 2, zWidth / 2);
                }
                alpha = EditorGUILayout.Slider("Alpha", alpha, 0f, 1f);
                beta = EditorGUILayout.Slider("Beta", beta, 0f, 0.1f);
            }
            EditorGUILayout.Space();
        }
#endif
    }

    public Config config;
    float currentTime = 0f;

    Vector<float> omega;
    Matrix<float> phi;
    public float[,] u;
    public float[,] uVel;
    public float[,] uAcc;
    public float maxDisplacement;
    public float maxVelocity;
    public float maxAcceleration;

    public Solver1D(Config config)
    {
        this.config = config;
    }

    // GB/T 17742-2020
    // peak ground acceleration according to seismic intensity
    // (m/s^2)
    public static readonly float[] peakAcc = {
        0f,
        0.018f,     // I
        0.0369f,    // II
        0.0757f,    // III
        0.155f,     // IV
        0.319f,     // V
        0.653f,     // VI
        1.35f,      // VII
        2.79f,      // VIII
        5.77f,      // IX
        11.9f,      // X
        24.7f,      // XI
        50f,        // XII
    };

    // peak ground velocity according to seismic intensity
    // (m/s)
    public static readonly float[] peakVel = {
        0f,
        0.00121f,   // I
        0.00259f,   // II
        0.00558f,   // III
        0.0120f,    // IV
        0.0259f,    // V
        0.0557f,    // VI
        0.120f,     // VII
        0.258f,     // VIII
        0.555f,     // IX
        1.19f,      // X
        2.57f,      // XI
        5f,         // XII
    };

    // GetMatrix(): get the Matrix M and K
    public void GetMatrix(out Matrix<float> M, out Matrix<float> K, out int ms)
    {
        float m0 = 2400f * config.xWidth * config.zWidth; // mass of each level
        float k0 = 1e7f * config.xWidth;
        float k1 = 0.4f * k0;

        {
            // float E = 3e10f; // Young's modulus
            // float I = 1 / 12f * Pow(config.xWidth, 3) * config.zWidth; // moment of inertia
            // float G = 0.4f * E; // shear modulus
            // float A = config.xWidth * config.zWidth; // cross-sectional area

            // k0 = G * A / config.levelHeight;
            // k1 = E * I / Pow(config.levelHeight, 3);

            // Shear:
            // \rho\frac{\partial^2 u}{\partial t^2} - GA\frac{\partial^2 u}{\partial x^2} = -\rho u_g''
            // f_i'' = (f_{i+1} - 2f_i + f_{i-1}) / h^2
            // -> mu_i'' - \frac{GA}{h}(u_{i+1} - 2u_i + u_{i-1}) = -mu_g''

            // FlexuralShear:
            // \rho\frac{\partial^2 u}{\partial t^2} + EI\frac{\partial^4 u}{\partial x^4} - GA\frac{\partial^2 u}{\partial x^2} = -\rho u_g''
            // f_i'' = (f_{i+1} - 2f_i + f_{i-1}) / h^2
            // f_i'''' = (f_{i+2} - 4f_{i+1} + 6f_i - 4f_{i-1} + f_{i-2}) / h^4
            // -> mu_i'' + \frac{EI}{h^3}(u_{i+2} - 4u_{i+1} + 6u_i - 4u_{i-1} + u_{i-2}) - \frac{GA}{h}(u_{i+1} - 2u_i + u_{i-1}) = -mu_g''
        }

        if (config.modelType != ModelType.Torsion)
        {
            ms = config.levels;
            M = Matrix<float>.Build.DenseOfDiagonalVector(Vector<float>.Build.Dense(ms, m0));
            K = Matrix<float>.Build.Dense(ms, ms, 0f);

            for (int i = 0; i < ms; ++i)
            {
                if (i > 0) K[i, i - 1] -= k0;
                K[i, i] += 2 * k0;
                K[i, Min(i + 1, ms - 1)] -= k0;
            }

            if (config.modelType == ModelType.FlexuralShear)
            {
                for (int i = 0; i < ms; ++i)
                {
                    if (i > 1) K[i, i - 2] += k1;
                    if (i > 0) K[i, i - 1] -= 4 * k1;
                    K[i, i] += 6 * k1;
                    K[i, Min(i + 1, ms - 1)] -= 4 * k1;
                    K[i, Min(i + 2, ms - 1)] += k1;
                }
            }
        }
        else
        {
            ms = config.levels * 2;
            M = Matrix<float>.Build.Dense(ms, ms, 0f);
            for (int i = 0; i < config.levels; ++i) M[i, i] = m0;

            // I_A = 1/12m(a^2 + b^2)
            // I_B = I_A + md^2
            float I0 =
                m0 / 12f * (config.xWidth * config.xWidth + config.zWidth * config.zWidth)
                + m0 * config.zCenterOffset * config.zCenterOffset;
            for (int i = config.levels; i < ms; ++i) M[i, i] = I0;

            K = Matrix<float>.Build.Dense(ms, ms, 0f);
            float r = config.zCenterOffset + config.zWidth / 2;
            float s = config.zWidth / 2 - config.zCenterOffset;
            float k = k0 / 4; // 4 springs between two levels
                              // T = 1/2\sum_{i=1}^{n}[m_i(\dot{u}_i + \dot{u}_g)^2 + I_i\dot{\theta}_i^2]
                              // V = k_1(x_1 + r_1\theta_1)^2 + k_1(x_1 - s_1\theta_1)^2
                              //     + \sum_{i = 2}^n k_i
                              //     [
                              //          (x_i + r_i\theta_i - x_{i-1} - r_{i-1}\theta_{i-1})^2
                              //          + (x_i - s_i\theta_i - x_{i-1} + s_{i-1}\theta_{i-1})^2
                              //     ]
                              //
                              // \frac{d}{dt}\frac{\partial T}{\partial \dot{q}_i} + \frac{\partial V}{\partial q_i} = 0
                              // q_i = u_i, \theta_i

            // \partial x_1
            K[0, 0] += 4 * k;
            K[0, config.levels] += 2 * k * (r - s);

            // \partial \theta_1
            K[config.levels, 0] += 2 * k * (r - s);
            K[config.levels, config.levels] += 2 * k * (r * r + s * s);

            for (int i = 2; i <= config.levels; ++i)
            {
                // \partial x_i
                K[i - 1, i - 1] += 4 * k; // x_i
                K[i - 1, i - 2] -= 4 * k; // x_{i-1}
                K[i - 1, i - 1 + config.levels] += 2 * k * (r - s); // \theta_i
                K[i - 1, i - 2 + config.levels] -= 2 * k * (r - s); // \theta_{i-1}

                // \partial x_{i-1}
                K[i - 2, i - 1] -= 4 * k; // x_i
                K[i - 2, i - 2] += 4 * k; // x_{i-1}
                K[i - 2, i - 1 + config.levels] -= 2 * k * (r - s); // \theta_i
                K[i - 2, i - 2 + config.levels] += 2 * k * (r - s); // \theta_{i-1}

                // \partial \theta_i
                K[i - 1 + config.levels, i - 1] += 2 * k * (r - s); // x_i
                K[i - 1 + config.levels, i - 2] -= 2 * k * (r - s); // x_{i-1}
                K[i - 1 + config.levels, i - 1 + config.levels] += 2 * k * (r * r + s * s); // \theta_i
                K[i - 1 + config.levels, i - 2 + config.levels] -= 2 * k * (r * r + s * s); // \theta_{i-1}

                // \partial \theta_{i-1}
                K[i - 2 + config.levels, i - 1] -= 2 * k * (r - s); // x_i
                K[i - 2 + config.levels, i - 2] += 2 * k * (r - s); // x_{i-1}
                K[i - 2 + config.levels, i - 1 + config.levels] -= 2 * k * (r * r + s * s); // \theta_i
                K[i - 2 + config.levels, i - 2 + config.levels] += 2 * k * (r * r + s * s); // \theta_{i-1}
            }
        }
    }

    // Calculate using Math.Net
    public void Solve()
    {
        Matrix<float> M, K;
        int ms; // matrix size

        // var stopWatch = new System.Diagnostics.Stopwatch();
        // stopWatch.Start();

        GetMatrix(out M, out K, out ms);

        // stopWatch.Stop();
        // Debug.Log($"GetMatrix: {stopWatch.ElapsedMilliseconds}ms");
        // stopWatch.Restart();

        Solver.SolveModeShape(M, K, out omega, out phi);

        // stopWatch.Stop();
        // Debug.Log($"SolveModeShape: {stopWatch.ElapsedMilliseconds}ms");

        if (config.mode == ShakeMode.ModeShape)
        {
            maxDisplacement = config.levels * config.levelHeight * 0.1f;
            maxVelocity = omega[config.modeShape - 1] * maxDisplacement;
            maxAcceleration = omega[config.modeShape - 1] * maxVelocity;
            return;
        }

        // stopWatch.Restart();

        Solver.SolveResponse(M, K, omega, phi, config.alpha, config.beta, config.acc, config.dt, out u, out uVel, out uAcc);

        // stopWatch.Stop();
        // Debug.Log($"SolveResponse: {stopWatch.ElapsedMilliseconds}ms");
        // stopWatch.Restart();

        for (int i = 0; i < config.nt; ++i)
            for (int j = 0; j < config.levels; ++j)
            {
                // maxDisplacement = Max(maxDisplacement, Abs(u[i, j]));
                // maxVelocity = Max(maxVelocity, Abs(uVel[i, j]));
                // maxAcceleration = Max(maxAcceleration, Abs(uAcc[i, j]));
                if (maxDisplacement < u[i, j]) maxDisplacement = u[i, j];
                else if (maxDisplacement < -u[i, j]) maxDisplacement = -u[i, j];
                if (maxVelocity < uVel[i, j]) maxVelocity = uVel[i, j];
                else if (maxVelocity < -uVel[i, j]) maxVelocity = -uVel[i, j];
                if (maxAcceleration < uAcc[i, j]) maxAcceleration = uAcc[i, j];
                else if (maxAcceleration < -uAcc[i, j]) maxAcceleration = -uAcc[i, j];
            }

        // stopWatch.Stop();
        // Debug.Log($"GetMaxDisp: {stopWatch.ElapsedMilliseconds}ms");
        // Debug.Log($"maxDisplacement: {maxDisplacement}, maxVelocity: {maxVelocity}, maxAcceleration: {maxAcceleration}");
    }

    // Set current time of the solver
    public void SetTime(float t)
    {
        currentTime = t;
    }

    // Get the response of the given level
    // Return (displacement, torsion, red)
    // displacement: displacement in x direction
    // torsion: degree of torsion
    // red: 0 ~ 1
    public Vector3 GetResponse(int level)
    {
        if (u == null) return Vector3.zero;
        if (level == 0) return Vector3.zero;
        Assert.IsTrue(level >= 1 && level <= config.levels);
        float displacement = 0f, torsion = 0f, red = 0f;
        if (config.mode == ShakeMode.ModeShape)
        {
            displacement = maxDisplacement * phi[level - 1, config.modeShape - 1] * Sin(omega[config.modeShape - 1] * currentTime);
            if (config.modelType == ModelType.Torsion)
            {
                torsion = maxDisplacement * phi[level - 1 + config.levels, config.modeShape - 1] * Sin(omega[config.modeShape - 1] * currentTime);
            }

            red = config.coloringType switch
            {
                ColoringType.None => 0f,
                ColoringType.Displacement => displacement,
                ColoringType.Velocity => maxVelocity * phi[level - 1, config.modeShape - 1] * Cos(omega[config.modeShape - 1] * currentTime),
                ColoringType.Acceleration => maxAcceleration * phi[level - 1, config.modeShape - 1] * -Sin(omega[config.modeShape - 1] * currentTime),
                ColoringType.Torsion => 0f,
                _ => 0f,
            };
        }
        else if (config.mode == ShakeMode.Response)
        {
            int idx = Clamp((int)(currentTime / config.dt), 0, config.nt - 1);
            displacement = u[idx, level - 1];
            if (config.modelType == ModelType.Torsion)
            {
                torsion = u[idx, level - 1 + config.levels];
            }

            red = config.coloringType switch
            {
                ColoringType.None => 0f,
                ColoringType.Displacement => displacement,
                ColoringType.Velocity => uVel[idx, level - 1],
                ColoringType.Acceleration => uAcc[idx, level - 1],
                ColoringType.Torsion => 0f,
                _ => 0f,
            };
        }

        // torsion = Atan2(Sin(torsion), Cos(torsion)); // [-PI, PI]
        red = Abs(red);

        red = config.coloringType switch
        {
            ColoringType.None => 0f,
            ColoringType.Displacement => red / maxDisplacement,
            ColoringType.Velocity => red / maxVelocity,
            ColoringType.Acceleration => red / maxAcceleration,
            ColoringType.Torsion => 0f,
            _ => 0f,
        };

        return new Vector3(displacement, torsion, red);
    }

    public Vector3 GetResponseInterpolated(float y)
    {
        int level = Clamp((int)(y / config.levelHeight), 0, config.levels - 1);
        Vector3 p1 = GetResponse(level);
        Vector3 p2 = GetResponse(level + 1);
        float t = Clamp((y - level * config.levelHeight) / config.levelHeight, 0f, 1f);
        return Vector3.Lerp(p1, p2, t);
    }

    private float[] GetAllResponse(int level, float[,] r)
    {
        float[] response = new float[config.nt];
        for (int i = 0; i < config.nt; ++i)
        {
            response[i] = r[i, level - 1];
        }
        return response;
    }

    private float[] GetAllResponseInterpolated(float y, float[,] r)
    {
        int level = Clamp((int)(y / config.levelHeight), 0, config.levels - 1);
        float t = Clamp((y - level * config.levelHeight) / config.levelHeight, 0f, 1f);
        float[] response = new float[config.nt];
        for (int i = 0; i < config.nt; ++i)
        {
            float v1 = level == 0 ? 0f : r[i, level - 1];
            float v2 = r[i, level];
            response[i] = Lerp(v1, v2, t);
        }
        return response;
    }

    public float[] GetAllDisplacementResponse(int level)
    {
        return GetAllResponse(level, u);
    }

    public float[] GetAllDisplacementResponseInterpolated(float y)
    {
        return GetAllResponseInterpolated(y, u);
    }

    public float[] GetAllVelocityResponse(int level)
    {
        return GetAllResponse(level, uVel);
    }

    public float[] GetAllVelocityResponseInterpolated(float y)
    {
        return GetAllResponseInterpolated(y, uVel);
    }

    public float[] GetAllAccelerationResponse(int level)
    {
        return GetAllResponse(level, uAcc);
    }

    public float[] GetAllAccelerationResponseInterpolated(float y)
    {
        return GetAllResponseInterpolated(y, uAcc);
    }
}
