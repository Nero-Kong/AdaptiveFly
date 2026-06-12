using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DefaultExecutionOrder(80)]
public class TransparentUpperBodyVisualizer : MonoBehaviour
{
    private const string HeavyStationSceneName = "HeavyStation_demo";
    private const string OceanSceneName = "Ocean";
    private const string CyberPunkSceneName = "CyberPunk";
    private const string LeftSampleHandPrefabPath = "Assets/Samples/XRHands/HandVisualizer/Prefabs/Left Hand Tracking.prefab";
    private const string RightSampleHandPrefabPath = "Assets/Samples/XRHands/HandVisualizer/Prefabs/Right Hand Tracking.prefab";
    private const string SampleHandMaterialPath = "Assets/Samples/XRHands/HandVisualizer/Materials/HandsDefaultMaterial.mat";
    private const string LeftHandResourcePath = "AdaptiveFlyXRHands/Left Hand Tracking";
    private const string RightHandResourcePath = "AdaptiveFlyXRHands/Right Hand Tracking";
    private const string HandMaterialResourcePath = "AdaptiveFlyXRHands/HandsDefaultMaterial";

    private static readonly XRHandJointID[] HandJointIds =
    {
        XRHandJointID.Wrist,
        XRHandJointID.Palm,
        XRHandJointID.ThumbMetacarpal,
        XRHandJointID.ThumbProximal,
        XRHandJointID.ThumbDistal,
        XRHandJointID.ThumbTip,
        XRHandJointID.IndexMetacarpal,
        XRHandJointID.IndexProximal,
        XRHandJointID.IndexIntermediate,
        XRHandJointID.IndexDistal,
        XRHandJointID.IndexTip,
        XRHandJointID.MiddleMetacarpal,
        XRHandJointID.MiddleProximal,
        XRHandJointID.MiddleIntermediate,
        XRHandJointID.MiddleDistal,
        XRHandJointID.MiddleTip,
        XRHandJointID.RingMetacarpal,
        XRHandJointID.RingProximal,
        XRHandJointID.RingIntermediate,
        XRHandJointID.RingDistal,
        XRHandJointID.RingTip,
        XRHandJointID.LittleMetacarpal,
        XRHandJointID.LittleProximal,
        XRHandJointID.LittleIntermediate,
        XRHandJointID.LittleDistal,
        XRHandJointID.LittleTip
    };

    private static readonly BonePair[] HandBones =
    {
        new BonePair(XRHandJointID.Wrist, XRHandJointID.Palm),
        new BonePair(XRHandJointID.Wrist, XRHandJointID.ThumbMetacarpal),
        new BonePair(XRHandJointID.ThumbMetacarpal, XRHandJointID.ThumbProximal),
        new BonePair(XRHandJointID.ThumbProximal, XRHandJointID.ThumbDistal),
        new BonePair(XRHandJointID.ThumbDistal, XRHandJointID.ThumbTip),
        new BonePair(XRHandJointID.Wrist, XRHandJointID.IndexMetacarpal),
        new BonePair(XRHandJointID.IndexMetacarpal, XRHandJointID.IndexProximal),
        new BonePair(XRHandJointID.IndexProximal, XRHandJointID.IndexIntermediate),
        new BonePair(XRHandJointID.IndexIntermediate, XRHandJointID.IndexDistal),
        new BonePair(XRHandJointID.IndexDistal, XRHandJointID.IndexTip),
        new BonePair(XRHandJointID.Wrist, XRHandJointID.MiddleMetacarpal),
        new BonePair(XRHandJointID.MiddleMetacarpal, XRHandJointID.MiddleProximal),
        new BonePair(XRHandJointID.MiddleProximal, XRHandJointID.MiddleIntermediate),
        new BonePair(XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleDistal),
        new BonePair(XRHandJointID.MiddleDistal, XRHandJointID.MiddleTip),
        new BonePair(XRHandJointID.Wrist, XRHandJointID.RingMetacarpal),
        new BonePair(XRHandJointID.RingMetacarpal, XRHandJointID.RingProximal),
        new BonePair(XRHandJointID.RingProximal, XRHandJointID.RingIntermediate),
        new BonePair(XRHandJointID.RingIntermediate, XRHandJointID.RingDistal),
        new BonePair(XRHandJointID.RingDistal, XRHandJointID.RingTip),
        new BonePair(XRHandJointID.Wrist, XRHandJointID.LittleMetacarpal),
        new BonePair(XRHandJointID.LittleMetacarpal, XRHandJointID.LittleProximal),
        new BonePair(XRHandJointID.LittleProximal, XRHandJointID.LittleIntermediate),
        new BonePair(XRHandJointID.LittleIntermediate, XRHandJointID.LittleDistal),
        new BonePair(XRHandJointID.LittleDistal, XRHandJointID.LittleTip)
    };

    private static readonly string[] LegacyVisualizerNames =
    {
        "Hand Visualizer",
        "Left Hand Tracking",
        "Right Hand Tracking",
        "LeftHand",
        "RightHand"
    };

    [Header("Rig")]
    public XROrigin xrOrigin;
    public Transform trackingSpace;
    public Transform head;

    [Header("Visibility")]
    public bool useXRHandTracking = true;
    public bool showControllerFallbackHands = false;
    public bool showControllerFallbackSkinnedHands = false;
    public bool ignoreWaistControllerFallback = true;
    public bool disableExistingHandVisualizers = false;
    public bool showHandSurfaces = false;
    public bool showJointSpheres = false;
    public bool showDebugLines = false;
    public bool showArms = false;

    [Header("XR Hands Mesh")]
    public bool useXRHandsSkinnedMesh = true;
    public bool useXRHandsDefaultMaterial = true;
    public bool hideProceduralHandWhenSkinnedMeshIsReady = true;
    public bool loadSampleHandPrefabsInEditor = true;
    public GameObject leftHandMeshPrefab;
    public GameObject rightHandMeshPrefab;
    public Material transparentMaterialOverride;

