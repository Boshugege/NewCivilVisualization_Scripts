using UnityEngine;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

public enum NormalizationMode
{
    Global,     // 使用全局最小最大值
    PerFrame    // 每帧自适应归一化
}

public class DasFrame
{
    public float[] values;
}

public class DasData
{
    public int width;
    public int height;

    public int total_channels;
    public float fps;
    public float global_min;
    public float global_max;

    public List<DasFrame> frames;
}

public class DasVisualizer : MonoBehaviour
{
    [Header("数据配置")]
    [Tooltip("CSV 文本资源（优先使用此项；为空则使用 csvFilePath 路径读取）")]
    public TextAsset csvTextAsset;

        [Tooltip("CSV 文件路径（当 csvTextAsset 为空时使用；如 Assets/prob_matrix_frames.csv 或绝对路径)")]
        public string csvFilePath = "Assets/prob_matrix_frames.csv";

    [Header("CSV 预处理参数")]
    [Tooltip("输入数据采样率（Hz），用于计算时间轴。<=0 时退回 targetFps 推动动画")]
    public float inputSampleRate = 2000f;

    [Tooltip("目标播放帧率（fallback，当采样率无效时使用）")]
    public int targetFps = 30;

    [Tooltip("通道列名前缀（默认 ch_）")]
    public string channelPrefix = "ch_";

    [Tooltip("归一化模式：Global使用全局最小最大值，PerFrame每帧自适应")]
    public NormalizationMode normalizationMode = NormalizationMode.PerFrame;

    [Tooltip("对比度增强倍数 (1.0 = 不增强, >1.0 = 增强对比度)")]
    [Range(1f, 5f)]
    public float contrastBoost = 1.5f;

    [Tooltip("使用百分位裁剪去除异常值 (0 = 不裁剪, 5 = 裁剪最低和最高5%)")]
    [Range(0f, 20f)]
    public float percentileClip = 2f;

    [Header("形变配置")]
    [Tooltip("信号强度映射到凸起高度的缩放系数")]
    public float heightScale = 1f;

    [Tooltip("凸起方向（与LineRenderer坐标系一致，受useWorldSpace影响）")]
    public Vector3 displacementDirection = Vector3.up;

    [Header("线路配置")]
    [Tooltip("多段折线控制点（至少2个）。与LineRenderer坐标系一致，受useWorldSpace影响")]
    public List<Vector3> controlPoints = new List<Vector3>
    {
        new Vector3(-5f, 0f, 0f),
        new Vector3(0f, 0f, 0f),
        new Vector3(5f, 0f, 0f)
    };

    [Tooltip("折线宽度（仅影响显示，不影响采样数量）")]
    public float lineWidth = 0.2f;

    [Tooltip("LineRenderer 使用世界坐标还是本地坐标")]
    public bool useWorldSpace = false;

    // --- 内部状态 ---
    private DasData data;
    private LineRenderer lineRenderer;
    private Vector3[] basePositions;
    private Vector3[] deformedPositions;

    private float totalDuration;
    private bool isPaused;

    private float timePerFrame;
    private int currentFrameIndex = 0;
    private float timer = 0f;

    void Start()
    {
        // 1. 加载和解析数据（仅 CSV）
        if (TryLoadDasFromCsv(out data) == false)
        {
            Debug.LogError("CSV 加载失败，已终止初始化。");
            return;
        }

        if (data == null || data.frames == null || data.frames.Count == 0) return;

        // 2. 初始化 LineRenderer（若不存在则自动添加）
        lineRenderer = GetComponent<LineRenderer>();
        if (lineRenderer == null)
        {
            lineRenderer = gameObject.AddComponent<LineRenderer>();
        }

        ConfigureLineRenderer();

        // 3. 依据数据长度生成折线采样点
        int sampleCount = data.width * data.height;
        if (data.frames[0].values != null && data.frames[0].values.Length > 0)
        {
            sampleCount = data.frames[0].values.Length;
        }

        if (sampleCount < 1)
        {
            Debug.LogError("数据长度为0，无法绘制折线。");
            return;
        }

        BuildPolylineSamples(sampleCount);

        // 4. 初始化显示：使用折线形变表达强度
        lineRenderer.alignment = LineAlignment.View;

        // 5. 初始化帧控制
        float effectiveFps = inputSampleRate > 0f ? inputSampleRate : Mathf.Max(1, targetFps);
        timePerFrame = 1f / effectiveFps;
        totalDuration = data.frames.Count * timePerFrame;
        isPaused = true;

        // 立即显示第一帧
        UpdateDasGeometry(data.frames[currentFrameIndex]);
    }

