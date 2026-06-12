using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Receives the Edge2/X5 WebRTC H.264 stream in Unity, stitches the incoming
/// side-by-side dual-fisheye texture on the GPU, and displays the resulting
/// equirectangular panorama as an immersive sky dome.
/// </summary>
[AddComponentMenu("VR Locomotion/Unity WebRTC Skybox Receiver")]
public sealed class UnityWebRtcSkyboxReceiver : MonoBehaviour
{
    [Header("Signaling")]
    [Tooltip("WebSocket signaling URL served by Tools/LocalWebRtcViewer/webrtc_signal_server.py.")]
    public string signalingUrl = "ws://192.168.68.50:8765";

    [Tooltip("Connect automatically when Play mode starts.")]
    public bool connectOnStart = true;

    [Tooltip("Reconnect signaling automatically if the socket closes.")]
    public bool autoReconnect = true;

    [Tooltip("Delay before reconnecting signaling or asking for a fresh offer.")]
    [Range(0.2f, 10f)]
    public float reconnectDelaySeconds = 1.5f;

    [Header("WebRTC")]
    [Tooltip("Flip the received WebRTC video before Unity exposes it as a Texture.")]
    public bool flipReceivedVideoVertically = true;

    [Tooltip("Ask the sender for a fresh offer if ICE/peer connection drops.")]
    public bool requestFreshOfferOnDisconnect = true;

    [Header("GPU Stitch")]
    public ComputeShader stitchComputeShader;

    [Range(512, 8192)]
    public int equirectWidth = 3840;

    [Range(256, 4096)]
    public int equirectHeight = 1920;

    [Tooltip("Per-lens fisheye FOV in degrees. Insta360 X5 is around 200 degrees.")]
    [Range(120f, 240f)]
    public float lensFovDegrees = 200f;

    [Tooltip("Flip the rear lens X axis. Usually needed for X5 side-by-side fisheye.")]
    public bool backLensFlipX = true;

    [Tooltip("Flip the full incoming dual-fisheye source horizontally before sampling.")]
    public bool sourceFlipX = false;

    [Tooltip("Flip the full incoming dual-fisheye source vertically before sampling.")]
    public bool sourceFlipY = false;

    [Tooltip("Rotate the panorama around the vertical axis after stitching.")]
    [Range(-180f, 180f)]
    public float yawOffsetDegrees = 0f;

    [Header("Display")]
    [Tooltip("Inside-out sphere used as a reliable HDRP sky dome.")]
    public Renderer skyDomeRenderer;

    [Tooltip("Also assign the stitched panorama to RenderSettings.skybox as a legacy panoramic skybox.")]
    public bool alsoSetRenderSettingsSkybox = true;

    [Tooltip("Runtime sky dome radius if the scene does not assign one.")]
    public float autoSkyDomeRadius = 50f;

    [Header("Debug")]
    public bool drawDebugOverlay = true;
    public bool drawTexturePreviews = true;
    public Color noSignalColor = new Color(0.02f, 0.025f, 0.035f, 1f);
    [Tooltip("Only run the expensive 4K GPU stitch when Unity.WebRTC reports a fresh video frame. Disabled by default because some Unity.WebRTC versions update the texture without firing the callback every frame.")]
    public bool stitchOnlyOnNewVideoFrame = false;
    [Tooltip("Seconds between WebRTC stats polls. Lower values update the overlay faster but add small CPU/GC overhead.")]
    [Range(0.25f, 5f)] public float statsPollIntervalSeconds = 1f;
    [Tooltip("Send Unity receiver WebRTC stats back to the local signaling log for stutter diagnosis.")]
    public bool sendStatsToSignaling = true;
    [Tooltip("Decoded-frame stall duration that counts as a visible freeze in the debug overlay.")]
    [Range(50f, 2000f)] public float freezeThresholdMs = 250f;

    [Header("Status")]
    [SerializeField] private string status = "Idle";
    [SerializeField] private string peerState = "none";
    [SerializeField] private string iceState = "none";
    [SerializeField] private int sourceWidth;
    [SerializeField] private int sourceHeight;
    [SerializeField] private float stitchFps;
    [SerializeField] private int reconnectAttempts;
    [SerializeField] private float receivedBitrateMbps;
    [SerializeField] private float decodedFps;
    [SerializeField] private uint framesDecoded;
    [SerializeField] private uint framesDropped;
    [SerializeField] private int packetsLost;
    [SerializeField] private float packetLossPercent;
    [SerializeField] private float networkJitterMs;
    [SerializeField] private float jitterBufferDelayMs;
    [SerializeField] private float decodeTimeMs;
    [SerializeField] private float rttMs;
    [SerializeField] private int freezeCount;
    [SerializeField] private float currentFreezeMs;