    [Header("Look")]
    public Color leftColor = new Color(0.82f, 0.86f, 0.92f, 0.55f);
    public Color rightColor = new Color(0.82f, 0.86f, 0.92f, 0.55f);
    public float handLineWidth = 0.006f;
    public float armLineWidth = 0.006f;
    public float jointRadius = 0.012f;
    public float palmRadius = 0.04f;
    public float fingerRadius = 0.014f;
    public float armRadius = 0.045f;
    public Vector3 palmSurfaceScale = new Vector3(0.095f, 0.04f, 0.12f);

    [Header("Inferred Arms")]
    public float shoulderDropFromHead = 0.26f;
    public float shoulderBackFromHead = 0.05f;
    public float shoulderHalfWidth = 0.18f;
    public float elbowDrop = 0.08f;
    public float elbowSideBias = 0.06f;

    [Header("Debug")]
    public bool logHandTrackingStatus = true;
    public float handTrackingLogInterval = 2f;

    private static bool sceneHookInstalled;
    private static readonly List<XRHandSubsystem> HandSubsystems = new List<XRHandSubsystem>();

    private readonly List<InputDevice> inputDevices = new List<InputDevice>();
    private InputDevice leftControllerDevice;
    private InputDevice rightControllerDevice;

    private XRHandSubsystem handSubsystem;
    private WaistControllerAnchor waistAnchor;
    private HandView leftHandView;
    private HandView rightHandView;
    private Material transparentMaterial;
    private Material generatedFallbackMaterial;
    private bool triedDisableLegacyVisualizers;
    private bool triedResolveHandMeshPrefabs;
    private SkinnedHandVisual leftSkinnedHand;
    private SkinnedHandVisual rightSkinnedHand;
    private float nextHandTrackingLogTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallSceneHook()
    {
        if (!sceneHookInstalled)
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            sceneHookInstalled = true;
        }

