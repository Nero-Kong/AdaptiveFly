using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

/// <summary>
/// Converts Z1/drone IMU attitude into horizon-only panorama compensation.
/// Translation and yaw are intentionally left under user control.
/// </summary>
[AddComponentMenu("VR Locomotion/THETA Z1 IMU Stabilizer")]
public sealed class ThetaZ1ImuStabilizer : MonoBehaviour
{
    private enum SampleKind
    {
        RawImu,
        Attitude
    }

    [Header("UDP Fallback")]
    [Tooltip("Optional local UDP receiver. The preferred Z1 path receives IMU messages through the WebRTC signaling side-channel.")]
    public bool listenForUdp = false;

    public int listenPort = 5601;
    public bool startUdpAutomatically = true;

    [Header("Axis Calibration")]
    [Tooltip("Per-axis sign/remap for accelerometer values before roll/pitch estimation.")]
    public Vector3 accelAxisScale = Vector3.one;

    [Tooltip("Per-axis sign/remap for gyro values. Yaw gyro is ignored unless yaw jitter damping is enabled.")]
    public Vector3 gyroAxisScale = Vector3.one;

    [Tooltip("Raw gyro values are expected to be radians/second. Disable if your IMU source reports degrees/second.")]
    public bool gyroIsRadiansPerSecond = true;

    [Tooltip("Multiply incoming attitude roll/pitch/yaw degrees before stabilization. Use this to flip signs.")]
    public Vector3 attitudeEulerScale = Vector3.one;

    [Header("Roll/Pitch Horizon Lock")]
    public bool compensateRollPitch = true;

    [Tooltip("Lock roll/pitch to a fixed absolute reference instead of treating the first received pose as neutral.")]
    public bool useAbsoluteRollPitchReference = true;

    [Tooltip("Target roll in degrees. Leave 0 to keep the panorama upright relative to the IMU/flight-controller level frame.")]
    public float referenceRollDeg = 0f;

    [Tooltip("Target pitch in degrees. Leave 0 to keep the panorama upright relative to the IMU/flight-controller level frame.")]
    public float referencePitchDeg = 0f;

    [Range(0f, 1.5f)]
    public float rollPitchStrength = 1f;

    [Tooltip("Higher values follow IMU roll/pitch faster. Lower values are smoother.")]
    [Range(0.1f, 30f)]
    public float rollPitchSmoothingHz = 8f;

    [Tooltip("Stop applying horizon lock when no fresh IMU sample has arrived within this many seconds.")]
    [Range(0.1f, 5f)]
    public float maxSampleAgeSeconds = 1f;

    [Tooltip("Capture the first stable pose as the neutral horizon. Ignored when absolute roll/pitch reference is enabled.")]
    public bool captureNeutralOnStart = true;

    [Range(0.1f, 5f)]
    public float neutralCalibrationSeconds = 1f;

    [Tooltip("Multiply pitch correction before rotating the Z1 sky dome. Flip sign here if compensation is inverted.")]
    public float pitchCompensationSign = -1f;

    [Tooltip("Multiply roll correction before rotating the Z1 sky dome. Flip sign here if compensation is inverted.")]
    public float rollCompensationSign = -1f;

    [Header("Yaw")]
    [Tooltip("Keep this off for flight preview. User yaw remains free; this only applies a tiny, leaky high-frequency yaw correction when enabled.")]
    public bool dampYawJitter = false;

    [Range(0f, 0.5f)]
    public float yawJitterStrength = 0.04f;

    [Range(0.1f, 20f)]
    public float yawReturnHz = 4f;

    [Range(0f, 8f)]
    public float maxYawJitterDegrees = 2f;

    public float yawCompensationSign = -1f;