    private readonly ConcurrentQueue<SignalMessage> _incomingSignals = new ConcurrentQueue<SignalMessage>();
    private readonly ConcurrentQueue<string> _mainThreadLogs = new ConcurrentQueue<string>();
    private readonly Queue<SignalMessage> _pendingRemoteCandidates = new Queue<SignalMessage>();
    private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

    private CancellationTokenSource _cancellation;
    private ClientWebSocket _socket;
    private Task _socketTask;
    private Coroutine _webRtcUpdateCoroutine;
    private RTCPeerConnection _peerConnection;
    private MediaStream _receiveStream;
    private VideoStreamTrack _remoteVideoTrack;
    private Texture _remoteTexture;
    private RenderTexture _equirectTexture;
    private Material _skyDomeMaterial;
    private Material _legacySkyboxMaterial;
    private int _kernel = -1;
    private bool _hasLoggedFirstFrame;
    private bool _hasLoggedFirstStitch;
    private bool _renegotiateRequested;
    private bool _hasEverConnected;
    private bool _newVideoFrameAvailable;
    private bool _canAddRemoteCandidates;
    private int _ignoredRemoteCandidateCount;
    private float _nextRenegotiateTime;
    private float _lastOfferReceivedTime = -1000f;
    private float _lastFreshOfferRequestTime = -1000f;
    private float _lastFpsTime;
    private int _stitchedFramesSinceLastSample;
    private bool _statsInFlight;
    private float _nextStatsTime;
    private double _lastStatsTime;
    private ulong _lastBytesReceived;
    private uint _lastFramesDecoded;
    private uint _lastPacketsReceived;
    private int _lastPacketsLost;
    private double _lastTotalDecodeTime;
    private double _lastJitterBufferDelay;
    private ulong _lastJitterBufferEmittedCount;
    private uint _lastObservedDecodedFrames;
    private float _lastFrameAdvanceTime;
    private bool _wasFrozen;

    private static readonly int SrcId = Shader.PropertyToID("_SrcDualFisheye");
    private static readonly int DstId = Shader.PropertyToID("_DstEquirect");
    private static readonly int SrcSizeId = Shader.PropertyToID("_SrcSize");
    private static readonly int DstSizeId = Shader.PropertyToID("_DstSize");
    private static readonly int FovDegId = Shader.PropertyToID("_FovDeg");
    private static readonly int BackFlipXId = Shader.PropertyToID("_BackFlipX");
    private static readonly int SourceFlipXId = Shader.PropertyToID("_SourceFlipX");
    private static readonly int SourceFlipYId = Shader.PropertyToID("_SourceFlipY");
    private static readonly int YawOffsetDegId = Shader.PropertyToID("_YawOffsetDeg");
    private static readonly int BaseColorMapId = Shader.PropertyToID("_BaseColorMap");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int UnlitColorMapId = Shader.PropertyToID("_UnlitColorMap");
    private static readonly int UnlitColorId = Shader.PropertyToID("_UnlitColor");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private const float StartupRenegotiateGraceSeconds = 5f;

    [Serializable]
    private sealed class SignalMessage
    {
        public string type;
        public string role;
        public string sdp;
        public string candidate;
        public string sdpMid;
        public int sdpMLineIndex;
        public string state;
        public string ice;
        public int width;
        public int height;
        public float bitrateMbps;
        public float fps;
        public uint dropped;
        public int lost;
        public float lossPct;
        public float jitterMs;
        public float jitterBufferMs;
        public float decodeMs;
        public float rttMs;
        public int freezeCount;
        public float freezeMs;
    }

    public Texture SourceTexture => _remoteTexture;
    public RenderTexture StitchedEquirectTexture => _equirectTexture;

    private void Reset()
    {
        LoadDefaultComputeShader();
    }

    private void Awake()
    {
        LoadDefaultComputeShader();
        EnsureOutputTexture();
        EnsureSkyDome();
        ApplyDisplayTexture();
    }

    private void Start()
    {
        _lastFpsTime = Time.realtimeSinceStartup;
        if (connectOnStart)
        {
            Connect();
        }
    }

    private void Update()
    {
        FlushLogs();
        ProcessIncomingSignals();
        HandleRenegotiationRequest();
        StitchLatestFrame();
        UpdateStitchFps();
        MaybePollStats();
        TrackFrameFreeze();
    }

    private void OnDisable()
    {
        Disconnect();
    }

    private void OnDestroy()
    {
        Disconnect();
        ReleaseRuntimeResources();
    }

