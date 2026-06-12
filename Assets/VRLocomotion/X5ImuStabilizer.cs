using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

/// <summary>
/// Receives Insta360 X5 IMU samples from the Edge2 side-channel and estimates a
/// horizon-stabilization correction. The correction intentionally compensates
/// roll/pitch strongly while only damping short yaw jitter, so user yaw control
/// and aircraft heading remain intact.
/// </summary>
[AddComponentMenu("VR Locomotion/X5 IMU Stabilizer")]
public class X5ImuStabilizer : MonoBehaviour
{
    [Header("UDP")]
    public int listenPort = 5601;
    public bool startAutomatically = true;

    [Header("Axis Calibration")]
    [Tooltip("Per-axis sign/remap for accelerometer values before roll/pitch estimation.")]
    public Vector3 accelAxisScale = Vector3.one;

    [Tooltip("Per-axis sign/remap for gyro values before yaw jitter damping.")]
    public Vector3 gyroAxisScale = Vector3.one;

    [Tooltip("X5 SDK gyro values are expected to be radians/second. Disable if your SDK reports degrees/second.")]
    public bool gyroIsRadiansPerSecond = true;

    [Header("Roll/Pitch Stabilization")]
    public bool compensateRollPitch = true;

    [Range(0f, 1.5f)]
    public float rollPitchStrength = 1f;

    [Tooltip("Higher values follow IMU roll/pitch faster. Lower values are smoother.")]
    [Range(0.1f, 30f)]
    public float rollPitchSmoothingHz = 8f;

    [Tooltip("Capture the first stable pose as the neutral horizon.")]
    public bool captureNeutralOnStart = true;

    [Range(0.1f, 5f)]
    public float neutralCalibrationSeconds = 1f;

    [Tooltip("Multiply estimated pitch before sending it to the shader. Flip sign here if compensation is inverted.")]
    public float pitchCompensationSign = -1f;

    [Tooltip("Multiply estimated roll before sending it to the shader. Flip sign here if compensation is inverted.")]
    public float rollCompensationSign = -1f;

    [Header("Yaw Jitter Damping")]
    [Tooltip("Do not lock yaw. This only integrates a tiny, leaky correction for high-frequency yaw shake.")]
    public bool dampYawJitter = true;

    [Range(0f, 0.5f)]
    public float yawJitterStrength = 0.04f;

    [Range(0.1f, 20f)]
    public float yawReturnHz = 4f;

    [Range(0f, 8f)]
    public float maxYawJitterDegrees = 2f;

    public float yawCompensationSign = -1f;

    [Header("Status")]
    [SerializeField] private bool running;
    [SerializeField] private bool hasSample;
    [SerializeField] private bool neutralReady;
    [SerializeField] private float estimatedRollDeg;
    [SerializeField] private float estimatedPitchDeg;
    [SerializeField] private float relativeRollDeg;
    [SerializeField] private float relativePitchDeg;
    [SerializeField] private float yawJitterDeg;
    [SerializeField] private Vector3 compensationEulerDeg;
    [SerializeField] private int receivedSamples;
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

    public bool HasPose => hasSample && (!captureNeutralOnStart || neutralReady);
    public Vector3 CompensationEulerDeg => compensationEulerDeg;
    public string LastError => lastError;

    private struct ImuSample
    {
        public long Timestamp;
        public float Ax;
        public float Ay;
        public float Az;
        public float Gx;
        public float Gy;
        public float Gz;
        public string Raw;
    }

    private void Start()
    {
        if (startAutomatically)
        {
            StartReceiver();
        }
    }