    void Update()
    {
        if (data == null || data.frames == null || data.frames.Count == 0) return;
        if (timePerFrame <= 0f) return;
        if (isPaused) return;

        // 帧率控制逻辑
        timer += Time.deltaTime;

        if (timer >= timePerFrame)
        {
            // 切换到下一帧 (循环播放)
            currentFrameIndex = (currentFrameIndex + 1) % data.frames.Count;
            // 更新折线形变
            UpdateDasGeometry(data.frames[currentFrameIndex]);

            // 重置计时器
            timer -= timePerFrame;
        }
    }

    /// <summary>
    /// 优先使用 TextAsset，其次按路径读取 CSV，并生成 DasData（不做平滑与降采样）。
    /// </summary>
    private bool TryLoadDasFromCsv(out DasData result)
    {
        result = null;

        string csvContent = null;
        if (csvTextAsset != null)
        {
            csvContent = csvTextAsset.text;
        }
        else
        {
            string resolvedPath = csvFilePath;
            if (string.IsNullOrEmpty(resolvedPath) == false && resolvedPath.StartsWith("Assets"))
            {
                // 支持相对路径（相对于项目根）
                resolvedPath = Path.Combine(Application.dataPath, resolvedPath.Substring("Assets".Length).TrimStart('/', '\\'));
            }

            if (string.IsNullOrEmpty(resolvedPath) || File.Exists(resolvedPath) == false)
            {
                Debug.LogError($"CSV 路径无效或文件不存在: {csvFilePath}");
                return false;
            }

            try
            {
                csvContent = File.ReadAllText(resolvedPath);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"读取 CSV 文件失败: {ex.Message}");
                return false;
            }
        }

        if (string.IsNullOrEmpty(csvContent))
        {
            Debug.LogError("CSV 内容为空。");
            return false;
        }

        try
        {
            result = ParseAndProcessCsv(csvContent);
            Debug.Log($"CSV 数据加载成功! 尺寸: {result.width}x{result.height}, 帧数: {result.frames.Count}");
            return true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"CSV 解析失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 解析 CSV，提取以 channelPrefix 开头的列，逐行作为帧返回。
    /// </summary>
    private DasData ParseAndProcessCsv(string csvContent)
    {
        var lines = csvContent.Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) throw new System.Exception("CSV 行数不足。");

        string[] headers = lines[0].Split(',');
        List<int> channelIndices = new List<int>();
        for (int i = 0; i < headers.Length; i++)
        {
            if (headers[i].StartsWith(channelPrefix)) channelIndices.Add(i);
        }
        if (channelIndices.Count == 0) throw new System.Exception($"未找到以 '{channelPrefix}' 开头的通道列。");

        int totalChannels = channelIndices.Count;
        List<float[]> rows = new List<float[]>();
        NumberFormatInfo nfi = CultureInfo.InvariantCulture.NumberFormat;

        for (int li = 1; li < lines.Length; li++)
        {
            if (string.IsNullOrWhiteSpace(lines[li])) continue;
            string[] parts = lines[li].Split(',');
            float[] row = new float[totalChannels];
            for (int ci = 0; ci < totalChannels; ci++)
            {
                int colIdx = channelIndices[ci];
                float value = 0f;
                if (colIdx < parts.Length)
                {
                    float.TryParse(parts[colIdx], NumberStyles.Float, nfi, out value);
                }
                row[ci] = value;
            }
            rows.Add(row);
        }

        int sampleCount = rows.Count;
        if (sampleCount == 0) throw new System.Exception("CSV 数据行为空。");

        List<DasFrame> frames = new List<DasFrame>(sampleCount);
        foreach (var r in rows)
        {
            frames.Add(new DasFrame { values = r });
        }

        float gMin = float.MaxValue;
        float gMax = float.MinValue;
        foreach (var f in frames)
        {
            foreach (var v in f.values)
            {
                if (v < gMin) gMin = v;
                if (v > gMax) gMax = v;
            }
        }

        return new DasData
        {
            width = totalChannels,
            height = 1,
            total_channels = totalChannels,
            fps = Mathf.Max(1, targetFps),
            global_min = gMin,
            global_max = gMax,
            frames = frames
        };
    }

    /// <summary>
    /// 初始化 LineRenderer 外观设置。
    /// </summary>
    private void ConfigureLineRenderer()
    {
        lineRenderer.useWorldSpace = useWorldSpace;
        lineRenderer.widthMultiplier = lineWidth;
        lineRenderer.numCapVertices = 2; // 轻微圆角
        lineRenderer.numCornerVertices = 2;
        lineRenderer.textureMode = LineTextureMode.Stretch;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.startColor = Color.white;
        lineRenderer.endColor = Color.white;
    }

