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
/// Receives the native RICOH THETA Z1 USB live stream from the Edge2 WebRTC
/// sender and displays the already-stitched equirectangular 360 texture.
/// </summary>
[AddComponentMenu("VR Locomotion/THETA Z1 WebRTC Skybox Receiver")]
public sealed class ThetaZ1WebRtcSkyboxReceiver : MonoBehaviour
{
    [Header("Signaling")]
    [Tooltip("WebSocket signaling URL served by Tools/ThetaZ1WebRtcViewer/webrtc_signal_server.py.")]
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

    [Tooltip("Ask the sender for a fresh offer if ICE/peer connection drops after a successful connection.")]
    public bool requestFreshOfferOnDisconnect = true;

    [Header("Display")]
    [Tooltip("Inside-out sphere used as the immersive 360 display surface.")]
    public Renderer skyDomeRenderer;

    [Tooltip("Create a sphere automatically if Sky Dome Renderer is not assigned.")]
    public bool autoCreateSkyDome = true;

    [Tooltip("Runtime sky dome radius if the scene does not assign one.")]
    public float autoSkyDomeRadius = 50f;

    [Tooltip("Use negative X scale for correct inside-sphere 360 orientation.")]
    public bool invertSphereForInnerViewing = true;

    [Tooltip("Also assign the Z1 panorama to RenderSettings.skybox as a legacy panoramic skybox.")]
    public bool alsoSetRenderSettingsSkybox = true;

    [Tooltip("Disable legacy UDP/X5 projection components that target the same display sphere so the WebRTC Z1 material cannot be overwritten.")]
    public bool disableLegacyProjectionComponents = true;

    [Tooltip("Yaw rotation for the 360 view in degrees.")]
    [Range(-180f, 180f)]
    public float yawOffsetDegrees = 0f;

    [Tooltip("Fixed equirectangular mount correction in degrees. Use this when the Z1 is mounted sideways, e.g. one fisheye up and one fisheye down.")]
    public Vector3 staticMountEulerDegrees = Vector3.zero;

    [Tooltip("Color shown before the first WebRTC video frame arrives.")]
    public Color noSignalColor = new Color(0.02f, 0.025f, 0.035f, 1f);

    [Header("IMU Horizon Lock")]
    [Tooltip("Roll/pitch-only horizon lock. This rotates the Z1 panorama sphere, not the XR rig.")]
    public bool enableImuStabilization = true;

    [Tooltip("Receives roll/pitch IMU samples from signaling or optional local UDP.")]
    public ThetaZ1ImuStabilizer imuStabilizer;

    [Tooltip("Create ThetaZ1ImuStabilizer automatically on this GameObject if no reference is assigned.")]
    public bool autoAddImuStabilizer = true;

    [Tooltip("Apply roll/pitch horizon lock by reprojecting the equirectangular texture before display. This is more reliable than rotating only the sky dome transform.")]
    public bool stabilizeTextureBeforeDisplay = true;

    [Tooltip("Compute shader used to rotate the Z1 equirectangular frame for horizon lock.")]
    public ComputeShader equirectStabilizeCompute;

    [Tooltip("In the Unity Editor, auto-load Assets/VRLocomotion/Shaders/ThetaZ1EquirectStabilize.compute if the field is empty.")]
    public bool autoLoadEquirectStabilizeCompute = true;

    [Header("Debug")]
    public bool drawDebugOverlay = true;
    public bool drawTexturePreview = true;

    [Tooltip("Seconds between WebRTC stats polls.")]
    [Range(0.25f, 5f)]
    public float statsPollIntervalSeconds = 1f;

    [Tooltip("Send Unity receiver WebRTC stats back to the local signaling log.")]
    public bool sendStatsToSignaling = true;

    [Header("Stream Control")]
    [Tooltip("Show native Z1 4K/2K switch buttons in the debug overlay.")]
    public bool drawStreamControls = true;

    [Header("Runtime Mount Tuning")]
    [Tooltip("Apply the default Z1 mount tuning values every time this receiver wakes up.")]
    public bool applyMountTuningDefaultsOnAwake = true;

    [Tooltip("Default yaw applied when Apply Mount Tuning Defaults On Awake or the Stable button is used.")]
    public float defaultYawOffsetDegrees = 0f;

    [Tooltip("Default static mount Euler applied when Apply Mount Tuning Defaults On Awake or the Stable button is used.")]
    public Vector3 defaultStaticMountEulerDegrees = new Vector3(0f, -90f, -90f);

    [Tooltip("Default legacy skybox setting applied with the Z1 mount defaults.")]
    public bool defaultAlsoSetRenderSettingsSkybox = false;

    [Tooltip("Default IMU horizon lock setting applied with the Z1 mount defaults.")]
    public bool defaultEnableImuStabilization = false;

    [Tooltip("Default texture stabilization setting applied with the Z1 mount defaults.")]
    public bool defaultStabilizeTextureBeforeDisplay = false;

    [Tooltip("Show live Z1 mount/orientation controls in the Game view debug overlay.")]
    public bool drawMountTuningControls = true;

    [Tooltip("Fine step used by the live mount/orientation nudge buttons.")]
    [Range(0.1f, 45f)]
    public float mountTuningStepDegrees = 5f;

