using UnityEngine;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System;
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
    public List<DasFrame> frames;
}
public class DasVisualizer : MonoBehaviour
{
    [Header("Data")]
    [Tooltip("CSV TextAsset (preferred). If empty, load from csvFilePath")]
    public TextAsset csvTextAsset;
    [Tooltip("CSV file path when csvTextAsset is empty (e.g. Assets/prob_matrix_frames.csv)")]
    public string csvFilePath = "Assets/prob_matrix_frames.csv";
    [Header("Network Stream (UDP, JSON per packet)")]
    [Tooltip("Use UDP stream input instead of CSV")]
    public bool useNetworkStream = false;
    [Tooltip("Local listen IP (empty = any)")]
    public string udpListenIp = "";
    [Tooltip("Local listen port")]
    public int udpListenPort = 9000;
    [Tooltip("Auto start listening")]
    public bool autoConnectStream = true;
    [Header("CSV Preprocess")]
    [Tooltip("Input sample rate (Hz) for time axis. <=0 fallback to targetFps")]
    public float inputSampleRate = 2000f;
    [Tooltip("Target FPS (fallback when sample rate invalid)")]
    public int targetFps = 30;
    [Tooltip("Channel column prefix (default ch_)")]
    public string channelPrefix = "ch_";
    [Header("Deformation")]
    [Tooltip("Signal to height scale")]
    public float heightScale = 1f;
    [Tooltip("Raw value scale (only when normalization is off)")]
    public float rawValueScale = 1f;
    [Tooltip("Displacement direction (LineRenderer space)")]
    public Vector3 displacementDirection = Vector3.up;
    [Header("Polyline")]
    [Tooltip("Polyline control points (>=2)")]
    public List<Vector3> controlPoints = new List<Vector3>
    {
        new Vector3(-5f, 0f, 0f),
        new Vector3(0f, 0f, 0f),
        new Vector3(5f, 0f, 0f)
    };
    [Tooltip("Line width (visual only)")]
    public float lineWidth = 0.2f;
    [Tooltip("Use world space for LineRenderer")]
    public bool useWorldSpace = false;
    private DasData data;
    private LineRenderer lineRenderer;
    private Vector3[] basePositions;
    private Vector3[] deformedPositions;
    private float totalDuration;
    private bool isPaused;
    private float timePerFrame;
    private int currentFrameIndex = 0;
    private float timer = 0f;
    private UdpClient udpClient;
    private Thread streamThread;
    private volatile bool streamRunning;
    private readonly object frameLock = new object();
    private readonly Queue<DasFrame> incomingFrames = new Queue<DasFrame>();
    [Serializable]
    private class DasPacket
    {
        public int total_channels;
        public float timestamp;
        public float sample_rate;
        public int sample_count;
        public float[] signals;
        public string timestamp_iso;
    }
    void Start()
    {
        lineRenderer = GetComponent<LineRenderer>();
        if (lineRenderer == null)
        {
            lineRenderer = gameObject.AddComponent<LineRenderer>();
        }
        ConfigureLineRenderer();
        lineRenderer.alignment = LineAlignment.View;
        if (useNetworkStream)
        {
            if (autoConnectStream) StartStream();
            isPaused = true;
            return;
        }
        if (TryLoadDasFromCsv(out data) == false)
        {
            Debug.LogError("CSV load failed.");
            return;
        }
        if (data == null || data.frames == null || data.frames.Count == 0) return;
        int sampleCount = data.width * data.height;
        if (data.frames[0].values != null && data.frames[0].values.Length > 0)
        {
            sampleCount = data.frames[0].values.Length;
        }
        if (sampleCount < 1)
        {
            Debug.LogError("Data length is 0.");
            return;
        }
        BuildPolylineSamples(sampleCount);
        float effectiveFps = inputSampleRate > 0f ? inputSampleRate : Mathf.Max(1, targetFps);
        timePerFrame = 1f / effectiveFps;
        totalDuration = data.frames.Count * timePerFrame;
        isPaused = true;
        UpdateDasGeometry(data.frames[currentFrameIndex]);
    }
    void Update()
    {
        if (useNetworkStream)
        {
            DrainIncomingFrames();
        }
        if (data == null || data.frames == null || data.frames.Count == 0) return;
        if (timePerFrame <= 0f) return;
        if (isPaused) return;
        timer += Time.deltaTime;
        if (timer >= timePerFrame)
        {
            if (useNetworkStream)
            {
                if (currentFrameIndex + 1 < data.frames.Count)
                {
                    currentFrameIndex++;
                    UpdateDasGeometry(data.frames[currentFrameIndex]);
                }
            }
            else
            {
                currentFrameIndex = (currentFrameIndex + 1) % data.frames.Count;
                UpdateDasGeometry(data.frames[currentFrameIndex]);
            }
            timer -= timePerFrame;
        }
    }
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
                resolvedPath = Path.Combine(Application.dataPath, resolvedPath.Substring("Assets".Length).TrimStart('/', '\\'));
            }
            if (string.IsNullOrEmpty(resolvedPath) || File.Exists(resolvedPath) == false)
            {
                Debug.LogError($"CSV path invalid or file not found: {csvFilePath}");
                return false;
            }
            try
            {
                csvContent = File.ReadAllText(resolvedPath);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Read CSV failed: {ex.Message}");
                return false;
            }
        }
        if (string.IsNullOrEmpty(csvContent))
        {
            Debug.LogError("CSV is empty.");
            return false;
        }
        try
        {
            result = ParseAndProcessCsv(csvContent);
            Debug.Log($"CSV loaded. Size: {result.width}x{result.height}, Frames: {result.frames.Count}");
            return true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"CSV parse failed: {ex.Message}");
            return false;
        }
    }
    private DasData ParseAndProcessCsv(string csvContent)
    {
        var lines = csvContent.Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) throw new System.Exception("CSV lines insufficient.");
        string[] headers = lines[0].Split(',');
        List<int> channelIndices = new List<int>();
        for (int i = 0; i < headers.Length; i++)
        {
            if (headers[i].StartsWith(channelPrefix)) channelIndices.Add(i);
        }
        if (channelIndices.Count == 0) throw new System.Exception($"No columns start with '{channelPrefix}'.");
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
        if (sampleCount == 0) throw new System.Exception("CSV data rows empty.");
        List<DasFrame> frames = new List<DasFrame>(sampleCount);
        foreach (var r in rows)
        {
            frames.Add(new DasFrame { values = r });
        }
        return new DasData
        {
            width = totalChannels,
            height = 1,
            total_channels = totalChannels,
            fps = Mathf.Max(1, targetFps),
            frames = frames
        };
    }
    private void ConfigureLineRenderer()
    {
        lineRenderer.useWorldSpace = useWorldSpace;
        lineRenderer.widthMultiplier = lineWidth;
        lineRenderer.numCapVertices = 2;
        lineRenderer.numCornerVertices = 2;
        lineRenderer.textureMode = LineTextureMode.Stretch;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.startColor = Color.white;
        lineRenderer.endColor = Color.white;
    }
    private void BuildPolylineSamples(int count)
    {
        basePositions = new Vector3[count];
        deformedPositions = new Vector3[count];
        if (controlPoints == null || controlPoints.Count < 2)
        {
            Debug.LogError("Need at least two control points.");
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
    private void UpdateDasGeometry(DasFrame frame)
    {
        if (lineRenderer == null || frame.values == null) return;
        int count = frame.values.Length;
        if (count != basePositions?.Length)
        {
            BuildPolylineSamples(count);
        }
        Vector3 dir = displacementDirection.sqrMagnitude > 1e-6f
            ? displacementDirection.normalized
            : Vector3.up;
        for (int i = 0; i < count; i++)
        {
            float v = frame.values[i];
            if (!IsFinite(v)) v = 0f;
            float raw = v * rawValueScale;
            deformedPositions[i] = basePositions[i] + dir * (raw * heightScale);
        }
        lineRenderer.SetPositions(deformedPositions);
    }
    private static bool IsFinite(float v)
    {
        return !(float.IsNaN(v) || float.IsInfinity(v));
    }
    private void StartStream()
    {
        if (streamRunning) return;
        streamRunning = true;
        streamThread = new Thread(StreamLoop);
        streamThread.IsBackground = true;
        streamThread.Start();
        Debug.Log($"DasVisualizer: UDP listen {udpListenIp}:{udpListenPort}");
    }
    private void StopStream()
    {
        streamRunning = false;
        try
        {
            if (udpClient != null)
            {
                udpClient.Close();
                udpClient = null;
            }
        }
        catch { }
        if (streamThread != null)
        {
            try { streamThread.Join(200); } catch { }
            streamThread = null;
        }
    }
    private void StreamLoop()
    {
        try
        {
            IPEndPoint listenEndPoint = null;
            if (string.IsNullOrWhiteSpace(udpListenIp))
            {
                listenEndPoint = new IPEndPoint(IPAddress.Any, udpListenPort);
            }
            else
            {
                listenEndPoint = new IPEndPoint(IPAddress.Parse(udpListenIp), udpListenPort);
            }
            udpClient = new UdpClient(listenEndPoint);
            udpClient.Client.ReceiveTimeout = 1000;
            var remote = new IPEndPoint(IPAddress.Any, 0);
            while (streamRunning)
            {
                byte[] bytes = null;
                try
                {
                    bytes = udpClient.Receive(ref remote);
                }
                catch (SocketException se)
                {
                    if (!streamRunning || se.ErrorCode == 10004 || se.ErrorCode == 10060) continue;
                    throw;
                }
                if (bytes == null || bytes.Length == 0) continue;
                string payload = System.Text.Encoding.UTF8.GetString(bytes);
                if (string.IsNullOrWhiteSpace(payload)) continue;
                string[] lines = payload.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (string.IsNullOrEmpty(line)) continue;
                    try
                    {
                        DasPacket packet = JsonUtility.FromJson<DasPacket>(line);
                        if (packet != null && packet.signals != null && packet.signals.Length > 0)
                        {
                            EnqueuePacket(packet);
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"DasVisualizer: UDP listen failed {ex.Message}");
        }
        finally
        {
            streamRunning = false;
        }
    }
    private void EnqueuePacket(DasPacket packet)
    {
        DasFrame frame = new DasFrame { values = packet.signals };
        lock (frameLock)
        {
            incomingFrames.Enqueue(frame);
        }
        if (data == null)
        {
            data = new DasData
            {
                width = packet.total_channels,
                height = 1,
                total_channels = packet.total_channels,
                fps = Mathf.Max(1, packet.sample_rate),
                frames = new List<DasFrame>()
            };
        }
        if (packet.sample_rate > 0.01f)
        {
            timePerFrame = 1f / packet.sample_rate;
        }
    }
    private void DrainIncomingFrames()
    {
        bool gotFirst = false;
        while (true)
        {
            DasFrame frame = null;
            lock (frameLock)
            {
                if (incomingFrames.Count > 0) frame = incomingFrames.Dequeue();
            }
            if (frame == null) break;
            data.frames.Add(frame);
            gotFirst = gotFirst || data.frames.Count == 1;
            totalDuration = data.frames.Count * timePerFrame;
        }
        if (gotFirst)
        {
            BuildPolylineSamples(data.frames[0].values.Length);
            currentFrameIndex = 0;
            UpdateDasGeometry(data.frames[currentFrameIndex]);
        }
    }
    public void Play()
    {
        isPaused = false;
        Debug.Log("DasVisualizer: Play");
    }
    public void Pause()
    {
        isPaused = true;
        Debug.Log("DasVisualizer: Pause");
    }
    public void TogglePlay()
    {
        isPaused = !isPaused;
        Debug.Log($"DasVisualizer: {(isPaused ? "Pause" : "Play")}");
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
    private void OnDestroy()
    {
        if (useNetworkStream) StopStream();
    }
}
