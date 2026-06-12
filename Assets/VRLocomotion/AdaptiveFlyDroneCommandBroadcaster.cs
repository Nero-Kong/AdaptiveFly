using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

[DefaultExecutionOrder(120)]
public class AdaptiveFlyDroneCommandBroadcaster : MonoBehaviour
{
    [Header("Source")]
    public HeadOffsetLocomotion locomotion;
    public bool autoFindLocomotion = true;

    [Header("UDP")]
    public bool broadcastEnabled = true;
    public string destinationHost = "127.0.0.1";
    public int destinationPort = 14560;
    public bool allowBroadcast = false;
    [Min(1f)]
    public float sendRateHz = 30f;
    public bool sendZeroCommandOnDisable = true;

    [Header("Command Limits")]
    [Tooltip("Clamp outgoing body-frame forward/right speed in m/s before sending.")]
    public float maxPlanarSpeedMps = 1f;
    [Tooltip("Clamp outgoing up/down speed in m/s before sending.")]
    public float maxVerticalSpeedMps = 0.5f;
    [Tooltip("Clamp outgoing yaw rate in deg/s before sending.")]
    public float maxYawRateDegPerSecond = 60f;

    [Header("Debug")]
    public bool logPackets = true;
    public int logEveryPackets = 30;

    private const int ProtocolVersion = 1;
    private readonly StringBuilder jsonBuilder = new StringBuilder(768);
    private UdpClient udpClient;
    private IPEndPoint remoteEndPoint;
    private string connectedHost;
    private int connectedPort;
    private int sequence;
    private float nextSendTime;
    private float lastWarningTime = -100f;

    private void OnEnable()
    {
        ResolveLocomotion();
        EnsureSocket();
    }

    private void OnDisable()
    {
        if (sendZeroCommandOnDisable)
        {
            SendPacket(forceZero: true);
        }

        CloseSocket();
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

        ResolveLocomotion();
        if (Time.unscaledTime < nextSendTime)
        {
            return;
        }

        nextSendTime = Time.unscaledTime + 1f / Mathf.Max(1f, sendRateHz);
        SendPacket(forceZero: false);
    }