    [Header("Status")]
    [SerializeField] private string status = "Idle";
    [SerializeField] private string peerState = "none";
    [SerializeField] private string iceState = "none";
    [SerializeField] private string currentNativePreset = "unknown";
    [SerializeField] private string requestedNativePreset = "z1-4k";
    [SerializeField] private string senderStatus = "idle";
    [SerializeField] private string decoderImplementation = "unknown";
    [SerializeField] private int sourceWidth;
    [SerializeField] private int sourceHeight;
    [SerializeField] private float receivedBitrateMbps;
    [SerializeField] private float decodedFps;
    [SerializeField] private uint framesDecoded;
    [SerializeField] private uint framesDropped;
    [SerializeField] private int packetsLost;
    [SerializeField] private float packetLossPercent;
    [SerializeField] private float networkJitterMs;
    [SerializeField] private float rttMs;
    [SerializeField] private int reconnectAttempts;
    [SerializeField] private Vector3 imuCompensationEulerDeg;

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
    private Texture _lastAppliedTexture;
    private RenderTexture _stabilizedTexture;
    private Material _skyDomeMaterial;
    private Material _legacySkyboxMaterial;
    private string _remoteVideoMid = "video0";
    private bool _canAddRemoteCandidates;
    private bool _hasEverConnected;
    private bool _renegotiateRequested;
    private bool _hasLoggedFirstFrame;
    private bool _statsInFlight;
    private bool _usingTextureStabilization;
    private bool _stabilizeComputeLoadAttempted;
    private bool _stabilizeComputeWarningLogged;
    private bool _legacyProjectionComponentsChecked;
    private int _stabilizeKernel = -1;
    private float _nextStatsTime;
    private float _nextRenegotiateTime;
    private float _lastOfferReceivedTime = -1000f;
    private float _lastFreshOfferRequestTime = -1000f;
    private double _lastStatsTime;
    private ulong _lastBytesReceived;
    private uint _lastFramesDecoded;
    private uint _lastPacketsReceived;
    private int _lastPacketsLost;
    private DelegateOnIceConnectionChange _onIceConnectionChange;
    private DelegateOnConnectionStateChange _onConnectionStateChange;
    private DelegateOnIceCandidate _onIceCandidate;
    private DelegateOnTrack _onTrack;

    private static readonly int BaseColorMapId = Shader.PropertyToID("_BaseColorMap");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int UnlitColorMapId = Shader.PropertyToID("_UnlitColorMap");
    private static readonly int UnlitColorId = Shader.PropertyToID("_UnlitColor");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int SrcEquirectId = Shader.PropertyToID("_SrcEquirect");
    private static readonly int DstEquirectId = Shader.PropertyToID("_DstEquirect");
    private static readonly int SrcSizeId = Shader.PropertyToID("_SrcSize");
    private static readonly int DstSizeId = Shader.PropertyToID("_DstSize");
    private static readonly int StabilizePitchDegId = Shader.PropertyToID("_StabilizePitchDeg");
    private static readonly int StabilizeYawDegId = Shader.PropertyToID("_StabilizeYawDeg");
    private static readonly int StabilizeRollDegId = Shader.PropertyToID("_StabilizeRollDeg");
    private const float StartupRenegotiateGraceSeconds = 5f;

    [Serializable]
    private sealed class SignalMessage
    {
        public string type;
        public string role;
        public string clientName;
        public string preset;
        public string status;
        public string message;
        public string sdp;
        public string candidate;
        public string sdpMid;
        public int sdpMLineIndex;
        public string state;
        public string ice;
        public int width;
        public int height;
        public string fpsText;
        public float bitrateMbps;
        public float fps;
        public uint dropped;
        public int lost;
        public float lossPct;
        public float jitterMs;
        public float rttMs;
        public string imuStatus;
        public int imuHasPose;
        public int imuTexLock;
        public float imuRelRoll;
        public float imuRelPitch;
        public float imuCompPitch;
        public float imuCompRoll;
        public string source;
        public long timestampUs;
        public long timestamp;
        public float ax;
        public float ay;
        public float az;
        public float gx;
        public float gy;
        public float gz;
        public float rollDeg;
        public float pitchDeg;
        public float yawDeg;
        public float roll;
        public float pitch;
        public float yaw;
    }

    public Texture SourceTexture => _remoteTexture;

    private void Awake()
    {
        if (applyMountTuningDefaultsOnAwake)
        {
            ApplyMountTuningDefaults();
        }

        EnsureSkyDome();
        DisableLegacyProjectionComponentsIfNeeded();
        EnsureImuStabilizer();
        ApplyDisplayTexture(null);
    }

    private void Start()
    {
        if (connectOnStart)
        {
            Connect();
        }
    }

    private void Update()
    {
        FlushLogs();
        ProcessIncomingSignals();
        ApplyLatestFrame();
        ApplySkyDomePose();
        HandleRenegotiationRequest();
        MaybePollStats();
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
        _renegotiateRequested = false;
        _hasLoggedFirstFrame = false;
        ResetStats();

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

    [ContextMenu("Switch Z1 Native 4K")]
    public void SwitchToNative4K()
    {
        RequestNativePreset("z1-4k");
    }

    [ContextMenu("Switch Z1 Native 2K")]
    public void SwitchToNative2K()
    {
        RequestNativePreset("z1-2k");
    }

    [ContextMenu("Recenter IMU Horizon")]
    public void RecenterImuHorizon()
    {
        EnsureImuStabilizer();
        imuStabilizer?.RecenterHorizon();
    }

    [ContextMenu("Reset IMU Horizon To Level")]
    public void ResetImuHorizonToLevel()
    {
        EnsureImuStabilizer();
        imuStabilizer?.ResetHorizonReferenceToLevel();
    }

    public void RequestNativePreset(string preset)
    {
        if (preset != "z1-4k" && preset != "z1-2k")
        {
            senderStatus = "Unsupported preset: " + preset;
            return;
        }

        requestedNativePreset = preset;
        senderStatus = "requesting " + preset;

        if (_socket == null || _socket.State != WebSocketState.Open)
        {
            status = "Signaling not connected";
            senderStatus = "signaling not connected";
            return;
        }

        ClosePeerConnection();
        ResetStats();
        status = "Switching native stream to " + preset;
        SendSignal(new SignalMessage
        {
            type = "switch-preset",
            role = "viewer",
            clientName = "unity",
            preset = preset
        });
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

                    await SendSignalAsync(new SignalMessage { type = "register", role = "viewer", clientName = "unity" }, token);
                    EnqueueLog("Registered as viewer; waiting for Edge2 offer");

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
        _ = SendSignalSafeAsync(message, token);
    }

    private async Task SendSignalSafeAsync(SignalMessage message, CancellationToken token)
    {
        try
        {
            await SendSignalAsync(message, token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            EnqueueLog("Signal send failed: " + ex.Message);
        }
    }

    private void ProcessIncomingSignals()
    {
        while (_incomingSignals.TryDequeue(out SignalMessage message))
        {
            switch (message.type)
            {
                case "offer":
                    status = "Offer received";
                    StartCoroutine(HandleOffer(message.sdp));
                    break;
                case "candidate":
                    AddRemoteCandidate(message);
                    break;
                case "sender-status":
                    HandleSenderStatus(message);
                    break;
                case "imu-attitude":
                    HandleImuAttitude(message);
                    break;
                case "imu-sample":
                    HandleImuSample(message);
                    break;
            }
        }
    }

    private void HandleSenderStatus(SignalMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.preset))
        {
            currentNativePreset = message.preset;
        }

        senderStatus = string.IsNullOrWhiteSpace(message.status) ? "sender-status" : message.status;
        if (!string.IsNullOrWhiteSpace(message.message))
        {
            senderStatus += ": " + message.message;
        }
    }

