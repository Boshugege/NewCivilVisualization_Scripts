using UnityEngine;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System;

// ════════════════════════════════════════════════════════════════
//  数据结构
// ════════════════════════════════════════════════════════════════

/// <summary>一帧 DAS 信号（各通道振幅值）。</summary>
public class DasFrame
{
    public float[] values;
}

/// <summary>脚步事件（Python 端 packet_type="event" 反序列化后）。</summary>
public class FootstepEvent
{
    public float timestamp;
    public int   channelIndex;
    public float confidence;
}

/// <summary>跟踪目标四态生命周期。</summary>
public enum TargetState
{
    Tentative,   // 候选：事件已聚合，尚未确认
    Confirmed,   // 确认：稳定跟踪中
    Lost,        // 丢失：暂时无事件，短期保留
    Removed      // 移除：淡出后即将销毁
}

/// <summary>一个被跟踪的行人目标。</summary>
public class TrackedTarget
{
    public int          id;
    public TargetState  state;
    public float        channelPosition;       // 平滑通道坐标（浮点）
    public float        velocity;              // 通道/秒
    public float        confidence;
    public float        lastEventTime;
    public float        stateEnterTime;
    public float        creationTime;
    public int          totalEventCount;
    public List<FootstepEvent> recentEvents = new List<FootstepEvent>();

    // 可视化
    public GameObject   visual;
    public Animator     animator;              // 人形动画控制器
    public float        animSpeed;             // 当前动画速度（平滑）
    public float        visualAlpha;           // 当前实际透明度 0‥1
    public float        targetAlpha;           // 目标透明度（由状态机驱动）
    public Vector3      displayPosition;
    public Vector3      previousDisplayPosition;
    public bool         isMoving;
}

// ════════════════════════════════════════════════════════════════
//  DasVisualizer — DAS 信号线 + 脚步小人 实时可视化
// ════════════════════════════════════════════════════════════════
public class DasVisualizer : MonoBehaviour
{
    // ──────────────────────────────────────────────────────────
    //  1. UDP 网络连接
    // ──────────────────────────────────────────────────────────
    [Header("UDP Network")]
    [Tooltip("Local listen IP (empty = any)")]
    public string udpListenIp = "";
    [Tooltip("Local listen port")]
    public int udpListenPort = 9000;
    [Tooltip("Auto start listening on Play")]
    public bool autoConnectStream = true;

    // ──────────────────────────────────────────────────────────
    //  2. DAS 信号线变形
    // ──────────────────────────────────────────────────────────
    [Header("DAS Signal Deformation")]
    [Tooltip("Signal amplitude → height scale factor")]
    public float heightScale = 1f;
    [Tooltip("Raw value multiplier (pre‑heightScale)")]
    public float rawValueScale = 1f;
    [Tooltip("Displacement direction in LineRenderer space")]
    public Vector3 displacementDirection = Vector3.up;

    [Header("DAS Polyline")]
    [Tooltip("Polyline control points (>=2), defines DAS fiber layout")]
    public List<Vector3> controlPoints = new List<Vector3>
    {
        new Vector3(-5f, 0f, 0f),
        new Vector3(0f, 0f, 0f),
        new Vector3(5f, 0f, 0f)
    };
    [Tooltip("Line visual width")]
    public float lineWidth = 0.2f;
    [Tooltip("Use world space for LineRenderer")]
    public bool useWorldSpace = false;

    // ──────────────────────────────────────────────────────────
    //  3. 脚步跟踪 — 总控
    // ──────────────────────────────────────────────────────────
    [Header("Footstep Tracking — Enable")]
    [Tooltip("Master switch: enable footstep → person visualization")]
    public bool enableFootstepTracking = true;
    [Tooltip("Total channel count for coordinate mapping (auto‑synced from signal packets)")]
    public int totalChannelCount = 156;

    // ──────────────────────────────────────────────────────────
    //  3a. 抗噪 / 候选触发
    // ──────────────────────────────────────────────────────────
    [Header("Footstep — Anti‑noise / Triggering")]
    [Tooltip("Minimum event confidence to accept (below this → discard)")]
    [Range(0f, 1f)]
    public float eventConfidenceThreshold = 0.3f;
    [Tooltip("Time window (s) for clustering unassigned events")]
    [Range(0.1f, 10f)]
    public float triggerWindowSeconds = 2.0f;
    [Tooltip("Spatial radius (channel units) for clustering events into one candidate")]
    [Range(1f, 50f)]
    public float triggerSpatialRadius = 8.0f;
    [Tooltip("Minimum events within window+radius to create a Tentative target")]
    [Range(1, 20)]
    public int triggerMinEvents = 3;

    // ──────────────────────────────────────────────────────────
    //  3b. 目标关联与跟踪
    // ──────────────────────────────────────────────────────────
    [Header("Footstep — Target Association")]
    [Tooltip("Max gating distance (ch) for associating an event to an existing target")]
    [Range(1f, 100f)]
    public float maxAssociationDistance = 15.0f;
    [Tooltip("Max plausible velocity (ch/s) — events implying faster motion are rejected")]
    [Range(1f, 200f)]
    public float maxVelocityChannelsPerSec = 30.0f;
    [Tooltip("Min accumulated events to promote Tentative → Confirmed")]
    [Range(1, 50)]
    public int confirmMinEvents = 5;
    [Tooltip("Min sustained duration (s) to promote Tentative → Confirmed")]
    [Range(0f, 10f)]
    public float confirmTimeSeconds = 1.5f;