    [Header("Status")]
    [SerializeField] private bool udpRunning;
    [SerializeField] private bool hasSample;
    [SerializeField] private bool neutralReady;
    [SerializeField] private string lastSource = "none";
    [SerializeField] private float estimatedRollDeg;
    [SerializeField] private float estimatedPitchDeg;
    [SerializeField] private float relativeRollDeg;
    [SerializeField] private float relativePitchDeg;
    [SerializeField] private float yawJitterDeg;
    [SerializeField] private Vector3 compensationEulerDeg;
    [SerializeField] private int receivedSamples;
    [SerializeField] private float secondsSinceSample = 999f;
    [SerializeField] private string lastPacket;
    [SerializeField] private string lastError;

    private readonly object queueLock = new object();
    private readonly Queue<ImuSample> samples = new Queue<ImuSample>(512);

    private UdpClient udpClient;
    private Thread receiveThread;
    private volatile bool receiveLoopRunning;

    private float neutralRollSum;
    private float neutralPitchSum;
    private Vector3 neutralGravitySum;
    private Vector3 neutralGravity;
    private Vector3 smoothedGravity;
    private int neutralSampleCount;
    private float neutralRollDeg;
    private float neutralPitchDeg;
    private float neutralElapsed;
    private float lastSampleRealtime = -1000f;
    private bool hasPreviousYaw;
    private float previousYawDeg;

    public bool HasFreshSample => hasSample && secondsSinceSample <= Mathf.Max(0.1f, maxSampleAgeSeconds);
    public bool HasPose => HasFreshSample && (useAbsoluteRollPitchReference || !captureNeutralOnStart || neutralReady);
    public Vector3 CompensationEulerDeg => compensationEulerDeg;
    public Vector2 EstimatedRollPitchDeg => new Vector2(estimatedRollDeg, estimatedPitchDeg);
    public Vector2 RelativeRollPitchDeg => new Vector2(relativeRollDeg, relativePitchDeg);
    public Vector2 ReferenceRollPitchDeg => new Vector2(neutralRollDeg, neutralPitchDeg);
    public bool UsesAbsoluteRollPitchReference => useAbsoluteRollPitchReference;
    public string LastSource => lastSource;
    public string LastError => lastError;
    public string StatusLabel
    {
        get
        {
            if (!hasSample)
            {
                return listenForUdp ? $"waiting udp:{listenPort}" : "waiting signaling";
            }

            if (secondsSinceSample > Mathf.Max(0.1f, maxSampleAgeSeconds))
            {
                return $"stale {secondsSinceSample:F1}s";
            }

            if (useAbsoluteRollPitchReference)
            {
                return $"ok {lastSource} lock=level";
            }

            return neutralReady || !captureNeutralOnStart ? $"ok {lastSource}" : "calibrating";
        }
    }

    private struct ImuSample
    {
        public SampleKind Kind;
        public long TimestampUs;
        public float Ax;
        public float Ay;
        public float Az;
        public float Gx;
        public float Gy;
        public float Gz;
        public float RollDeg;
        public float PitchDeg;
        public float YawDeg;
        public string Source;
        public string Raw;
    }

    [Serializable]
    private sealed class JsonImuPacket
    {
        public string type;
        public string source;
        public long timestampUs;
        public long timestamp;
        public float ax;
        public float ay;
        public float az;
        public float gx;
        public float gy;
        public float gz;
        public float roll;
        public float pitch;
        public float yaw;
        public float rollDeg;
        public float pitchDeg;
        public float yawDeg;
    }

    private void Start()
    {
        ResetNeutralState();
        if (listenForUdp && startUdpAutomatically)
        {
            StartReceiver();
        }
    }

