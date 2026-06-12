using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Minimal UDP MPEG-TS/H.264 receiver for the current X5 -> Edge2 -> Unity path.
/// It deliberately contains no AI/super-resolution worker code.
/// </summary>
public class UdpVideoStreamReceiver : MonoBehaviour
{
    public enum DecodeBackend
    {
        Software,
        D3D11VA,
        CUDA,
        Auto
    }

    [Header("FFmpeg")]
    [Tooltip("Path to ffmpeg.exe. Leave as 'ffmpeg' if it is available on PATH.")]
    public string ffmpegExecutable =
        @"C:\Users\InamiLab_Kong\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg.Essentials_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-8.1-essentials_build\bin\ffmpeg.exe";

    [Tooltip("Start receiving automatically when this component starts.")]
    public bool startAutomatically = true;

    [Tooltip("Configured UDP stream URL. The receiver listens on the extracted port.")]
    public string streamUrl = "udp://@:5600";

    [Tooltip("Expected incoming video width.")]
    public int forcedWidth = 3840;

    [Tooltip("Expected incoming video height.")]
    public int forcedHeight = 1920;

    [Tooltip("Optional BGRA upload width after ffmpeg decoding. Leave 0 to upload the full incoming width.")]
    public int outputWidthOverride;

    [Tooltip("Optional BGRA upload height after ffmpeg decoding. Leave 0 to upload the full incoming height.")]
    public int outputHeightOverride;

    [Tooltip("Receiver cache in milliseconds. Higher values are more stable; lower values reduce latency.")]
    public int networkCachingMs = 30;

    [Tooltip("Extra input-side ffmpeg options.")]
    public string ffmpegInputOptions = "-fflags nobuffer+discardcorrupt -flags low_delay -probesize 262144 -analyzeduration 100000";

    [Tooltip("FFmpeg decode backend. D3D11VA keeps H.264 decode off the CPU on Windows, then downloads BGRA frames for Unity upload.")]
    public DecodeBackend decodeBackend = DecodeBackend.D3D11VA;

    [Header("Display")]
    [Tooltip("Optional renderer to receive the decoded video texture.")]
    public Renderer targetRenderer;

    [Tooltip("Texture property used when assigning to the target renderer/material.")]
    public string targetTextureProperty = "_BaseColorMap";

    [Tooltip("Apply the decoded texture to this material instead of a renderer if assigned.")]
    public Material targetMaterial;

    [Tooltip("Create and assign a dedicated runtime playback material for the target renderer.")]
    public bool createRuntimePlaybackMaterial = true;

    [Tooltip("Find or create a projection sphere at runtime if Target Renderer is not assigned.")]
    public bool autoFindOrCreateProjectionSphere = true;

    [Tooltip("Name of the projection sphere to find or create.")]
    public string projectionSphereName = "UDP Video Sphere";

    [Tooltip("World position used when a projection sphere must be created at runtime.")]
    public Vector3 projectionSphereWorldPosition = Vector3.zero;

    [Tooltip("Radius used when a projection sphere must be created at runtime.")]
    public float projectionSphereRadius = 10f;

    [Tooltip("Configure the playback material for viewing from inside a sphere.")]
    public bool projectOnInnerSurface = true;

    [Tooltip("Create the texture in linear color space.")]
    public bool linearTexture;

    [Header("Status")]
    [SerializeField] private int decodedWidth;
    [SerializeField] private int decodedHeight;
    [SerializeField] private bool streamRunning;
    [SerializeField] private string lastError;

    private readonly object frameLock = new object();
    private readonly ConcurrentQueue<QueuedLog> logQueue = new ConcurrentQueue<QueuedLog>();

    private Process ffmpegProcess;
    private Thread stdoutThread;
    private Thread stderrThread;
    private volatile bool receiverRunning;

    private Texture2D videoTexture;
    private Material runtimePlaybackMaterial;
    private byte[] latestFrameBuffer;
    private byte[] videoUploadBuffer;
    private bool hasNewFrame;
    private bool loggedFirstFrame;
    private bool loggedStartupTimeout;
    private float streamStartRealtime;
    private int frameByteCount;
    private double lastFfmpegWarningRealtime;

    private static readonly int BaseColorMapId = Shader.PropertyToID("_BaseColorMap");
    private static readonly int UnlitColorMapId = Shader.PropertyToID("_UnlitColorMap");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int UnlitColorId = Shader.PropertyToID("_UnlitColor");

