using UnityEngine;

/// <summary>
/// 4K preset wrapper for UdpVideoStreamReceiver.
/// It fixes the incoming UDP/MPEG-TS frame size to the X5 4K panorama stream.
/// </summary>
[AddComponentMenu("VR Locomotion/UDP Video Stream Receiver 4K No AI")]
public class UdpVideoStreamReceiver4KNoAi : UdpVideoStreamReceiver
{
    private const int FourKPanoramaWidth = 3840;
    private const int FourKPanoramaHeight = 1920;

    [Header("4K No AI Preset")]
    [Tooltip("Disable other UdpVideoStreamReceiver components on this GameObject to avoid UDP port conflicts.")]
    public bool disableSiblingReceivers = true;

    private void Awake()
    {
        ApplyPreset();
        DisableSiblingReceivers();
    }

    private void Reset()
    {
        ApplyPreset();
    }

    private void OnValidate()
    {
        ApplyPreset();
    }

    protected override void Start()
    {
        ApplyPreset();
        base.Start();
    }

    private void ApplyPreset()
    {
        forcedWidth = FourKPanoramaWidth;
        forcedHeight = FourKPanoramaHeight;
        outputWidthOverride = 0;
        outputHeightOverride = 0;
    }

    private void DisableSiblingReceivers()
    {
        if (!disableSiblingReceivers)
        {
            return;
        }

        UdpVideoStreamReceiver[] receivers = GetComponents<UdpVideoStreamReceiver>();
        foreach (UdpVideoStreamReceiver receiver in receivers)
        {
            if (receiver != null && !ReferenceEquals(receiver, this))
            {
                receiver.enabled = false;
            }
        }
    }
}