    private void HandleImuAttitude(SignalMessage message)
    {
        if (!enableImuStabilization)
        {
            return;
        }

        EnsureImuStabilizer();
        if (imuStabilizer == null)
        {
            return;
        }

        long timestamp = message.timestampUs != 0 ? message.timestampUs : message.timestamp;
        float roll = Mathf.Abs(message.rollDeg) > 1e-6f ? message.rollDeg : message.roll;
        float pitch = Mathf.Abs(message.pitchDeg) > 1e-6f ? message.pitchDeg : message.pitch;
        float yaw = Mathf.Abs(message.yawDeg) > 1e-6f ? message.yawDeg : message.yaw;
        imuStabilizer.PushAttitudeSample(timestamp, roll, pitch, yaw, ImuSourceLabel(message));
    }

    private void HandleImuSample(SignalMessage message)
    {
        if (!enableImuStabilization)
        {
            return;
        }

        EnsureImuStabilizer();
        if (imuStabilizer == null)
        {
            return;
        }

        long timestamp = message.timestampUs != 0 ? message.timestampUs : message.timestamp;
        imuStabilizer.PushRawImuSample(timestamp, message.ax, message.ay, message.az, message.gx, message.gy, message.gz, ImuSourceLabel(message));
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
        _remoteVideoMid = ExtractFirstMid(sdp) ?? "video0";
        CreatePeerConnection();

        status = "Applying offer";
        Debug.Log("[THETA Z1 WebRTC] Offer summary: " + SummarizeSdp(sdp), this);
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
            Debug.LogError("[THETA Z1 WebRTC] SetRemoteDescription failed: " + remoteOp.Error.message, this);
            yield break;
        }

        var answerOp = _peerConnection.CreateAnswer();
        yield return answerOp;
        if (answerOp.IsError)
        {
            status = "CreateAnswer failed";
            Debug.LogError("[THETA Z1 WebRTC] CreateAnswer failed: " + answerOp.Error.message, this);
            yield break;
        }

        RTCSessionDescription answer = answerOp.Desc;
        Debug.Log("[THETA Z1 WebRTC] Answer summary: " + SummarizeSdp(answer.sdp), this);
        decoderImplementation = ExtractFmtpParameter(answer.sdp, "implementation_name") ?? "unknown";
        if (answer.sdp.Contains("m=video 0 "))
        {
            Debug.LogError("[THETA Z1 WebRTC] Unity rejected the offered video m-line. This usually means the offered H.264 profile is not accepted by Unity.WebRTC.", this);
        }

        var localOp = _peerConnection.SetLocalDescription(ref answer);
        yield return localOp;
        if (localOp.IsError)
        {
            status = "SetLocalDescription failed";
            Debug.LogError("[THETA Z1 WebRTC] SetLocalDescription failed: " + localOp.Error.message, this);
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
        Debug.Log("[THETA Z1 WebRTC] Answer sent to Edge2 sender.", this);
    }