    // ──────────────────────────────────────────────────────────
    //  3c. EMA 平滑系数（关联更新）
    // ──────────────────────────────────────────────────────────
    [Header("Footstep — Tracking Smoothing")]
    [Tooltip("EMA alpha for position smoothing (0=never update, 1=snap to event)")]
    [Range(0.01f, 1f)]
    public float trackingPositionAlpha = 0.35f;
    [Tooltip("EMA alpha for velocity smoothing")]
    [Range(0.01f, 1f)]
    public float trackingVelocityAlpha = 0.35f;
    [Tooltip("EMA alpha for confidence smoothing")]
    [Range(0.01f, 1f)]
    public float trackingConfidenceAlpha = 0.3f;

    // ──────────────────────────────────────────────────────────
    //  3d. 生命周期超时
    // ──────────────────────────────────────────────────────────
    [Header("Footstep — Lifecycle Timeouts")]
    [Tooltip("Seconds without event before Confirmed → Lost")]
    [Range(0.5f, 30f)]
    public float lostTimeoutSeconds = 3.0f;
    [Tooltip("Seconds in Lost state before final removal")]
    [Range(0.5f, 60f)]
    public float removeTimeoutSeconds = 5.0f;
    [Tooltip("Max age (s) for Tentative before discard (not enough evidence)")]
    [Range(0.5f, 30f)]
    public float tentativeMaxAge = 4.0f;

    // ──────────────────────────────────────────────────────────
    //  3e. 各状态透明度目标（驱动淡入淡出）
    // ──────────────────────────────────────────────────────────
    [Header("Footstep — State Visibility")]
    [Tooltip("Alpha when target is Tentative (semi-transparent = unconfirmed)")]
    [Range(0f, 1f)]
    public float tentativeAlpha = 0.4f;
    [Tooltip("Alpha when target is Confirmed (fully opaque = stable)")]
    [Range(0f, 1f)]
    public float confirmedAlpha = 1.0f;
    [Tooltip("Alpha when target is Lost (fading = about to disappear)")]
    [Range(0f, 1f)]
    public float lostAlpha = 0.3f;

    // ──────────────────────────────────────────────────────────
    //  4. 小人可视化
    // ──────────────────────────────────────────────────────────
    [Header("Person Visualization")]
    [Tooltip("Optional prefab for person. If null → loads StarterAssets PlayerArmature humanoid")]
    public GameObject personPrefab;
    [Tooltip("Walk animation speed multiplier")]
    [Range(0.5f, 5f)]
    public float walkAnimSpeed = 2.0f;
    [Tooltip("Animation speed smoothing (higher = snappier)")]
    [Range(1f, 20f)]
    public float animSpeedSmoothing = 8.0f;
    [Tooltip("Person object uniform scale")]
    [Range(0.05f, 5f)]
    public float personScale = 0.5f;
    [Tooltip("Position offset from DAS line (e.g. stand above)")]
    public Vector3 personOffset = new Vector3(0f, 1f, 0f);
    [Tooltip("Fallback person color (used when palette exhausted)")]
    public Color personColor = new Color(0.2f, 0.8f, 0.3f, 1f);

    [Header("Person — Motion Smoothing")]
    [Tooltip("Visual position interpolation speed (higher = snappier)")]
    [Range(0.5f, 30f)]
    public float positionSmoothSpeed = 5.0f;
    [Tooltip("Rotation interpolation speed (higher = snappier turning)")]
    [Range(0.5f, 30f)]
    public float rotationSmoothSpeed = 5.0f;
    [Tooltip("Fade‑in duration (s) when person appears")]
    [Range(0f, 3f)]
    public float fadeInDuration = 0.5f;
    [Tooltip("Fade‑out duration (s) when person disappears")]
    [Range(0f, 3f)]
    public float fadeOutDuration = 1.0f;

    [Header("Person — Labels")]
    [Tooltip("Show floating ID label above person")]
    public bool showPersonLabels = true;
    [Tooltip("Label character size")]
    [Range(0.01f, 1f)]
    public float labelCharacterSize = 0.12f;
    [Tooltip("Label font size")]
    [Range(8, 128)]
    public int labelFontSize = 48;

    // ──────────────────────────────────────────────────────────
    //  5. 调试
    // ──────────────────────────────────────────────────────────
    [Header("Debug")]
    [Tooltip("Log incoming footstep events to Console")]
    public bool logFootstepEvents = false;
    [Tooltip("Log tracking state transitions to Console")]
    public bool logStateTransitions = true;
    [Tooltip("Max events drained from queue per frame (burst limit)")]
    [Range(1, 512)]
    public int maxEventsPerFrame = 64;

    // ──────────────────────────────────────────────────────────
    //  Private — DAS 信号
    // ──────────────────────────────────────────────────────────
    private int totalChannelsFromSignal;            // 从 signal 包同步的通道数
    private LineRenderer lineRenderer;
    private Vector3[] basePositions;
    private Vector3[] deformedPositions;
    private bool signalInitialized;                 // 第一个 signal 包到达后 = true
    private float timePerFrame;
    private int currentFrameIndex = 0;
    private float timer = 0f;

    // 信号帧累积缓冲（仅用于流模式连续回放）
    private List<DasFrame> frameBuffer = new List<DasFrame>();

    // ──────────────────────────────────────────────────────────
    //  Private — 网络
    // ──────────────────────────────────────────────────────────
    private UdpClient udpClient;
    private Thread streamThread;
    private volatile bool streamRunning;
    private readonly object frameLock = new object();
    private readonly Queue<DasFrame> incomingFrames = new Queue<DasFrame>();