    /// <summary>
    /// 按照控制点列表将等距采样点投射到多段折线上。
    /// </summary>
    private void BuildPolylineSamples(int count)
    {
        basePositions = new Vector3[count];
        deformedPositions = new Vector3[count];

        if (controlPoints == null || controlPoints.Count < 2)
        {
            Debug.LogError("需要至少两个控制点来生成折线。");
            for (int i = 0; i < count; i++) basePositions[i] = Vector3.zero;
            lineRenderer.positionCount = count;
            lineRenderer.SetPositions(basePositions);
            return;
        }

        int segmentCount = controlPoints.Count - 1;
        float[] segLengths = new float[segmentCount];
        float total = 0f;

        for (int s = 0; s < segmentCount; s++)
        {
            float len = Vector3.Distance(controlPoints[s], controlPoints[s + 1]);
            segLengths[s] = len;
            total += len;
        }

        if (total <= Mathf.Epsilon)
        {
            Vector3 p = controlPoints[0];
            for (int i = 0; i < count; i++) basePositions[i] = p;
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                float t = count == 1 ? 0f : (float)i / (count - 1);
                float targetDist = t * total;

                float acc = 0f;
                for (int s = 0; s < segmentCount; s++)
                {
                    float segLen = segLengths[s];
                    if (targetDist <= acc + segLen || s == segmentCount - 1)
                    {
                        float localT = segLen <= Mathf.Epsilon ? 0f : (targetDist - acc) / segLen;
                        basePositions[i] = Vector3.Lerp(controlPoints[s], controlPoints[s + 1], localT);
                        break;
                    }
                    acc += segLen;
                }
            }
        }

        lineRenderer.positionCount = count;
        lineRenderer.SetPositions(basePositions);
    }

    /// <summary>
    /// 将一帧的归一化数据映射为折线形变（凸起）。
    /// </summary>
    private void UpdateDasGeometry(DasFrame frame)
    {
        if (lineRenderer == null || frame.values == null) return;

        int count = frame.values.Length;
        if (count != basePositions?.Length)
        {
            // 数据长度变化时重新采样位置，以保持一一对应
            BuildPolylineSamples(count);
        }

        // 计算归一化范围
        float minVal, maxVal;
        if (normalizationMode == NormalizationMode.Global)
        {
            // 使用全局最小最大值
            minVal = data.global_min;
            maxVal = data.global_max;
        }
        else
        {
            // 每帧自适应：计算当前帧的范围
            minVal = float.MaxValue;
            maxVal = float.MinValue;
            foreach (float v in frame.values)
            {
                if (v < minVal) minVal = v;
                if (v > maxVal) maxVal = v;
            }
        }

        // 应用百分位裁剪去除异常值
        if (percentileClip > 0.01f)
        {
            float[] sortedValues = new float[frame.values.Length];
            System.Array.Copy(frame.values, sortedValues, frame.values.Length);
            System.Array.Sort(sortedValues);

            int clipCount = Mathf.FloorToInt(sortedValues.Length * percentileClip / 100f);
            if (clipCount > 0 && clipCount < sortedValues.Length / 2)
            {
                minVal = sortedValues[clipCount];
                maxVal = sortedValues[sortedValues.Length - 1 - clipCount];
            }
        }

        // 防止除零
        float range = maxVal - minVal;
        if (range < 1e-6f) range = 1f;

        Vector3 dir = displacementDirection.sqrMagnitude > 1e-6f
            ? displacementDirection.normalized
            : Vector3.up;

        for (int i = 0; i < count; i++)
        {
            // 归一化到 [0, 1]
            float normalizedValue = (frame.values[i] - minVal) / range;
            
            // 对比度增强
            if (contrastBoost > 1.01f)
            {
                // 使用 pow 函数增强对比度，中心值保持在 0.5
                normalizedValue = Mathf.Pow(normalizedValue, 1f / contrastBoost);
            }
            
            // 限制在 [0, 1] 范围
            normalizedValue = Mathf.Clamp01(normalizedValue);

            // 沿指定方向产生凸起
            deformedPositions[i] = basePositions[i] + dir * (normalizedValue * heightScale);
        }

        lineRenderer.SetPositions(deformedPositions);
    }

    public void Play()
    {
        isPaused = false;
        Debug.Log("DasVisualizer: 播放开始");
    }

    public void Pause()
    {
        isPaused = true;
        Debug.Log("DasVisualizer: 已暂停");
    }

    public void TogglePlay()
    {
        isPaused = !isPaused;
        Debug.Log($"DasVisualizer: {(isPaused ? "已暂停" : "播放开始")}");
    }

    public bool IsPaused()
    {
        return isPaused;
    }

    public void SetTime(float seconds)
    {
        if (data == null || data.frames == null || data.frames.Count == 0) return;
        if (timePerFrame <= 0f) return;

        float clamped = Mathf.Clamp(seconds, 0f, totalDuration);
        int idx = Mathf.Min(data.frames.Count - 1, Mathf.FloorToInt(clamped / timePerFrame));
        currentFrameIndex = idx;
        timer = 0f;
            UpdateDasGeometry(data.frames[currentFrameIndex]);
    }

    public float GetCurrentTime()
    {
        return currentFrameIndex * timePerFrame;
    }

    public float GetTotalTime()
    {
        return totalDuration;
    }
}