    [ContextMenu("Connect WebRTC")]
    public void Connect()
    {
        Disconnect();

        status = "Connecting signaling";
        _hasEverConnected = false;
        _lastOfferReceivedTime = -1000f;
        _lastFreshOfferRequestTime = -1000f;
        _lastObservedDecodedFrames = 0;
        _lastFrameAdvanceTime = Time.realtimeSinceStartup;
        _wasFrozen = false;
        _newVideoFrameAvailable = false;
        freezeCount = 0;
        currentFreezeMs = 0f;
        _cancellation = new CancellationTokenSource();
        VideoStreamTrack.NeedReceivedVideoFlipVertically = flipReceivedVideoVertically;
        _webRtcUpdateCoroutine = StartCoroutine(WebRTC.Update());
        _socketTask = Task.Run(() => SignalingLoopAsync(_cancellation.Token));
    }

    [ContextMenu("Disconnect WebRTC")]
    public void Disconnect()
    {
        _renegotiateRequested = false;
        _hasEverConnected = false;

        if (_cancellation != null)
        {
            _cancellation.Cancel();
            _cancellation.Dispose();
            _cancellation = null;
        }

        if (_socket != null)
        {
            try
            {
                _socket.Abort();
                _socket.Dispose();
            }
            catch
            {
                // Ignore shutdown races.
            }
            _socket = null;
        }

        ClosePeerConnection();

        if (_webRtcUpdateCoroutine != null)
        {
            StopCoroutine(_webRtcUpdateCoroutine);
            _webRtcUpdateCoroutine = null;
        }

        status = "Disconnected";
    }

    [ContextMenu("Request Fresh Offer")]
    public void RequestFreshOffer()
    {
        _renegotiateRequested = true;
        _nextRenegotiateTime = Time.realtimeSinceStartup;
    }

    private async Task SignalingLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using (var socket = new ClientWebSocket())
                {
                    _socket = socket;
                    await socket.ConnectAsync(new Uri(signalingUrl), token);
                    reconnectAttempts = 0;
                    EnqueueLog("Connected signaling: " + signalingUrl);
                    await SendSignalAsync(new SignalMessage { type = "register", role = "unity-viewer" }, token);
                    EnqueueLog("Registered viewer; signaling server will request a fresh offer");
                    await ReceiveLoopAsync(socket, token);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                reconnectAttempts++;
                EnqueueLog("Signaling error: " + ex.Message);
            }
            finally
            {
                if (_socket != null)
                {
                    try { _socket.Dispose(); } catch { }
                    _socket = null;
                }
            }

            if (!autoReconnect || token.IsCancellationRequested)
            {
                break;
            }