    // ──────────────────────────────────────────────────────────
    //  Private — 脚步跟踪
    // ──────────────────────────────────────────────────────────
    private readonly object eventLock = new object();
    private readonly Queue<FootstepEvent> incomingEvents = new Queue<FootstepEvent>();
    private readonly List<TrackedTarget> activeTargets = new List<TrackedTarget>();
    private readonly List<FootstepEvent> unassignedEvents = new List<FootstepEvent>();
    private int nextTargetId = 1;
    private float latestStreamTime = 0f;

    // polyline 段长缓存（坐标映射）
    private float[] segLengthsCache;
    private float totalPolylineLength;

    // 多人配色板
    private static readonly Color[] PersonPalette = new Color[]
    {
        new Color(0.20f, 0.80f, 0.30f, 1f),
        new Color(0.25f, 0.55f, 0.95f, 1f),
        new Color(0.95f, 0.60f, 0.10f, 1f),
        new Color(0.85f, 0.25f, 0.60f, 1f),
        new Color(0.10f, 0.85f, 0.85f, 1f),
        new Color(0.95f, 0.95f, 0.20f, 1f),
        new Color(0.60f, 0.35f, 0.85f, 1f),
        new Color(0.90f, 0.40f, 0.40f, 1f),
    };

    // ──────────────────────────────────────────────────────────
    //  JSON 包结构（与 Python realtime_infer_stream.py 对齐）
    // ──────────────────────────────────────────────────────────
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

    [Serializable]
    private class EventPacket
    {
        public string packet_type;
        public float timestamp;
        public int channel_index;
        public float confidence;
    }

    // ══════════════════════════════════════════════════════════
    //  Unity 生命周期
    // ══════════════════════════════════════════════════════════

    void Start()
    {
        lineRenderer = GetComponent<LineRenderer>();
        if (lineRenderer == null)
            lineRenderer = gameObject.AddComponent<LineRenderer>();
        ConfigureLineRenderer();
        lineRenderer.alignment = LineAlignment.View;

        CachePolylineSegments();

        if (autoConnectStream)
            StartStream();
    }

    void Update()
    {
        // ── 1. 消费信号帧 ──
        DrainIncomingFrames();

        // ── 2. 消费脚步事件 ──
        if (enableFootstepTracking)
            DrainAndProcessEvents();

        // ── 3. DAS 信号回放（连续推进帧索引） ──
        if (signalInitialized && frameBuffer.Count > 0 && timePerFrame > 0f)
        {
            timer += Time.deltaTime;
            if (timer >= timePerFrame)
            {
                if (currentFrameIndex + 1 < frameBuffer.Count)
                {
                    currentFrameIndex++;
                    UpdateDasGeometry(frameBuffer[currentFrameIndex]);
                }
                timer -= timePerFrame;
            }
        }

        // ── 4. 人物可视化更新 ──
        if (enableFootstepTracking)
        {
            UpdateTargetLifecycles();
            UpdatePersonVisuals(Time.deltaTime);
        }
    }

    private void OnDestroy()
    {
        StopStream();
        CleanupAllTargets();
    }

    // ══════════════════════════════════════════════════════════
    //  LineRenderer 配置与几何更新
    // ══════════════════════════════════════════════════════════

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
            BuildPolylineSamples(count);