    public Texture2D VideoTexture => videoTexture;
    public int VideoWidth => decodedWidth;
    public int VideoHeight => decodedHeight;
    public string LastError => lastError;

    private enum LogLevel
    {
        Info,
        Warning,
        Error
    }

    private readonly struct QueuedLog
    {
        public QueuedLog(LogLevel level, string message)
        {
            Level = level;
            Message = message;
        }

        public LogLevel Level { get; }
        public string Message { get; }
    }

    protected virtual void Start()
    {
        if (startAutomatically)
        {
            StartStream();
        }
    }

    private void Update()
    {
        FlushQueuedLogs();
        UploadLatestFrame();

        if (streamRunning && !loggedFirstFrame && !loggedStartupTimeout && Time.realtimeSinceStartup - streamStartRealtime > 5f)
        {
            loggedStartupTimeout = true;
            EnqueueLog(
                LogLevel.Warning,
                "No decoded frame has arrived after 5 seconds. Check that Edge2 is targeting this PC and that no other process is using the UDP port.");
        }
    }

    protected virtual void OnDisable()
    {
        StopStream();
    }

    protected virtual void OnDestroy()
    {
        StopStream();
    }

    [ContextMenu("Start UDP Stream")]
    public void StartStream()
    {
        StopStream();

        lastError = string.Empty;
        streamRunning = false;
        hasNewFrame = false;
        loggedFirstFrame = false;
        loggedStartupTimeout = false;
        lastFfmpegWarningRealtime = -999d;

        if (forcedWidth <= 0 || forcedHeight <= 0)
        {
            SetError("Forced width/height must be greater than zero.");
            return;
        }

        string resolvedFfmpeg = ResolveFfmpegExecutable();
        if (string.IsNullOrWhiteSpace(resolvedFfmpeg))
        {
            SetError("ffmpeg executable path is empty.");
            return;
        }

        if (!IsExecutableResolvable(resolvedFfmpeg))
        {
            SetError($"ffmpeg executable was not found: {resolvedFfmpeg}");
            return;
        }

        if (!TryGetConfiguredPort(streamUrl, out int listenPort))
        {
            SetError("Could not determine the UDP listen port from the configured stream URL.");
            return;
        }

        if (IsUdpPortAlreadyInUse(listenPort))
        {
            SetError($"UDP port {listenPort} is already in use. Close VLC, ffplay, or another receiver on this port.");
            return;
        }

        decodedWidth = outputWidthOverride > 0 ? outputWidthOverride : forcedWidth;
        decodedHeight = outputHeightOverride > 0 ? outputHeightOverride : forcedHeight;
        frameByteCount = decodedWidth * decodedHeight * 4;
        latestFrameBuffer = new byte[frameByteCount];
        videoUploadBuffer = new byte[frameByteCount];

        EnsureProjectionRenderer();
        EnsureTexture();

        string ffmpegInputUrl = BuildFfmpegListenUrl(listenPort);
        string hardwareDecodeOptions = BuildHardwareDecodeOptions(decodedWidth, decodedHeight);
        string videoFilter = BuildVideoFilter(decodedWidth, decodedHeight);
        int maxDelayMicroseconds = Mathf.Max(0, networkCachingMs) * 1000;
        string arguments =
            $"-hide_banner -nostats -loglevel warning {ffmpegInputOptions} -max_delay {maxDelayMicroseconds} " +
            $"{hardwareDecodeOptions} -f mpegts -i \"{ffmpegInputUrl}\" " +
            $"-map 0:v:0 -an -sn -dn -vf \"{videoFilter}\" -pix_fmt bgra -f rawvideo pipe:1";

        EnqueueLog(LogLevel.Info, $"FFmpeg input URL: {ffmpegInputUrl}");
        if (decodedWidth != forcedWidth || decodedHeight != forcedHeight)
        {
            EnqueueLog(LogLevel.Info, $"Unity upload downscale: {forcedWidth}x{forcedHeight} -> {decodedWidth}x{decodedHeight}.");
        }

        try
        {
            ffmpegProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = resolvedFfmpeg,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = GetProcessWorkingDirectory(resolvedFfmpeg)
                },
                EnableRaisingEvents = true
            };
            ffmpegProcess.Exited += OnFfmpegExited;

            if (!ffmpegProcess.Start())
            {
                SetError("ffmpeg failed to start.");
                return;
            }

            receiverRunning = true;
            stdoutThread = new Thread(ReadFramesLoop)
            {
                IsBackground = true,
                Name = "UDP Video ffmpeg stdout"
            };
            stderrThread = new Thread(ReadFfmpegStderrLoop)
            {
                IsBackground = true,
                Name = "UDP Video ffmpeg stderr"
            };
            stdoutThread.Start();
            stderrThread.Start();