    private void Update()
    {
        secondsSinceSample = lastSampleRealtime > 0f ? Time.realtimeSinceStartup - lastSampleRealtime : 999f;
        if (hasSample && secondsSinceSample > Mathf.Max(0.1f, maxSampleAgeSeconds))
        {
            compensationEulerDeg = Vector3.zero;
            yawJitterDeg = 0f;
        }

        if (!TryGetLatestSample(out ImuSample sample))
        {
            return;
        }

        hasSample = true;
        receivedSamples++;
        lastSource = string.IsNullOrWhiteSpace(sample.Source) ? "unknown" : sample.Source;
        lastPacket = sample.Raw;
        lastSampleRealtime = Time.realtimeSinceStartup;
        secondsSinceSample = 0f;

        float dt = Mathf.Max(Time.deltaTime, 1f / 240f);
        float alpha = 1f - Mathf.Exp(-Mathf.Max(0.001f, rollPitchSmoothingHz) * dt);

        if (sample.Kind == SampleKind.Attitude)
        {
            UpdateFromAttitude(sample, dt, alpha);
        }
        else
        {
            UpdateFromRawImu(sample, dt, alpha);
        }
    }

    private void OnDisable()
    {
        StopReceiver();
    }

    private void OnDestroy()
    {
        StopReceiver();
    }

    public void PushRawImuSample(long timestampUs, float ax, float ay, float az, float gx, float gy, float gz, string source = "signaling")
    {
        EnqueueSample(new ImuSample
        {
            Kind = SampleKind.RawImu,
            TimestampUs = timestampUs,
            Ax = ax,
            Ay = ay,
            Az = az,
            Gx = gx,
            Gy = gy,
            Gz = gz,
            Source = source,
            Raw = $"raw {source}"
        });
    }

    public void PushAttitudeSample(long timestampUs, float rollDeg, float pitchDeg, float yawDeg, string source = "signaling")
    {
        EnqueueSample(new ImuSample
        {
            Kind = SampleKind.Attitude,
            TimestampUs = timestampUs,
            RollDeg = rollDeg,
            PitchDeg = pitchDeg,
            YawDeg = yawDeg,
            Source = source,
            Raw = $"att {source}"
        });
    }

    [ContextMenu("Start IMU Receiver")]
    public void StartReceiver()
    {
        StopReceiver();
        lastError = string.Empty;
        ResetNeutralState();

        if (listenPort <= 0)
        {
            lastError = "UDP listen port is disabled.";
            return;
        }

        try
        {
            udpClient = new UdpClient(listenPort);
            receiveLoopRunning = true;
            receiveThread = new Thread(ReceiveLoop)
            {
                IsBackground = true,
                Name = "THETA Z1 IMU UDP Receiver"
            };
            receiveThread.Start();
            udpRunning = true;
            Debug.Log($"[ThetaZ1ImuStabilizer] Listening for IMU on udp://@:{listenPort}", this);
        }
        catch (Exception ex)
        {
            lastError = ex.Message;
            udpRunning = false;
            Debug.LogWarning($"[ThetaZ1ImuStabilizer] Failed to start UDP receiver: {ex.Message}", this);
        }
    }

    [ContextMenu("Stop IMU Receiver")]
    public void StopReceiver()
    {
        udpRunning = false;
        receiveLoopRunning = false;

        try
        {
            udpClient?.Close();
        }
        catch
        {
            // Ignore shutdown races.
        }
        udpClient = null;

        if (receiveThread != null && receiveThread.IsAlive)
        {
            receiveThread.Join(250);
        }
        receiveThread = null;
    }

    [ContextMenu("Recenter Horizon")]
    public void RecenterHorizon()
    {
        if (useAbsoluteRollPitchReference)
        {
            referenceRollDeg = estimatedRollDeg;
            referencePitchDeg = estimatedPitchDeg;
            neutralRollDeg = referenceRollDeg;
            neutralPitchDeg = referencePitchDeg;
            neutralGravity = smoothedGravity.sqrMagnitude > 1e-6f
                ? smoothedGravity.normalized
                : GravityFromRollPitch(referenceRollDeg, referencePitchDeg);
            neutralReady = true;
            yawJitterDeg = 0f;
            Debug.Log($"[ThetaZ1ImuStabilizer] Horizon reference recentered: roll={referenceRollDeg:F2}, pitch={referencePitchDeg:F2}", this);
            return;
        }

        neutralRollDeg = estimatedRollDeg;
        neutralPitchDeg = estimatedPitchDeg;
        neutralGravity = smoothedGravity.sqrMagnitude > 1e-6f ? smoothedGravity.normalized : Vector3.up;
        neutralReady = true;
        neutralElapsed = neutralCalibrationSeconds;
        neutralSampleCount = 1;
        yawJitterDeg = 0f;
        Debug.Log("[ThetaZ1ImuStabilizer] Horizon recentered.", this);
    }

