using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

[DefaultExecutionOrder(121)]
public class AdaptiveFlyArmCommandBroadcaster : MonoBehaviour
{
    [Header("Source")]
    public Transform controlTarget;
    public bool autoBindControlTargetFromMainCamera = true;
    public Transform armOrigin;
    public bool useOwnTransformAsArmOrigin = true;

    [Header("UDP")]
    public bool broadcastEnabled = true;
    public string destinationHost = "127.0.0.1";
    public int destinationPort = 14561;
    public bool allowBroadcast = false;
    [Min(1f)]
    public float sendRateHz = 30f;
    public bool sendHoldCommandOnDisable = true;

    [Header("Command Limits")]
    [Tooltip("Clamp outgoing arm-origin-relative position components in meters before sending.")]
    public float maxRelativePositionMeters = 0.5f;
    [Tooltip("Clamp outgoing relative linear velocity components in m/s before sending.")]
    public float maxLinearSpeedMps = 0.5f;
    [Tooltip("Clamp outgoing relative angular velocity components in rad/s before sending.")]
    public float maxAngularSpeedRadPerSecond = 1f;

    [Header("Debug")]
    public bool logPackets = true;
    public int logEveryPackets = 30;

    private const int ProtocolVersion = 1;
    private readonly StringBuilder jsonBuilder = new StringBuilder(1024);
    private UdpClient udpClient;
    private IPEndPoint remoteEndPoint;
    private string connectedHost;
    private int connectedPort;
    private int sequence;
    private float nextSendTime;
    private float lastWarningTime = -100f;
    private bool previousPoseValid;
    private Vector3 previousRelativePosition;
    private Quaternion previousRelativeRotation = Quaternion.identity;
    private float previousSampleTime;

    private void OnEnable()
    {
        ResolveSourceTransforms();
        EnsureSocket();
        ResetVelocityState();
    }

    private void OnDisable()
    {
        if (sendHoldCommandOnDisable)
        {
            SendPacket(forceHold: true);
        }

        CloseSocket();
        ResetVelocityState();
    }

    private void OnDestroy()
    {
        CloseSocket();
    }

    private void LateUpdate()
    {
        if (!broadcastEnabled)
        {
            return;
        }

        ResolveSourceTransforms();
        if (Time.unscaledTime < nextSendTime)
        {
            return;
        }

        nextSendTime = Time.unscaledTime + 1f / Mathf.Max(1f, sendRateHz);
        SendPacket(forceHold: false);
    }

    private void ResolveSourceTransforms()
    {
        if (controlTarget == null && autoBindControlTargetFromMainCamera && Camera.main != null)
        {
            controlTarget = Camera.main.transform;
        }

        if (armOrigin == null && useOwnTransformAsArmOrigin)
        {
            armOrigin = transform;
        }
    }

    private bool EnsureSocket()
    {
        if (udpClient != null &&
            remoteEndPoint != null &&
            connectedHost == destinationHost &&
            connectedPort == destinationPort)
        {
            return true;
        }

        CloseSocket();

        if (string.IsNullOrWhiteSpace(destinationHost) || destinationPort <= 0 || destinationPort > 65535)
        {
            WarnThrottled("AdaptiveFly arm command UDP destination is invalid.");
            return false;
        }

        try
        {
            IPAddress[] addresses = Dns.GetHostAddresses(destinationHost);
            if (addresses == null || addresses.Length == 0)
            {
                WarnThrottled($"AdaptiveFly arm command UDP destination '{destinationHost}' did not resolve.");
                return false;
            }

            remoteEndPoint = new IPEndPoint(addresses[0], destinationPort);
            udpClient = new UdpClient();
            udpClient.EnableBroadcast = allowBroadcast;
            connectedHost = destinationHost;
            connectedPort = destinationPort;
            return true;
        }
        catch (Exception ex)
        {
            WarnThrottled($"AdaptiveFly arm command UDP setup failed: {ex.Message}");
            CloseSocket();
            return false;
        }
    }