            status = "Reconnecting signaling";
            await Task.Delay(TimeSpan.FromSeconds(Mathf.Max(0.2f, reconnectDelaySeconds)), token);
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken token)
    {
        var buffer = new byte[64 * 1024];

        while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            using (var messageBytes = new System.IO.MemoryStream())
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    messageBytes.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                string json = Encoding.UTF8.GetString(messageBytes.ToArray());
                if (string.IsNullOrWhiteSpace(json))
                {
                    continue;
                }

                try
                {
                    var message = JsonUtility.FromJson<SignalMessage>(json);
                    if (message != null && !string.IsNullOrWhiteSpace(message.type))
                    {
                        _incomingSignals.Enqueue(message);
                    }
                }
                catch (Exception ex)
                {
                    EnqueueLog("Signal JSON parse failed: " + ex.Message);
                }
            }
        }
    }

    private async Task SendSignalAsync(SignalMessage message, CancellationToken token)
    {
        ClientWebSocket socket = _socket;
        if (socket == null || socket.State != WebSocketState.Open)
        {
            return;
        }

        string json = JsonUtility.ToJson(message);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        await _sendLock.WaitAsync(token);
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private void SendSignal(SignalMessage message)
    {
        CancellationToken token = _cancellation != null ? _cancellation.Token : CancellationToken.None;
        _ = SendSignalAsync(message, token);
    }

    private void ProcessIncomingSignals()
    {
        while (_incomingSignals.TryDequeue(out SignalMessage message))
        {
            switch (message.type)
            {
                case "offer":
                    status = "Offer received";
                    Debug.Log("[Unity WebRTC Skybox] Offer received from Edge2 sender.", this);
                    StartCoroutine(HandleOffer(message.sdp));
                    break;
                case "candidate":
                    AddRemoteCandidate(message);
                    break;
            }
        }
    }

    private IEnumerator HandleOffer(string sdp)
    {
        if (string.IsNullOrWhiteSpace(sdp))
        {
            yield break;
        }

        _lastOfferReceivedTime = Time.realtimeSinceStartup;
        _renegotiateRequested = false;
        ClosePeerConnection();
        CreatePeerConnection();

        status = "Applying offer";
        var offer = new RTCSessionDescription
        {
            type = RTCSdpType.Offer,
            sdp = sdp
        };

        var remoteOp = _peerConnection.SetRemoteDescription(ref offer);
        yield return remoteOp;
        if (remoteOp.IsError)
        {
            status = "SetRemoteDescription failed";
            Debug.LogError("[Unity WebRTC Skybox] SetRemoteDescription failed: " + remoteOp.Error.message, this);
            yield break;
        }
        var answerOp = _peerConnection.CreateAnswer();
        yield return answerOp;
        if (answerOp.IsError)
        {
            status = "CreateAnswer failed";
            Debug.LogError("[Unity WebRTC Skybox] CreateAnswer failed: " + answerOp.Error.message, this);
            yield break;
        }

        RTCSessionDescription answer = answerOp.Desc;
        var localOp = _peerConnection.SetLocalDescription(ref answer);
        yield return localOp;
        if (localOp.IsError)
        {
            status = "SetLocalDescription failed";
            Debug.LogError("[Unity WebRTC Skybox] SetLocalDescription failed: " + localOp.Error.message, this);
            yield break;
        }

        _canAddRemoteCandidates = true;
        DrainPendingRemoteCandidates();

        SendSignal(new SignalMessage
        {
            type = "answer",
            sdp = answer.sdp
        });
        status = "Answer sent";
        Debug.Log("[Unity WebRTC Skybox] Answer sent to Edge2 sender.", this);
        DrainPendingRemoteCandidates();
    }

    private void CreatePeerConnection()
    {
        _receiveStream = new MediaStream();
        _receiveStream.OnAddTrack = e =>
        {
            if (e.Track is VideoStreamTrack videoTrack)
            {
                AttachVideoTrack(videoTrack);
            }
        };

        _peerConnection = new RTCPeerConnection();
        _peerConnection.OnIceCandidate = candidate =>
        {
            if (candidate == null)
            {
                return;
            }

            SendSignal(new SignalMessage
            {
                type = "candidate",
                candidate = candidate.Candidate,
                sdpMid = candidate.SdpMid,
                sdpMLineIndex = candidate.SdpMLineIndex.GetValueOrDefault()
            });
        };

        _peerConnection.OnIceConnectionChange = state =>
        {
            iceState = state.ToString();
            if (state == RTCIceConnectionState.Connected ||
                state == RTCIceConnectionState.Completed)
            {
                _hasEverConnected = true;
                _renegotiateRequested = false;
            }
            else if (state == RTCIceConnectionState.Disconnected ||
                     state == RTCIceConnectionState.Failed ||
                     state == RTCIceConnectionState.Closed)
            {
                MaybeScheduleFreshOffer("ice " + state);
            }
        };

        _peerConnection.OnConnectionStateChange = state =>
        {
            peerState = state.ToString();
            if (state == RTCPeerConnectionState.Connected)
            {
                _hasEverConnected = true;
                _renegotiateRequested = false;
            }
            else if (state == RTCPeerConnectionState.Disconnected ||
                     state == RTCPeerConnectionState.Failed ||
                     state == RTCPeerConnectionState.Closed)
            {
                MaybeScheduleFreshOffer("peer " + state);
            }
        };

        _peerConnection.OnTrack = e =>
        {
            if (e.Track.Kind == TrackKind.Video)
            {
                _receiveStream.AddTrack(e.Track);
            }
        };

        var init = new RTCRtpTransceiverInit
        {
            direction = RTCRtpTransceiverDirection.RecvOnly
        };
        _peerConnection.AddTransceiver(TrackKind.Video, init);
        peerState = _peerConnection.ConnectionState.ToString();
        iceState = _peerConnection.IceConnectionState.ToString();
    }

    private void AttachVideoTrack(VideoStreamTrack videoTrack)
    {
        if (_remoteVideoTrack == videoTrack)
        {
            return;
        }

        _remoteVideoTrack = videoTrack;
        _remoteTexture = videoTrack.Texture;
        status = "Video track attached";

        videoTrack.OnVideoReceived += texture =>
        {
            _remoteTexture = texture;
            sourceWidth = texture != null ? texture.width : 0;
            sourceHeight = texture != null ? texture.height : 0;
            if (texture != null)
            {
                _hasEverConnected = true;
                _newVideoFrameAvailable = true;
            }
            if (!_hasLoggedFirstFrame)
            {
                _hasLoggedFirstFrame = true;
                Debug.Log($"[Unity WebRTC Skybox] First WebRTC frame: {sourceWidth}x{sourceHeight}, texture={texture.GetType().Name}", this);
            }
        };
    }

    private void AddRemoteCandidate(SignalMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.candidate))
        {
            return;
        }

        if (_peerConnection == null || !_canAddRemoteCandidates)
        {
            if (_pendingRemoteCandidates.Count < 128)
            {
                _pendingRemoteCandidates.Enqueue(message);
            }
            return;
        }

        var init = new RTCIceCandidateInit
        {
            candidate = message.candidate,
            sdpMid = message.sdpMid,
            sdpMLineIndex = message.sdpMLineIndex
        };

        using (var candidate = new RTCIceCandidate(init))
        {
            bool added = _peerConnection.AddIceCandidate(candidate);
            if (!added)
            {
                _ignoredRemoteCandidateCount++;
                if (_ignoredRemoteCandidateCount == 1)
                {
                    Debug.Log("[Unity WebRTC Skybox] Ignored one or more remote ICE candidates that Unity.WebRTC rejected. This is usually harmless if the connection reaches Connected.", this);
                }
            }
        }
    }

    private void DrainPendingRemoteCandidates()
    {
        while (_pendingRemoteCandidates.Count > 0 && _peerConnection != null && _canAddRemoteCandidates)
        {
            AddRemoteCandidate(_pendingRemoteCandidates.Dequeue());
        }
    }

    private void MaybeScheduleFreshOffer(string reason)
    {
        if (!requestFreshOfferOnDisconnect || !_hasEverConnected)
        {
            return;
        }

        float now = Time.realtimeSinceStartup;
        if (now - _lastOfferReceivedTime < StartupRenegotiateGraceSeconds)
        {
            return;
        }

        float minimumRetryInterval = Mathf.Max(3f, reconnectDelaySeconds * 2f);
        if (now - _lastFreshOfferRequestTime < minimumRetryInterval)
        {
            return;
        }

        _renegotiateRequested = true;
        _nextRenegotiateTime = now + reconnectDelaySeconds;
        _lastFreshOfferRequestTime = now;
        status = "Will request fresh offer: " + reason;
    }

    private void HandleRenegotiationRequest()
    {
        if (!_renegotiateRequested || Time.realtimeSinceStartup < _nextRenegotiateTime)
        {
            return;
        }

        _renegotiateRequested = false;
        ClosePeerConnection();
        SendSignal(new SignalMessage { type = "viewer-ready" });
        status = "Requested fresh offer";
    }

    private void StitchLatestFrame()
    {
        Texture source = _remoteTexture;
        if (source == null || stitchComputeShader == null)
        {
            return;
        }

        if (stitchOnlyOnNewVideoFrame && !_newVideoFrameAvailable)
        {
            return;
        }

        EnsureOutputTexture();
        if (_kernel < 0)
        {
            _kernel = stitchComputeShader.FindKernel("DualFisheyeToEquirect");
        }

        sourceWidth = source.width;
        sourceHeight = source.height;

        stitchComputeShader.SetTexture(_kernel, SrcId, source);
        stitchComputeShader.SetTexture(_kernel, DstId, _equirectTexture);
        stitchComputeShader.SetInts(SrcSizeId, source.width, source.height);
        stitchComputeShader.SetInts(DstSizeId, _equirectTexture.width, _equirectTexture.height);
        stitchComputeShader.SetFloat(FovDegId, lensFovDegrees);
        stitchComputeShader.SetFloat(BackFlipXId, backLensFlipX ? 1f : 0f);
        stitchComputeShader.SetFloat(SourceFlipXId, sourceFlipX ? 1f : 0f);
        stitchComputeShader.SetFloat(SourceFlipYId, sourceFlipY ? 1f : 0f);
        stitchComputeShader.SetFloat(YawOffsetDegId, yawOffsetDegrees);

        int groupsX = (_equirectTexture.width + 7) / 8;
        int groupsY = (_equirectTexture.height + 7) / 8;
        stitchComputeShader.Dispatch(_kernel, groupsX, groupsY, 1);
        _newVideoFrameAvailable = false;
        _stitchedFramesSinceLastSample++;

        if (!_hasLoggedFirstStitch)
        {
            _hasLoggedFirstStitch = true;
            Debug.Log($"[Unity WebRTC Skybox] First GPU stitch frame: {source.width}x{source.height} -> {_equirectTexture.width}x{_equirectTexture.height}", this);
        }
    }

    private void UpdateStitchFps()
    {
        float now = Time.realtimeSinceStartup;
        float delta = now - _lastFpsTime;
        if (delta < 1f)
        {
            return;
        }

        stitchFps = _stitchedFramesSinceLastSample / Mathf.Max(0.001f, delta);
        _stitchedFramesSinceLastSample = 0;
        _lastFpsTime = now;
    }

    private void MaybePollStats()
    {
        if (_peerConnection == null || _statsInFlight || Time.realtimeSinceStartup < _nextStatsTime)
        {
            return;
        }

        _nextStatsTime = Time.realtimeSinceStartup + Mathf.Max(0.25f, statsPollIntervalSeconds);
        StartCoroutine(PollStatsOnce());
    }

    private IEnumerator PollStatsOnce()
    {
        if (_peerConnection == null)
        {
            yield break;
        }

        _statsInFlight = true;
        RTCStatsReportAsyncOperation operation = _peerConnection.GetStats();
        yield return operation;

        try
        {
            if (!operation.IsError && operation.Value != null)
            {
                using (RTCStatsReport report = operation.Value)
                {
                    ApplyStatsReport(report);
                }
            }
        }
        finally
        {
            _statsInFlight = false;
        }
    }

    private void ApplyStatsReport(RTCStatsReport report)
    {
        double now = Time.realtimeSinceStartupAsDouble;

        foreach (RTCStats stat in report.Stats.Values)
        {
            if (stat is RTCInboundRTPStreamStats inbound && inbound.kind == "video")
            {
                double deltaSeconds = Math.Max(0.001, now - _lastStatsTime);
                ulong bytes = inbound.bytesReceived;
                uint packetsReceived = inbound.packetsReceived;
                int lost = inbound.packetsLost;
                uint decoded = inbound.framesDecoded;
                uint dropped = inbound.framesDropped;
                double totalDecode = inbound.totalDecodeTime;
                double jitterDelay = inbound.jitterBufferDelay;
                ulong jitterEmitted = inbound.jitterBufferEmittedCount;

                ulong deltaBytes = bytes >= _lastBytesReceived ? bytes - _lastBytesReceived : 0;
                uint deltaFrames = decoded >= _lastFramesDecoded ? decoded - _lastFramesDecoded : 0;
                uint deltaPacketsReceived = packetsReceived >= _lastPacketsReceived ? packetsReceived - _lastPacketsReceived : 0;
                int deltaLost = lost >= _lastPacketsLost ? lost - _lastPacketsLost : 0;
                double deltaDecode = Math.Max(0.0, totalDecode - _lastTotalDecodeTime);
                double deltaJitterDelay = Math.Max(0.0, jitterDelay - _lastJitterBufferDelay);
                ulong deltaJitterEmitted = jitterEmitted >= _lastJitterBufferEmittedCount
                    ? jitterEmitted - _lastJitterBufferEmittedCount
                    : 0;

                receivedBitrateMbps = (float)(deltaBytes * 8.0 / deltaSeconds / 1_000_000.0);
                decodedFps = (float)(deltaFrames / deltaSeconds);
                framesDecoded = decoded;
                framesDropped = dropped;
                packetsLost = lost;
                packetLossPercent = (deltaLost + deltaPacketsReceived) > 0
                    ? (float)(deltaLost * 100.0 / (deltaLost + deltaPacketsReceived))
                    : 0f;
                networkJitterMs = (float)(inbound.jitter * 1000.0);
                decodeTimeMs = deltaFrames > 0 ? (float)(deltaDecode * 1000.0 / deltaFrames) : 0f;
                jitterBufferDelayMs = deltaJitterEmitted > 0 ? (float)(deltaJitterDelay * 1000.0 / deltaJitterEmitted) : 0f;

                _lastStatsTime = now;
                _lastBytesReceived = bytes;
                _lastFramesDecoded = decoded;
                _lastPacketsReceived = packetsReceived;
                _lastPacketsLost = lost;
                _lastTotalDecodeTime = totalDecode;
                _lastJitterBufferDelay = jitterDelay;
                _lastJitterBufferEmittedCount = jitterEmitted;
            }
            else if (stat is RTCIceCandidatePairStats pair && pair.nominated && pair.state == "succeeded")
            {
                rttMs = (float)(pair.currentRoundTripTime * 1000.0);
            }
        }

        if (sendStatsToSignaling)
        {
            SendSignal(new SignalMessage
            {
                type = "viewer-stats",
                role = "unity-viewer",
                state = peerState,
                ice = iceState,
                width = sourceWidth,
                height = sourceHeight,
                bitrateMbps = receivedBitrateMbps,
                fps = decodedFps,
                dropped = framesDropped,
                lost = packetsLost,
                lossPct = packetLossPercent,
                jitterMs = networkJitterMs,
                jitterBufferMs = jitterBufferDelayMs,
                decodeMs = decodeTimeMs,
                rttMs = rttMs,
                freezeCount = freezeCount,
                freezeMs = currentFreezeMs
            });
        }
    }

    private void TrackFrameFreeze()
    {
        bool connected = peerState == RTCPeerConnectionState.Connected.ToString() ||
                         iceState == RTCIceConnectionState.Connected.ToString() ||
                         iceState == RTCIceConnectionState.Completed.ToString();
        if (!connected || framesDecoded == 0)
        {
            currentFreezeMs = 0f;
            _wasFrozen = false;
            _lastObservedDecodedFrames = framesDecoded;
            _lastFrameAdvanceTime = Time.realtimeSinceStartup;
            return;
        }

        if (framesDecoded != _lastObservedDecodedFrames)
        {
            _lastObservedDecodedFrames = framesDecoded;
            _lastFrameAdvanceTime = Time.realtimeSinceStartup;
            currentFreezeMs = 0f;
            _wasFrozen = false;
            return;
        }

        currentFreezeMs = (Time.realtimeSinceStartup - _lastFrameAdvanceTime) * 1000f;
        if (!_wasFrozen && currentFreezeMs > freezeThresholdMs)
        {
            freezeCount++;
            _wasFrozen = true;
        }
    }

    private void EnsureOutputTexture()
    {
        int width = Mathf.Max(512, equirectWidth);
        int height = Mathf.Max(256, equirectHeight);
        if (_equirectTexture != null && _equirectTexture.width == width && _equirectTexture.height == height)
        {
            return;
        }

        if (_equirectTexture != null)
        {
            _equirectTexture.Release();
            Destroy(_equirectTexture);
        }

        _equirectTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
        {
            name = "UnityWebRTC_StitchedEquirect",
            enableRandomWrite = true,
            filterMode = FilterMode.Bilinear,
            wrapModeU = TextureWrapMode.Repeat,
            wrapModeV = TextureWrapMode.Clamp
        };
        _equirectTexture.Create();
        ApplyDisplayTexture();
    }

    private void EnsureSkyDome()
    {
        if (skyDomeRenderer == null)
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "WebRTC GPU Stitched Sky Dome";
            sphere.transform.SetParent(transform, false);
            sphere.transform.localPosition = Vector3.zero;
            sphere.transform.localRotation = Quaternion.identity;
            sphere.transform.localScale = Vector3.one * Mathf.Max(1f, autoSkyDomeRadius * 2f);
            Destroy(sphere.GetComponent<Collider>());
            skyDomeRenderer = sphere.GetComponent<Renderer>();
        }

        Shader unlit = Shader.Find("HDRP/Unlit");
        if (unlit == null)
        {
            Debug.LogError("[Unity WebRTC Skybox] HDRP/Unlit shader not found.", this);
            return;
        }

        if (_skyDomeMaterial == null)
        {
            _skyDomeMaterial = new Material(unlit) { name = "UnityWebRTC_SkyDome_Runtime" };
            _skyDomeMaterial.SetFloat("_DoubleSidedEnable", 1f);
            _skyDomeMaterial.SetInt("_CullMode", (int)CullMode.Off);
            _skyDomeMaterial.SetInt("_CullModeForward", (int)CullMode.Off);
            _skyDomeMaterial.SetColor(BaseColorId, Color.white);
            _skyDomeMaterial.SetColor(UnlitColorId, Color.white);
            HDMaterial.ValidateMaterial(_skyDomeMaterial);
        }

        skyDomeRenderer.sharedMaterial = _skyDomeMaterial;
        ApplyDisplayTexture();
    }

    private void ApplyDisplayTexture()
    {
        if (_equirectTexture == null)
        {
            return;
        }

        if (_skyDomeMaterial != null)
        {
            _skyDomeMaterial.SetTexture(BaseColorMapId, _equirectTexture);
            _skyDomeMaterial.SetTexture(UnlitColorMapId, _equirectTexture);
            _skyDomeMaterial.SetColor(BaseColorId, Color.white);
            _skyDomeMaterial.SetColor(UnlitColorId, Color.white);
        }

        if (alsoSetRenderSettingsSkybox)
        {
            EnsureLegacySkyboxMaterial();
            if (_legacySkyboxMaterial != null)
            {
                _legacySkyboxMaterial.SetTexture(MainTexId, _equirectTexture);
                RenderSettings.skybox = _legacySkyboxMaterial;
                DynamicGI.UpdateEnvironment();
            }
        }
    }

    private void EnsureLegacySkyboxMaterial()
    {
        if (_legacySkyboxMaterial != null)
        {
            return;
        }

        Shader panoramic = Shader.Find("Skybox/Panoramic");
        if (panoramic == null)
        {
            return;
        }

        _legacySkyboxMaterial = new Material(panoramic) { name = "UnityWebRTC_PanoramicSkybox_Runtime" };
        _legacySkyboxMaterial.SetFloat("_ImageType", 0f);
        _legacySkyboxMaterial.SetFloat("_Mapping", 1f);
        _legacySkyboxMaterial.SetColor("_Tint", Color.white);
        _legacySkyboxMaterial.SetFloat("_Exposure", 1f);
        _legacySkyboxMaterial.SetFloat("_Rotation", 0f);
    }

    private void LoadDefaultComputeShader()
    {
        if (stitchComputeShader != null)
        {
            return;
        }

#if UNITY_EDITOR
        string[] guids = UnityEditor.AssetDatabase.FindAssets("DualFisheyeStitch t:ComputeShader");
        if (guids != null && guids.Length > 0)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
            stitchComputeShader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
        }