    [ContextMenu("Reset Horizon Reference To Level")]
    public void ResetHorizonReferenceToLevel()
    {
        referenceRollDeg = 0f;
        referencePitchDeg = 0f;
        neutralRollDeg = 0f;
        neutralPitchDeg = 0f;
        neutralGravity = GravityFromRollPitch(0f, 0f);
        neutralReady = true;
        yawJitterDeg = 0f;
        Debug.Log("[ThetaZ1ImuStabilizer] Horizon reference reset to roll=0, pitch=0.", this);
    }

    private void UpdateFromRawImu(ImuSample sample, float dt, float alpha)
    {
        float ax = sample.Ax * accelAxisScale.x;
        float ay = sample.Ay * accelAxisScale.y;
        float az = sample.Az * accelAxisScale.z;
        float accelLength = Mathf.Sqrt(ax * ax + ay * ay + az * az);
        if (accelLength < 1e-5f)
        {
            return;
        }

        Vector3 gravity = new Vector3(ax, ay, az) / accelLength;
        if (smoothedGravity.sqrMagnitude < 1e-6f)
        {
            smoothedGravity = gravity;
        }

        smoothedGravity = Vector3.Slerp(smoothedGravity, gravity, alpha).normalized;

        float rawRollDeg = Mathf.Atan2(smoothedGravity.y, smoothedGravity.z) * Mathf.Rad2Deg;
        float rawPitchDeg = Mathf.Atan2(-smoothedGravity.x, Mathf.Sqrt(smoothedGravity.y * smoothedGravity.y + smoothedGravity.z * smoothedGravity.z)) * Mathf.Rad2Deg;
        estimatedRollDeg = Mathf.LerpAngle(estimatedRollDeg, rawRollDeg, alpha);
        estimatedPitchDeg = Mathf.LerpAngle(estimatedPitchDeg, rawPitchDeg, alpha);

        if (useAbsoluteRollPitchReference && neutralGravity.sqrMagnitude < 1e-6f)
        {
            neutralGravity = GravityFromRollPitch(referenceRollDeg, referencePitchDeg);
            neutralRollDeg = referenceRollDeg;
            neutralPitchDeg = referencePitchDeg;
            neutralReady = true;
        }
        else if (!captureNeutralOnStart && neutralGravity.sqrMagnitude < 1e-6f)
        {
            neutralGravity = smoothedGravity;
            neutralRollDeg = estimatedRollDeg;
            neutralPitchDeg = estimatedPitchDeg;
            neutralReady = true;
        }

        UpdateNeutral(dt, smoothedGravity, estimatedRollDeg, estimatedPitchDeg);

        relativeRollDeg = Mathf.DeltaAngle(neutralRollDeg, estimatedRollDeg);
        relativePitchDeg = Mathf.DeltaAngle(neutralPitchDeg, estimatedPitchDeg);
        UpdateYawJitter(sample, dt);

        Vector3 tiltEuler = Vector3.zero;
        if (compensateRollPitch && neutralReady && neutralGravity.sqrMagnitude > 1e-6f)
        {
            Quaternion currentToNeutral = Quaternion.FromToRotation(smoothedGravity, neutralGravity);
            tiltEuler = NormalizeEuler(currentToNeutral.eulerAngles);
        }

        float pitch = compensateRollPitch ? tiltEuler.x * pitchCompensationSign * rollPitchStrength : 0f;
        float roll = compensateRollPitch ? tiltEuler.z * rollCompensationSign * rollPitchStrength : 0f;
        float yaw = dampYawJitter ? yawJitterDeg * yawCompensationSign : 0f;
        compensationEulerDeg = new Vector3(pitch, yaw, roll);
    }