    private void ResolveLocomotion()
    {
        if (locomotion != null || !autoFindLocomotion)
        {
            return;
        }

        locomotion = GetComponent<HeadOffsetLocomotion>();
        if (locomotion == null)
        {
            locomotion = FindAnyObjectByType<HeadOffsetLocomotion>();
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
            WarnThrottled("AdaptiveFly drone command UDP destination is invalid.");
            return false;
        }

        try
        {
            IPAddress[] addresses = Dns.GetHostAddresses(destinationHost);
            if (addresses == null || addresses.Length == 0)
            {
                WarnThrottled($"AdaptiveFly drone command UDP destination '{destinationHost}' did not resolve.");
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
            WarnThrottled($"AdaptiveFly drone command UDP setup failed: {ex.Message}");
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

    private void SendPacket(bool forceZero)
    {
        if (!EnsureSocket())
        {
            return;
        }

        bool hasLocomotion = locomotion != null;
        Vector3 localVelocity = Vector3.zero;
        Vector2 planarCommand = Vector2.zero;
        float upMps = 0f;
        float yawDegPerSecond = 0f;
        bool hasBodyAnchor = false;
        bool usingHmdFallback = false;

        if (!forceZero && hasLocomotion)
        {
            localVelocity = locomotion.DebugSmoothedPlanarVelocityLocal;
            upMps = locomotion.DebugVerticalCommand;
            yawDegPerSecond = locomotion.DebugYawRateDegPerSecond;
            planarCommand = locomotion.DebugPlanarCommand;
            hasBodyAnchor = locomotion.DebugHasBodyAnchor;
            usingHmdFallback = locomotion.DebugUsingInitialHeadReferenceFallback;
        }

        float rightMps = Clamp(localVelocity.x, maxPlanarSpeedMps);
        float forwardMps = Clamp(localVelocity.z, maxPlanarSpeedMps);
        upMps = Clamp(upMps, maxVerticalSpeedMps);
        yawDegPerSecond = Clamp(yawDegPerSecond, maxYawRateDegPerSecond);

        float planarLimit = Mathf.Max(1e-4f, Mathf.Abs(maxPlanarSpeedMps));
        float verticalLimit = Mathf.Max(1e-4f, Mathf.Abs(maxVerticalSpeedMps));
        float yawLimit = Mathf.Max(1e-4f, Mathf.Abs(maxYawRateDegPerSecond));

        BuildJson(
            hasLocomotion,
            forceZero,
            rightMps,
            forwardMps,
            upMps,
            yawDegPerSecond,
            planarCommand,
            planarLimit,
            verticalLimit,
            yawLimit,
            hasBodyAnchor,
            usingHmdFallback);

        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(jsonBuilder.ToString());
            udpClient.Send(bytes, bytes.Length, remoteEndPoint);

            if (logPackets && logEveryPackets > 0 && sequence % logEveryPackets == 0)
            {
                Debug.Log(
                    $"{nameof(AdaptiveFlyDroneCommandBroadcaster)} sent seq={sequence} " +
                    $"fwd={forwardMps:0.00}m/s right={rightMps:0.00}m/s up={upMps:0.00}m/s yaw={yawDegPerSecond:0.0}deg/s " +
                    $"to {destinationHost}:{destinationPort}.",
                    this);
            }
        }
        catch (Exception ex)
        {
            WarnThrottled($"AdaptiveFly drone command UDP send failed: {ex.Message}");
        }
    }

    private void BuildJson(
        bool hasLocomotion,
        bool forceZero,
        float rightMps,
        float forwardMps,
        float upMps,
        float yawDegPerSecond,
        Vector2 planarCommand,
        float planarLimit,
        float verticalLimit,
        float yawLimit,
        bool hasBodyAnchor,
        bool usingHmdFallback)
    {
        sequence++;
        jsonBuilder.Clear();
        jsonBuilder.Append('{');
        AppendJson("type", "adaptivefly_control", comma: false);
        AppendJson("version", ProtocolVersion);
        AppendJson("seq", sequence);
        AppendJson("time_s", Time.realtimeSinceStartupAsDouble);
        AppendJson("frame", "body_frd");
        AppendJson("valid", hasLocomotion && !forceZero);
        AppendJson("has_body_anchor", hasBodyAnchor);
        AppendJson("using_hmd_fallback", usingHmdFallback);
        AppendJson("right_mps", rightMps);
        AppendJson("forward_mps", forwardMps);
        AppendJson("up_mps", upMps);
        AppendJson("down_mps", -upMps);
        AppendJson("yaw_deg_s", yawDegPerSecond);
        AppendJson("cmd_right", Mathf.Clamp(rightMps / planarLimit, -1f, 1f));
        AppendJson("cmd_forward", Mathf.Clamp(forwardMps / planarLimit, -1f, 1f));
        AppendJson("cmd_up", Mathf.Clamp(upMps / verticalLimit, -1f, 1f));
        AppendJson("cmd_down", Mathf.Clamp(-upMps / verticalLimit, -1f, 1f));
        AppendJson("cmd_yaw", Mathf.Clamp(yawDegPerSecond / yawLimit, -1f, 1f));
        AppendJson("planar_input_x", planarCommand.x);
        AppendJson("planar_input_z", planarCommand.y);
        jsonBuilder.Append('}');
    }

    private void AppendJson(string key, string value, bool comma = true)
    {
        if (comma)
        {
            jsonBuilder.Append(',');
        }

        jsonBuilder.Append('"').Append(key).Append("\":\"");
        jsonBuilder.Append(value.Replace("\\", "\\\\").Replace("\"", "\\\""));
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

    private static float Clamp(float value, float maxAbs)
    {
        maxAbs = Mathf.Max(0f, maxAbs);
        return Mathf.Clamp(value, -maxAbs, maxAbs);
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