#endif
    }

    private void ClosePeerConnection()
    {
        if (_remoteVideoTrack != null)
        {
            try { _remoteVideoTrack.Dispose(); } catch { }
            _remoteVideoTrack = null;
        }

        _remoteTexture = null;
        _hasLoggedFirstFrame = false;
        _hasLoggedFirstStitch = false;
        _canAddRemoteCandidates = false;
        _ignoredRemoteCandidateCount = 0;
        _pendingRemoteCandidates.Clear();

        if (_receiveStream != null)
        {
            try { _receiveStream.Dispose(); } catch { }
            _receiveStream = null;
        }

        if (_peerConnection != null)
        {
            try { _peerConnection.Close(); } catch { }
            try { _peerConnection.Dispose(); } catch { }
            _peerConnection = null;
        }

        peerState = "none";
        iceState = "none";
    }

    private void ReleaseRuntimeResources()
    {
        if (_equirectTexture != null)
        {
            _equirectTexture.Release();
            Destroy(_equirectTexture);
            _equirectTexture = null;
        }

        if (_skyDomeMaterial != null)
        {
            Destroy(_skyDomeMaterial);
            _skyDomeMaterial = null;
        }

        if (_legacySkyboxMaterial != null)
        {
            Destroy(_legacySkyboxMaterial);
            _legacySkyboxMaterial = null;
        }
    }

    private void FlushLogs()
    {
        while (_mainThreadLogs.TryDequeue(out string message))
        {
            status = message;
            Debug.Log("[Unity WebRTC Skybox] " + message, this);
        }
    }

    private void EnqueueLog(string message)
    {
        _mainThreadLogs.Enqueue(message);
    }

    private void OnGUI()
    {
        if (!drawDebugOverlay)
        {
            return;
        }

        const int margin = 14;
        int panelWidth = Mathf.Min(560, Screen.width - margin * 2);
        var rect = new Rect(margin, margin, panelWidth, 212);
        GUI.color = new Color(0f, 0f, 0f, 0.68f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x + 10, rect.y + 8, rect.width - 20, rect.height - 16),
            "Unity WebRTC Skybox\n" +
            $"status={status}\n" +
            $"peer={peerState} ice={iceState} reconnects={reconnectAttempts}\n" +
            $"source={sourceWidth}x{sourceHeight} stitched={equirectWidth}x{equirectHeight}\n" +
            $"bitrate={receivedBitrateMbps:F2}Mbps decoded_fps={decodedFps:F1} stitch_fps={stitchFps:F1}\n" +
            $"frames={framesDecoded} dropped={framesDropped} lost={packetsLost} loss={packetLossPercent:F2}%\n" +
            $"jitter={networkJitterMs:F1}ms rtt={rttMs:F1}ms decode={decodeTimeMs:F2}ms jb={jitterBufferDelayMs:F1}ms freeze={freezeCount}/{currentFreezeMs:F0}ms\n" +
            $"signal={signalingUrl}");

        if (!drawTexturePreviews)
        {
            return;
        }

        int previewWidth = Mathf.Min(420, Screen.width / 4);
        int rawHeight = previewWidth / 2;
        var rawRect = new Rect(margin, rect.yMax + 10, previewWidth, rawHeight);
        var stitchRect = new Rect(margin + previewWidth + 10, rect.yMax + 10, previewWidth, rawHeight);

        DrawPreview(rawRect, "WebRTC raw dual-fisheye", _remoteTexture);
        DrawPreview(stitchRect, "GPU stitched equirect", _equirectTexture);
    }

    private static void DrawPreview(Rect rect, string label, Texture texture)
    {
        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        if (texture != null)
        {
            GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, false);
        }
        GUI.Label(new Rect(rect.x + 6, rect.y + 4, rect.width - 12, 22), label);
    }
}