        EnsureVisualizerForScene(SceneManager.GetActiveScene());
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureVisualizerForScene(scene);
    }

    private static void EnsureVisualizerForScene(Scene scene)
    {
        if (!ShouldInstallInScene(scene))
        {
            return;
        }

        XROrigin origin = FindXROriginInScene(scene);
        if (origin == null || origin.GetComponent<TransparentUpperBodyVisualizer>() != null)
        {
            return;
        }

        TransparentUpperBodyVisualizer visualizer = origin.gameObject.AddComponent<TransparentUpperBodyVisualizer>();
        visualizer.xrOrigin = origin;
        Debug.Log($"{nameof(TransparentUpperBodyVisualizer)} added to '{origin.name}' in scene '{scene.name}'.", origin);
    }

    private static bool ShouldInstallInScene(Scene scene)
    {
        return scene.IsValid() &&
            (scene.name == HeavyStationSceneName ||
             scene.name == OceanSceneName ||
             scene.name == CyberPunkSceneName);
    }

    private static XROrigin FindXROriginInScene(Scene scene)
    {
        XROrigin[] origins = FindObjectsByType<XROrigin>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < origins.Length; i++)
        {
            if (origins[i] != null && origins[i].gameObject.scene == scene)
            {
                return origins[i];
            }
        }

        return null;
    }

    private void Awake()
    {
        ResolveReferences();
        EnsureViews();
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsureViews();
    }

    private void OnDestroy()
    {
        DestroyMaterial(generatedFallbackMaterial);
    }

    private void LateUpdate()
    {
        ResolveReferences();
        EnsureViews();

        if (disableExistingHandVisualizers && !triedDisableLegacyVisualizers)
        {
            DisableLegacyVisualizers();
            triedDisableLegacyVisualizers = true;
        }

        bool leftVisible = TryUpdateHand(leftHandView, true, out Vector3 leftWristLocal);
        bool rightVisible = TryUpdateHand(rightHandView, false, out Vector3 rightWristLocal);

        UpdateArm(leftHandView, true, leftVisible, leftWristLocal);
        UpdateArm(rightHandView, false, rightVisible, rightWristLocal);
        LogHandTrackingStatusIfNeeded(leftVisible, rightVisible);
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

        if (trackingSpace == null && xrOrigin != null)
        {
            trackingSpace = xrOrigin.transform;
        }

        if (head == null && xrOrigin != null && xrOrigin.Camera != null)
        {
            head = xrOrigin.Camera.transform;
        }

        if (head == null && Camera.main != null)
        {
            head = Camera.main.transform;
        }

        if (waistAnchor == null)
        {
            waistAnchor = GetComponent<WaistControllerAnchor>();
        }
    }

    private void EnsureViews()
    {
        if (trackingSpace == null)
        {
            return;
        }

        Material material = ResolveTransparentMaterial();

        EnsureSkinnedHandMeshes();

        if (leftHandView == null)
        {
            leftHandView = new HandView(
                "Left Transparent Upper Body",
                trackingSpace,
                material,
                leftColor,
                showHandSurfaces,
                showJointSpheres,
                showDebugLines,
                handLineWidth,
                armLineWidth,
                jointRadius,
                palmRadius,
                fingerRadius,
                armRadius,
                palmSurfaceScale);
        }

        if (rightHandView == null)
        {
            rightHandView = new HandView(
                "Right Transparent Upper Body",
                trackingSpace,
                material,
                rightColor,
                showHandSurfaces,
                showJointSpheres,
                showDebugLines,
                handLineWidth,
                armLineWidth,
                jointRadius,
                palmRadius,
                fingerRadius,
                armRadius,
                palmSurfaceScale);
        }

    }

    private bool TryUpdateHand(HandView view, bool leftHand, out Vector3 wristLocal)
    {
        wristLocal = Vector3.zero;
        if (view == null)
        {
            return false;
        }

        if (useXRHandTracking && TryUpdateXRHand(view, leftHand, out wristLocal))
        {
            return true;
        }

        SetSkinnedHandVisible(leftHand, false);
        view.SetVisible(false);
        return false;
    }

    private bool TryUpdateXRHand(HandView view, bool leftHand, out Vector3 wristLocal)
    {
        wristLocal = Vector3.zero;
        if (!TryGetRunningHandSubsystem(out XRHandSubsystem subsystem))
        {
            return false;
        }

        XRHand hand = leftHand ? subsystem.leftHand : subsystem.rightHand;
        if (!hand.isTracked)
        {
            return false;
        }

        bool hasWrist = false;
        bool hasSkinnedMesh = HasSkinnedHandMesh(leftHand);
        SetSkinnedHandTrackingMode(leftHand);
        view.SetXRHandMode(!hasSkinnedMesh || !hideProceduralHandWhenSkinnedMeshIsReady);
        for (int i = 0; i < HandJointIds.Length; i++)
        {
            XRHandJointID jointId = HandJointIds[i];
            XRHandJoint joint = hand.GetJoint(jointId);
            bool tracked = joint.TryGetPose(out Pose pose);
            view.SetJointTracked(jointId, tracked);
            if (!tracked)
            {
                continue;
            }

            view.SetJointPose(jointId, pose.position, pose.rotation);
            if (jointId == XRHandJointID.Wrist)
            {
                wristLocal = pose.position;
                hasWrist = true;
            }
        }

        view.UpdateHandBones();
        if (hasWrist && hasSkinnedMesh)
        {
            SetSkinnedHandVisible(leftHand, true);
        }

        view.SetVisible(hasWrist && (!hasSkinnedMesh || !hideProceduralHandWhenSkinnedMeshIsReady));
        return hasWrist;
    }

    private void EnsureSkinnedHandMeshes()
    {
        if (!useXRHandsSkinnedMesh || trackingSpace == null)
        {
            return;
        }

        Material material = ResolveTransparentMaterial();
        ResolveHandMeshPrefabs();
        if (leftSkinnedHand == null && leftHandMeshPrefab != null)
        {
            leftSkinnedHand = CreateSkinnedHandMesh(leftHandMeshPrefab, true, material);
        }

        if (rightSkinnedHand == null && rightHandMeshPrefab != null)
        {
            rightSkinnedHand = CreateSkinnedHandMesh(rightHandMeshPrefab, false, material);
        }
    }

    private Material ResolveTransparentMaterial()
    {
        if (transparentMaterialOverride != null)
        {
            return transparentMaterialOverride;
        }

        if (transparentMaterial == null && useXRHandsDefaultMaterial)
        {
            transparentMaterial = Resources.Load<Material>(HandMaterialResourcePath);

#if UNITY_EDITOR
            if (transparentMaterial == null && loadSampleHandPrefabsInEditor)
            {
                transparentMaterial = AssetDatabase.LoadAssetAtPath<Material>(SampleHandMaterialPath);
            }
#endif
        }

        if (transparentMaterial != null)
        {
            return transparentMaterial;
        }

        if (generatedFallbackMaterial == null)
        {
            generatedFallbackMaterial = CreateTransparentMaterial(leftColor);
        }

        return generatedFallbackMaterial;
    }

    private void ResolveHandMeshPrefabs()
    {
        if (triedResolveHandMeshPrefabs)
        {
            return;
        }

        triedResolveHandMeshPrefabs = true;

        if (leftHandMeshPrefab == null)
        {
            leftHandMeshPrefab = Resources.Load<GameObject>(LeftHandResourcePath);
        }

        if (rightHandMeshPrefab == null)
        {
            rightHandMeshPrefab = Resources.Load<GameObject>(RightHandResourcePath);
        }

#if UNITY_EDITOR
        if (loadSampleHandPrefabsInEditor && leftHandMeshPrefab == null)
        {
            leftHandMeshPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LeftSampleHandPrefabPath);
        }

        if (loadSampleHandPrefabsInEditor && rightHandMeshPrefab == null)
        {
            rightHandMeshPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RightSampleHandPrefabPath);
        }