    private void UpdateFromAttitude(ImuSample sample, float dt, float alpha)
    {
        float roll = sample.RollDeg * attitudeEulerScale.x;
        float pitch = sample.PitchDeg * attitudeEulerScale.y;

        estimatedRollDeg = Mathf.LerpAngle(estimatedRollDeg, roll, alpha);
        estimatedPitchDeg = Mathf.LerpAngle(estimatedPitchDeg, pitch, alpha);

        if (useAbsoluteRollPitchReference)
        {
            neutralRollDeg = referenceRollDeg;
            neutralPitchDeg = referencePitchDeg;
            neutralReady = true;
        }
        else if (!captureNeutralOnStart && !neutralReady)
        {
            neutralRollDeg = estimatedRollDeg;
            neutralPitchDeg = estimatedPitchDeg;
            neutralReady = true;
        }

        UpdateNeutral(dt, Vector3.zero, estimatedRollDeg, estimatedPitchDeg);

        relativeRollDeg = Mathf.DeltaAngle(neutralRollDeg, estimatedRollDeg);
        relativePitchDeg = Mathf.DeltaAngle(neutralPitchDeg, estimatedPitchDeg);
        UpdateYawJitter(sample, dt);

        float pitchCorrection = compensateRollPitch ? relativePitchDeg * pitchCompensationSign * rollPitchStrength : 0f;
        float rollCorrection = compensateRollPitch ? relativeRollDeg * rollCompensationSign * rollPitchStrength : 0f;
        float yawCorrection = dampYawJitter ? yawJitterDeg * yawCompensationSign : 0f;
        compensationEulerDeg = new Vector3(pitchCorrection, yawCorrection, rollCorrection);
    }