    private void CreatePeerConnection()
    {
        _receiveStream = new MediaStream();

        _peerConnection = new RTCPeerConnection();
        peerState = "new";
        iceState = "new";

        _onIceCandidate = candidate =>
        {
            if (candidate == null)
            {
                return;
            }

            Debug.Log("[THETA Z1 WebRTC] Local ICE candidate: " + candidate.Candidate, this);
            SendSignal(new SignalMessage
            {
                type = "candidate",
                candidate = candidate.Candidate,
                sdpMid = candidate.SdpMid,
                sdpMLineIndex = candidate.SdpMLineIndex.GetValueOrDefault()
            });
        };
        _peerConnection.OnIceCandidate = _onIceCandidate;

        _onIceConnectionChange = state =>
        {
            iceState = state.ToString();
            Debug.Log("[THETA Z1 WebRTC] ICE state: " + iceState, this);
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
        _peerConnection.OnIceConnectionChange = _onIceConnectionChange;

        _onConnectionStateChange = state =>
        {
            peerState = state.ToString();
            Debug.Log("[THETA Z1 WebRTC] Peer state: " + peerState, this);
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
        _peerConnection.OnConnectionStateChange = _onConnectionStateChange;

        _onTrack = e =>
        {
            Debug.Log("[THETA Z1 WebRTC] Track received: " + e.Track.Kind, this);
            if (e.Track.Kind == TrackKind.Video)
            {
                _receiveStream.AddTrack(e.Track);
                if (e.Track is VideoStreamTrack videoTrack)
                {
                    AttachVideoTrack(videoTrack);
                }
            }
        };
        _peerConnection.OnTrack = _onTrack;
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
                currentNativePreset = ResolutionToNativePreset(sourceWidth, sourceHeight);
            }

            if (!_hasLoggedFirstFrame)
            {
                _hasLoggedFirstFrame = true;
                Debug.Log($"[THETA Z1 WebRTC] First frame: {sourceWidth}x{sourceHeight}, texture={texture.GetType().Name}", this);
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
                Debug.Log("[THETA Z1 WebRTC] Queued remote ICE candidate: " + ShortCandidate(message.candidate), this);
            }
            return;
        }

        var init = new RTCIceCandidateInit
        {
            candidate = message.candidate,
            sdpMid = string.IsNullOrWhiteSpace(message.sdpMid) ? _remoteVideoMid : message.sdpMid,
            sdpMLineIndex = message.sdpMLineIndex
        };

        try
        {
            var candidate = new RTCIceCandidate(init);
            bool added = _peerConnection.AddIceCandidate(candidate);
            if (added)
            {
                Debug.Log("[THETA Z1 WebRTC] Added remote ICE candidate: " + ShortCandidate(message.candidate), this);
            }
            else
            {
                Debug.LogWarning("[THETA Z1 WebRTC] Unity rejected remote ICE candidate: " + ShortCandidate(message.candidate), this);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[THETA Z1 WebRTC] Failed to add remote ICE candidate: " + ex.Message + " candidate=" + ShortCandidate(message.candidate), this);
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

    private void ApplyLatestFrame()
    {
        Texture texture = _remoteTexture;
        if (texture == null)
        {
            _usingTextureStabilization = false;
            if (_lastAppliedTexture != null)
            {
                _lastAppliedTexture = null;
                ApplyDisplayTexture(null);
            }
            return;
        }

        Texture displayTexture = GetDisplayTexture(texture);
        if (displayTexture == null || ReferenceEquals(displayTexture, _lastAppliedTexture))
        {
            return;
        }

        _lastAppliedTexture = displayTexture;
        ApplyDisplayTexture(displayTexture);
    }

    private void EnsureImuStabilizer()
    {
        if (!enableImuStabilization || imuStabilizer != null)
        {
            return;
        }

        imuStabilizer = GetComponent<ThetaZ1ImuStabilizer>();
        if (imuStabilizer == null && autoAddImuStabilizer)
        {
            imuStabilizer = gameObject.AddComponent<ThetaZ1ImuStabilizer>();
        }
    }

    private void EnsureSkyDome()
    {
        if (skyDomeRenderer == null && autoCreateSkyDome)
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "THETA Z1 Equirect Sky Dome";
            sphere.transform.SetParent(transform, false);
            sphere.transform.localPosition = Vector3.zero;
            sphere.transform.localRotation = Quaternion.identity;
            sphere.transform.localScale = Vector3.one * Mathf.Max(1f, autoSkyDomeRadius * 2f);
            Destroy(sphere.GetComponent<Collider>());
            skyDomeRenderer = sphere.GetComponent<Renderer>();
        }

        if (skyDomeRenderer == null)
        {
            return;
        }

        Shader unlit = Shader.Find("HDRP/Unlit");
        if (unlit == null)
        {
            Debug.LogError("[THETA Z1 WebRTC] HDRP/Unlit shader not found.", this);
            return;
        }

        if (_skyDomeMaterial == null)
        {
            _skyDomeMaterial = new Material(unlit) { name = "ThetaZ1_EquirectSkyDome_Runtime" };
            _skyDomeMaterial.SetFloat("_DoubleSidedEnable", 1f);
            _skyDomeMaterial.SetInt("_CullMode", (int)CullMode.Off);
            _skyDomeMaterial.SetInt("_CullModeForward", (int)CullMode.Off);
            SetColorIfPresent(_skyDomeMaterial, BaseColorId, noSignalColor);
            SetColorIfPresent(_skyDomeMaterial, UnlitColorId, noSignalColor);
            HDMaterial.ValidateMaterial(_skyDomeMaterial);
        }

        skyDomeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        skyDomeRenderer.receiveShadows = false;
        skyDomeRenderer.sharedMaterial = _skyDomeMaterial;
        DisableLegacyProjectionComponentsIfNeeded();

        Transform domeTransform = skyDomeRenderer.transform;
        if (invertSphereForInnerViewing)
        {
            Vector3 scale = domeTransform.localScale;
            domeTransform.localScale = new Vector3(-Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        }

        ApplySkyDomePose();
    }

    private void DisableLegacyProjectionComponentsIfNeeded()
    {
        if (!disableLegacyProjectionComponents || _legacyProjectionComponentsChecked || skyDomeRenderer == null)
        {
            return;
        }

        _legacyProjectionComponentsChecked = true;
        int disabledCount = 0;

        foreach (DualFisheyeStitcher stitcher in FindObjectsByType<DualFisheyeStitcher>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (stitcher == null || stitcher.sphereRenderer != skyDomeRenderer)
            {
                continue;
            }

            if (stitcher.enabled)
            {
                stitcher.enabled = false;
                disabledCount++;
            }

            if (stitcher.receiver != null && stitcher.receiver.enabled)
            {
                stitcher.receiver.StopStream();
                stitcher.receiver.enabled = false;
                disabledCount++;
            }
        }

        foreach (UdpVideoStreamReceiver receiver in FindObjectsByType<UdpVideoStreamReceiver>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (receiver == null || !receiver.enabled || receiver.targetRenderer != skyDomeRenderer)
            {
                continue;
            }

            receiver.StopStream();
            receiver.enabled = false;
            disabledCount++;
        }

        if (disabledCount > 0)
        {
            Debug.Log($"[THETA Z1 WebRTC] Disabled {disabledCount} legacy UDP/X5 projection component(s) targeting {skyDomeRenderer.name}.", this);
        }
    }

    private void ApplySkyDomePose()
    {
        if (skyDomeRenderer == null)
        {
            return;
        }

        if (_skyDomeMaterial != null && skyDomeRenderer.sharedMaterial != _skyDomeMaterial)
        {
            skyDomeRenderer.sharedMaterial = _skyDomeMaterial;
        }

        Vector3 stabilizationEuler = Vector3.zero;
        bool hasImuPose = enableImuStabilization && imuStabilizer != null && imuStabilizer.HasPose;
        if (hasImuPose && !_usingTextureStabilization)
        {
            stabilizationEuler = imuStabilizer.CompensationEulerDeg;
        }

        imuCompensationEulerDeg = stabilizationEuler;
        Quaternion staticYaw = Quaternion.Euler(0f, yawOffsetDegrees + stabilizationEuler.y, 0f);
        Quaternion staticMount = Quaternion.Euler(staticMountEulerDegrees);
        Quaternion rollPitch = Quaternion.Euler(stabilizationEuler.x, 0f, stabilizationEuler.z);
        skyDomeRenderer.transform.localRotation = staticYaw * staticMount * rollPitch;
    }

    private Texture GetDisplayTexture(Texture source)
    {
        _usingTextureStabilization = false;
        if (source == null ||
            !stabilizeTextureBeforeDisplay ||
            !enableImuStabilization ||
            imuStabilizer == null ||
            !imuStabilizer.HasPose)
        {
            return source;
        }

        if (!EnsureEquirectStabilizeCompute())
        {
            return source;
        }

        EnsureStabilizedTexture(source.width, source.height);
        if (_stabilizedTexture == null)
        {
            return source;
        }

        Vector3 correction = imuStabilizer.CompensationEulerDeg;
        equirectStabilizeCompute.SetTexture(_stabilizeKernel, SrcEquirectId, source);
        equirectStabilizeCompute.SetTexture(_stabilizeKernel, DstEquirectId, _stabilizedTexture);
        equirectStabilizeCompute.SetInts(SrcSizeId, source.width, source.height);
        equirectStabilizeCompute.SetInts(DstSizeId, _stabilizedTexture.width, _stabilizedTexture.height);
        equirectStabilizeCompute.SetFloat(StabilizePitchDegId, correction.x);
        equirectStabilizeCompute.SetFloat(StabilizeYawDegId, correction.y);
        equirectStabilizeCompute.SetFloat(StabilizeRollDegId, correction.z);
        equirectStabilizeCompute.Dispatch(
            _stabilizeKernel,
            (_stabilizedTexture.width + 7) / 8,
            (_stabilizedTexture.height + 7) / 8,
            1);

        _usingTextureStabilization = true;
        return _stabilizedTexture;
    }

    private bool EnsureEquirectStabilizeCompute()
    {
        if (equirectStabilizeCompute == null && autoLoadEquirectStabilizeCompute && !_stabilizeComputeLoadAttempted)
        {
            _stabilizeComputeLoadAttempted = true;
#if UNITY_EDITOR
            string[] guids = UnityEditor.AssetDatabase.FindAssets("ThetaZ1EquirectStabilize t:ComputeShader");
            if (guids != null && guids.Length > 0)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                equirectStabilizeCompute = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                if (equirectStabilizeCompute != null)
                {
                    Debug.Log($"[THETA Z1 WebRTC] Loaded equirect horizon-lock compute shader: {path}", this);
                }
            }
#endif
        }

        if (equirectStabilizeCompute == null)
        {
            if (!_stabilizeComputeWarningLogged)
            {
                _stabilizeComputeWarningLogged = true;
                Debug.LogWarning("[THETA Z1 WebRTC] Equirect horizon-lock compute shader is missing; falling back to sky dome transform rotation.", this);
            }
            return false;
        }

        if (_stabilizeKernel < 0)
        {
            try
            {
                _stabilizeKernel = equirectStabilizeCompute.FindKernel("EquirectHorizonLock");
            }
            catch (Exception ex)
            {
                if (!_stabilizeComputeWarningLogged)
                {
                    _stabilizeComputeWarningLogged = true;
                    Debug.LogWarning("[THETA Z1 WebRTC] Failed to find EquirectHorizonLock kernel: " + ex.Message, this);
                }
                return false;
            }
        }

        return _stabilizeKernel >= 0;
    }

    private void EnsureStabilizedTexture(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (_stabilizedTexture != null && _stabilizedTexture.width == width && _stabilizedTexture.height == height)
        {
            return;
        }

        ReleaseStabilizedTexture();
        _stabilizedTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
        {
            enableRandomWrite = true,
            filterMode = FilterMode.Bilinear,
            wrapModeU = TextureWrapMode.Repeat,
            wrapModeV = TextureWrapMode.Clamp,
            name = "ThetaZ1_WebRTC_HorizonLocked"
        };
        _stabilizedTexture.Create();
        _lastAppliedTexture = null;
        Debug.Log($"[THETA Z1 WebRTC] Allocated horizon-lock texture: {width}x{height}", this);
    }

    private void ApplyDisplayTexture(Texture texture)
    {
        EnsureSkyDome();

        if (_skyDomeMaterial != null)
        {
            if (skyDomeRenderer != null && skyDomeRenderer.sharedMaterial != _skyDomeMaterial)
            {
                skyDomeRenderer.sharedMaterial = _skyDomeMaterial;
            }

            SetTextureIfPresent(_skyDomeMaterial, BaseColorMapId, texture);
            SetTextureIfPresent(_skyDomeMaterial, UnlitColorMapId, texture);
            SetColorIfPresent(_skyDomeMaterial, BaseColorId, texture != null ? Color.white : noSignalColor);
            SetColorIfPresent(_skyDomeMaterial, UnlitColorId, texture != null ? Color.white : noSignalColor);
        }

        if (alsoSetRenderSettingsSkybox)
        {
            EnsureLegacySkyboxMaterial();
            if (_legacySkyboxMaterial != null)
            {
                _legacySkyboxMaterial.SetTexture(MainTexId, texture);
                _legacySkyboxMaterial.SetFloat("_Rotation", yawOffsetDegrees + staticMountEulerDegrees.y);
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

        _legacySkyboxMaterial = new Material(panoramic) { name = "ThetaZ1_PanoramicSkybox_Runtime" };
        _legacySkyboxMaterial.SetFloat("_ImageType", 0f);
        _legacySkyboxMaterial.SetFloat("_Mapping", 1f);
        _legacySkyboxMaterial.SetColor("_Tint", Color.white);
        _legacySkyboxMaterial.SetFloat("_Exposure", 1f);
        _legacySkyboxMaterial.SetFloat("_Rotation", yawOffsetDegrees + staticMountEulerDegrees.y);
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

                ulong deltaBytes = bytes >= _lastBytesReceived ? bytes - _lastBytesReceived : 0;
                uint deltaFrames = decoded >= _lastFramesDecoded ? decoded - _lastFramesDecoded : 0;
                uint deltaPacketsReceived = packetsReceived >= _lastPacketsReceived ? packetsReceived - _lastPacketsReceived : 0;
                int deltaLost = lost >= _lastPacketsLost ? lost - _lastPacketsLost : 0;

                receivedBitrateMbps = (float)(deltaBytes * 8.0 / deltaSeconds / 1_000_000.0);
                decodedFps = (float)(deltaFrames / deltaSeconds);
                framesDecoded = decoded;
                framesDropped = dropped;
                packetsLost = lost;
                packetLossPercent = (deltaLost + deltaPacketsReceived) > 0
                    ? (float)(deltaLost * 100.0 / (deltaLost + deltaPacketsReceived))
                    : 0f;
                networkJitterMs = (float)(inbound.jitter * 1000.0);

                _lastStatsTime = now;
                _lastBytesReceived = bytes;
                _lastFramesDecoded = decoded;
                _lastPacketsReceived = packetsReceived;
                _lastPacketsLost = lost;
            }
            else if (stat is RTCIceCandidatePairStats pair && pair.nominated && pair.state == "succeeded")
            {
                rttMs = (float)(pair.currentRoundTripTime * 1000.0);
            }
        }

        if (sendStatsToSignaling)
        {
            Vector2 imuRelative = imuStabilizer != null ? imuStabilizer.RelativeRollPitchDeg : Vector2.zero;
            Vector3 imuCorrection = imuStabilizer != null ? imuStabilizer.CompensationEulerDeg : Vector3.zero;
            string imuStatusText = enableImuStabilization
                ? (imuStabilizer != null ? imuStabilizer.StatusLabel : "not-attached")
                : "off";
            bool imuHasPose = enableImuStabilization && imuStabilizer != null && imuStabilizer.HasPose;

            SendSignal(new SignalMessage
            {
                type = "viewer-stats",
                role = "viewer",
                clientName = "unity",
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
                rttMs = rttMs,
                imuStatus = imuStatusText,
                imuHasPose = imuHasPose ? 1 : 0,
                imuTexLock = _usingTextureStabilization ? 1 : 0,
                imuRelRoll = imuRelative.x,
                imuRelPitch = imuRelative.y,
                imuCompPitch = imuCorrection.x,
                imuCompRoll = imuCorrection.z
            });
        }
    }

    private void ResetStats()
    {
        receivedBitrateMbps = 0f;
        decodedFps = 0f;
        framesDecoded = 0;
        framesDropped = 0;
        packetsLost = 0;
        packetLossPercent = 0f;
        networkJitterMs = 0f;
        rttMs = 0f;
        _lastStatsTime = 0d;
        _lastBytesReceived = 0;
        _lastFramesDecoded = 0;
        _lastPacketsReceived = 0;
        _lastPacketsLost = 0;
    }

    private void ClosePeerConnection()
    {
        if (_remoteVideoTrack != null)
        {
            try { _remoteVideoTrack.Dispose(); } catch { }
            _remoteVideoTrack = null;
        }

        _remoteTexture = null;
        _lastAppliedTexture = null;
        _hasLoggedFirstFrame = false;
        _canAddRemoteCandidates = false;
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

        _onIceConnectionChange = null;
        _onConnectionStateChange = null;
        _onIceCandidate = null;
        _onTrack = null;

        sourceWidth = 0;
        sourceHeight = 0;
        peerState = "none";
        iceState = "none";
        ResetStats();
        ApplyDisplayTexture(null);
    }

    private void ReleaseRuntimeResources()
    {
        ReleaseStabilizedTexture();

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

    private void ReleaseStabilizedTexture()
    {
        if (_stabilizedTexture == null)
        {
            return;
        }

        _stabilizedTexture.Release();
        Destroy(_stabilizedTexture);
        _stabilizedTexture = null;
    }

    private void FlushLogs()
    {
        while (_mainThreadLogs.TryDequeue(out string message))
        {
            status = message;
            Debug.Log("[THETA Z1 WebRTC] " + message, this);
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
        int panelWidth = Mathf.Min(580, Screen.width - margin * 2);
        int panelHeight = drawStreamControls ? 250 : 216;
        var rect = new Rect(margin, margin, panelWidth, panelHeight);
        GUI.color = new Color(0f, 0f, 0f, 0.68f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        string imuLine = ImuOverlayLine();
        GUI.Label(new Rect(rect.x + 10, rect.y + 8, rect.width - 20, rect.height - 16),
            "THETA Z1 WebRTC\n" +
            $"status={status}\n" +
            $"peer={peerState} ice={iceState} reconnects={reconnectAttempts}\n" +
            $"native={currentNativePreset} target={requestedNativePreset} sender={senderStatus}\n" +
            $"source={sourceWidth}x{sourceHeight} decoder={DecoderLabel()}\n" +
            $"bitrate={receivedBitrateMbps:F2}Mbps decoded_fps={decodedFps:F1}\n" +
            $"frames={framesDecoded} dropped={framesDropped} lost={packetsLost} loss={packetLossPercent:F2}%\n" +
            $"jitter={networkJitterMs:F1}ms rtt={rttMs:F1}ms\n" +
            imuLine + "\n" +
            $"signal={signalingUrl}");

        if (drawStreamControls)
        {
            float buttonY = rect.yMax - 40f;
            float buttonHeight = 28f;
            float buttonWidth = 112f;
            float gap = 8f;
            GUI.enabled = _socket != null && _socket.State == WebSocketState.Open;
            if (GUI.Button(new Rect(rect.x + 10f, buttonY, buttonWidth, buttonHeight), "4K Native"))
            {
                SwitchToNative4K();
            }

            if (GUI.Button(new Rect(rect.x + 10f + buttonWidth + gap, buttonY, buttonWidth, buttonHeight), "2K Native"))
            {
                SwitchToNative2K();
            }
            GUI.enabled = true;

            GUI.enabled = enableImuStabilization && imuStabilizer != null && imuStabilizer.HasPose;
            if (GUI.Button(new Rect(rect.x + 10f + (buttonWidth + gap) * 2f, buttonY, 120f, buttonHeight), "Set Current Ref"))
            {
                RecenterImuHorizon();
            }

            if (GUI.Button(new Rect(rect.x + 10f + (buttonWidth + gap) * 2f + 120f + gap, buttonY, 92f, buttonHeight), "Level Ref"))
            {
                ResetImuHorizonToLevel();
            }
            GUI.enabled = true;
        }

        float previewY = rect.yMax + 10f;
        if (drawMountTuningControls)
        {
            float tuningWidth = Mathf.Min(460f, Screen.width - margin * 2f);
            float tuningX = Screen.width - margin - tuningWidth;
            float tuningY = margin;
            if (tuningX < rect.xMax + margin)
            {
                tuningX = margin;
                tuningY = rect.yMax + 10f;
                previewY = tuningY + 318f + 10f;
            }

            DrawMountTuningPanel(new Rect(tuningX, tuningY, tuningWidth, 318f));
        }

        if (!drawTexturePreview || _remoteTexture == null)
        {
            return;
        }

        int previewWidth = Mathf.Min(420, Screen.width / 3);
        int previewHeight = previewWidth / 2;
        var previewRect = new Rect(margin, previewY, previewWidth, previewHeight);
        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(previewRect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.DrawTexture(previewRect, _remoteTexture, ScaleMode.ScaleToFit, false);
        GUI.Label(new Rect(previewRect.x + 6, previewRect.y + 4, previewRect.width - 12, 22), "Z1 equirect WebRTC texture");
    }

    private void DrawMountTuningPanel(Rect rect)
    {
        GUI.color = new Color(0f, 0f, 0f, 0.72f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;

        float y = rect.y + 8f;
        GUI.Label(new Rect(rect.x + 10f, y, rect.width - 20f, 22f),
            $"Z1 Mount Tuning  yaw={yawOffsetDegrees:F1} static=({staticMountEulerDegrees.x:F1}, {staticMountEulerDegrees.y:F1}, {staticMountEulerDegrees.z:F1})");
        y += 28f;

        y = DrawAngleControl(rect, y, "Yaw", ref yawOffsetDegrees);

        Vector3 mount = staticMountEulerDegrees;
        y = DrawAngleControl(rect, y, "X", ref mount.x);
        y = DrawAngleControl(rect, y, "Y", ref mount.y);
        y = DrawAngleControl(rect, y, "Z", ref mount.z);
        staticMountEulerDegrees = mount;

        y += 4f;
        alsoSetRenderSettingsSkybox = GUI.Toggle(
            new Rect(rect.x + 10f, y, rect.width - 20f, 22f),
            alsoSetRenderSettingsSkybox,
            "Also set RenderSettings.skybox (legacy panoramic path; ignores most static X/Z tuning)");
        y += 24f;

        enableImuStabilization = GUI.Toggle(
            new Rect(rect.x + 10f, y, 190f, 22f),
            enableImuStabilization,
            "IMU horizon lock");
        stabilizeTextureBeforeDisplay = GUI.Toggle(
            new Rect(rect.x + 210f, y, 190f, 22f),
            stabilizeTextureBeforeDisplay,
            "Texture stabilize");
        y += 28f;

        const float buttonHeight = 24f;
        float buttonWidth = Mathf.Min(92f, (rect.width - 50f) / 4f);
        float x = rect.x + 10f;
        if (GUI.Button(new Rect(x, y, buttonWidth, buttonHeight), "Stable"))
        {
            ApplyMountTuningDefaults();
        }

        x += buttonWidth + 10f;
        if (GUI.Button(new Rect(x, y, buttonWidth, buttonHeight), "Zero"))
        {
            yawOffsetDegrees = 0f;
            staticMountEulerDegrees = Vector3.zero;
        }

        x += buttonWidth + 10f;
        if (GUI.Button(new Rect(x, y, buttonWidth, buttonHeight), "Log"))
        {
            Debug.Log($"[THETA Z1 WebRTC] Mount tuning: yaw={yawOffsetDegrees:F3}, staticMountEuler={staticMountEulerDegrees}, legacySkybox={alsoSetRenderSettingsSkybox}, imu={enableImuStabilization}, texStabilize={stabilizeTextureBeforeDisplay}", this);
        }

        y += buttonHeight + 8f;
        GUI.Label(new Rect(rect.x + 10f, y, rect.width - 20f, 22f), $"Step={Mathf.Max(0.1f, mountTuningStepDegrees):F1} deg. Values update every frame in Play mode.");
    }

    private float DrawAngleControl(Rect rect, float y, string label, ref float value)
    {
        float step = Mathf.Max(0.1f, mountTuningStepDegrees);
        float x = rect.x + 10f;
        float rowWidth = rect.width - 20f;
        GUI.Label(new Rect(x, y, 40f, 22f), label);
        GUI.Label(new Rect(x + 42f, y, 64f, 22f), $"{value:F1}");

        float buttonWidth = 38f;
        float sliderX = x + 110f;
        float sliderWidth = Mathf.Max(80f, rowWidth - 110f - buttonWidth * 5f - 20f);
        value = GUI.HorizontalSlider(new Rect(sliderX, y + 4f, sliderWidth, 18f), value, -180f, 180f);

        float buttonX = sliderX + sliderWidth + 6f;
        if (GUI.Button(new Rect(buttonX, y, buttonWidth, 22f), "-90")) value = NormalizeDegrees(value - 90f);
        buttonX += buttonWidth;
        if (GUI.Button(new Rect(buttonX, y, buttonWidth, 22f), "-")) value = NormalizeDegrees(value - step);
        buttonX += buttonWidth;
        if (GUI.Button(new Rect(buttonX, y, buttonWidth, 22f), "+")) value = NormalizeDegrees(value + step);
        buttonX += buttonWidth;
        if (GUI.Button(new Rect(buttonX, y, buttonWidth, 22f), "+90")) value = NormalizeDegrees(value + 90f);
        buttonX += buttonWidth;
        if (GUI.Button(new Rect(buttonX, y, buttonWidth, 22f), "0")) value = 0f;

        value = NormalizeDegrees(value);
        return y + 30f;
    }

    private static float NormalizeDegrees(float degrees)
    {
        degrees = Mathf.Repeat(degrees + 180f, 360f) - 180f;
        return Mathf.Approximately(degrees, -180f) ? 180f : degrees;
    }

    private void ApplyMountTuningDefaults()
    {
        yawOffsetDegrees = NormalizeDegrees(defaultYawOffsetDegrees);
        staticMountEulerDegrees = new Vector3(
            NormalizeDegrees(defaultStaticMountEulerDegrees.x),
            NormalizeDegrees(defaultStaticMountEulerDegrees.y),
            NormalizeDegrees(defaultStaticMountEulerDegrees.z));
        alsoSetRenderSettingsSkybox = defaultAlsoSetRenderSettingsSkybox;
        enableImuStabilization = defaultEnableImuStabilization;
        stabilizeTextureBeforeDisplay = defaultStabilizeTextureBeforeDisplay;

        if (!enableImuStabilization)
        {
            imuCompensationEulerDeg = Vector3.zero;
        }
    }

    private static void SetTextureIfPresent(Material material, int propertyId, Texture texture)
    {
        if (material.HasProperty(propertyId))
        {
            material.SetTexture(propertyId, texture);
        }
    }

    private static void SetColorIfPresent(Material material, int propertyId, Color color)
    {
        if (material.HasProperty(propertyId))
        {
            material.SetColor(propertyId, color);
        }
    }

    private string DecoderLabel()
    {
        if (string.IsNullOrWhiteSpace(decoderImplementation) || decoderImplementation == "unknown")
        {
            return "unknown";
        }

        if (decoderImplementation.IndexOf("NvCodec", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return decoderImplementation + "/HW";
        }

        return decoderImplementation;
    }

    private string ImuOverlayLine()
    {
        if (!enableImuStabilization)
        {
            return "imu=off roll/pitch stabilization disabled";
        }

        if (imuStabilizer == null)
        {
            return "imu=not attached";
        }

        Vector2 relative = imuStabilizer.RelativeRollPitchDeg;
        Vector2 reference = imuStabilizer.ReferenceRollPitchDeg;
        Vector3 correction = imuStabilizer.CompensationEulerDeg;
        return
            $"imu={imuStabilizer.StatusLabel} " +
            $"rel_rp=({relative.x:F1},{relative.y:F1}) " +
            $"ref=({reference.x:F1},{reference.y:F1}) " +
            $"comp_pr=({correction.x:F1},{correction.z:F1}) " +
            $"tex_lock={(_usingTextureStabilization ? "on" : "off")} yaw_free";
    }

    private static string ImuSourceLabel(SignalMessage message)
    {
        if (message != null && !string.IsNullOrWhiteSpace(message.source))
        {
            return message.source;
        }

        return "signaling";
    }

    private static string ResolutionToNativePreset(int width, int height)
    {
        if (width == 3840 && height == 1920)
        {
            return "z1-4k";
        }

        if (width == 1920 && height == 960)
        {
            return "z1-2k";
        }

        return width > 0 && height > 0 ? "custom" : "unknown";
    }

    private static string ExtractFmtpParameter(string sdp, string key)
    {
        if (string.IsNullOrWhiteSpace(sdp) || string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        string[] lines = sdp.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        string prefix = key + "=";
        foreach (string line in lines)
        {
            if (!line.StartsWith("a=fmtp:", StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = line.Split(';');
            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed.Substring(prefix.Length);
                }
            }
        }

        return null;
    }

    private static string SummarizeSdp(string sdp)
    {
        if (string.IsNullOrWhiteSpace(sdp))
        {
            return "(empty)";
        }

        string[] lines = sdp.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var parts = new List<string>();
        foreach (string line in lines)
        {
            if (line.StartsWith("m=video ", StringComparison.Ordinal) ||
                line.StartsWith("a=sendonly", StringComparison.Ordinal) ||
                line.StartsWith("a=recvonly", StringComparison.Ordinal) ||
                line.StartsWith("a=inactive", StringComparison.Ordinal) ||
                line.StartsWith("a=rtpmap:", StringComparison.Ordinal) ||
                line.StartsWith("a=fmtp:", StringComparison.Ordinal))
            {
                parts.Add(line);
            }
        }

        return parts.Count > 0 ? string.Join(" | ", parts) : "(no video lines)";
    }

    private static string ExtractFirstMid(string sdp)
    {
        if (string.IsNullOrWhiteSpace(sdp))
        {
            return null;
        }

        string[] lines = sdp.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string line in lines)
        {
            const string prefix = "a=mid:";
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return line.Substring(prefix.Length).Trim();
            }
        }

        return null;
    }

    private static string ShortCandidate(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return "(empty)";
        }

        const int maxLength = 160;
        return candidate.Length <= maxLength ? candidate : candidate.Substring(0, maxLength) + "...";
    }
}