        Vector3 dir = displacementDirection.sqrMagnitude > 1e-6f
            ? displacementDirection.normalized
            : Vector3.up;
        for (int i = 0; i < count; i++)
        {
            float v = frame.values[i];
            if (!IsFinite(v)) v = 0f;
            deformedPositions[i] = basePositions[i] + dir * (v * rawValueScale * heightScale);
        }
        lineRenderer.SetPositions(deformedPositions);
    }

    private static bool IsFinite(float v)
    {
        return !(float.IsNaN(v) || float.IsInfinity(v));
    }

    // ══════════════════════════════════════════════════════════
    //  UDP 网络接收（signal / event 分流）
    // ══════════════════════════════════════════════════════════

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
        try { if (udpClient != null) { udpClient.Close(); udpClient = null; } } catch { }
        if (streamThread != null)
        {
            try { streamThread.Join(200); } catch { }
            streamThread = null;
        }
    }

    /// <summary>
    /// 后台线程：持续接收 UDP 包，按 packet_type 分流到 incomingFrames 或 incomingEvents。
    /// 支持乱序、丢包、突发流量（UDP 天然特性）。
    /// </summary>
    private void StreamLoop()
    {
        try
        {
            IPEndPoint listenEndPoint = string.IsNullOrWhiteSpace(udpListenIp)
                ? new IPEndPoint(IPAddress.Any, udpListenPort)
                : new IPEndPoint(IPAddress.Parse(udpListenIp), udpListenPort);

            udpClient = new UdpClient(listenEndPoint);
            udpClient.Client.ReceiveTimeout = 1000;
            var remote = new IPEndPoint(IPAddress.Any, 0);

            while (streamRunning)
            {
                byte[] bytes = null;
                try { bytes = udpClient.Receive(ref remote); }
                catch (SocketException se)
                {
                    if (!streamRunning || se.ErrorCode == 10004 || se.ErrorCode == 10060) continue;
                    throw;
                }
                if (bytes == null || bytes.Length == 0) continue;

                string payload = System.Text.Encoding.UTF8.GetString(bytes);
                if (string.IsNullOrWhiteSpace(payload)) continue;

                // 一个 UDP 包可能包含多行 JSON（TCP 同理）
                string[] lines = payload.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (string.IsNullOrEmpty(line)) continue;
                    try
                    {
                        string ptype = DetectPacketType(line);
                        if (ptype == "event")
                        {
                            EventPacket ep = JsonUtility.FromJson<EventPacket>(line);
                            if (ep != null)
                            {
                                lock (eventLock)
                                {
                                    incomingEvents.Enqueue(new FootstepEvent
                                    {
                                        timestamp    = ep.timestamp,
                                        channelIndex = ep.channel_index,
                                        confidence   = ep.confidence
                                    });
                                }
                                if (ep.timestamp > latestStreamTime)
                                    latestStreamTime = ep.timestamp;
                            }
                        }
                        else
                        {
                            DasPacket packet = JsonUtility.FromJson<DasPacket>(line);
                            if (packet != null && packet.signals != null && packet.signals.Length > 0)
                                EnqueueSignalPacket(packet);
                        }
                    }
                    catch { /* 解析失败静默跳过 */ }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"DasVisualizer: UDP listen failed — {ex.Message}");
        }
        finally
        {
            streamRunning = false;
        }
    }

    /// <summary>从 JSON 原文快速提取 "packet_type" 值，避免双重完整反序列化。</summary>
    private static string DetectPacketType(string json)
    {
        int idx = json.IndexOf("\"packet_type\"", StringComparison.Ordinal);
        if (idx < 0) return "signal";
        int colon = json.IndexOf(':', idx + 13);
        if (colon < 0) return "signal";
        int q1 = json.IndexOf('"', colon + 1);
        if (q1 < 0) return "signal";
        int q2 = json.IndexOf('"', q1 + 1);
        if (q2 <= q1) return "signal";
        return json.Substring(q1 + 1, q2 - q1 - 1);
    }

    private void EnqueueSignalPacket(DasPacket packet)
    {
        lock (frameLock)
        {
            incomingFrames.Enqueue(new DasFrame { values = packet.signals });
        }
        if (packet.sample_rate > 0.01f)
            timePerFrame = 1f / packet.sample_rate;
        if (packet.total_channels > 0)
            totalChannelsFromSignal = packet.total_channels;
        if (packet.timestamp > latestStreamTime)
            latestStreamTime = packet.timestamp;
    }

    /// <summary>主线程：把后台收到的信号帧逐个入缓冲并驱动第一帧初始化。</summary>
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

            frameBuffer.Add(frame);
            gotFirst = gotFirst || frameBuffer.Count == 1;
        }

        if (gotFirst)
        {
            BuildPolylineSamples(frameBuffer[0].values.Length);
            currentFrameIndex = 0;
            signalInitialized = true;
            UpdateDasGeometry(frameBuffer[0]);

            // 同步通道数
            if (totalChannelsFromSignal > 0)
                totalChannelCount = totalChannelsFromSignal;
        }
        // 持续同步通道数（可能在后续包中更新）
        else if (totalChannelsFromSignal > 0 && totalChannelsFromSignal != totalChannelCount)
        {
            totalChannelCount = totalChannelsFromSignal;
        }
    }

    // ══════════════════════════════════════════════════════════
    //  回放控制 API（供 UI / 外部脚本调用）
    // ══════════════════════════════════════════════════════════

    public void Play()  { Debug.Log("DasVisualizer: Play");  }
    public void Pause() { Debug.Log("DasVisualizer: Pause"); }
    public void TogglePlay() { Debug.Log("DasVisualizer: Toggle"); }

    public float GetCurrentTime() { return currentFrameIndex * timePerFrame; }
    public float GetTotalTime()   { return frameBuffer.Count * timePerFrame; }

    // ══════════════════════════════════════════════════════════
    //  坐标映射：channel_index ↔ 世界坐标
    // ══════════════════════════════════════════════════════════

    private void CachePolylineSegments()
    {
        if (controlPoints == null || controlPoints.Count < 2)
        {
            segLengthsCache = new float[0];
            totalPolylineLength = 0f;
            return;
        }
        int n = controlPoints.Count - 1;
        segLengthsCache = new float[n];
        totalPolylineLength = 0f;
        for (int i = 0; i < n; i++)
        {
            float len = Vector3.Distance(controlPoints[i], controlPoints[i + 1]);
            segLengthsCache[i] = len;
            totalPolylineLength += len;
        }
    }

    /// <summary>
    /// 将浮点通道坐标映射到世界／本地坐标。
    /// 映射方式：channelIndex ∈ [0, totalCh-1] → polyline 上等比例位置 + personOffset。
    /// </summary>
    private Vector3 ChannelToWorldPosition(float channelIndex, int totalCh)
    {
        if (controlPoints == null || controlPoints.Count == 0)
            return personOffset;
        if (controlPoints.Count == 1 || totalCh <= 1 || totalPolylineLength <= Mathf.Epsilon)
            return controlPoints[0] + personOffset;

        float t = Mathf.Clamp01(channelIndex / (totalCh - 1));
        float targetDist = t * totalPolylineLength;
        float acc = 0f;
        for (int s = 0; s < segLengthsCache.Length; s++)
        {
            float segLen = segLengthsCache[s];
            if (targetDist <= acc + segLen || s == segLengthsCache.Length - 1)
            {
                float localT = segLen <= Mathf.Epsilon ? 0f : (targetDist - acc) / segLen;
                return Vector3.Lerp(controlPoints[s], controlPoints[s + 1], localT) + personOffset;
            }
            acc += segLen;
        }
        return controlPoints[controlPoints.Count - 1] + personOffset;
    }

    // ══════════════════════════════════════════════════════════
    //  脚步事件接入与跟踪
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// 主线程：从队列取出事件 → 置信度过滤 → 关联/聚簇 → 建目标。
    /// 每帧最多处理 maxEventsPerFrame 个事件，防止突发卡顿。
    /// </summary>
    private void DrainAndProcessEvents()
    {
        int processed = 0;
        while (processed < maxEventsPerFrame)
        {
            FootstepEvent evt = null;
            lock (eventLock)
            {
                if (incomingEvents.Count > 0) evt = incomingEvents.Dequeue();
            }
            if (evt == null) break;

            // ── 第一道抗噪：置信度过滤 ──
            if (evt.confidence < eventConfidenceThreshold) { processed++; continue; }

            if (logFootstepEvents)
                Debug.Log($"[Footstep] t={evt.timestamp:F3} ch={evt.channelIndex} conf={evt.confidence:F3}");

            ProcessFootstepEvent(evt);
            processed++;
        }

        PurgeOldUnassignedEvents();
    }

    /// <summary>
    /// 核心路由：先尝试关联到已有目标，失败则入未分配池并尝试聚簇建新目标。
    /// </summary>
    private void ProcessFootstepEvent(FootstepEvent evt)
    {
        TrackedTarget assigned = TryAssignEventToTarget(evt);
        if (assigned != null)
        {
            UpdateTargetWithEvent(assigned, evt);
            return;
        }

        unassignedEvents.Add(evt);
        TryFormNewTarget(evt);
    }

    /// <summary>
    /// 最近邻关联：遍历所有活跃目标，用速度预测位置+距离门控+速度门控选最优。
    /// 避免了不合理的远距跳变和超速关联。
    /// </summary>
    private TrackedTarget TryAssignEventToTarget(FootstepEvent evt)
    {
        TrackedTarget best = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < activeTargets.Count; i++)
        {
            var tgt = activeTargets[i];
            if (tgt.state == TargetState.Removed) continue;

            float dt = Mathf.Max(0.001f, evt.timestamp - tgt.lastEventTime);

            // 速度预测：目标应该在哪里？
            float predicted = tgt.channelPosition + tgt.velocity * dt;
            float distPredicted = Mathf.Abs(evt.channelIndex - predicted);
            float distRaw = Mathf.Abs(evt.channelIndex - tgt.channelPosition);
            float dist = Mathf.Min(distPredicted, distRaw);

            // 双重门控
            if (dist > maxAssociationDistance) continue;
            if (distRaw / dt > maxVelocityChannelsPerSec) continue;

            if (dist < bestDist)
            {
                bestDist = dist;
                best = tgt;
            }
        }
        return best;
    }

    /// <summary>
    /// 用 EMA（指数移动平均）平滑更新目标的位置、速度、置信度。
    /// 如果目标处于 Lost 状态，收到新事件时复活为 Confirmed。
    /// </summary>
    private void UpdateTargetWithEvent(TrackedTarget tgt, FootstepEvent evt)
    {
        float dt = Mathf.Max(0.001f, evt.timestamp - tgt.lastEventTime);
        float rawVel = (evt.channelIndex - tgt.channelPosition) / dt;

        tgt.channelPosition = Mathf.Lerp(tgt.channelPosition, evt.channelIndex, trackingPositionAlpha);
        tgt.velocity = Mathf.Lerp(tgt.velocity, rawVel, trackingVelocityAlpha);
        tgt.velocity = Mathf.Clamp(tgt.velocity, -maxVelocityChannelsPerSec, maxVelocityChannelsPerSec);
        tgt.confidence = Mathf.Lerp(tgt.confidence, evt.confidence, trackingConfidenceAlpha);
        tgt.lastEventTime = evt.timestamp;
        tgt.totalEventCount++;

        tgt.recentEvents.Add(evt);
        PruneRecentEvents(tgt);

        // Lost → Confirmed 复活
        if (tgt.state == TargetState.Lost)
        {
            tgt.state = TargetState.Confirmed;
            tgt.stateEnterTime = evt.timestamp;
            tgt.targetAlpha = confirmedAlpha;
            if (logStateTransitions)
                Debug.Log($"[Track] P{tgt.id} Lost → Confirmed (revived)");
        }
    }

    /// <summary>
    /// 抗噪聚簇：以新事件为中心，在 triggerWindowSeconds × triggerSpatialRadius 窗口搜索
    /// 未分配事件，若数量 ≥ triggerMinEvents 且远离已有目标，则创建 Tentative 候选。
    /// </summary>
    private void TryFormNewTarget(FootstepEvent pivot)
    {
        List<FootstepEvent> cluster = new List<FootstepEvent>();
        float channelSum = 0f;
        float confSum = 0f;

        for (int i = unassignedEvents.Count - 1; i >= 0; i--)
        {
            var e = unassignedEvents[i];
            if (pivot.timestamp - e.timestamp > triggerWindowSeconds) continue;
            if (Mathf.Abs(e.channelIndex - pivot.channelIndex) > triggerSpatialRadius) continue;
            cluster.Add(e);
            channelSum += e.channelIndex;
            confSum += e.confidence;
        }

        if (cluster.Count < triggerMinEvents) return;

        // 去重：不在已有目标附近建新目标
        float meanCh = channelSum / cluster.Count;
        for (int i = 0; i < activeTargets.Count; i++)
        {
            var t = activeTargets[i];
            if (t.state == TargetState.Removed) continue;
            if (Mathf.Abs(t.channelPosition - meanCh) < triggerSpatialRadius) return;
        }

        var newTarget = new TrackedTarget
        {
            id              = nextTargetId++,
            state           = TargetState.Tentative,
            channelPosition = meanCh,
            velocity        = 0f,
            confidence      = confSum / cluster.Count,
            lastEventTime   = pivot.timestamp,
            stateEnterTime  = pivot.timestamp,
            creationTime    = pivot.timestamp,
            totalEventCount = cluster.Count,
            targetAlpha     = tentativeAlpha,
            visualAlpha     = 0f,
        };
        newTarget.recentEvents.AddRange(cluster);

        newTarget.visual = CreatePersonVisual(newTarget.id);
        newTarget.animator = newTarget.visual.GetComponentInChildren<Animator>();
        Vector3 worldPos = ChannelToWorldPosition(meanCh, totalChannelCount);
        newTarget.displayPosition = worldPos;
        newTarget.previousDisplayPosition = worldPos;
        SetVisualPosition(newTarget);

        activeTargets.Add(newTarget);

        for (int i = 0; i < cluster.Count; i++)
            unassignedEvents.Remove(cluster[i]);

        if (logStateTransitions)
            Debug.Log($"[Track] NEW Tentative P{newTarget.id} at ch={meanCh:F1} ({cluster.Count} events)");
    }

    // ══════════════════════════════════════════════════════════
    //  目标生命周期状态机
    //
    //  Tentative ──（够事件+够时间）──→ Confirmed
    //     │                                │
    //     │(超龄不够事件)                   │(无事件超 lostTimeout)
    //     ↓                                ↓
    //  Removed ←──(超 removeTimeout)──── Lost
    //                                      │
    //                                      │(收到新事件 → 复活)
    //                                      ↓
    //                                  Confirmed
    // ══════════════════════════════════════════════════════════

    private void UpdateTargetLifecycles()
    {
        float now = latestStreamTime;
        for (int i = activeTargets.Count - 1; i >= 0; i--)
        {
            var tgt = activeTargets[i];
            PruneRecentEvents(tgt);

            switch (tgt.state)
            {
                case TargetState.Tentative:
                    if (tgt.totalEventCount >= confirmMinEvents
                        && (now - tgt.creationTime) >= confirmTimeSeconds)
                    {
                        tgt.state = TargetState.Confirmed;
                        tgt.stateEnterTime = now;
                        tgt.targetAlpha = confirmedAlpha;
                        if (logStateTransitions)
                            Debug.Log($"[Track] P{tgt.id} Tentative → Confirmed");
                    }
                    else if ((now - tgt.creationTime) > tentativeMaxAge
                             && tgt.totalEventCount < confirmMinEvents)
                    {
                        tgt.state = TargetState.Removed;
                        tgt.stateEnterTime = now;
                        tgt.targetAlpha = 0f;
                        if (logStateTransitions)
                            Debug.Log($"[Track] P{tgt.id} Tentative → Removed (timeout, only {tgt.totalEventCount} events)");
                    }
                    break;

                case TargetState.Confirmed:
                    if ((now - tgt.lastEventTime) > lostTimeoutSeconds)
                    {
                        tgt.state = TargetState.Lost;
                        tgt.stateEnterTime = now;
                        tgt.targetAlpha = lostAlpha;
                        if (logStateTransitions)
                            Debug.Log($"[Track] P{tgt.id} Confirmed → Lost");
                    }
                    break;

                case TargetState.Lost:
                    if ((now - tgt.stateEnterTime) > removeTimeoutSeconds)
                    {
                        tgt.state = TargetState.Removed;
                        tgt.stateEnterTime = now;
                        tgt.targetAlpha = 0f;
                        if (logStateTransitions)
                            Debug.Log($"[Track] P{tgt.id} Lost → Removed");
                    }
                    break;

                case TargetState.Removed:
                    if (tgt.visualAlpha <= 0.01f)
                    {
                        if (tgt.visual != null) Destroy(tgt.visual);
                        activeTargets.RemoveAt(i);
                    }
                    break;
            }
        }
    }

    private void PruneRecentEvents(TrackedTarget tgt)
    {
        float cutoff = latestStreamTime - triggerWindowSeconds;
        tgt.recentEvents.RemoveAll(e => e.timestamp < cutoff);
    }

    private void PurgeOldUnassignedEvents()
    {
        float cutoff = latestStreamTime - triggerWindowSeconds * 1.5f;
        unassignedEvents.RemoveAll(e => e.timestamp < cutoff);
    }

    // ══════════════════════════════════════════════════════════
    //  人物可视化
    // ══════════════════════════════════════════════════════════

    private void UpdatePersonVisuals(float dt)
    {
        for (int i = 0; i < activeTargets.Count; i++)
        {
            var tgt = activeTargets[i];
            if (tgt.visual == null) continue;

            // ── 位置平滑插值 ──
            Vector3 worldTarget = ChannelToWorldPosition(tgt.channelPosition, totalChannelCount);
            float smoothFactor = 1f - Mathf.Exp(-positionSmoothSpeed * dt);
            tgt.displayPosition = Vector3.Lerp(tgt.displayPosition, worldTarget, smoothFactor);
            SetVisualPosition(tgt);

            // ── 朝向跟随运动方向 ──
            Vector3 delta = tgt.displayPosition - tgt.previousDisplayPosition;
            tgt.isMoving = delta.sqrMagnitude > 1e-6f * dt * dt;
            if (tgt.isMoving)
            {
                Vector3 flatDir = new Vector3(delta.x, 0f, delta.z);
                if (flatDir.sqrMagnitude > 1e-8f)
                    tgt.visual.transform.rotation = Quaternion.Slerp(
                        tgt.visual.transform.rotation,
                        Quaternion.LookRotation(flatDir, Vector3.up),
                        rotationSmoothSpeed * dt);
            }
            tgt.previousDisplayPosition = tgt.displayPosition;

            // ── 驱动走路动画 ──
            if (tgt.animator != null)
            {
                float targetSpeed = tgt.isMoving ? walkAnimSpeed : 0f;
                tgt.animSpeed = Mathf.Lerp(tgt.animSpeed, targetSpeed, animSpeedSmoothing * dt);
                tgt.animator.SetFloat(AnimIDSpeed, tgt.animSpeed);
                tgt.animator.SetFloat(AnimIDMotionSpeed, 1f);
                tgt.animator.SetBool(AnimIDGrounded, true);
            }

            // ── 透明度淡入/淡出 ──
            float fadeDuration = (tgt.targetAlpha > tgt.visualAlpha) ? fadeInDuration : fadeOutDuration;
            tgt.visualAlpha = (fadeDuration <= 0f)
                ? tgt.targetAlpha
                : Mathf.MoveTowards(tgt.visualAlpha, tgt.targetAlpha, dt / fadeDuration);
            SetPersonAlpha(tgt, tgt.visualAlpha);

            // ── 标签 ──
            if (showPersonLabels) UpdateLabel(tgt);
        }
    }

    private void SetVisualPosition(TrackedTarget tgt)
    {
        if (tgt.visual == null) return;
        if (useWorldSpace)
            tgt.visual.transform.position = tgt.displayPosition;
        else
            tgt.visual.transform.localPosition = tgt.displayPosition;
    }

    // Animator 参数 Hash 缓存
    private static readonly int AnimIDSpeed       = Animator.StringToHash("Speed");
    [Header("Robot Textures (Auto-fix)")]
    public Texture2D armTexture;
    public Texture2D bodyTexture;
    public Texture2D legTexture;

    private static readonly int AnimIDGrounded     = Animator.StringToHash("Grounded");
    private static readonly int AnimIDMotionSpeed  = Animator.StringToHash("MotionSpeed");

    // ──────────────────────────────────────────────────────────
    //  Helper: Try to find asset in Editor
    // ──────────────────────────────────────────────────────────