            streamRunning = true;
            streamStartRealtime = Time.realtimeSinceStartup;
            EnqueueLog(LogLevel.Info, "FFmpeg UDP receiver started.");
        }
        catch (Exception ex)
        {
            SetError($"Failed to start ffmpeg receiver: {ex.Message}");
            StopStream();
        }
    }

    [ContextMenu("Stop UDP Stream")]
    public void StopStream()
    {
        streamRunning = false;
        receiverRunning = false;

        if (ffmpegProcess != null)
        {
            try
            {
                if (!ffmpegProcess.HasExited)
                {
                    ffmpegProcess.Kill();
                    ffmpegProcess.WaitForExit(1000);
                }
            }
            catch
            {
                // Ignore teardown races.
            }

            ffmpegProcess.Exited -= OnFfmpegExited;
            ffmpegProcess.Dispose();
            ffmpegProcess = null;
        }

        JoinWorkerThread(stdoutThread);
        JoinWorkerThread(stderrThread);
        stdoutThread = null;
        stderrThread = null;

        latestFrameBuffer = null;
        videoUploadBuffer = null;
        hasNewFrame = false;

        if (runtimePlaybackMaterial != null)
        {
            DestroyUnityObject(runtimePlaybackMaterial);
            runtimePlaybackMaterial = null;
        }
    }

    private void ReadFramesLoop()
    {
        byte[] readBuffer = new byte[frameByteCount];
        try
        {
            Stream stdout = ffmpegProcess.StandardOutput.BaseStream;
            while (receiverRunning)
            {
                if (!ReadExact(stdout, readBuffer, frameByteCount))
                {
                    break;
                }

                FlipRowsInPlace(readBuffer, decodedWidth, decodedHeight);
                lock (frameLock)
                {
                    Buffer.BlockCopy(readBuffer, 0, latestFrameBuffer, 0, frameByteCount);
                    hasNewFrame = true;
                }
            }
        }
        catch (Exception ex)
        {
            if (receiverRunning)
            {
                EnqueueLog(LogLevel.Error, $"ffmpeg stdout read failed: {ex.Message}");
            }
        }
    }

    private void ReadFfmpegStderrLoop()
    {
        try
        {
            while (receiverRunning && ffmpegProcess != null && !ffmpegProcess.StandardError.EndOfStream)
            {
                string line = ffmpegProcess.StandardError.ReadLine();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string lower = line.ToLowerInvariant();
                bool important = lower.Contains("error") ||
                                 lower.Contains("invalid") ||
                                 lower.Contains("non-existing") ||
                                 lower.Contains("no frame") ||
                                 lower.Contains("failed");
                if (!important)
                {
                    continue;
                }

                double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
                if (now - lastFfmpegWarningRealtime > 2f)
                {
                    lastFfmpegWarningRealtime = now;
                    EnqueueLog(LogLevel.Warning, $"[ffmpeg] {line}");
                }
            }
        }
        catch (Exception ex)
        {
            if (receiverRunning)
            {
                EnqueueLog(LogLevel.Warning, $"ffmpeg stderr read failed: {ex.Message}");
            }
        }
    }

    private void UploadLatestFrame()
    {
        if (!hasNewFrame || videoTexture == null || videoUploadBuffer == null)
        {
            return;
        }

        lock (frameLock)
        {
            if (!hasNewFrame || latestFrameBuffer == null)
            {
                return;
            }

            Buffer.BlockCopy(latestFrameBuffer, 0, videoUploadBuffer, 0, frameByteCount);
            hasNewFrame = false;
        }

        videoTexture.LoadRawTextureData(videoUploadBuffer);
        videoTexture.Apply(false, false);
        AssignTextureToTargets(videoTexture);

        if (!loggedFirstFrame)
        {
            loggedFirstFrame = true;
            EnqueueLog(LogLevel.Info, $"First decoded frame received: {decodedWidth}x{decodedHeight}");
        }
    }

    private void EnsureTexture()
    {
        if (videoTexture != null && videoTexture.width == decodedWidth && videoTexture.height == decodedHeight)
        {
            return;
        }

        if (videoTexture != null)
        {
            DestroyUnityObject(videoTexture);
        }

        videoTexture = new Texture2D(decodedWidth, decodedHeight, TextureFormat.BGRA32, false, linearTexture)
        {
            name = "UDP Video Frame",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Repeat
        };
        AssignTextureToTargets(videoTexture);
    }

    private void AssignTextureToTargets(Texture texture)
    {
        if (texture == null)
        {
            return;
        }

        if (targetMaterial != null)
        {
            AssignTexture(targetMaterial, texture);
        }
        else if (targetRenderer != null)
        {
            EnsureRendererMaterial();
            if (runtimePlaybackMaterial != null)
            {
                AssignTexture(runtimePlaybackMaterial, texture);
            }
        }
    }

    private void EnsureRendererMaterial()
    {
        if (targetRenderer == null)
        {
            return;
        }

        if (!createRuntimePlaybackMaterial)
        {
            runtimePlaybackMaterial = targetRenderer.material;
            ConfigureProjectionMaterial(runtimePlaybackMaterial);
            return;
        }

        if (runtimePlaybackMaterial != null)
        {
            return;
        }

        Shader hdrpUnlit = Shader.Find("HDRP/Unlit");
        runtimePlaybackMaterial = hdrpUnlit != null
            ? new Material(hdrpUnlit) { name = "UDPVideo_Runtime" }
            : new Material(Shader.Find("Unlit/Texture")) { name = "UDPVideo_Runtime" };
        ConfigureProjectionMaterial(runtimePlaybackMaterial);
        targetRenderer.material = runtimePlaybackMaterial;
    }

    private void EnsureProjectionRenderer()
    {
        if (!autoFindOrCreateProjectionSphere || targetRenderer != null || targetMaterial != null)
        {
            return;
        }

        GameObject sphereObject = GameObject.Find(projectionSphereName);
        if (sphereObject == null)
        {
            sphereObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphereObject.name = projectionSphereName;
            sphereObject.transform.position = projectionSphereWorldPosition;
            sphereObject.transform.localScale = Vector3.one * Mathf.Max(0.1f, projectionSphereRadius * 2f);

            Collider collider = sphereObject.GetComponent<Collider>();
            if (collider != null)
            {
                DestroyUnityObject(collider);
            }
        }

        targetRenderer = sphereObject.GetComponent<Renderer>();
        EnsureRendererMaterial();
    }

    private void ConfigureProjectionMaterial(Material material)
    {
        if (material == null)
        {
            return;
        }

        if (projectOnInnerSurface)
        {
            SetFloatIfPresent(material, "_DoubleSidedEnable", 1f);
            SetIntIfPresent(material, "_CullMode", (int)UnityEngine.Rendering.CullMode.Off);
            SetIntIfPresent(material, "_CullModeForward", (int)UnityEngine.Rendering.CullMode.Off);
        }

        SetColorIfPresent(material, BaseColorId, Color.white);
        SetColorIfPresent(material, UnlitColorId, Color.white);
    }

    private void AssignTexture(Material material, Texture texture)
    {
        if (material == null || texture == null)
        {
            return;
        }

        int configuredId = Shader.PropertyToID(string.IsNullOrWhiteSpace(targetTextureProperty) ? "_BaseColorMap" : targetTextureProperty);
        if (material.HasProperty(configuredId))
        {
            material.SetTexture(configuredId, texture);
            return;
        }

        if (material.HasProperty(UnlitColorMapId)) material.SetTexture(UnlitColorMapId, texture);
        if (material.HasProperty(BaseColorMapId)) material.SetTexture(BaseColorMapId, texture);
        if (material.HasProperty(MainTexId)) material.SetTexture(MainTexId, texture);
    }

    private string BuildHardwareDecodeOptions(int width, int height)
    {
        switch (decodeBackend)
        {
            case DecodeBackend.D3D11VA:
                return "-hwaccel d3d11va -hwaccel_output_format d3d11";
            case DecodeBackend.CUDA:
                return $"-hwaccel cuda -hwaccel_output_format cuda -c:v h264_cuvid -resize {width}x{height}";
            case DecodeBackend.Auto:
                return "-hwaccel auto";
            default:
                return string.Empty;
        }
    }

    private string BuildVideoFilter(int width, int height)
    {
        switch (decodeBackend)
        {
            case DecodeBackend.D3D11VA:
                return $"hwdownload,format=nv12,scale={width}:{height}:flags=fast_bilinear,format=bgra";
            case DecodeBackend.CUDA:
                return "hwdownload,format=nv12,format=bgra";
            default:
                return $"scale={width}:{height}:flags=fast_bilinear,format=bgra";
        }
    }

    private string ResolveFfmpegExecutable()
    {
        return string.IsNullOrWhiteSpace(ffmpegExecutable) ? "ffmpeg" : ffmpegExecutable.Trim();
    }

    private static string GetProcessWorkingDirectory(string executable)
    {
        if (!string.IsNullOrWhiteSpace(executable) && Path.IsPathRooted(executable))
        {
            string directory = Path.GetDirectoryName(executable);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                return directory;
            }
        }

        return Environment.CurrentDirectory;
    }

    private static bool IsExecutableResolvable(string executablePath)
    {
        if (File.Exists(executablePath))
        {
            return true;
        }

        if (Path.IsPathRooted(executablePath))
        {
            return false;
        }

        string environmentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (string directory in environmentPath.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            string candidate = Path.Combine(directory.Trim(), executablePath);
            if (File.Exists(candidate) || File.Exists(candidate + ".exe"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetConfiguredPort(string url, out int port)
    {
        port = 0;
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        int colon = url.LastIndexOf(':');
        if (colon < 0 || colon + 1 >= url.Length)
        {
            return false;
        }

        int end = url.IndexOfAny(new[] { '/', '?', '&' }, colon + 1);
        string portText = end >= 0 ? url.Substring(colon + 1, end - colon - 1) : url.Substring(colon + 1);
        return int.TryParse(portText, out port) && port > 0 && port <= 65535;
    }

    private static string BuildFfmpegListenUrl(int listenPort)
    {
        return $"udp://0.0.0.0:{listenPort}?fifo_size=1000000&overrun_nonfatal=1&buffer_size=65536";
    }

    private static bool IsUdpPortAlreadyInUse(int port)
    {
        try
        {
            IPEndPoint[] listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners();
            foreach (IPEndPoint endpoint in listeners)
            {
                if (endpoint.Port == port)
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool ReadExact(Stream stream, byte[] buffer, int count)
    {
        int offset = 0;
        while (offset < count)
        {
            int read = stream.Read(buffer, offset, count - offset);
            if (read <= 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    private static void FlipRowsInPlace(byte[] buffer, int width, int height)
    {
        int rowSize = width * 4;
        byte[] temp = new byte[rowSize];
        for (int y = 0; y < height / 2; y++)
        {
            int top = y * rowSize;
            int bottom = (height - 1 - y) * rowSize;
            Buffer.BlockCopy(buffer, top, temp, 0, rowSize);
            Buffer.BlockCopy(buffer, bottom, buffer, top, rowSize);
            Buffer.BlockCopy(temp, 0, buffer, bottom, rowSize);
        }
    }

    private void OnFfmpegExited(object sender, EventArgs args)
    {
        if (!receiverRunning)
        {
            return;
        }

        EnqueueLog(LogLevel.Warning, "ffmpeg receiver exited unexpectedly.");
        receiverRunning = false;
        streamRunning = false;
    }

    private static void JoinWorkerThread(Thread thread)
    {
        if (thread == null || !thread.IsAlive)
        {
            return;
        }

        try
        {
            thread.Join(500);
        }
        catch
        {
            // Ignore teardown races.
        }
    }

    private void SetError(string message)
    {
        lastError = message;
        EnqueueLog(LogLevel.Error, message);
    }

    private void EnqueueLog(LogLevel level, string message)
    {
        logQueue.Enqueue(new QueuedLog(level, $"[UDP Video] {message}"));
    }

    private void FlushQueuedLogs()
    {
        while (logQueue.TryDequeue(out QueuedLog entry))
        {
            switch (entry.Level)
            {
                case LogLevel.Error:
                    Debug.LogError(entry.Message, this);
                    break;
                case LogLevel.Warning:
                    Debug.LogWarning(entry.Message, this);
                    break;
                default:
                    Debug.Log(entry.Message, this);
                    break;
            }
        }
    }

    private static void SetColorIfPresent(Material material, int propertyId, Color color)
    {
        if (material != null && material.HasProperty(propertyId))
        {
            material.SetColor(propertyId, color);
        }
    }

    private static void SetFloatIfPresent(Material material, string property, float value)
    {
        if (material != null && material.HasProperty(property))
        {
            material.SetFloat(property, value);
        }
    }

    private static void SetIntIfPresent(Material material, string property, int value)
    {
        if (material != null && material.HasProperty(property))
        {
            material.SetInt(property, value);
        }
    }

    private static void DestroyUnityObject(UnityEngine.Object obj)
    {
        if (obj == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(obj);
        }
        else
        {
            DestroyImmediate(obj);
        }
    }
}