    private void CloseSocket()
    {
        if (udpClient != null)
        {
            udpClient.Close();
            udpClient = null;
        }

        remoteEndPoint = null;
        connectedHost = null;
        connectedPort = 0;
    }

    private void SendPacket(bool forceHold)
    {
        if (!EnsureSocket())
        {
            return;
        }

        bool hasSource = controlTarget != null && armOrigin != null;
        bool valid = hasSource && !forceHold;
        Vector3 relativePosition = Vector3.zero;
        Quaternion relativeRotation = Quaternion.identity;
        Vector3 relativeLinearVelocity = Vector3.zero;
        Vector3 relativeAngularVelocity = Vector3.zero;
        float sampleTime = Time.unscaledTime;

        if (valid)
        {
            relativePosition = armOrigin.InverseTransformPoint(controlTarget.position);
            relativeRotation = Quaternion.Inverse(armOrigin.rotation) * controlTarget.rotation;
            relativeRotation = Normalize(relativeRotation);

            float dt = sampleTime - previousSampleTime;
            if (previousPoseValid && dt > 1e-4f)
            {
                relativeLinearVelocity = (relativePosition - previousRelativePosition) / dt;
                relativeAngularVelocity = ComputeAngularVelocity(previousRelativeRotation, relativeRotation, dt);
            }

            previousPoseValid = true;
            previousRelativePosition = relativePosition;
            previousRelativeRotation = relativeRotation;
            previousSampleTime = sampleTime;
        }
        else
        {
            ResetVelocityState();
        }

        relativePosition = ClampVector(relativePosition, maxRelativePositionMeters);
        relativeLinearVelocity = ClampVector(relativeLinearVelocity, maxLinearSpeedMps);
        relativeAngularVelocity = ClampVector(relativeAngularVelocity, maxAngularSpeedRadPerSecond);

        BuildJson(
            valid,
            relativePosition,
            relativeRotation,
            relativeLinearVelocity,
            relativeAngularVelocity);

        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(jsonBuilder.ToString());
            udpClient.Send(bytes, bytes.Length, remoteEndPoint);

            if (logPackets && logEveryPackets > 0 && sequence % logEveryPackets == 0)
            {
                Debug.Log(
                    $"{nameof(AdaptiveFlyArmCommandBroadcaster)} sent seq={sequence} " +
                    $"pos=({relativePosition.x:0.000},{relativePosition.y:0.000},{relativePosition.z:0.000})m " +
                    $"to {destinationHost}:{destinationPort}.",
                    this);
            }
        }
        catch (Exception ex)
        {
            WarnThrottled($"AdaptiveFly arm command UDP send failed: {ex.Message}");
        }
    }

    private void BuildJson(
        bool valid,
        Vector3 relativePosition,
        Quaternion relativeRotation,
        Vector3 relativeLinearVelocity,
        Vector3 relativeAngularVelocity)
    {
        sequence++;
        jsonBuilder.Clear();
        jsonBuilder.Append('{');
        AppendJson("type", "arm_ee_pose_relative", comma: false);
        AppendJson("version", ProtocolVersion);
        AppendJson("seq", sequence);
        AppendJson("time_s", Time.realtimeSinceStartupAsDouble);
        AppendJson("frame", "arm_origin_unity_relative");
        AppendJson("relative_to", "arm_origin");
        AppendJson("valid", valid);
        AppendJson("control_target_name", controlTarget != null ? controlTarget.name : "");
        AppendJson("origin_name", armOrigin != null ? armOrigin.name : "");
        AppendJson("x_unity_m", relativePosition.x);
        AppendJson("y_unity_m", relativePosition.y);
        AppendJson("z_unity_m", relativePosition.z);
        AppendJson("qx_unity", relativeRotation.x);
        AppendJson("qy_unity", relativeRotation.y);
        AppendJson("qz_unity", relativeRotation.z);
        AppendJson("qw_unity", relativeRotation.w);
        AppendJson("vx_unity_mps", relativeLinearVelocity.x);
        AppendJson("vy_unity_mps", relativeLinearVelocity.y);
        AppendJson("vz_unity_mps", relativeLinearVelocity.z);
        AppendJson("wx_unity_rad_s", relativeAngularVelocity.x);
        AppendJson("wy_unity_rad_s", relativeAngularVelocity.y);
        AppendJson("wz_unity_rad_s", relativeAngularVelocity.z);
        jsonBuilder.Append('}');
    }

    private void ResetVelocityState()
    {
        previousPoseValid = false;
        previousRelativePosition = Vector3.zero;
        previousRelativeRotation = Quaternion.identity;
        previousSampleTime = 0f;
    }

    private static Vector3 ComputeAngularVelocity(Quaternion previous, Quaternion current, float dt)
    {
        Quaternion delta = current * Quaternion.Inverse(previous);
        delta = Normalize(delta);
        if (delta.w < 0f)
        {
            delta.x = -delta.x;
            delta.y = -delta.y;
            delta.z = -delta.z;
            delta.w = -delta.w;
        }

        delta.ToAngleAxis(out float angleDeg, out Vector3 axis);
        if (!IsFinite(angleDeg) || axis.sqrMagnitude < 1e-8f)
        {
            return Vector3.zero;
        }

        if (angleDeg > 180f)
        {
            angleDeg -= 360f;
        }

        return axis.normalized * (angleDeg * Mathf.Deg2Rad / dt);
    }

    private static Quaternion Normalize(Quaternion rotation)
    {
        float magnitude = Mathf.Sqrt(
            rotation.x * rotation.x +
            rotation.y * rotation.y +
            rotation.z * rotation.z +
            rotation.w * rotation.w);
        if (magnitude <= 1e-8f || !IsFinite(magnitude))
        {
            return Quaternion.identity;
        }

        float inv = 1f / magnitude;
        return new Quaternion(rotation.x * inv, rotation.y * inv, rotation.z * inv, rotation.w * inv);
    }

    private static Vector3 ClampVector(Vector3 value, float maxAbs)
    {
        maxAbs = Mathf.Max(0f, maxAbs);
        return new Vector3(
            Mathf.Clamp(value.x, -maxAbs, maxAbs),
            Mathf.Clamp(value.y, -maxAbs, maxAbs),
            Mathf.Clamp(value.z, -maxAbs, maxAbs));
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private void AppendJson(string key, string value, bool comma = true)
    {
        if (comma)
        {
            jsonBuilder.Append(',');
        }

        jsonBuilder.Append('"').Append(key).Append("\":\"");
        jsonBuilder.Append((value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\""));
        jsonBuilder.Append('"');
    }

    private void AppendJson(string key, int value)
    {
        jsonBuilder.Append(',').Append('"').Append(key).Append("\":").Append(value);
    }

    private void AppendJson(string key, bool value)
    {
        jsonBuilder.Append(',').Append('"').Append(key).Append("\":").Append(value ? "true" : "false");
    }

    private void AppendJson(string key, float value)
    {
        jsonBuilder.Append(',').Append('"').Append(key).Append("\":");
        jsonBuilder.Append(value.ToString("0.######", CultureInfo.InvariantCulture));
    }

    private void AppendJson(string key, double value)
    {
        jsonBuilder.Append(',').Append('"').Append(key).Append("\":");
        jsonBuilder.Append(value.ToString("0.######", CultureInfo.InvariantCulture));
    }

    private void WarnThrottled(string message)
    {
        if (Time.unscaledTime - lastWarningTime < 2f)
        {
            return;
        }

        lastWarningTime = Time.unscaledTime;
        Debug.LogWarning(message, this);
    }
}