#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            // Auto-locate prefab if missing
            if (personPrefab == null)
            {
                var guids = UnityEditor.AssetDatabase.FindAssets("Armature t:Prefab", new[] { "Assets/StarterAssets" });
                if (guids.Length > 0)
                    personPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
            }
            // Auto-locate textures
            if (armTexture == null) armTexture = FindTex("Armature_Arms_AlbedoTransparency");
            if (bodyTexture == null) bodyTexture = FindTex("Armature_Body_AlbedoTransparency");
            if (legTexture == null) legTexture = FindTex("Armature_Legs_AlbedoTransparency");
        }
    }
    private Texture2D FindTex(string name)
    {
        var guids = UnityEditor.AssetDatabase.FindAssets(name + " t:Texture2D", new[] { "Assets/StarterAssets" });
        return guids.Length > 0 ? UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0])) : null;
    }
#endif

    /// <summary>创建人物可视对象（Prefab → StarterAssets 人形模型 → 回退胶囊体）。</summary>
    private GameObject CreatePersonVisual(int id)
    {
        GameObject go;
        bool isHumanoid = false;

        if (personPrefab != null)
        {
            go = Instantiate(personPrefab, transform);
            isHumanoid = go.GetComponentInChildren<Animator>() != null;
        }
        else
        {
            // Fallback to Capsule if absolutely nothing found
            go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.transform.SetParent(transform, false);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        go.name = $"Person_{id}";
        go.transform.localScale = Vector3.one * personScale;

        // Cleanup components
        foreach (var c in go.GetComponentsInChildren<Component>())
        {
            // Remove scripts that might interfere (Input, CharacterController, etc.)
            // We only keep Transform, Animator, Renderers, Filters
            if (c is Transform || c is Animator || c is Renderer || c is MeshFilter || c is SkinnedMeshRenderer) continue;
            DestroyImmediate(c);
        }

        // Setup Animator
        var animator = go.GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.applyRootMotion = false;
            animator.SetBool(AnimIDGrounded, true);
            animator.SetFloat(AnimIDSpeed, 0f);
            animator.SetFloat(AnimIDMotionSpeed, 1f);
            
            // Add event receiver to prevent errors
             var receiver = animator.gameObject;
            if (receiver.GetComponent<AnimEventReceiver>() == null)
                receiver.AddComponent<AnimEventReceiver>();
        }

        // ── 材质修复与透明度设置 ──
        if (isHumanoid)
        {
            // Fix materials for the robot
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
            {
                Material[] newMats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < r.sharedMaterials.Length; i++)
                {
                    // Create new material
                    bool isURP = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
                    Shader shader = Shader.Find(isURP ? "Universal Render Pipeline/Lit" : "Standard");
                    if (shader == null) shader = Shader.Find("Transparent/Diffuse");
                    
                    Material mat = new Material(shader);
                    ConfigureTransparentMaterial(mat); // Ensure transparency settings from start

                    // Determine texture
                    Texture2D tex = bodyTexture; // Default to body
                    string matName = (r.sharedMaterials[i] != null) ? r.sharedMaterials[i].name.ToLower() : "";
                    string objName = r.name.ToLower();

                    // Heuristic to map texture
                    if (matName.Contains("arm")) tex = armTexture;
                    else if (matName.Contains("leg")) tex = legTexture;
                    else if (matName.Contains("body")) tex = bodyTexture;
                    else 
                    {
                        // Fallback to object name
                        if (objName.Contains("arm") && !objName.Contains("armature")) tex = armTexture;
                        else if (objName.Contains("leg")) tex = legTexture;
                    }

                    // Assign texture
                    if (tex != null)
                    {
                        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                        else if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
                    }
                    newMats[i] = mat;
                }
                r.materials = newMats;
            }
        }
        else
        {
            // Capsule fallback
            var rend = go.GetComponent<Renderer>();
            if (rend != null)
            {
                Material mat = new Material(rend.sharedMaterial);
                ConfigureTransparentMaterial(mat);
                rend.material = mat;
            }
        }

        // ── 浮动 ID 标签 ──
        if (showPersonLabels)
        {
            GameObject labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, false);
            labelGo.transform.localPosition = Vector3.up * 2.0f / Mathf.Max(0.01f, personScale);
            TextMesh tm = labelGo.AddComponent<TextMesh>();
            tm.text = $"P{id}";
            tm.characterSize = labelCharacterSize;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;
            tm.fontSize = labelFontSize;
        }

        return go;
    }

    /// <summary>移除 StarterAssets Prefab 上无用的运行时控制组件。</summary>
    // (This function signature is kept to satisfy potential callers if any, but implementation is effectively inlined above or replaced)
    private void StripUnwantedComponents(GameObject go) 
    { 
        // Logic moved to CreatePersonVisual
    }

    private void ConfigureTransparentMaterial(Material mat)
    {
        // Standard / URP Setup
        mat.SetFloat("_Mode", 3f); // Standard: Transparent
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;

        if (mat.HasProperty("_Surface")) // URP
        {
            mat.SetFloat("_Surface", 1f); // Transparent
            mat.SetFloat("_Blend", 0f); // Alpha
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
    }

    private void SetPersonAlpha(TrackedTarget tgt, float alpha)
    {
        if (tgt.visual == null) return;
        float a = Mathf.Clamp01(alpha);
        
        // Apply alpha to all renderers
        var renderers = tgt.visual.GetComponentsInChildren<Renderer>();
        foreach (var r in renderers)
        {
            if (r == null || r.material == null) continue;
            
            // Standard Shader
            if (r.material.HasProperty("_Color"))
            {
                Color c = r.material.color;
                c.a = a;
                r.material.color = c;
            }
            // URP Shader
            else if (r.material.HasProperty("_BaseColor"))
            {
                Color c = r.material.GetColor("_BaseColor");
                c.a = a;
                r.material.SetColor("_BaseColor", c);
            }
        }
    }

    /// <summary>设置 GameObject 下所有 Renderer 的显/隐状态。</summary>
    private void SetRenderersVisible(GameObject go, bool visible)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        foreach (var rend in renderers)
        {
            if (rend != null) rend.enabled = visible;
        }
    }

    private void UpdateLabel(TrackedTarget tgt)
    {
        if (tgt.visual == null) return;
        Transform labelTr = tgt.visual.transform.Find("Label");
        if (labelTr == null) return;

        Camera cam = Camera.main;
        if (cam != null)
            labelTr.rotation = Quaternion.LookRotation(labelTr.position - cam.transform.position, Vector3.up);

        TextMesh tm = labelTr.GetComponent<TextMesh>();
        if (tm == null) return;
        string stateChar;
        switch (tgt.state)
        {
            case TargetState.Tentative: stateChar = "?"; break;
            case TargetState.Confirmed: stateChar = "";  break;
            case TargetState.Lost:      stateChar = "~"; break;
            default:                    stateChar = "x"; break;
        }
        tm.text = $"P{tgt.id}{stateChar}";
        Color lc = tm.color;
        lc.a = tgt.visualAlpha;
        tm.color = lc;
    }

    private Color GetPersonColor(int id)
    {
        if (PersonPalette.Length == 0) return personColor;
        return PersonPalette[(id - 1) % PersonPalette.Length];
    }

    private void CleanupAllTargets()
    {
        for (int i = 0; i < activeTargets.Count; i++)
        {
            if (activeTargets[i].visual != null)
                Destroy(activeTargets[i].visual);
        }
        activeTargets.Clear();
        unassignedEvents.Clear();
    }

    // ══════════════════════════════════════════════════════════
    //  编辑器 Gizmo 调试
    // ══════════════════════════════════════════════════════════

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!enableFootstepTracking || activeTargets == null) return;
        CachePolylineSegments();

        foreach (var tgt in activeTargets)
        {
            switch (tgt.state)
            {
                case TargetState.Tentative: Gizmos.color = new Color(1f, 1f, 0f, 0.5f); break;
                case TargetState.Confirmed: Gizmos.color = new Color(0f, 1f, 0f, 0.8f); break;
                case TargetState.Lost:      Gizmos.color = new Color(1f, 0f, 0f, 0.5f); break;
                default:                    Gizmos.color = new Color(0.5f, 0.5f, 0.5f, 0.3f); break;
            }
            Vector3 pos = ChannelToWorldPosition(tgt.channelPosition, totalChannelCount);
            Gizmos.DrawWireSphere(pos, personScale * 0.6f);
            Gizmos.DrawLine(pos, pos + Vector3.right * tgt.velocity * 0.05f);
        }
    }
#endif
}
