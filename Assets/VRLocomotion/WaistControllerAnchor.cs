using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

[DefaultExecutionOrder(-200)]
public class WaistControllerAnchor : MonoBehaviour
{
    public enum ControllerHand
    {
        Left,
        Right
    }

    [Header("Controller")]
    public ControllerHand controllerHand = ControllerHand.Left;
    public bool requireTrackedPosition = true;
    public bool requireTrackedRotation = true;

    [Header("Rig")]
    public XROrigin xrOrigin;
    public Transform trackingSpace;
    public BodyAnchorProvider bodyAnchorProvider;
    public bool configureBodyAnchorProvider = true;
    public bool clearProviderWhenUntracked = true;

    [Header("Debug")]
    public bool logStatus = true;
    public float logInterval = 2f;

    public Transform AnchorTransform => anchorTransform;
    public bool IsTracked => isTracked;
    public string LastStatus => lastStatus;

    private readonly List<InputDevice> devices = new List<InputDevice>();
    private InputDevice controllerDevice;
    private Transform anchorTransform;
    private bool isTracked;
    private string lastStatus = "Waist controller anchor has not been queried.";
    private float nextLogTime;

    private void Awake()
    {
        ResolveReferences();
        EnsureAnchorTransform();
        ConfigureBodyAnchorProvider();
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsureAnchorTransform();
        ConfigureBodyAnchorProvider();
        TryRefreshControllerDevice();
    }

    private void OnDisable()
    {
        isTracked = false;

        if (clearProviderWhenUntracked &&
            bodyAnchorProvider != null &&
            bodyAnchorProvider.externalAnchor == anchorTransform)
        {
            bodyAnchorProvider.externalAnchor = null;
        }
    }

    private void Update()
    {
        ResolveReferences();
        EnsureAnchorTransform();
        ConfigureBodyAnchorProvider();

        if (!controllerDevice.isValid)
        {
            TryRefreshControllerDevice();
        }

        bool trackedThisFrame = TryUpdateAnchorPose();
        if (clearProviderWhenUntracked &&
            bodyAnchorProvider != null &&
            bodyAnchorProvider.externalAnchor == anchorTransform &&
            !trackedThisFrame)
        {
            bodyAnchorProvider.externalAnchor = null;
        }

        LogStatusIfNeeded(trackedThisFrame);
    }

    private void ResolveReferences()
    {
        if (xrOrigin == null)
        {
            xrOrigin = GetComponent<XROrigin>();
        }

        if (xrOrigin == null)
        {
            xrOrigin = FindAnyObjectByType<XROrigin>();
        }

        if (trackingSpace == null && xrOrigin != null && xrOrigin.CameraFloorOffsetObject != null)
        {
            trackingSpace = xrOrigin.CameraFloorOffsetObject.transform;
        }

        if (trackingSpace == null)
        {
            trackingSpace = transform;
        }

        if (bodyAnchorProvider == null)
        {
            bodyAnchorProvider = GetComponent<BodyAnchorProvider>();
        }

        if (bodyAnchorProvider == null)
        {
            bodyAnchorProvider = gameObject.AddComponent<BodyAnchorProvider>();
        }
    }

    private void EnsureAnchorTransform()
    {
        if (anchorTransform == null)
        {
            var anchorObject = new GameObject($"AdaptiveFly {controllerHand} Controller Waist Anchor");
            anchorObject.hideFlags = HideFlags.DontSave;
            anchorTransform = anchorObject.transform;
        }

        if (anchorTransform.parent != trackingSpace)
        {
            anchorTransform.SetParent(trackingSpace, false);
        }
    }

    private void ConfigureBodyAnchorProvider()
    {
        if (!configureBodyAnchorProvider || bodyAnchorProvider == null)
        {
            return;
        }

        bodyAnchorProvider.source = BodyAnchorProvider.AnchorSource.ExternalTransform;
        bodyAnchorProvider.target = transform;

        if (isTracked || !clearProviderWhenUntracked)
        {
            bodyAnchorProvider.externalAnchor = anchorTransform;
        }
    }

    private bool TryRefreshControllerDevice()
    {
        devices.Clear();
        InputDeviceCharacteristics handCharacteristic = controllerHand == ControllerHand.Left
            ? InputDeviceCharacteristics.Left
            : InputDeviceCharacteristics.Right;
        InputDeviceCharacteristics characteristics =
            InputDeviceCharacteristics.Controller |
            InputDeviceCharacteristics.TrackedDevice |
            handCharacteristic;

        InputDevices.GetDevicesWithCharacteristics(characteristics, devices);
        for (int i = 0; i < devices.Count; i++)
        {
            InputDevice candidate = devices[i];
            bool leftHand = controllerHand == ControllerHand.Left;
            if (XRDeviceFilters.IsHandController(candidate, leftHand))
            {
                controllerDevice = candidate;
                lastStatus = $"Using {controllerHand} controller {XRDeviceFilters.Describe(candidate)} as waist anchor.";
                return true;
            }
        }

        controllerDevice = default;
        lastStatus = $"{controllerHand} controller was not found.";
        return false;
    }

    private bool TryUpdateAnchorPose()
    {
        isTracked = false;

        if (!controllerDevice.isValid && !TryRefreshControllerDevice())
        {
            return false;
        }

        bool hasPosition = controllerDevice.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 localPosition);
        bool hasRotation = controllerDevice.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion localRotation);
        bool hasTrackingState = controllerDevice.TryGetFeatureValue(CommonUsages.trackingState, out InputTrackingState trackingState);
        bool positionTracked = !hasTrackingState || (trackingState & InputTrackingState.Position) != 0;
        bool rotationTracked = !hasTrackingState || (trackingState & InputTrackingState.Rotation) != 0;

        if (!hasPosition || !hasRotation ||
            (requireTrackedPosition && !positionTracked) ||
            (requireTrackedRotation && !rotationTracked))
        {
            lastStatus =
                $"{controllerHand} controller pose is incomplete. " +
                $"Position: {hasPosition}/{positionTracked}, rotation: {hasRotation}/{rotationTracked}.";
            return false;
        }

        anchorTransform.localPosition = localPosition;
        anchorTransform.localRotation = localRotation;
        isTracked = true;
        ConfigureBodyAnchorProvider();
        lastStatus =
            $"{controllerHand} controller waist anchor active. " +
            $"local=({localPosition.x:0.000}, {localPosition.y:0.000}, {localPosition.z:0.000}).";
        return true;
    }

    private void LogStatusIfNeeded(bool trackedThisFrame)
    {
        if (!logStatus || Time.unscaledTime < nextLogTime)
        {
            return;
        }

        if (trackedThisFrame)
        {
            Debug.Log($"{nameof(WaistControllerAnchor)} on '{name}': {lastStatus}", this);
        }
        else
        {
            Debug.LogWarning($"{nameof(WaistControllerAnchor)} on '{name}': {lastStatus}", this);
        }

        nextLogTime = Time.unscaledTime + Mathf.Max(0.25f, logInterval);
    }
}