    private void Update()
    {
        if (!TryGetLatestSample(out ImuSample sample))
        {
            return;
        }

        hasSample = true;
        receivedSamples++;
        lastPacket = sample.Raw;

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

        float dt = Mathf.Max(Time.deltaTime, 1f / 240f);
        float alpha = 1f - Mathf.Exp(-Mathf.Max(0.001f, rollPitchSmoothingHz) * dt);
        smoothedGravity = Vector3.Slerp(smoothedGravity, gravity, alpha).normalized;

        float rawRollDeg = Mathf.Atan2(smoothedGravity.y, smoothedGravity.z) * Mathf.Rad2Deg;
        float rawPitchDeg = Mathf.Atan2(-smoothedGravity.x, Mathf.Sqrt(smoothedGravity.y * smoothedGravity.y + smoothedGravity.z * smoothedGravity.z)) * Mathf.Rad2Deg;
        estimatedRollDeg = Mathf.LerpAngle(estimatedRollDeg, rawRollDeg, alpha);
        estimatedPitchDeg = Mathf.LerpAngle(estimatedPitchDeg, rawPitchDeg, alpha);

        if (!captureNeutralOnStart && neutralGravity.sqrMagnitude < 1e-6f)
        {
            neutralGravity = smoothedGravity;
            neutralRollDeg = estimatedRollDeg;
            neutralPitchDeg = estimatedPitchDeg;
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

    private void OnDisable()
    {
        StopReceiver();
    }

    private void OnDestroy()
    {
        StopReceiver();
    }

    [ContextMenu("Start IMU Receiver")]
    public void StartReceiver()
    {
        StopReceiver();
        lastError = string.Empty;
        neutralReady = !captureNeutralOnStart;
        neutralElapsed = 0f;
        neutralSampleCount = 0;
        neutralRollSum = 0f;
        neutralPitchSum = 0f;
        neutralGravitySum = Vector3.zero;
        neutralGravity = Vector3.zero;
        smoothedGravity = Vector3.zero;
        yawJitterDeg = 0f;

        try
        {
            udpClient = new UdpClient(listenPort);
            receiveLoopRunning = true;
            receiveThread = new Thread(ReceiveLoop)
            {
                IsBackground = true,
                Name = "X5 IMU UDP Receiver"
            };
            receiveThread.Start();
            running = true;
            Debug.Log($"[X5ImuStabilizer] Listening for X5 IMU on udp://@:{listenPort}");
        }
        catch (Exception ex)
        {
            lastError = ex.Message;
            running = false;
            Debug.LogWarning($"[X5ImuStabilizer] Failed to start UDP receiver: {ex.Message}");
        }
    }

    [ContextMenu("Stop IMU Receiver")]
    public void StopReceiver()
    {
        running = false;
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
        neutralRollDeg = estimatedRollDeg;
        neutralPitchDeg = estimatedPitchDeg;
        neutralGravity = smoothedGravity.sqrMagnitude > 1e-6f ? smoothedGravity.normalized : Vector3.up;
        neutralReady = true;
        neutralElapsed = neutralCalibrationSeconds;
        neutralSampleCount = 1;
        yawJitterDeg = 0f;
        Debug.Log("[X5ImuStabilizer] Horizon recentered.");
    }

    private void ReceiveLoop()
    {
        IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, 0);
        while (receiveLoopRunning)
        {
            try
            {
                byte[] data = udpClient.Receive(ref endpoint);
                string packet = System.Text.Encoding.ASCII.GetString(data);
                if (TryParsePacket(packet, out ImuSample sample))
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

    private static bool TryParsePacket(string packet, out ImuSample sample)
    {
        sample = default;
        if (string.IsNullOrWhiteSpace(packet))
        {
            return false;
        }

        string[] parts = packet.Trim().Split(',');
        if (parts.Length < 8 || parts[0] != "X5IMU")
        {
            return false;
        }

        CultureInfo culture = CultureInfo.InvariantCulture;
        if (!long.TryParse(parts[1], NumberStyles.Integer, culture, out long timestamp))
        {
            return false;
        }

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
            Timestamp = timestamp,
            Ax = ax,
            Ay = ay,
            Az = az,
            Gx = gx,
            Gy = gy,
            Gz = gz,
            Raw = packet.Trim()
        };
        return true;
    }

    private void UpdateNeutral(float dt, Vector3 gravity, float rollDeg, float pitchDeg)
    {
        if (!captureNeutralOnStart || neutralReady)
        {
            return;
        }

        neutralElapsed += dt;
        neutralRollSum += rollDeg;
        neutralPitchSum += pitchDeg;
        neutralGravitySum += gravity;
        neutralSampleCount++;

        if (neutralElapsed >= neutralCalibrationSeconds && neutralSampleCount > 0)
        {
            neutralRollDeg = neutralRollSum / neutralSampleCount;
            neutralPitchDeg = neutralPitchSum / neutralSampleCount;
            neutralGravity = neutralGravitySum.normalized;
            neutralReady = true;
            Debug.Log($"[X5ImuStabilizer] Neutral horizon captured: roll={neutralRollDeg:F2}, pitch={neutralPitchDeg:F2}");
        }
    }

    private void UpdateYawJitter(ImuSample sample, float dt)
    {
        if (!dampYawJitter || yawJitterStrength <= 0f)
        {
            yawJitterDeg = 0f;
            return;
        }

        float yawRate = sample.Gz * gyroAxisScale.z;
        if (gyroIsRadiansPerSecond)
        {
            yawRate *= Mathf.Rad2Deg;
        }

        yawJitterDeg += yawRate * dt * yawJitterStrength;
        float returnAlpha = 1f - Mathf.Exp(-Mathf.Max(0.001f, yawReturnHz) * dt);
        yawJitterDeg = Mathf.Lerp(yawJitterDeg, 0f, returnAlpha);
        yawJitterDeg = Mathf.Clamp(yawJitterDeg, -maxYawJitterDegrees, maxYawJitterDegrees);
    }

    private static Vector3 NormalizeEuler(Vector3 euler)
    {
        return new Vector3(
            Mathf.DeltaAngle(0f, euler.x),
            Mathf.DeltaAngle(0f, euler.y),
            Mathf.DeltaAngle(0f, euler.z));
    }
}