#endif
    }

    private SkinnedHandVisual CreateSkinnedHandMesh(GameObject prefab, bool leftHand, Material material)
    {
        GameObject instance = Instantiate(prefab, trackingSpace, false);
        instance.name = leftHand
            ? "AdaptiveFly Left Transparent XR Hands Mesh"
            : "AdaptiveFly Right Transparent XR Hands Mesh";
        instance.hideFlags = HideFlags.DontSave;
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        ConfigureSkinnedHandMesh(instance, leftHand, material);
        return new SkinnedHandVisual(instance);
    }

    private static void ConfigureSkinnedHandMesh(GameObject instance, bool leftHand, Material material)
    {
        XRHandTrackingEvents[] trackingEvents = instance.GetComponentsInChildren<XRHandTrackingEvents>(true);
        for (int i = 0; i < trackingEvents.Length; i++)
        {
            trackingEvents[i].handedness = leftHand ? Handedness.Left : Handedness.Right;
            trackingEvents[i].updateType = XRHandTrackingEvents.UpdateTypes.BeforeRender;
        }

        XRHandMeshController[] meshControllers = instance.GetComponentsInChildren<XRHandMeshController>(true);
        for (int i = 0; i < meshControllers.Length; i++)
        {
            meshControllers[i].showMeshWhenTrackingIsAcquired = true;
            meshControllers[i].hideMeshWhenTrackingIsLost = true;
            if (meshControllers[i].handMeshRenderer != null && material != null)
            {
                meshControllers[i].handMeshRenderer.sharedMaterial = material;
            }
        }

        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (material != null)
            {
                renderers[i].sharedMaterial = material;
            }

            renderers[i].shadowCastingMode = ShadowCastingMode.Off;
            renderers[i].receiveShadows = false;
        }
    }

    private bool HasSkinnedHandMesh(bool leftHand)
    {
        if (!useXRHandsSkinnedMesh)
        {
            return false;
        }

        EnsureSkinnedHandMeshes();
        return leftHand
            ? leftSkinnedHand != null
            : rightSkinnedHand != null;
    }

    private bool TryUpdateControllerSkinnedHand(bool leftHand, out Vector3 wristLocal)
    {
        wristLocal = Vector3.zero;
        if (ignoreWaistControllerFallback && IsWaistControllerHand(leftHand))
        {
            SetSkinnedHandVisible(leftHand, false);
            return false;
        }

        EnsureSkinnedHandMeshes();
        SkinnedHandVisual visual = leftHand ? leftSkinnedHand : rightSkinnedHand;
        if (visual == null)
        {
            return false;
        }

        ref InputDevice cachedDevice = ref (leftHand ? ref leftControllerDevice : ref rightControllerDevice);
        if (!cachedDevice.isValid && !TryResolveControllerDevice(leftHand, out cachedDevice))
        {
            visual.SetVisible(false);
            return false;
        }

        if (!TryReadDevicePose(cachedDevice, out Vector3 localPosition, out Quaternion localRotation))
        {
            cachedDevice = default;
            visual.SetVisible(false);
            return false;
        }

        wristLocal = localPosition;
        visual.SetControllerFallbackPose(localPosition, localRotation);
        return true;
    }

    private void SetSkinnedHandTrackingMode(bool leftHand)
    {
        EnsureSkinnedHandMeshes();
        SkinnedHandVisual visual = leftHand ? leftSkinnedHand : rightSkinnedHand;
        if (visual != null)
        {
            visual.SetTrackingMode();
        }
    }

    private void SetSkinnedHandVisible(bool leftHand, bool visible)
    {
        SkinnedHandVisual visual = leftHand ? leftSkinnedHand : rightSkinnedHand;
        if (visual != null)
        {
            visual.SetVisible(visible);
        }
    }

    private bool TryGetRunningHandSubsystem(out XRHandSubsystem subsystem)
    {
        if (handSubsystem != null && handSubsystem.running)
        {
            subsystem = handSubsystem;
            return true;
        }

        HandSubsystems.Clear();
        SubsystemManager.GetSubsystems(HandSubsystems);
        for (int i = 0; i < HandSubsystems.Count; i++)
        {
            XRHandSubsystem candidate = HandSubsystems[i];
            if (candidate != null && candidate.running)
            {
                handSubsystem = candidate;
                subsystem = candidate;
                return true;
            }
        }

        subsystem = null;
        return false;
    }

    private void LogHandTrackingStatusIfNeeded(bool leftVisible, bool rightVisible)
    {
        if (!logHandTrackingStatus || Time.unscaledTime < nextHandTrackingLogTime)
        {
            return;
        }

        nextHandTrackingLogTime = Time.unscaledTime + Mathf.Max(0.25f, handTrackingLogInterval);
        if (!TryGetRunningHandSubsystem(out XRHandSubsystem subsystem))
        {
            Debug.LogWarning(
                $"{nameof(TransparentUpperBodyVisualizer)} on '{name}': XRHandSubsystem is not running. " +
                $"Controller fallback is disabled, so hands will be hidden.",
                this);
            return;
        }

        Debug.Log(
            $"{nameof(TransparentUpperBodyVisualizer)} on '{name}': " +
            $"XRHandSubsystem running={subsystem.running}, " +
            $"leftTracked={subsystem.leftHand.isTracked}, rightTracked={subsystem.rightHand.isTracked}, " +
            $"leftVisible={leftVisible}, rightVisible={rightVisible}, " +
            $"controllerFallbackSkinned={showControllerFallbackSkinnedHands}, controllerFallbackProxy={showControllerFallbackHands}.",
            this);
    }

    private bool TryUpdateControllerHandProxy(HandView view, bool leftHand, out Vector3 wristLocal)
    {
        wristLocal = Vector3.zero;
        if (ignoreWaistControllerFallback && IsWaistControllerHand(leftHand))
        {
            return false;
        }

        ref InputDevice cachedDevice = ref (leftHand ? ref leftControllerDevice : ref rightControllerDevice);
        if (!cachedDevice.isValid && !TryResolveControllerDevice(leftHand, out cachedDevice))
        {
            return false;
        }

        if (!TryReadDevicePose(cachedDevice, out Vector3 localPosition, out Quaternion localRotation))
        {
            cachedDevice = default;
            return false;
        }

        wristLocal = localPosition;
        view.SetControllerProxyMode(localPosition, localRotation, leftHand);
        view.SetVisible(true);
        return true;
    }

    private bool TryResolveControllerDevice(bool leftHand, out InputDevice device)
    {
        inputDevices.Clear();
        InputDeviceCharacteristics handCharacteristic = leftHand
            ? InputDeviceCharacteristics.Left
            : InputDeviceCharacteristics.Right;
        InputDeviceCharacteristics characteristics =
            InputDeviceCharacteristics.Controller |
            InputDeviceCharacteristics.TrackedDevice |
            handCharacteristic;

        InputDevices.GetDevicesWithCharacteristics(characteristics, inputDevices);
        for (int i = 0; i < inputDevices.Count; i++)
        {
            InputDevice candidate = inputDevices[i];
            if (XRDeviceFilters.IsHandController(candidate, leftHand))
            {
                device = candidate;
                return true;
            }
        }

        device = default;
        return false;
    }

    private static bool TryReadDevicePose(InputDevice device, out Vector3 localPosition, out Quaternion localRotation)
    {
        localPosition = Vector3.zero;
        localRotation = Quaternion.identity;

        bool hasPosition = device.TryGetFeatureValue(CommonUsages.devicePosition, out localPosition);
        bool hasRotation = device.TryGetFeatureValue(CommonUsages.deviceRotation, out localRotation);
        bool hasTrackingState = device.TryGetFeatureValue(CommonUsages.trackingState, out InputTrackingState trackingState);
        bool positionTracked = !hasTrackingState || (trackingState & InputTrackingState.Position) != 0;
        bool rotationTracked = !hasTrackingState || (trackingState & InputTrackingState.Rotation) != 0;

        return hasPosition && hasRotation && positionTracked && rotationTracked;
    }

    private bool IsWaistControllerHand(bool leftHand)
    {
        return waistAnchor != null &&
            waistAnchor.IsTracked &&
            ((leftHand && waistAnchor.controllerHand == WaistControllerAnchor.ControllerHand.Left) ||
             (!leftHand && waistAnchor.controllerHand == WaistControllerAnchor.ControllerHand.Right));
    }

    private void UpdateArm(HandView view, bool leftHand, bool handVisible, Vector3 wristLocal)
    {
        if (view == null || !showArms || !handVisible || trackingSpace == null || head == null)
        {
            if (view != null)
            {
                view.SetArmVisible(false);
            }

            return;
        }

        Vector3 headLocal = trackingSpace.InverseTransformPoint(head.position);
        Vector3 headForwardLocal = trackingSpace.InverseTransformDirection(head.forward);
        headForwardLocal.y = 0f;
        if (headForwardLocal.sqrMagnitude < 0.0001f)
        {
            headForwardLocal = Vector3.forward;
        }
        else
        {
            headForwardLocal.Normalize();
        }

        Vector3 rightLocal = Vector3.Cross(Vector3.up, headForwardLocal).normalized;
        Vector3 shoulderCenter = headLocal - Vector3.up * shoulderDropFromHead - headForwardLocal * shoulderBackFromHead;
        Vector3 shoulderLocal = shoulderCenter + rightLocal * (leftHand ? -shoulderHalfWidth : shoulderHalfWidth);
        Vector3 sideBias = rightLocal * (leftHand ? -elbowSideBias : elbowSideBias);
        Vector3 elbowLocal = Vector3.Lerp(shoulderLocal, wristLocal, 0.55f) - Vector3.up * elbowDrop + sideBias;

        view.SetArm(shoulderLocal, elbowLocal, wristLocal);
    }

    private void DisableLegacyVisualizers()
    {
        Scene scene = gameObject.scene;
        if (!scene.IsValid())
        {
            return;
        }

        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            DisableLegacyVisualizersRecursive(roots[i].transform);
        }
    }

    private void DisableLegacyVisualizersRecursive(Transform current)
    {
        if (current == null)
        {
            return;
        }

        if (current != transform)
        {
            for (int i = 0; i < LegacyVisualizerNames.Length; i++)
            {
                if (current.name == LegacyVisualizerNames[i])
                {
                    current.gameObject.SetActive(false);
                    return;
                }
            }
        }

        for (int i = 0; i < current.childCount; i++)
        {
            DisableLegacyVisualizersRecursive(current.GetChild(i));
        }
    }

    private static Material CreateTransparentMaterial(Color color)
    {
        Shader shader =
            Shader.Find("HDRP/Unlit") ??
            Shader.Find("Universal Render Pipeline/Unlit") ??
            Shader.Find("Unlit/Color") ??
            Shader.Find("Sprites/Default");

        Material material = new Material(shader)
        {
            hideFlags = HideFlags.DontSave,
            renderQueue = 3000
        };

        SetMaterialColor(material, "_BaseColor", color);
        SetMaterialColor(material, "_Color", color);
        SetMaterialColor(material, "_UnlitColor", color);
        SetMaterialColor(material, "_EmissiveColor", new Color(color.r, color.g, color.b, 0f));
        SetMaterialFloat(material, "_SurfaceType", 1f);
        SetMaterialFloat(material, "_BlendMode", 0f);
        SetMaterialFloat(material, "_AlphaCutoffEnable", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_ENABLE_BLENDMODE_ALPHA");
        material.EnableKeyword("_ENABLE_FOG_ON_TRANSPARENT");
        return material;
    }

    private static void SetMaterialColor(Material material, string propertyName, Color color)
    {
        if (material != null && material.HasProperty(propertyName))
        {
            material.SetColor(propertyName, color);
        }
    }

    private static void SetMaterialFloat(Material material, string propertyName, float value)
    {
        if (material != null && material.HasProperty(propertyName))
        {
            material.SetFloat(propertyName, value);
        }
    }

    private static void DestroyMaterial(Material material)
    {
        if (material == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(material);
        }
        else
        {
            DestroyImmediate(material);
        }
    }

    private readonly struct BonePair
    {
        public readonly XRHandJointID Start;
        public readonly XRHandJointID End;

        public BonePair(XRHandJointID start, XRHandJointID end)
        {
            Start = start;
            End = end;
        }
    }

    private sealed class SkinnedHandVisual
    {
        private readonly GameObject instance;
        private readonly Renderer[] renderers;
        private readonly XRHandTrackingEvents[] trackingEvents;
        private readonly XRHandSkeletonDriver[] skeletonDrivers;
        private readonly XRHandMeshController[] meshControllers;
        private readonly Transform rootTransform;
        private readonly Vector3 defaultRootLocalPosition;
        private readonly Quaternion defaultRootLocalRotation;
        private bool controllerFallbackMode;

        public SkinnedHandVisual(GameObject instance)
        {
            this.instance = instance;
            renderers = instance.GetComponentsInChildren<Renderer>(true);
            trackingEvents = instance.GetComponentsInChildren<XRHandTrackingEvents>(true);
            skeletonDrivers = instance.GetComponentsInChildren<XRHandSkeletonDriver>(true);
            meshControllers = instance.GetComponentsInChildren<XRHandMeshController>(true);

            rootTransform = skeletonDrivers.Length > 0 ? skeletonDrivers[0].rootTransform : null;
            if (rootTransform != null)
            {
                defaultRootLocalPosition = instance.transform.InverseTransformPoint(rootTransform.position);
                defaultRootLocalRotation = Quaternion.Inverse(instance.transform.rotation) * rootTransform.rotation;
            }
            else
            {
                defaultRootLocalPosition = Vector3.zero;
                defaultRootLocalRotation = Quaternion.identity;
            }

            SetTrackingMode();
        }

        public void SetTrackingMode()
        {
            if (instance == null)
            {
                return;
            }

            if (!instance.activeSelf)
            {
                instance.SetActive(true);
            }

            if (!controllerFallbackMode)
            {
                return;
            }

            controllerFallbackMode = false;
            SetComponentsEnabled(trackingEvents, true);
            SetComponentsEnabled(skeletonDrivers, true);
            SetComponentsEnabled(meshControllers, true);
        }

        public void SetControllerFallbackPose(Vector3 localPosition, Quaternion localRotation)
        {
            if (instance == null)
            {
                return;
            }

            if (!instance.activeSelf)
            {
                instance.SetActive(true);
            }

            if (!controllerFallbackMode)
            {
                controllerFallbackMode = true;
                SetComponentsEnabled(trackingEvents, false);
                SetComponentsEnabled(skeletonDrivers, false);
                SetComponentsEnabled(meshControllers, false);
            }

            instance.transform.localRotation = localRotation * Quaternion.Inverse(defaultRootLocalRotation);
            instance.transform.localPosition = localPosition - instance.transform.localRotation * defaultRootLocalPosition;
            SetRenderersVisible(true);
        }

        public void SetVisible(bool visible)
        {
            if (instance == null)
            {
                return;
            }

            SetRenderersVisible(visible);
        }

        private void SetRenderersVisible(bool visible)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].enabled = visible;
                }
            }
        }

        private static void SetComponentsEnabled<TComponent>(TComponent[] components, bool enabled)
            where TComponent : Behaviour
        {
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] != null)
                {
                    components[i].enabled = enabled;
                }
            }
        }
    }

    private sealed class HandView
    {
        private static readonly Vector3[] ProxyFingerStarts =
        {
            new Vector3(-0.025f, 0.002f, 0.015f),
            new Vector3(-0.012f, 0.004f, 0.026f),
            new Vector3(0f, 0.005f, 0.03f),
            new Vector3(0.012f, 0.004f, 0.026f),
            new Vector3(0.024f, 0.002f, 0.017f)
        };

        private static readonly Vector3[] ProxyFingerEnds =
        {
            new Vector3(-0.066f, 0.015f, 0.073f),
            new Vector3(-0.026f, 0.014f, 0.114f),
            new Vector3(0f, 0.014f, 0.126f),
            new Vector3(0.026f, 0.014f, 0.112f),
            new Vector3(0.056f, 0.011f, 0.086f)
        };

        private readonly GameObject root;
        private readonly GameObject xrHandRoot;
        private readonly GameObject proxyRoot;
        private readonly GameObject armRoot;
        private readonly Dictionary<XRHandJointID, Transform> jointCaps = new Dictionary<XRHandJointID, Transform>();
        private readonly Dictionary<XRHandJointID, bool> jointTracked = new Dictionary<XRHandJointID, bool>();
        private readonly List<BoneVolume> boneVolumes = new List<BoneVolume>();
        private readonly List<BoneLine> debugLines = new List<BoneLine>();
        private readonly SegmentVisual[] proxyFingerVolumes = new SegmentVisual[ProxyFingerStarts.Length];
        private readonly Transform palmSurface;
        private readonly Transform proxyPalmSurface;
        private readonly Transform shoulderCap;
        private readonly Transform elbowCap;
        private readonly Transform wristCap;
        private readonly SegmentVisual upperArmVolume;
        private readonly SegmentVisual forearmVolume;
        private readonly LineRenderer armDebugLine;
        private readonly bool showHandSurfaces;
        private readonly bool showJointSpheres;
        private readonly bool showDebugLines;
        private readonly float handLineWidth;
        private readonly float armLineWidth;
        private readonly float jointRadius;
        private readonly float palmRadius;
        private readonly float fingerRadius;
        private readonly float armRadius;
        private readonly Vector3 palmSurfaceScale;

        public HandView(
            string name,
            Transform parent,
            Material material,
            Color color,
            bool showHandSurfaces,
            bool showJointSpheres,
            bool showDebugLines,
            float handLineWidth,
            float armLineWidth,
            float jointRadius,
            float palmRadius,
            float fingerRadius,
            float armRadius,
            Vector3 palmSurfaceScale)
        {
            this.showHandSurfaces = showHandSurfaces;
            this.showJointSpheres = showJointSpheres;
            this.showDebugLines = showDebugLines;
            this.handLineWidth = handLineWidth;
            this.armLineWidth = armLineWidth;
            this.jointRadius = jointRadius;
            this.palmRadius = palmRadius;
            this.fingerRadius = fingerRadius;
            this.armRadius = armRadius;
            this.palmSurfaceScale = palmSurfaceScale;

            root = new GameObject(name);
            root.hideFlags = HideFlags.DontSave;
            root.transform.SetParent(parent, false);

            xrHandRoot = new GameObject("XR Hand Transparent Surface");
            xrHandRoot.hideFlags = HideFlags.DontSave;
            xrHandRoot.transform.SetParent(root.transform, false);

            proxyRoot = new GameObject("Controller Transparent Hand Proxy");
            proxyRoot.hideFlags = HideFlags.DontSave;
            proxyRoot.transform.SetParent(root.transform, false);

            armRoot = new GameObject("Inferred Transparent Arm");
            armRoot.hideFlags = HideFlags.DontSave;
            armRoot.transform.SetParent(root.transform, false);

            for (int i = 0; i < HandJointIds.Length; i++)
            {
                XRHandJointID jointId = HandJointIds[i];
                Transform cap = CreateSphere($"{jointId} Volume Cap", xrHandRoot.transform, material);
                cap.gameObject.SetActive(false);
                jointCaps[jointId] = cap;
                jointTracked[jointId] = false;
            }

            palmSurface = CreateSphere("Palm Transparent Surface", xrHandRoot.transform, material);
            palmSurface.gameObject.SetActive(false);

            for (int i = 0; i < HandBones.Length; i++)
            {
                BonePair bone = HandBones[i];
                SegmentVisual segment = new SegmentVisual($"{bone.Start}-{bone.End} Transparent Segment", xrHandRoot.transform, material);
                LineRenderer debugLine = CreateLine($"{bone.Start}-{bone.End} Debug Line", xrHandRoot.transform, material, color, handLineWidth, 2);
                debugLine.enabled = false;
                boneVolumes.Add(new BoneVolume(bone.Start, bone.End, segment));
                debugLines.Add(new BoneLine(bone.Start, bone.End, debugLine));
            }

            proxyPalmSurface = CreateSphere("Controller Palm Transparent Surface", proxyRoot.transform, material);
            proxyPalmSurface.localPosition = new Vector3(0f, 0f, 0.018f);
            proxyPalmSurface.localScale = palmSurfaceScale;
            for (int i = 0; i < proxyFingerVolumes.Length; i++)
            {
                proxyFingerVolumes[i] = new SegmentVisual($"Controller Finger {i + 1} Transparent Volume", proxyRoot.transform, material);
            }

            shoulderCap = CreateSphere("Shoulder Transparent Cap", armRoot.transform, material);
            elbowCap = CreateSphere("Elbow Transparent Cap", armRoot.transform, material);
            wristCap = CreateSphere("Wrist Transparent Cap", armRoot.transform, material);
            upperArmVolume = new SegmentVisual("Upper Arm Transparent Volume", armRoot.transform, material);
            forearmVolume = new SegmentVisual("Forearm Transparent Volume", armRoot.transform, material);
            armDebugLine = CreateLine("Arm Debug Line", armRoot.transform, material, new Color(color.r, color.g, color.b, color.a * 0.75f), armLineWidth, 3);
            armDebugLine.enabled = false;
            SetVisible(false);
        }

        public void SetVisible(bool visible)
        {
            if (root.activeSelf != visible)
            {
                root.SetActive(visible);
            }
        }

        public void SetXRHandMode(bool showProceduralHand)
        {
            if (xrHandRoot.activeSelf != showProceduralHand)
            {
                xrHandRoot.SetActive(showProceduralHand);
            }

            if (proxyRoot.activeSelf)
            {
                proxyRoot.SetActive(false);
            }
        }

        public void SetControllerProxyMode(Vector3 localPosition, Quaternion localRotation, bool leftHand)
        {
            if (xrHandRoot.activeSelf)
            {
                xrHandRoot.SetActive(false);
            }

            if (!proxyRoot.activeSelf)
            {
                proxyRoot.SetActive(true);
            }

            proxyRoot.transform.localPosition = localPosition;
            proxyRoot.transform.localRotation = localRotation;
            proxyPalmSurface.gameObject.SetActive(showHandSurfaces);

            float side = leftHand ? -1f : 1f;
            for (int i = 0; i < proxyFingerVolumes.Length; i++)
            {
                Vector3 start = ProxyFingerStarts[i];
                Vector3 end = ProxyFingerEnds[i];
                start.x *= side;
                end.x *= side;
                proxyFingerVolumes[i].SetSegment(start, end, FingerRadiusForProxyIndex(i), showHandSurfaces);
            }
        }

        public void SetJointTracked(XRHandJointID jointId, bool tracked)
        {
            jointTracked[jointId] = tracked;
            if (jointCaps.TryGetValue(jointId, out Transform cap))
            {
                cap.gameObject.SetActive(showHandSurfaces && showJointSpheres && tracked);
            }
        }

        public void SetJointPose(XRHandJointID jointId, Vector3 localPosition, Quaternion localRotation)
        {
            if (!jointCaps.TryGetValue(jointId, out Transform cap))
            {
                return;
            }

            cap.localPosition = localPosition;
            cap.localRotation = localRotation;
            float radius = JointRadius(jointId);
            cap.localScale = Vector3.one * (radius * 2f);
        }

        public void UpdateHandBones()
        {
            bool palmTracked = IsTracked(XRHandJointID.Palm);
            palmSurface.gameObject.SetActive(showHandSurfaces && palmTracked);
            if (palmTracked)
            {
                Transform palm = jointCaps[XRHandJointID.Palm];
                palmSurface.localPosition = palm.localPosition;
                palmSurface.localRotation = palm.localRotation;
                palmSurface.localScale = palmSurfaceScale;
            }

            for (int i = 0; i < boneVolumes.Count; i++)
            {
                BoneVolume boneVolume = boneVolumes[i];
                BoneLine debugLine = debugLines[i];
                bool visible = IsTracked(boneVolume.Start) && IsTracked(boneVolume.End);

                Vector3 start = visible ? jointCaps[boneVolume.Start].localPosition : Vector3.zero;
                Vector3 end = visible ? jointCaps[boneVolume.End].localPosition : Vector3.zero;
                boneVolume.Volume.SetSegment(start, end, BoneRadius(boneVolume.Start, boneVolume.End), showHandSurfaces && visible);

                debugLine.Line.enabled = showDebugLines && visible;
                if (debugLine.Line.enabled)
                {
                    debugLine.Line.SetPosition(0, start);
                    debugLine.Line.SetPosition(1, end);
                }
            }
        }

        public void SetArm(Vector3 shoulderLocal, Vector3 elbowLocal, Vector3 wristLocal)
        {
            armRoot.SetActive(true);
            upperArmVolume.SetSegment(shoulderLocal, elbowLocal, armRadius, showHandSurfaces);
            forearmVolume.SetSegment(elbowLocal, wristLocal, armRadius * 0.9f, showHandSurfaces);
            SetCap(shoulderCap, shoulderLocal, armRadius * 1.05f, showHandSurfaces && showJointSpheres);
            SetCap(elbowCap, elbowLocal, armRadius * 0.95f, showHandSurfaces && showJointSpheres);
            SetCap(wristCap, wristLocal, armRadius * 0.85f, showHandSurfaces && showJointSpheres);

            armDebugLine.enabled = showDebugLines;
            if (armDebugLine.enabled)
            {
                armDebugLine.SetPosition(0, shoulderLocal);
                armDebugLine.SetPosition(1, elbowLocal);
                armDebugLine.SetPosition(2, wristLocal);
            }
        }

        public void SetArmVisible(bool visible)
        {
            armRoot.SetActive(visible);
            if (!visible)
            {
                upperArmVolume.SetVisible(false);
                forearmVolume.SetVisible(false);
                armDebugLine.enabled = false;
            }
        }

        private bool IsTracked(XRHandJointID jointId)
        {
            return jointTracked.TryGetValue(jointId, out bool tracked) && tracked;
        }

        private float JointRadius(XRHandJointID jointId)
        {
            if (jointId == XRHandJointID.Palm)
            {
                return palmRadius * 0.6f;
            }

            if (jointId == XRHandJointID.Wrist)
            {
                return palmRadius * 0.45f;
            }

            if (jointId == XRHandJointID.ThumbTip ||
                jointId == XRHandJointID.IndexTip ||
                jointId == XRHandJointID.MiddleTip ||
                jointId == XRHandJointID.RingTip ||
                jointId == XRHandJointID.LittleTip)
            {
                return fingerRadius * 0.95f;
            }

            return jointRadius;
        }

        private float BoneRadius(XRHandJointID start, XRHandJointID end)
        {
            if (start == XRHandJointID.Wrist && end == XRHandJointID.Palm)
            {
                return palmRadius * 0.42f;
            }

            if (start == XRHandJointID.Wrist)
            {
                return fingerRadius * 1.25f;
            }

            if (end == XRHandJointID.ThumbTip ||
                end == XRHandJointID.IndexTip ||
                end == XRHandJointID.MiddleTip ||
                end == XRHandJointID.RingTip ||
                end == XRHandJointID.LittleTip)
            {
                return fingerRadius * 0.8f;
            }

            return fingerRadius;
        }

        private float FingerRadiusForProxyIndex(int index)
        {
            return index == 0 ? fingerRadius * 1.05f : fingerRadius;
        }

        private static void SetCap(Transform cap, Vector3 localPosition, float radius, bool visible)
        {
            cap.gameObject.SetActive(visible);
            if (!visible)
            {
                return;
            }

            cap.localPosition = localPosition;
            cap.localRotation = Quaternion.identity;
            cap.localScale = Vector3.one * (radius * 2f);
        }

        private static Transform CreateSphere(string name, Transform parent, Material material)
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = name;
            sphere.hideFlags = HideFlags.DontSave;
            sphere.transform.SetParent(parent, false);

            Collider collider = sphere.GetComponent<Collider>();
            if (collider != null)
            {
                Object.Destroy(collider);
            }

            MeshRenderer renderer = sphere.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            return sphere.transform;
        }

        private static LineRenderer CreateLine(string name, Transform parent, Material material, Color color, float width, int positionCount)
        {
            GameObject lineObject = new GameObject(name);
            lineObject.hideFlags = HideFlags.DontSave;
            lineObject.transform.SetParent(parent, false);

            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.sharedMaterial = material;
            line.positionCount = positionCount;
            line.startWidth = width;
            line.endWidth = width;
            line.numCapVertices = 4;
            line.numCornerVertices = 4;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.startColor = color;
            line.endColor = color;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        private readonly struct BoneVolume
        {
            public readonly XRHandJointID Start;
            public readonly XRHandJointID End;
            public readonly SegmentVisual Volume;

            public BoneVolume(XRHandJointID start, XRHandJointID end, SegmentVisual volume)
            {
                Start = start;
                End = end;
                Volume = volume;
            }
        }

        private readonly struct BoneLine
        {
            public readonly XRHandJointID Start;
            public readonly XRHandJointID End;
            public readonly LineRenderer Line;

            public BoneLine(XRHandJointID start, XRHandJointID end, LineRenderer line)
            {
                Start = start;
                End = end;
                Line = line;
            }
        }
    }

    private sealed class SegmentVisual
    {
        private readonly Transform transform;

        public SegmentVisual(string name, Transform parent, Material material)
        {
            GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.name = name;
            cylinder.hideFlags = HideFlags.DontSave;
            transform = cylinder.transform;
            transform.SetParent(parent, false);

            Collider collider = cylinder.GetComponent<Collider>();
            if (collider != null)
            {
                Object.Destroy(collider);
            }

            MeshRenderer renderer = cylinder.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            SetVisible(false);
        }

        public void SetSegment(Vector3 startLocal, Vector3 endLocal, float radius, bool visible)
        {
            Vector3 delta = endLocal - startLocal;
            float length = delta.magnitude;
            if (!visible || length < 0.0001f)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);
            transform.localPosition = (startLocal + endLocal) * 0.5f;
            transform.localRotation = Quaternion.FromToRotation(Vector3.up, delta / length);
            transform.localScale = new Vector3(radius, length * 0.5f, radius);
        }

        public void SetVisible(bool visible)
        {
            if (transform.gameObject.activeSelf != visible)
            {
                transform.gameObject.SetActive(visible);
            }
        }
    }
}