    private void ReceiveLoop()
    {
        IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, 0);
        while (receiveLoopRunning)
        {
            try
            {
                byte[] data = udpClient.Receive(ref endpoint);
                string text = System.Text.Encoding.UTF8.GetString(data);
                foreach (string packet in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (TryParsePacket(packet, "udp", out ImuSample sample))
                    {
                        EnqueueSample(sample);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                if (receiveLoopRunning)
                {
                    lastError = "Socket receive failed.";
                }
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }
        }
    }

    private void EnqueueSample(ImuSample sample)
    {
        lock (queueLock)
        {
            samples.Enqueue(sample);
            while (samples.Count > 256)
            {
                samples.Dequeue();
            }
        }
    }

    private bool TryGetLatestSample(out ImuSample latest)
    {
        latest = default;
        bool found = false;
        lock (queueLock)
        {
            while (samples.Count > 0)
            {
                latest = samples.Dequeue();
                found = true;
            }
        }
        return found;
    }

    private void ResetNeutralState()
    {
        neutralReady = useAbsoluteRollPitchReference || !captureNeutralOnStart;
        neutralElapsed = 0f;
        neutralSampleCount = 0;
        neutralRollSum = 0f;
        neutralPitchSum = 0f;
        neutralGravitySum = Vector3.zero;
        neutralRollDeg = useAbsoluteRollPitchReference ? referenceRollDeg : 0f;
        neutralPitchDeg = useAbsoluteRollPitchReference ? referencePitchDeg : 0f;
        neutralGravity = useAbsoluteRollPitchReference
            ? GravityFromRollPitch(referenceRollDeg, referencePitchDeg)
            : Vector3.zero;
        smoothedGravity = Vector3.zero;
        hasSample = false;
        yawJitterDeg = 0f;
        compensationEulerDeg = Vector3.zero;
        secondsSinceSample = 999f;
        lastSampleRealtime = -1000f;
        hasPreviousYaw = false;
        previousYawDeg = 0f;
    }

    private void UpdateNeutral(float dt, Vector3 gravity, float rollDeg, float pitchDeg)
    {
        if (useAbsoluteRollPitchReference)
        {
            neutralRollDeg = referenceRollDeg;
            neutralPitchDeg = referencePitchDeg;
            neutralGravity = GravityFromRollPitch(referenceRollDeg, referencePitchDeg);
            neutralReady = true;
            return;
        }

        if (!captureNeutralOnStart || neutralReady)
        {
            return;
        }

        neutralElapsed += dt;
        neutralRollSum += rollDeg;
        neutralPitchSum += pitchDeg;
        if (gravity.sqrMagnitude > 1e-6f)
        {
            neutralGravitySum += gravity;
        }
        neutralSampleCount++;

        if (neutralElapsed >= neutralCalibrationSeconds && neutralSampleCount > 0)
        {
            neutralRollDeg = neutralRollSum / neutralSampleCount;
            neutralPitchDeg = neutralPitchSum / neutralSampleCount;
            neutralGravity = neutralGravitySum.sqrMagnitude > 1e-6f ? neutralGravitySum.normalized : Vector3.zero;
            neutralReady = true;
            Debug.Log($"[ThetaZ1ImuStabilizer] Neutral horizon captured: roll={neutralRollDeg:F2}, pitch={neutralPitchDeg:F2}", this);
        }
    }

    private void UpdateYawJitter(ImuSample sample, float dt)
    {
        if (!dampYawJitter || yawJitterStrength <= 0f)
        {
            yawJitterDeg = 0f;
            return;
        }

        float yawRate;
        if (sample.Kind == SampleKind.RawImu)
        {
            yawRate = sample.Gz * gyroAxisScale.z;
        }
        else
        {
            float scaledYawDeg = sample.YawDeg * attitudeEulerScale.z;
            if (!hasPreviousYaw)
            {
                hasPreviousYaw = true;
                previousYawDeg = scaledYawDeg;
                yawRate = 0f;
            }
            else
            {
                yawRate = Mathf.DeltaAngle(previousYawDeg, scaledYawDeg) / Mathf.Max(dt, 1e-4f);
                previousYawDeg = scaledYawDeg;
            }
        }
        if (sample.Kind == SampleKind.RawImu && gyroIsRadiansPerSecond)
        {
            yawRate *= Mathf.Rad2Deg;
        }

        yawJitterDeg += yawRate * dt * yawJitterStrength;
        float returnAlpha = 1f - Mathf.Exp(-Mathf.Max(0.001f, yawReturnHz) * dt);
        yawJitterDeg = Mathf.Lerp(yawJitterDeg, 0f, returnAlpha);
        yawJitterDeg = Mathf.Clamp(yawJitterDeg, -maxYawJitterDegrees, maxYawJitterDegrees);
    }

    private static bool TryParsePacket(string packet, string defaultSource, out ImuSample sample)
    {
        sample = default;
        if (string.IsNullOrWhiteSpace(packet))
        {
            return false;
        }

        string trimmed = packet.Trim();
        if (trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            return TryParseJsonPacket(trimmed, defaultSource, out sample);
        }

        string[] parts = trimmed.Split(',');
        if (parts.Length < 5)
        {
            return false;
        }

        string kind = parts[0].Trim().ToUpperInvariant();
        CultureInfo culture = CultureInfo.InvariantCulture;
        if (!long.TryParse(parts[1], NumberStyles.Integer, culture, out long timestampUs))
        {
            timestampUs = 0;
        }

        if ((kind == "Z1ATT" || kind == "Z1ATTITUDE" || kind == "ATT" || kind == "ATTITUDE") && parts.Length >= 5)
        {
            if (!float.TryParse(parts[2], NumberStyles.Float, culture, out float rollDeg) ||
                !float.TryParse(parts[3], NumberStyles.Float, culture, out float pitchDeg) ||
                !float.TryParse(parts[4], NumberStyles.Float, culture, out float yawDeg))
            {
                return false;
            }

            sample = new ImuSample
            {
                Kind = SampleKind.Attitude,
                TimestampUs = timestampUs,
                RollDeg = rollDeg,
                PitchDeg = pitchDeg,
                YawDeg = yawDeg,
                Source = defaultSource,
                Raw = trimmed
            };
            return true;
        }

        if ((kind == "Z1IMU" || kind == "IMU" || kind == "X5IMU") && parts.Length >= 8)
        {
            if (!float.TryParse(parts[2], NumberStyles.Float, culture, out float ax) ||
                !float.TryParse(parts[3], NumberStyles.Float, culture, out float ay) ||
                !float.TryParse(parts[4], NumberStyles.Float, culture, out float az) ||
                !float.TryParse(parts[5], NumberStyles.Float, culture, out float gx) ||
                !float.TryParse(parts[6], NumberStyles.Float, culture, out float gy) ||
                !float.TryParse(parts[7], NumberStyles.Float, culture, out float gz))
            {
                return false;
            }

            sample = new ImuSample
            {
                Kind = SampleKind.RawImu,
                TimestampUs = timestampUs,
                Ax = ax,
                Ay = ay,
                Az = az,
                Gx = gx,
                Gy = gy,
                Gz = gz,
                Source = defaultSource,
                Raw = trimmed
            };
            return true;
        }

        return false;
    }

    private static bool TryParseJsonPacket(string packet, string defaultSource, out ImuSample sample)
    {
        sample = default;
        try
        {
            JsonImuPacket json = JsonUtility.FromJson<JsonImuPacket>(packet);
            if (json == null)
            {
                return false;
            }

            string type = (json.type ?? string.Empty).Trim().ToLowerInvariant();
            long timestampUs = json.timestampUs != 0 ? json.timestampUs : json.timestamp;
            string source = string.IsNullOrWhiteSpace(json.source) ? defaultSource : json.source;

            if (type.Contains("attitude") || type == "att" || type == "imu-attitude")
            {
                float rollDeg = Mathf.Abs(json.rollDeg) > 1e-6f ? json.rollDeg : json.roll;
                float pitchDeg = Mathf.Abs(json.pitchDeg) > 1e-6f ? json.pitchDeg : json.pitch;
                float yawDeg = Mathf.Abs(json.yawDeg) > 1e-6f ? json.yawDeg : json.yaw;
                sample = new ImuSample
                {
                    Kind = SampleKind.Attitude,
                    TimestampUs = timestampUs,
                    RollDeg = rollDeg,
                    PitchDeg = pitchDeg,
                    YawDeg = yawDeg,
                    Source = source,
                    Raw = packet
                };
                return true;
            }

            if (type.Contains("imu") || Mathf.Abs(json.ax) + Mathf.Abs(json.ay) + Mathf.Abs(json.az) > 1e-6f)
            {
                sample = new ImuSample
                {
                    Kind = SampleKind.RawImu,
                    TimestampUs = timestampUs,
                    Ax = json.ax,
                    Ay = json.ay,
                    Az = json.az,
                    Gx = json.gx,
                    Gy = json.gy,
                    Gz = json.gz,
                    Source = source,
                    Raw = packet
                };
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static Vector3 NormalizeEuler(Vector3 euler)
    {
        return new Vector3(
            Mathf.DeltaAngle(0f, euler.x),
            Mathf.DeltaAngle(0f, euler.y),
            Mathf.DeltaAngle(0f, euler.z));
    }

    private static Vector3 GravityFromRollPitch(float rollDeg, float pitchDeg)
    {
        float rollRad = rollDeg * Mathf.Deg2Rad;
        float pitchRad = pitchDeg * Mathf.Deg2Rad;
        float cosPitch = Mathf.Cos(pitchRad);
        return new Vector3(
            -Mathf.Sin(pitchRad),
            Mathf.Sin(rollRad) * cosPitch,
            Mathf.Cos(rollRad) * cosPitch).normalized;
    }
}
