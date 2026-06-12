using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Single-script locomotion:
/// 1) Planar translation from HMD offset relative to an initialized center.
/// 2) Vertical and yaw commands from HMD forward vector.
/// </summary>
public class HeadOffsetLocomotion : MonoBehaviour
{
    private const int OnlineDirectionLeft = 0;
    private const int OnlineDirectionRight = 1;
    private const int OnlineDirectionForward = 2;
    private const int OnlineDirectionBackward = 3;
    private const int OnlineDirectionCount = 4;

    public enum OrientationControlMode
    {
        Dynamic,
        Static,
        Coupled
    }

    public enum PlanarReferenceMode
    {
        BodyAnchor,
        InitialHeadPosition
    }

    public enum AdaptiveCalibrationStage
    {
        Idle,
        NeutralAndSway,
        Forward,
        Backward,
        Left,
        Right
    }

    [Header("Rig / HMD")]
    [Tooltip("Transform that will be moved. Defaults to this object.")]
    public Transform target;
    [Tooltip("HMD/camera transform.")]
    public Transform head;
    [Tooltip("Only use Camera.main as a fallback when head is left empty on purpose.")]
    public bool autoBindHeadFromMainCamera = false;

    [Header("Planar Translation")]
    [Tooltip("Maximum planar speed in m/s.")]
    public float horizontalSpeed = 5f;
    [Tooltip("Ignore small lean around center.")]
    public float planarDeadZone = 0.003f;
    [Tooltip("Lean magnitude corresponding to max planar speed.")]
    public float planarMaxOffset = 0.025f;
    [Tooltip("1 = linear, >1 softer near center.")]
    public float planarResponseExponent = 0.55f;
    [Tooltip("0 = use lean direction only, 1 = use head forward only. Keep this at 0 for body-referenced planar input so head turns do not steer translation.")]
    [Range(0f, 1f)]
    public float headDirectionBlend = 0f;
    [Tooltip("Optional manual override if your XR runtime still reports yaw opposite to the locomotion convention after neutral calibration.")]
    public bool invertYawDirection = false;
    [Tooltip("Planar velocity smoothing (1/s).")]
    public float planarSmoothing = 10f;

    [Header("Body Reference")]
    [Tooltip("BodyAnchor uses the current head-to-body-anchor offset, so stance drift and small steps do not become locomotion input.")]
    public PlanarReferenceMode planarReferenceMode = PlanarReferenceMode.BodyAnchor;
    [Tooltip("Provider for the waist controller pose. Defaults to a BodyAnchorProvider on this object, adding one at runtime if needed.")]
    public BodyAnchorProvider bodyAnchorProvider;
    [Tooltip("Create a BodyAnchorProvider at runtime if body reference mode is enabled and no provider is assigned.")]
    public bool autoCreateBodyAnchorProvider = true;
    [Tooltip("Use the original initial-head-position reference while the waist controller is unavailable.")]
    public bool fallbackToInitialHeadReference = false;
    [Tooltip("Compute planar lean in the current body-yaw frame so whole-body turns do not become movement input.")]
    public bool normalizePlanarOffsetByBodyYaw = true;
    [Tooltip("Experimental: compute yaw control from HMD yaw relative to body yaw. Keep off until the body yaw source is stable; planar translation can still use the body anchor.")]
    public bool useBodyRelativeHeadYaw = false;
    [Tooltip("When body tracking returns after a Link/session dropout, refresh the neutral HMD yaw so a tracking-origin reset does not become continuous turning.")]
    public bool recenterHeadYawOnBodyAnchorReacquired = true;
    public float bodyAnchorReacquireRecenterDelay = 0.5f;
    [Tooltip("Log which planar reference is actually being used at runtime.")]
    public bool logBodyAnchorStatus = true;
    public float bodyAnchorStatusLogInterval = 2f;

    [Header("Adaptive Calibration")]
    [Tooltip("Estimate each user's neutral body lean, natural sway, and optional comfortable max lean from live body tracking.")]
    public bool adaptiveCalibrationEnabled = true;
    [Tooltip("Automatically sample neutral posture and natural sway when the component starts.")]
    public bool autoCalibrateNeutralOnStart = true;
    [Tooltip("Starts a full adaptive calibration sequence: neutral/sway, forward, backward, left, right.")]
    public KeyCode fullCalibrationKey = KeyCode.V;
    [Tooltip("Seconds to hold a relaxed neutral posture while estimating neutral and natural sway.")]
    public float neutralCalibrationDuration = 2f;
    [Tooltip("Seconds to lean comfortably in each requested direction during full calibration.")]
    public float directionCalibrationDuration = 1.5f;
    [Tooltip("Keep locomotion output at zero while a calibration stage is running.")]
    public bool freezeMovementDuringCalibration = true;
    [Tooltip("Natural sway standard deviation multiplier used to derive the adaptive deadzone.")]
    public float swayDeadZoneMultiplier = 3f;
    public float minAdaptiveDeadZone = 0.002f;
    public float maxAdaptiveDeadZone = 0.02f;
    public float minAdaptiveMaxOffset = 0.012f;
    public float maxAdaptiveMaxOffset = 0.16f;
    [Tooltip("Use this fraction of the user's comfortable max lean as the offset that reaches max speed.")]
    public float comfortableMaxOffsetScale = 0.85f;

    [Header("Online MaxOffset Learning")]
    [Tooltip("Continuously estimate each user's forward/back/left/right comfortable lean range from recent movement.")]
    public bool onlineMaxOffsetLearningEnabled = false;
    [Tooltip("Number of recent samples kept per direction for percentile estimation.")]
    public int onlineMaxOffsetWindowSamples = 240;
    [Tooltip("Seconds between online learning samples.")]
    public float onlineMaxOffsetSampleInterval = 0.1f;
    [Tooltip("Seconds between maxOffset updates from the sampled percentile.")]
    public float onlineMaxOffsetUpdateInterval = 0.5f;
    [Tooltip("High percentile used as the recent comfortable offset estimate.")]
    [Range(0.5f, 0.98f)]
    public float onlineMaxOffsetPercentile = 0.9f;
    [Tooltip("Minimum samples in a direction before that direction can update maxOffset.")]
    public int onlineMaxOffsetMinSamplesPerDirection = 8;
    [Tooltip("Ignore projections below this fraction of the current deadzone.")]
    public float onlineMaxOffsetDeadZoneMultiplier = 1.15f;
    [Tooltip("Ignore projections smaller than this absolute offset in meters.")]
    public float onlineMaxOffsetMinProjection = 0.002f;
    [Tooltip("Adapt faster when the learned maxOffset becomes smaller, making locomotion more sensitive.")]
    public float onlineMaxOffsetMoreSensitiveRate = 4f;
    [Tooltip("Adapt slower when the learned maxOffset becomes larger, avoiding one big motion making locomotion sluggish.")]
    public float onlineMaxOffsetLessSensitiveRate = 0.8f;
    [Tooltip("Seconds to pause online learning after neutral recalibration or tracking reacquisition.")]
    public float onlineMaxOffsetPauseAfterRecenter = 0.75f;
    [Tooltip("Also estimate the start-moving threshold from near-neutral natural sway samples.")]
    public bool onlineDeadZoneLearningEnabled = true;
    [Tooltip("High percentile used as the recent natural sway estimate for the movement deadzone.")]
    [Range(0.5f, 0.98f)]
    public float onlineDeadZonePercentile = 0.9f;
    [Tooltip("Safety multiplier applied to the learned natural sway percentile.")]
    public float onlineDeadZoneSafetyScale = 1.25f;
    [Tooltip("Minimum near-neutral samples before the deadzone can update.")]
    public int onlineDeadZoneMinSamples = 12;
    [Tooltip("Only collect deadzone samples while the previous planar command is below this normalized magnitude.")]
    public float onlineDeadZoneMaxPlanarCommandForSample = 0.08f;
    [Tooltip("Only collect deadzone samples below current deadzone times this gate.")]
    public float onlineDeadZoneSampleGateMultiplier = 2.5f;
    [Tooltip("How quickly the learned movement threshold follows recent natural sway.")]
    public float onlineDeadZoneAdaptRate = 1.5f;

    [Header("Bayes Data Logging")]
    [Tooltip("Record locomotion/tracking samples for later Bayesian personalization experiments.")]
    public bool bayesDataLoggingEnabled = true;
    [Tooltip("Seconds between recorded samples. 0 records every frame.")]
    public float bayesDataLoggingInterval = 1f / 30f;
    [Tooltip("Folder name under the user's Desktop.")]
    public string bayesDataFolderName = "BayesData";
    [Tooltip("Seconds between file flushes.")]
    public float bayesDataFlushInterval = 2f;

    public bool DebugUsingBodyRelativeHeadYaw => debugUsingBodyRelativeHeadYaw;
    public bool DebugHasBodyRelativeHeadYaw => debugHasBodyRelativeHeadYaw;
    public bool DebugHasNeutralBodyRelativeHeadYaw => hasNeutralBodyRelativeHeadYaw;
    public float DebugBodyRelativeHeadYawDeg => debugBodyRelativeHeadYawDeg;
    public float DebugNeutralBodyRelativeHeadYawDeg => neutralBodyRelativeHeadYawDeg;
    public float DebugBodyRelativeHeadYawDeltaDeg => debugBodyRelativeHeadYawDeltaDeg;
    public float DebugYawRateDegPerSecond => debugYawRateDegPerSecond;
    public bool DebugHasBodyAnchor => debugHasBodyAnchor;
    public bool DebugBodyAnchorUsesYaw => debugBodyAnchorUsesYaw;
    public bool DebugUsingInitialHeadReferenceFallback => usingInitialHeadReferenceThisFrame;
    public Pose DebugBodyAnchorLocalPose => debugBodyAnchorLocalPose;
    public Vector3 DebugHeadLocalPosition => debugHeadLocalPosition;
    public Vector3 DebugCenterOffsetLocal => debugCenterOffsetLocal;
    public Vector3 DebugPlanarOffsetLocal => debugPlanarOffsetLocal;
    public Vector3 DebugPlanarDirectionLocal => debugPlanarDirectionLocal;
    public Vector3 DebugDesiredPlanarVelocityLocal => debugDesiredPlanarVelocityLocal;
    public Vector3 DebugSmoothedPlanarVelocityLocal => smoothedPlanarVelocityLocal;
    public Vector3 DebugControlHeadForwardLocal => debugControlHeadForwardLocal;
    public Vector3 DebugPlanarSignalPointLocal => debugPlanarSignalPointLocal;
    public Vector3 DebugPlanarSignalAnchorLocal => debugPlanarSignalAnchorLocal;
    public string DebugPlanarSignalName => debugPlanarSignalName;
    public Vector3 DebugBodyLocalHeadOffset => debugBodyLocalHeadOffset;
    public Vector3 DebugNeutralBodyLocalHeadOffset => neutralBodyLocalHeadOffset;
    public Vector3 DebugBodyLocalMoveOffset => debugBodyLocalMoveOffset;
    public Quaternion DebugBodyYawLocal => debugBodyYawLocal;
    public Vector2 DebugPlanarCommand => debugPlanarCommand;
    public float DebugVerticalCommand => debugVerticalCommand;
    public AdaptiveCalibrationStage DebugAdaptiveCalibrationStage => adaptiveCalibrationStage;
    public string DebugAdaptiveCalibrationStatus => debugAdaptiveCalibrationStatus;
    public float DebugAdaptiveCalibrationProgress => debugAdaptiveCalibrationProgress;
    public bool DebugHasAdaptiveNeutral => hasAdaptiveNeutral;
    public bool DebugHasDirectionalCalibration => hasDirectionalCalibration;
    public float DebugEffectivePlanarDeadZone => debugEffectivePlanarDeadZone;
    public float DebugEffectivePlanarMaxOffset => debugEffectivePlanarMaxOffset;
    public bool DebugOnlineMaxOffsetLearningEnabled => onlineMaxOffsetLearningEnabled;
    public bool DebugHasOnlineMaxOffsetLearning => hasOnlineMaxOffsetLearning;
    public bool DebugHasOnlineDeadZoneLearning => hasOnlineDeadZoneLearning;
    public float DebugOnlineLearnedDeadZone => onlineLearnedDeadZone;
    public string DebugOnlineMaxOffsetLearningStatus => debugOnlineMaxOffsetLearningStatus;
    public Vector4 DebugOnlineMaxOffsetSampleCounts => new Vector4(
        onlineDirectionSampleCounts != null ? onlineDirectionSampleCounts[OnlineDirectionLeft] : 0,
        onlineDirectionSampleCounts != null ? onlineDirectionSampleCounts[OnlineDirectionRight] : 0,
        onlineDirectionSampleCounts != null ? onlineDirectionSampleCounts[OnlineDirectionForward] : 0,
        onlineDirectionSampleCounts != null ? onlineDirectionSampleCounts[OnlineDirectionBackward] : 0);
    public Vector4 DebugDirectionalMaxOffsets => new Vector4(
        calibratedLeftMaxOffset,
        calibratedRightMaxOffset,
        calibratedForwardMaxOffset,
        calibratedBackwardMaxOffset);

    [Header("Orientation & Vertical")]
    public OrientationControlMode orientationMode = OrientationControlMode.Dynamic;
    [Tooltip("Maximum vertical speed for dynamic mode (m/s).")]
    public float verticalSpeed = 10f;
    [Tooltip("Maximum yaw rate for dynamic mode (deg/s).")]
    public float yawSpeed = 180f;
    public float staticYawThresholdDeg = 30f;
    public float staticYawSpeedDeg = 60f;
    public float staticPitchUpThresholdDeg = 30f;
    public float staticPitchDownThresholdDeg = 30f;
    public float staticPitchUpSpeed = 5f;
    public float staticPitchDownSpeed = 5f;
    public float coupledYawMaxSpeedDeg = 200f;
    public float coupledYawGain = 4f;
    public float coupledPitchMaxSpeed = 5f;
    public float coupledPitchReferenceDeg = 30f;

    [Header("Bounds / Recenter")]
    public Vector3 positionLimits = 2000f * Vector3.one;
    public KeyCode recenterKey = KeyCode.C;
    [Tooltip("Also reset target rig pose to its initial pose when recenter key is pressed.")]
    public bool resetTargetPoseOnRecenter = true;

    private Vector3 centerHeadLocal;
    private Vector3 neutralBodyLocalHeadOffset;
    private Quaternion centerHeadOrientationLocal = Quaternion.identity;
    private Vector3 smoothedPlanarVelocityLocal;
    private Vector3 initialTargetPosition;
    private Quaternion initialTargetRotation = Quaternion.identity;
    private bool hasInitialTargetPose;
    private bool hasCenterHeadOrientation;
    private bool hasCenterBodyAnchor;
    private bool centerBodyAnchorUsesYaw;
    private bool hasNeutralBodyRelativeHeadYaw;
    private float neutralBodyRelativeHeadYawDeg;
    private bool warnedUnsupportedRecenterKey;
    private bool warnedUnsupportedFullCalibrationKey;
    private bool warnedBodyAnchorFallback;
    private bool usingInitialHeadReferenceThisFrame;
    private bool hasLoggedBodyAnchorState;
    private bool lastUsingBodyAnchor;
    private float nextBodyAnchorStatusLogTime;
    private bool wasBodyAnchorAvailable;
    private float bodyAnchorLostTime = -1f;
    private bool debugUsingBodyRelativeHeadYaw;
    private bool debugHasBodyRelativeHeadYaw;
    private bool debugHasBodyAnchor;
    private bool debugBodyAnchorUsesYaw;
    private Pose debugBodyAnchorLocalPose;
    private Vector3 debugHeadLocalPosition;
    private Vector3 debugCenterOffsetLocal;
    private Vector3 debugPlanarOffsetLocal;
    private Vector3 debugPlanarDirectionLocal;
    private Vector3 debugDesiredPlanarVelocityLocal;
    private Vector3 debugControlHeadForwardLocal;
    private Vector3 debugPlanarSignalPointLocal;
    private Vector3 debugPlanarSignalAnchorLocal;
    private string debugPlanarSignalName = "Unavailable";
    private Vector3 debugBodyLocalHeadOffset;
    private Vector3 debugBodyLocalMoveOffset;
    private Quaternion debugBodyYawLocal = Quaternion.identity;
    private Vector2 debugPlanarCommand;
    private float debugVerticalCommand;
    private float debugBodyRelativeHeadYawDeg;
    private float debugBodyRelativeHeadYawDeltaDeg;
    private float debugYawRateDegPerSecond;
    private AdaptiveCalibrationStage adaptiveCalibrationStage = AdaptiveCalibrationStage.Idle;
    private bool calibrationWantsDirectionalSequence;
    private bool hasAdaptiveNeutral;
    private bool hasDirectionalCalibration;
    private float calibrationElapsed;
    private int calibrationSampleCount;
    private Vector2 neutralMean2;
    private Vector2 neutralM2;
    private float currentDirectionMaxProjection;
    private float adaptiveDeadZone;
    private float calibratedForwardMaxOffset;
    private float calibratedBackwardMaxOffset;
    private float calibratedLeftMaxOffset;
    private float calibratedRightMaxOffset;
    private string debugAdaptiveCalibrationStatus = "Idle";
    private float debugAdaptiveCalibrationProgress;
    private float debugEffectivePlanarDeadZone;
    private float debugEffectivePlanarMaxOffset;
    private float[][] onlineDirectionSamples;
    private float[] onlineDirectionScratch;
    private int[] onlineDirectionSampleCounts;
    private int[] onlineDirectionSampleIndices;
    private float onlineMaxOffsetSampleTimer;
    private float onlineMaxOffsetUpdateTimer;
    private float onlineMaxOffsetPauseUntilTime;
    private bool hasOnlineMaxOffsetLearning;
    private float[] onlineDeadZoneSamples;
    private int onlineDeadZoneSampleCount;
    private int onlineDeadZoneSampleIndex;
    private bool hasOnlineDeadZoneLearning;
    private float onlineLearnedDeadZone;
    private string debugOnlineMaxOffsetLearningStatus = "Idle";
    private StreamWriter bayesDataWriter;
    private readonly StringBuilder bayesDataLineBuilder = new StringBuilder(2048);
    private string bayesDataFilePath;
    private string bayesDataSessionId;
    private string pendingBayesEvent = "session_start";
    private float bayesDataSampleTimer;
    private float bayesDataFlushTimer;
    private int bayesDataSampleIndex;

    private void Awake()
    {
        if (target == null)
        {
            target = transform;
        }
    }

    private void OnEnable()
    {
        if (!TryResolveReferences())
        {
            enabled = false;
            return;
        }

        var controllerLocomotion = GetComponent<QuestDualStickXRRigLocomotion>();
        if (controllerLocomotion != null && controllerLocomotion.enabled)
        {
            Debug.LogWarning(
                $"{nameof(HeadOffsetLocomotion)} and {nameof(QuestDualStickXRRigLocomotion)} are both enabled on '{name}'. " +
                $"{nameof(HeadOffsetLocomotion)} has been disabled to avoid conflicting movement directions.",
                this);
            enabled = false;
            return;
        }

        CacheInitialTargetPose();
        ResetAdaptiveCalibrationRuntime();
        CaptureCenter();
        if (adaptiveCalibrationEnabled && autoCalibrateNeutralOnStart && planarReferenceMode == PlanarReferenceMode.BodyAnchor)
        {
            BeginNeutralAndSwayCalibration(false, "Auto neutral/sway calibration started. Hold a relaxed posture.");
        }

        smoothedPlanarVelocityLocal = Vector3.zero;
        BeginBayesDataLogging();
    }

    private void OnDisable()
    {
        EndBayesDataLogging();
    }

    private void Update()
    {
        if (target == null || head == null)
        {
            return;
        }

        if (WasRecenterPressedThisFrame())
        {
            RecenterNow();
        }

        if (WasFullCalibrationPressedThisFrame())
        {
            BeginFullAdaptiveCalibration();
        }

        Vector3 headLocalPos = target.InverseTransformPoint(head.position);
        debugHeadLocalPosition = headLocalPos;
        Vector3 centerOffsetLocal = ComputeCenterOffsetLocal(headLocalPos);
        debugCenterOffsetLocal = centerOffsetLocal;
        Vector3 planarOffsetLocal = new Vector3(centerOffsetLocal.x, 0f, centerOffsetLocal.z);
        debugPlanarOffsetLocal = planarOffsetLocal;

        float planarSpeed = ComputePlanarSpeed(planarOffsetLocal);
        Vector3 planarDirectionLocal = ComputePlanarDirectionLocal(planarOffsetLocal);
        debugPlanarDirectionLocal = planarDirectionLocal;
        Vector3 desiredPlanarVelocityLocal = planarDirectionLocal * planarSpeed;
        debugDesiredPlanarVelocityLocal = desiredPlanarVelocityLocal;
        float planarLerp = 1f - Mathf.Exp(-Mathf.Max(0f, planarSmoothing) * Time.deltaTime);
        smoothedPlanarVelocityLocal = Vector3.Lerp(smoothedPlanarVelocityLocal, desiredPlanarVelocityLocal, planarLerp);

        Vector3 headForwardLocal = GetControlHeadForwardLocal();
        debugControlHeadForwardLocal = headForwardLocal;
        Vector2 planarCommand = horizontalSpeed > 1e-6f
            ? new Vector2(smoothedPlanarVelocityLocal.x, smoothedPlanarVelocityLocal.z) / horizontalSpeed
            : Vector2.zero;
        debugPlanarCommand = planarCommand;

        float yawRateRad = ComputeYawRate(headForwardLocal, planarCommand);
        debugYawRateDegPerSecond = yawRateRad * Mathf.Rad2Deg;
        float verticalCommand = ComputeVerticalSpeed(headForwardLocal);
        debugVerticalCommand = verticalCommand;

        Vector3 localVelocity = new Vector3(smoothedPlanarVelocityLocal.x, verticalCommand, smoothedPlanarVelocityLocal.z);
        Vector3 worldVelocity = target.TransformDirection(localVelocity);
        Vector3 newPos = target.position + worldVelocity * Time.deltaTime;
        target.position = ClampPosition(newPos, positionLimits);

        float yawDeg = yawRateRad * Mathf.Rad2Deg * Time.deltaTime;
        target.Rotate(Vector3.up, yawDeg, Space.World);

        RecordBayesDataSampleIfNeeded();
    }

    private bool TryResolveReferences()
    {
        if (target == null)
        {
            target = transform;
        }

        if (head == null && autoBindHeadFromMainCamera && Camera.main != null)
        {
            head = Camera.main.transform;
        }

        if (target != null && head != null)
        {
            ResolveBodyAnchorProvider();
            return true;
        }

        Debug.LogWarning(
            $"{nameof(HeadOffsetLocomotion)} on '{name}' requires both a target rig and a head transform. " +
            $"Assign them explicitly, or enable {nameof(autoBindHeadFromMainCamera)} if you want to fall back to Camera.main.",
            this);
        return false;
    }

    private void CaptureCenter()
    {
        if (target == null || head == null)
        {
            centerHeadLocal = Vector3.zero;
            centerHeadOrientationLocal = Quaternion.identity;
            hasCenterHeadOrientation = false;
            hasNeutralBodyRelativeHeadYaw = false;
            return;
        }

        centerHeadLocal = target.InverseTransformPoint(head.position);
        hasCenterBodyAnchor = false;
        centerBodyAnchorUsesYaw = false;
        hasAdaptiveNeutral = false;
        adaptiveDeadZone = Mathf.Max(0f, planarDeadZone);
        hasNeutralBodyRelativeHeadYaw = false;
        wasBodyAnchorAvailable = false;
        bodyAnchorLostTime = -1f;
        ResetOnlineMaxOffsetLearningBuffers(false);
        if (TryGetBodyAnchorLocalPose(out Pose bodyAnchorLocalPose))
        {
            if (TryGetBodyPlanarSignalLocal(bodyAnchorLocalPose, centerHeadLocal, out Vector3 signalLocalPos, out _))
            {
                neutralBodyLocalHeadOffset = ComputeBodyReferencedOffsetLocal(
                    signalLocalPos,
                    bodyAnchorLocalPose,
                    out centerBodyAnchorUsesYaw,
                    out _);
                hasCenterBodyAnchor = true;
                warnedBodyAnchorFallback = false;
            }

            if (TryComputeBodyRelativeHeadYawDeg(
                    bodyAnchorLocalPose,
                    target.InverseTransformDirection(head.forward),
                    out neutralBodyRelativeHeadYawDeg))
            {
                hasNeutralBodyRelativeHeadYaw = true;
            }

            wasBodyAnchorAvailable = true;
        }

        CaptureHeadOrientationCenter();
    }

    private void CacheInitialTargetPose()
    {
        if (target == null)
        {
            hasInitialTargetPose = false;
            return;
        }

        initialTargetPosition = target.position;
        initialTargetRotation = target.rotation;
        hasInitialTargetPose = true;
    }

    private void RecenterNow()
    {
        if (resetTargetPoseOnRecenter && hasInitialTargetPose && target != null)
        {
            target.SetPositionAndRotation(initialTargetPosition, initialTargetRotation);
        }

        smoothedPlanarVelocityLocal = Vector3.zero;
        CaptureInitialHeadReferenceCenter();
        AddBayesEvent("recenter");
        if (adaptiveCalibrationEnabled && planarReferenceMode == PlanarReferenceMode.BodyAnchor)
        {
            BeginNeutralAndSwayCalibration(false, "Neutral/sway calibration restarted. Hold a relaxed posture.");
        }
        else
        {
            CaptureCenter();
        }
    }

    private void BeginBayesDataLogging()
    {
        EndBayesDataLogging();

        if (!bayesDataLoggingEnabled)
        {
            return;
        }

        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktop))
            {
                desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            }

            if (string.IsNullOrWhiteSpace(desktop))
            {
                Debug.LogWarning($"{nameof(HeadOffsetLocomotion)} on '{name}' could not resolve the Desktop path for Bayes data logging.", this);
                return;
            }

            string folderName = string.IsNullOrWhiteSpace(bayesDataFolderName) ? "BayesData" : bayesDataFolderName.Trim();
            string directory = Path.Combine(desktop, folderName);
            Directory.CreateDirectory(directory);

            bayesDataSessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string sceneName = SanitizeFileName(SceneManager.GetActiveScene().name);
            string fileName = $"AdaptiveFly_{sceneName}_{bayesDataSessionId}_{GetInstanceID()}.csv";
            bayesDataFilePath = Path.Combine(directory, fileName);
            bayesDataWriter = new StreamWriter(bayesDataFilePath, false, Encoding.UTF8);
            WriteBayesDataHeader();

            bayesDataSampleIndex = 0;
            bayesDataSampleTimer = 0f;
            bayesDataFlushTimer = 0f;
            pendingBayesEvent = "session_start";

            Debug.Log($"{nameof(HeadOffsetLocomotion)} on '{name}' logging Bayes data to {bayesDataFilePath}", this);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{nameof(HeadOffsetLocomotion)} on '{name}' could not start Bayes data logging: {ex.Message}", this);
            EndBayesDataLogging();
        }
    }

    private void EndBayesDataLogging()
    {
        if (bayesDataWriter == null)
        {
            return;
        }

        try
        {
            AddBayesEvent("session_end");
            WriteBayesDataSample(true);
            bayesDataWriter.Flush();
            bayesDataWriter.Dispose();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{nameof(HeadOffsetLocomotion)} on '{name}' could not close Bayes data logging cleanly: {ex.Message}", this);
        }
        finally
        {
            bayesDataWriter = null;
        }
    }

    private void RecordBayesDataSampleIfNeeded()
    {
        if (bayesDataWriter == null)
        {
            return;
        }

        float interval = Mathf.Max(0f, bayesDataLoggingInterval);
        if (interval > 0f)
        {
            bayesDataSampleTimer += Time.unscaledDeltaTime;
            if (bayesDataSampleTimer < interval && string.IsNullOrEmpty(pendingBayesEvent))
            {
                return;
            }

            bayesDataSampleTimer = Mathf.Repeat(bayesDataSampleTimer, interval);
        }

        WriteBayesDataSample(false);

        bayesDataFlushTimer += Time.unscaledDeltaTime;
        if (bayesDataFlushTimer >= Mathf.Max(0.1f, bayesDataFlushInterval))
        {
            bayesDataFlushTimer = 0f;
            bayesDataWriter.Flush();
        }
    }

    private void AddBayesEvent(string eventName)
    {
        if (string.IsNullOrEmpty(eventName))
        {
            return;
        }

        pendingBayesEvent = string.IsNullOrEmpty(pendingBayesEvent)
            ? eventName
            : $"{pendingBayesEvent}|{eventName}";
    }

    private void WriteBayesDataHeader()
    {
        bayesDataWriter.WriteLine(
            "session_id,sample_index,utc_iso,realtime_s,time_s,delta_time_s,frame,scene,event,reference_mode," +
            "has_waist_anchor,using_hmd_fallback,body_anchor_uses_yaw,planar_reference_mode,orientation_mode," +
            "online_learning_enabled,has_online_learning,has_online_deadzone_learning,online_learned_deadzone_m,online_deadzone_sample_count,online_learning_status," +
            "target_world_x,target_world_y,target_world_z,target_yaw_deg," +
            "hmd_world_x,hmd_world_y,hmd_world_z,hmd_local_x,hmd_local_y,hmd_local_z," +
            "hmd_forward_local_x,hmd_forward_local_y,hmd_forward_local_z,hmd_yaw_local_deg,hmd_pitch_local_deg," +
            "control_forward_x,control_forward_y,control_forward_z,control_yaw_deg,control_pitch_deg," +
            "waist_local_x,waist_local_y,waist_local_z,waist_yaw_deg," +
            "body_local_head_x,body_local_head_z,neutral_body_local_head_x,neutral_body_local_head_z,body_local_move_x,body_local_move_z," +
            "center_offset_x,center_offset_z,planar_offset_x,planar_offset_z,planar_direction_x,planar_direction_z," +
            "desired_velocity_x,desired_velocity_z,smoothed_velocity_x,smoothed_velocity_z,planar_command_x,planar_command_z," +
            "yaw_rate_deg_s,vertical_command_m_s,effective_deadzone_m,effective_max_offset_m,horizontal_speed_m_s,vertical_speed_m_s,yaw_speed_deg_s," +
            "learned_left_max_m,learned_right_max_m,learned_forward_max_m,learned_backward_max_m," +
            "sample_count_left,sample_count_right,sample_count_forward,sample_count_backward,provider_status");
    }

    private void WriteBayesDataSample(bool force)
    {
        if (bayesDataWriter == null || (!force && target == null))
        {
            return;
        }

        string eventName = string.IsNullOrEmpty(pendingBayesEvent) ? "sample" : pendingBayesEvent;
        pendingBayesEvent = string.Empty;

        Vector3 targetPosition = target != null ? target.position : Vector3.zero;
        float targetYawDeg = target != null ? target.eulerAngles.y : 0f;
        Vector3 hmdWorld = head != null ? head.position : Vector3.zero;
        Vector3 hmdLocal = debugHeadLocalPosition;
        Vector3 hmdForwardLocal = target != null && head != null ? target.InverseTransformDirection(head.forward).normalized : Vector3.forward;
        Vector3 controlForward = debugControlHeadForwardLocal.sqrMagnitude > 1e-8f ? debugControlHeadForwardLocal.normalized : Vector3.forward;
        Pose waistPose = debugBodyAnchorLocalPose;
        Vector4 sampleCounts = DebugOnlineMaxOffsetSampleCounts;
        Vector4 learnedOffsets = DebugDirectionalMaxOffsets;

        bayesDataLineBuilder.Length = 0;
        AppendCsv(bayesDataSessionId);
        AppendCsv(bayesDataSampleIndex++);
        AppendCsv(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        AppendCsv(Time.realtimeSinceStartup);
        AppendCsv(Time.time);
        AppendCsv(Time.unscaledDeltaTime);
        AppendCsv(Time.frameCount);
        AppendCsv(SceneManager.GetActiveScene().name);
        AppendCsv(eventName);
        AppendCsv(usingInitialHeadReferenceThisFrame ? "HMD fallback" : debugHasBodyAnchor ? "Waist controller" : "No planar input");
        AppendCsv(debugHasBodyAnchor);
        AppendCsv(usingInitialHeadReferenceThisFrame);
        AppendCsv(debugBodyAnchorUsesYaw);
        AppendCsv(planarReferenceMode.ToString());
        AppendCsv(orientationMode.ToString());
        AppendCsv(onlineMaxOffsetLearningEnabled);
        AppendCsv(hasOnlineMaxOffsetLearning);
        AppendCsv(hasOnlineDeadZoneLearning);
        AppendCsv(onlineLearnedDeadZone);
        AppendCsv(onlineDeadZoneSampleCount);
        AppendCsv(debugOnlineMaxOffsetLearningStatus);
        AppendCsv(targetPosition);
        AppendCsv(targetYawDeg);
        AppendCsv(hmdWorld);
        AppendCsv(hmdLocal);
        AppendCsv(hmdForwardLocal);
        AppendCsv(GetYawDeg(hmdForwardLocal));
        AppendCsv(GetPitchDeg(hmdForwardLocal));
        AppendCsv(controlForward);
        AppendCsv(GetYawDeg(controlForward));
        AppendCsv(GetPitchDeg(controlForward));
        AppendCsv(waistPose.position);
        AppendCsv(debugHasBodyAnchor ? GetYawDeg(waistPose.rotation * Vector3.forward) : 0f);
        AppendCsv(debugBodyLocalHeadOffset.x);
        AppendCsv(debugBodyLocalHeadOffset.z);
        AppendCsv(neutralBodyLocalHeadOffset.x);
        AppendCsv(neutralBodyLocalHeadOffset.z);
        AppendCsv(debugBodyLocalMoveOffset.x);
        AppendCsv(debugBodyLocalMoveOffset.z);
        AppendCsv(debugCenterOffsetLocal.x);
        AppendCsv(debugCenterOffsetLocal.z);
        AppendCsv(debugPlanarOffsetLocal.x);
        AppendCsv(debugPlanarOffsetLocal.z);
        AppendCsv(debugPlanarDirectionLocal.x);
        AppendCsv(debugPlanarDirectionLocal.z);
        AppendCsv(debugDesiredPlanarVelocityLocal.x);
        AppendCsv(debugDesiredPlanarVelocityLocal.z);
        AppendCsv(smoothedPlanarVelocityLocal.x);
        AppendCsv(smoothedPlanarVelocityLocal.z);
        AppendCsv(debugPlanarCommand.x);
        AppendCsv(debugPlanarCommand.y);
        AppendCsv(debugYawRateDegPerSecond);
        AppendCsv(debugVerticalCommand);
        AppendCsv(debugEffectivePlanarDeadZone);
        AppendCsv(debugEffectivePlanarMaxOffset);
        AppendCsv(horizontalSpeed);
        AppendCsv(verticalSpeed);
        AppendCsv(yawSpeed);
        AppendCsv(learnedOffsets.x);
        AppendCsv(learnedOffsets.y);
        AppendCsv(learnedOffsets.z);
        AppendCsv(learnedOffsets.w);
        AppendCsv(sampleCounts.x);
        AppendCsv(sampleCounts.y);
        AppendCsv(sampleCounts.z);
        AppendCsv(sampleCounts.w);
        AppendCsv(bodyAnchorProvider != null ? bodyAnchorProvider.LastStatus : "No BodyAnchorProvider");

        bayesDataWriter.WriteLine(bayesDataLineBuilder.ToString());
    }

    private void AppendCsv(Vector3 value)
    {
        AppendCsv(value.x);
        AppendCsv(value.y);
        AppendCsv(value.z);
    }

    private void AppendCsv(int value)
    {
        AppendCsv(value.ToString(CultureInfo.InvariantCulture));
    }

    private void AppendCsv(float value)
    {
        AppendCsv(value.ToString("0.######", CultureInfo.InvariantCulture));
    }

    private void AppendCsv(bool value)
    {
        AppendCsv(value ? "1" : "0");
    }

    private void AppendCsv(string value)
    {
        if (bayesDataLineBuilder.Length > 0)
        {
            bayesDataLineBuilder.Append(',');
        }

        if (value == null)
        {
            value = string.Empty;
        }
        bool mustQuote = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
        if (!mustQuote)
        {
            bayesDataLineBuilder.Append(value);
            return;
        }

        bayesDataLineBuilder.Append('"');
        bayesDataLineBuilder.Append(value.Replace("\"", "\"\""));
        bayesDataLineBuilder.Append('"');
    }

    private void CaptureInitialHeadReferenceCenter()
    {
        if (target == null || head == null)
        {
            centerHeadLocal = Vector3.zero;
            centerHeadOrientationLocal = Quaternion.identity;
            hasCenterHeadOrientation = false;
            return;
        }

        centerHeadLocal = target.InverseTransformPoint(head.position);
        CaptureHeadOrientationCenter();
    }

    private void ResetAdaptiveCalibrationRuntime()
    {
        adaptiveCalibrationStage = AdaptiveCalibrationStage.Idle;
        calibrationWantsDirectionalSequence = false;
        hasAdaptiveNeutral = false;
        hasDirectionalCalibration = false;
        calibrationElapsed = 0f;
        calibrationSampleCount = 0;
        neutralMean2 = Vector2.zero;
        neutralM2 = Vector2.zero;
        currentDirectionMaxProjection = 0f;
        adaptiveDeadZone = Mathf.Max(0f, planarDeadZone);
        onlineLearnedDeadZone = ClampAdaptiveDeadZone(planarDeadZone);
        ResetDirectionalCalibrationToDefaults();
        ResetOnlineMaxOffsetLearningBuffers(false);
        debugAdaptiveCalibrationStatus = "Idle";
        debugAdaptiveCalibrationProgress = 0f;
        debugEffectivePlanarDeadZone = Mathf.Max(0f, planarDeadZone);
        debugEffectivePlanarMaxOffset = Mathf.Max(debugEffectivePlanarDeadZone + 1e-4f, planarMaxOffset);
    }

    private void ResetDirectionalCalibrationToDefaults()
    {
        float defaultMax = ClampAdaptiveMaxOffset(planarMaxOffset);
        calibratedForwardMaxOffset = defaultMax;
        calibratedBackwardMaxOffset = defaultMax;
        calibratedLeftMaxOffset = defaultMax;
        calibratedRightMaxOffset = defaultMax;
    }

    private void ResetOnlineMaxOffsetLearningBuffers(bool resetLearnedMaxOffsets)
    {
        EnsureOnlineMaxOffsetLearningBuffers();

        for (int i = 0; i < OnlineDirectionCount; i++)
        {
            onlineDirectionSampleCounts[i] = 0;
            onlineDirectionSampleIndices[i] = 0;
        }

        onlineDeadZoneSampleCount = 0;
        onlineDeadZoneSampleIndex = 0;
        onlineMaxOffsetSampleTimer = 0f;
        onlineMaxOffsetUpdateTimer = 0f;
        onlineMaxOffsetPauseUntilTime = Time.unscaledTime + Mathf.Max(0f, onlineMaxOffsetPauseAfterRecenter);
        hasOnlineMaxOffsetLearning = false;
        debugOnlineMaxOffsetLearningStatus = onlineMaxOffsetLearningEnabled ? "Collecting samples." : "Disabled";

        if (resetLearnedMaxOffsets)
        {
            ResetDirectionalCalibrationToDefaults();
            hasDirectionalCalibration = false;
            onlineLearnedDeadZone = ClampAdaptiveDeadZone(planarDeadZone);
            hasOnlineDeadZoneLearning = false;
        }
    }

    private void EnsureOnlineMaxOffsetLearningBuffers()
    {
        int windowSize = Mathf.Clamp(onlineMaxOffsetWindowSamples, 8, 2000);
        bool needsAllocate =
            onlineDirectionSamples == null ||
            onlineDirectionSamples.Length != OnlineDirectionCount ||
            onlineDirectionSamples[0] == null ||
            onlineDirectionSamples[0].Length != windowSize ||
            onlineDeadZoneSamples == null ||
            onlineDeadZoneSamples.Length != windowSize;

        if (!needsAllocate)
        {
            return;
        }

        onlineDirectionSamples = new float[OnlineDirectionCount][];
        for (int i = 0; i < OnlineDirectionCount; i++)
        {
            onlineDirectionSamples[i] = new float[windowSize];
        }

        onlineDirectionScratch = new float[windowSize];
        onlineDirectionSampleCounts = new int[OnlineDirectionCount];
        onlineDirectionSampleIndices = new int[OnlineDirectionCount];
        onlineDeadZoneSamples = new float[windowSize];
        onlineDeadZoneSampleCount = 0;
        onlineDeadZoneSampleIndex = 0;
    }

    private void PauseOnlineMaxOffsetLearning()
    {
        onlineMaxOffsetSampleTimer = 0f;
        onlineMaxOffsetUpdateTimer = 0f;
        onlineMaxOffsetPauseUntilTime = Time.unscaledTime + Mathf.Max(0f, onlineMaxOffsetPauseAfterRecenter);
    }

    private void BeginFullAdaptiveCalibration()
    {
        if (!adaptiveCalibrationEnabled || planarReferenceMode != PlanarReferenceMode.BodyAnchor)
        {
            Debug.LogWarning(
                $"{nameof(HeadOffsetLocomotion)} on '{name}' cannot start full adaptive calibration because adaptive body-reference calibration is disabled.",
                this);
            return;
        }

        ResetDirectionalCalibrationToDefaults();
        ResetOnlineMaxOffsetLearningBuffers(false);
        hasDirectionalCalibration = false;
        BeginNeutralAndSwayCalibration(true, "Full adaptive calibration started. Hold a relaxed posture.");
    }

    private void BeginNeutralAndSwayCalibration(bool includeDirectionalSequence, string logMessage)
    {
        ResolveBodyAnchorProvider();
        CaptureHeadOrientationCenter();
        smoothedPlanarVelocityLocal = Vector3.zero;
        adaptiveCalibrationStage = AdaptiveCalibrationStage.NeutralAndSway;
        calibrationWantsDirectionalSequence = includeDirectionalSequence;
        calibrationElapsed = 0f;
        calibrationSampleCount = 0;
        neutralMean2 = Vector2.zero;
        neutralM2 = Vector2.zero;
        currentDirectionMaxProjection = 0f;
        ResetOnlineMaxOffsetLearningBuffers(false);
        debugAdaptiveCalibrationProgress = 0f;
        debugAdaptiveCalibrationStatus = "Neutral/sway: hold relaxed posture.";
        Debug.Log($"{nameof(HeadOffsetLocomotion)} on '{name}': {logMessage}", this);
    }

    private bool ProcessAdaptiveCalibrationSample(Vector3 bodyReferencedOffset, bool usesBodyYaw, Pose bodyAnchorLocalPose)
    {
        if (!IsAdaptiveCalibrationActive())
        {
            return false;
        }

        bodyReferencedOffset.y = 0f;

        switch (adaptiveCalibrationStage)
        {
            case AdaptiveCalibrationStage.NeutralAndSway:
                SampleNeutralAndSway(bodyReferencedOffset, usesBodyYaw, bodyAnchorLocalPose);
                break;
            case AdaptiveCalibrationStage.Forward:
            case AdaptiveCalibrationStage.Backward:
            case AdaptiveCalibrationStage.Left:
            case AdaptiveCalibrationStage.Right:
                SampleDirectionalCalibration(bodyReferencedOffset);
                break;
        }

        return true;
    }

    private void SampleNeutralAndSway(Vector3 bodyReferencedOffset, bool usesBodyYaw, Pose bodyAnchorLocalPose)
    {
        AddNeutralSample(new Vector2(bodyReferencedOffset.x, bodyReferencedOffset.z));

        float duration = Mathf.Max(0.05f, neutralCalibrationDuration);
        calibrationElapsed += Mathf.Max(0f, Time.unscaledDeltaTime);
        debugAdaptiveCalibrationProgress = Mathf.Clamp01(calibrationElapsed / duration);
        debugAdaptiveCalibrationStatus =
            $"Neutral/sway: hold relaxed posture ({debugAdaptiveCalibrationProgress:P0}).";

        if (calibrationElapsed < duration || calibrationSampleCount <= 0)
        {
            return;
        }

        CompleteNeutralAndSwayCalibration(usesBodyYaw, bodyAnchorLocalPose);
    }

    private void AddNeutralSample(Vector2 sample)
    {
        calibrationSampleCount++;
        if (calibrationSampleCount == 1)
        {
            neutralMean2 = sample;
            neutralM2 = Vector2.zero;
            return;
        }

        Vector2 delta = sample - neutralMean2;
        neutralMean2 += delta / calibrationSampleCount;
        Vector2 delta2 = sample - neutralMean2;
        neutralM2.x += delta.x * delta2.x;
        neutralM2.y += delta.y * delta2.y;
    }

    private void CompleteNeutralAndSwayCalibration(bool usesBodyYaw, Pose bodyAnchorLocalPose)
    {
        neutralBodyLocalHeadOffset = new Vector3(neutralMean2.x, 0f, neutralMean2.y);
        hasCenterBodyAnchor = true;
        centerBodyAnchorUsesYaw = usesBodyYaw;
        hasAdaptiveNeutral = true;
        warnedBodyAnchorFallback = false;

        float varianceX = calibrationSampleCount > 1 ? neutralM2.x / (calibrationSampleCount - 1) : 0f;
        float varianceZ = calibrationSampleCount > 1 ? neutralM2.y / (calibrationSampleCount - 1) : 0f;
        float swaySigma = Mathf.Sqrt(Mathf.Max(0f, varianceX + varianceZ));
        adaptiveDeadZone = ClampAdaptiveDeadZone(swaySigma * Mathf.Max(0f, swayDeadZoneMultiplier));

        hasNeutralBodyRelativeHeadYaw = TryComputeBodyRelativeHeadYawDeg(
            bodyAnchorLocalPose,
            target.InverseTransformDirection(head.forward),
            out neutralBodyRelativeHeadYawDeg);

        Debug.Log(
            $"{nameof(HeadOffsetLocomotion)} on '{name}': neutral/sway complete. " +
            $"neutral=({neutralBodyLocalHeadOffset.x:0.000}, {neutralBodyLocalHeadOffset.z:0.000})m, " +
            $"swaySigma={swaySigma:0.0000}m, deadzone={adaptiveDeadZone:0.000}m.",
            this);

        if (calibrationWantsDirectionalSequence)
        {
            BeginDirectionalCalibration(AdaptiveCalibrationStage.Forward);
        }
        else
        {
            FinishAdaptiveCalibration("Neutral/sway calibration complete.");
        }
    }

    private void BeginDirectionalCalibration(AdaptiveCalibrationStage stage)
    {
        adaptiveCalibrationStage = stage;
        calibrationElapsed = 0f;
        calibrationSampleCount = 0;
        currentDirectionMaxProjection = 0f;
        debugAdaptiveCalibrationProgress = 0f;
        debugAdaptiveCalibrationStatus = $"{GetDirectionStageLabel(stage)}: lean comfortably.";
        Debug.Log(
            $"{nameof(HeadOffsetLocomotion)} on '{name}': lean {GetDirectionStageLabel(stage).ToLowerInvariant()} comfortably.",
            this);
    }

    private void SampleDirectionalCalibration(Vector3 bodyReferencedOffset)
    {
        Vector3 delta = bodyReferencedOffset - neutralBodyLocalHeadOffset;
        delta.y = 0f;
        Vector3 axis = GetCalibrationAxis(adaptiveCalibrationStage);
        float projection = Mathf.Max(0f, Vector3.Dot(delta, axis));
        currentDirectionMaxProjection = Mathf.Max(currentDirectionMaxProjection, projection);
        calibrationSampleCount++;

        float duration = Mathf.Max(0.05f, directionCalibrationDuration);
        calibrationElapsed += Mathf.Max(0f, Time.unscaledDeltaTime);
        debugAdaptiveCalibrationProgress = Mathf.Clamp01(calibrationElapsed / duration);
        debugAdaptiveCalibrationStatus =
            $"{GetDirectionStageLabel(adaptiveCalibrationStage)}: lean comfortably ({debugAdaptiveCalibrationProgress:P0}, max {currentDirectionMaxProjection:0.000}m).";

        if (calibrationElapsed < duration)
        {
            return;
        }

        CompleteDirectionalCalibration(adaptiveCalibrationStage);
    }

    private void CompleteDirectionalCalibration(AdaptiveCalibrationStage completedStage)
    {
        float calibratedMax = currentDirectionMaxProjection > 1e-4f
            ? ClampAdaptiveMaxOffset(currentDirectionMaxProjection * Mathf.Max(0.1f, comfortableMaxOffsetScale))
            : ClampAdaptiveMaxOffset(planarMaxOffset);

        Debug.Log(
            $"{nameof(HeadOffsetLocomotion)} on '{name}': {GetDirectionStageLabel(completedStage)} max offset = {calibratedMax:0.000}m.",
            this);

        switch (completedStage)
        {
            case AdaptiveCalibrationStage.Forward:
                calibratedForwardMaxOffset = calibratedMax;
                BeginDirectionalCalibration(AdaptiveCalibrationStage.Backward);
                break;
            case AdaptiveCalibrationStage.Backward:
                calibratedBackwardMaxOffset = calibratedMax;
                BeginDirectionalCalibration(AdaptiveCalibrationStage.Left);
                break;
            case AdaptiveCalibrationStage.Left:
                calibratedLeftMaxOffset = calibratedMax;
                BeginDirectionalCalibration(AdaptiveCalibrationStage.Right);
                break;
            case AdaptiveCalibrationStage.Right:
                calibratedRightMaxOffset = calibratedMax;
                hasDirectionalCalibration = true;
                FinishAdaptiveCalibration(
                    $"Full calibration complete. L/R/F/B max = " +
                    $"{calibratedLeftMaxOffset:0.000}/{calibratedRightMaxOffset:0.000}/" +
                    $"{calibratedForwardMaxOffset:0.000}/{calibratedBackwardMaxOffset:0.000}m.");
                break;
            default:
                FinishAdaptiveCalibration("Directional calibration complete.");
                break;
        }
    }

    private void FinishAdaptiveCalibration(string status)
    {
        adaptiveCalibrationStage = AdaptiveCalibrationStage.Idle;
        calibrationWantsDirectionalSequence = false;
        calibrationElapsed = 0f;
        calibrationSampleCount = 0;
        currentDirectionMaxProjection = 0f;
        debugAdaptiveCalibrationProgress = 0f;
        debugAdaptiveCalibrationStatus = status;
        PauseOnlineMaxOffsetLearning();
        Debug.Log($"{nameof(HeadOffsetLocomotion)} on '{name}': {status}", this);
    }

    private void MarkAdaptiveCalibrationWaitingForBody()
    {
        if (!IsAdaptiveCalibrationActive())
        {
            return;
        }

        debugAdaptiveCalibrationStatus =
            $"Waiting for body tracking during {GetDirectionStageLabel(adaptiveCalibrationStage)} calibration.";
    }

    private void UpdateOnlineMaxOffsetLearning(Vector3 bodyLocalMoveOffset)
    {
        if (!onlineMaxOffsetLearningEnabled)
        {
            if (hasOnlineMaxOffsetLearning)
            {
                ResetDirectionalCalibrationToDefaults();
                hasDirectionalCalibration = false;
                hasOnlineMaxOffsetLearning = false;
            }

            hasOnlineDeadZoneLearning = false;
            onlineLearnedDeadZone = ClampAdaptiveDeadZone(planarDeadZone);
            debugOnlineMaxOffsetLearningStatus = "Disabled";
            return;
        }

        EnsureOnlineMaxOffsetLearningBuffers();

        if (!hasCenterBodyAnchor || IsAdaptiveCalibrationActive())
        {
            debugOnlineMaxOffsetLearningStatus = "Paused during calibration.";
            return;
        }

        if (Time.unscaledTime < onlineMaxOffsetPauseUntilTime)
        {
            debugOnlineMaxOffsetLearningStatus = "Paused after recenter/tracking reset.";
            return;
        }

        float deltaTime = Mathf.Max(0f, Time.unscaledDeltaTime);
        onlineMaxOffsetSampleTimer += deltaTime;
        onlineMaxOffsetUpdateTimer += deltaTime;

        float sampleInterval = Mathf.Max(0.02f, onlineMaxOffsetSampleInterval);
        if (onlineMaxOffsetSampleTimer >= sampleInterval)
        {
            onlineMaxOffsetSampleTimer = Mathf.Repeat(onlineMaxOffsetSampleTimer, sampleInterval);
            AddOnlineMaxOffsetSamples(bodyLocalMoveOffset);
            AddOnlineDeadZoneSample(bodyLocalMoveOffset);
        }

        float updateInterval = Mathf.Max(0.05f, onlineMaxOffsetUpdateInterval);
        if (onlineMaxOffsetUpdateTimer >= updateInterval)
        {
            float elapsed = onlineMaxOffsetUpdateTimer;
            onlineMaxOffsetUpdateTimer = Mathf.Repeat(onlineMaxOffsetUpdateTimer, updateInterval);
            UpdateOnlineLearningFromPercentiles(elapsed);
        }
    }

    private void AddOnlineMaxOffsetSamples(Vector3 bodyLocalMoveOffset)
    {
        bodyLocalMoveOffset.y = 0f;
        float deadZone = GetEffectivePlanarDeadZone();
        float minProjection = Mathf.Max(
            Mathf.Max(0f, onlineMaxOffsetMinProjection),
            deadZone * Mathf.Max(0f, onlineMaxOffsetDeadZoneMultiplier));

        AddOnlineDirectionSample(OnlineDirectionLeft, Mathf.Max(0f, -bodyLocalMoveOffset.x), minProjection);
        AddOnlineDirectionSample(OnlineDirectionRight, Mathf.Max(0f, bodyLocalMoveOffset.x), minProjection);
        AddOnlineDirectionSample(OnlineDirectionForward, Mathf.Max(0f, bodyLocalMoveOffset.z), minProjection);
        AddOnlineDirectionSample(OnlineDirectionBackward, Mathf.Max(0f, -bodyLocalMoveOffset.z), minProjection);
    }

    private void AddOnlineDeadZoneSample(Vector3 bodyLocalMoveOffset)
    {
        if (!onlineDeadZoneLearningEnabled || onlineDeadZoneSamples == null || onlineDeadZoneSamples.Length == 0)
        {
            return;
        }

        bodyLocalMoveOffset.y = 0f;
        float previousPlanarCommand = horizontalSpeed > 1e-6f
            ? new Vector2(smoothedPlanarVelocityLocal.x, smoothedPlanarVelocityLocal.z).magnitude / Mathf.Max(1e-6f, horizontalSpeed)
            : 0f;
        if (previousPlanarCommand > Mathf.Max(0f, onlineDeadZoneMaxPlanarCommandForSample))
        {
            return;
        }

        float currentDeadZone = hasOnlineDeadZoneLearning
            ? ClampAdaptiveDeadZone(onlineLearnedDeadZone)
            : Mathf.Max(0f, planarDeadZone);
        float gate = Mathf.Max(
            Mathf.Max(0.001f, currentDeadZone * Mathf.Max(1f, onlineDeadZoneSampleGateMultiplier)),
            Mathf.Max(0.001f, planarDeadZone * Mathf.Max(1f, onlineDeadZoneSampleGateMultiplier)));

        float magnitude = bodyLocalMoveOffset.magnitude;
        if (magnitude > gate)
        {
            return;
        }

        onlineDeadZoneSamples[onlineDeadZoneSampleIndex] = magnitude;
        onlineDeadZoneSampleIndex = (onlineDeadZoneSampleIndex + 1) % onlineDeadZoneSamples.Length;
        onlineDeadZoneSampleCount = Mathf.Min(onlineDeadZoneSampleCount + 1, onlineDeadZoneSamples.Length);
    }

    private void AddOnlineDirectionSample(int directionIndex, float projection, float minProjection)
    {
        if (projection < minProjection)
        {
            return;
        }

        float outlierLimit = Mathf.Max(maxAdaptiveMaxOffset, planarMaxOffset) * 1.5f;
        if (projection > outlierLimit)
        {
            return;
        }

        projection = ClampAdaptiveMaxOffset(projection);
        float[] samples = onlineDirectionSamples[directionIndex];
        int writeIndex = onlineDirectionSampleIndices[directionIndex];
        samples[writeIndex] = projection;
        onlineDirectionSampleIndices[directionIndex] = (writeIndex + 1) % samples.Length;
        onlineDirectionSampleCounts[directionIndex] = Mathf.Min(onlineDirectionSampleCounts[directionIndex] + 1, samples.Length);
    }

    private void UpdateOnlineLearningFromPercentiles(float elapsedSeconds)
    {
        int minSamples = Mathf.Max(2, onlineMaxOffsetMinSamplesPerDirection);
        bool updatedAny = false;
        bool updatedDeadZone = TryUpdateDeadZoneFromOnlinePercentile(elapsedSeconds);

        updatedAny |= TryUpdateDirectionalMaxFromPercentile(
            OnlineDirectionLeft,
            minSamples,
            elapsedSeconds,
            ref calibratedLeftMaxOffset);
        updatedAny |= TryUpdateDirectionalMaxFromPercentile(
            OnlineDirectionRight,
            minSamples,
            elapsedSeconds,
            ref calibratedRightMaxOffset);
        updatedAny |= TryUpdateDirectionalMaxFromPercentile(
            OnlineDirectionForward,
            minSamples,
            elapsedSeconds,
            ref calibratedForwardMaxOffset);
        updatedAny |= TryUpdateDirectionalMaxFromPercentile(
            OnlineDirectionBackward,
            minSamples,
            elapsedSeconds,
            ref calibratedBackwardMaxOffset);

        if (updatedAny)
        {
            hasOnlineMaxOffsetLearning = true;
            hasDirectionalCalibration = true;
        }

        string mode = updatedAny || updatedDeadZone ? "Learning" : "Collecting";
        debugOnlineMaxOffsetLearningStatus =
            $"{mode}: DZ {onlineDeadZoneSampleCount} ({GetEffectivePlanarDeadZone():0.000}m), " +
            $"L/R/F/B {onlineDirectionSampleCounts[OnlineDirectionLeft]}/" +
            $"{onlineDirectionSampleCounts[OnlineDirectionRight]}/" +
            $"{onlineDirectionSampleCounts[OnlineDirectionForward]}/" +
            $"{onlineDirectionSampleCounts[OnlineDirectionBackward]}.";
    }

    private bool TryUpdateDeadZoneFromOnlinePercentile(float elapsedSeconds)
    {
        if (!onlineDeadZoneLearningEnabled || onlineDeadZoneSamples == null)
        {
            return false;
        }

        int minSamples = Mathf.Max(2, onlineDeadZoneMinSamples);
        if (onlineDeadZoneSampleCount < minSamples)
        {
            return false;
        }

        float percentileSway = ComputeOnlineDeadZonePercentile(onlineDeadZoneSampleCount);
        float targetDeadZone = ClampAdaptiveDeadZone(percentileSway * Mathf.Max(0.1f, onlineDeadZoneSafetyScale));
        float current = hasOnlineDeadZoneLearning
            ? ClampAdaptiveDeadZone(onlineLearnedDeadZone)
            : ClampAdaptiveDeadZone(planarDeadZone);
        float alpha = 1f - Mathf.Exp(-Mathf.Max(0f, onlineDeadZoneAdaptRate) * Mathf.Max(0f, elapsedSeconds));
        onlineLearnedDeadZone = ClampAdaptiveDeadZone(Mathf.Lerp(current, targetDeadZone, alpha));
        hasOnlineDeadZoneLearning = true;
        return true;
    }

    private bool TryUpdateDirectionalMaxFromPercentile(
        int directionIndex,
        int minSamples,
        float elapsedSeconds,
        ref float currentMaxOffset)
    {
        int count = onlineDirectionSampleCounts[directionIndex];
        if (count < minSamples)
        {
            return false;
        }

        float percentileOffset = ComputeOnlineDirectionPercentile(directionIndex, count);
        float targetMaxOffset = ClampAdaptiveMaxOffset(percentileOffset * Mathf.Max(0.1f, comfortableMaxOffsetScale));
        float rate = targetMaxOffset < currentMaxOffset
            ? onlineMaxOffsetMoreSensitiveRate
            : onlineMaxOffsetLessSensitiveRate;
        float alpha = 1f - Mathf.Exp(-Mathf.Max(0f, rate) * Mathf.Max(0f, elapsedSeconds));
        currentMaxOffset = Mathf.Lerp(currentMaxOffset, targetMaxOffset, alpha);
        currentMaxOffset = ClampAdaptiveMaxOffset(currentMaxOffset);
        return true;
    }

    private float ComputeOnlineDirectionPercentile(int directionIndex, int count)
    {
        float[] samples = onlineDirectionSamples[directionIndex];
        Array.Copy(samples, onlineDirectionScratch, count);
        Array.Sort(onlineDirectionScratch, 0, count);

        float p = Mathf.Clamp01(onlineMaxOffsetPercentile);
        float rank = (count - 1) * p;
        int lower = Mathf.FloorToInt(rank);
        int upper = Mathf.CeilToInt(rank);
        if (lower == upper)
        {
            return onlineDirectionScratch[lower];
        }

        return Mathf.Lerp(onlineDirectionScratch[lower], onlineDirectionScratch[upper], rank - lower);
    }

    private float ComputeOnlineDeadZonePercentile(int count)
    {
        Array.Copy(onlineDeadZoneSamples, onlineDirectionScratch, count);
        Array.Sort(onlineDirectionScratch, 0, count);

        float p = Mathf.Clamp01(onlineDeadZonePercentile);
        float rank = (count - 1) * p;
        int lower = Mathf.FloorToInt(rank);
        int upper = Mathf.CeilToInt(rank);
        if (lower == upper)
        {
            return onlineDirectionScratch[lower];
        }

        return Mathf.Lerp(onlineDirectionScratch[lower], onlineDirectionScratch[upper], rank - lower);
    }

    private bool IsAdaptiveCalibrationActive()
    {
        return adaptiveCalibrationStage != AdaptiveCalibrationStage.Idle;
    }

    private static Vector3 GetCalibrationAxis(AdaptiveCalibrationStage stage)
    {
        switch (stage)
        {
            case AdaptiveCalibrationStage.Backward:
                return Vector3.back;
            case AdaptiveCalibrationStage.Left:
                return Vector3.left;
            case AdaptiveCalibrationStage.Right:
                return Vector3.right;
            case AdaptiveCalibrationStage.Forward:
            default:
                return Vector3.forward;
        }
    }

    private static string GetDirectionStageLabel(AdaptiveCalibrationStage stage)
    {
        switch (stage)
        {
            case AdaptiveCalibrationStage.NeutralAndSway:
                return "Neutral/sway";
            case AdaptiveCalibrationStage.Forward:
                return "Forward";
            case AdaptiveCalibrationStage.Backward:
                return "Backward";
            case AdaptiveCalibrationStage.Left:
                return "Left";
            case AdaptiveCalibrationStage.Right:
                return "Right";
            case AdaptiveCalibrationStage.Idle:
            default:
                return "Idle";
        }
    }

    private Vector3 ComputeCenterOffsetLocal(Vector3 headLocalPos)
    {
        usingInitialHeadReferenceThisFrame = planarReferenceMode == PlanarReferenceMode.InitialHeadPosition;

        if (planarReferenceMode == PlanarReferenceMode.BodyAnchor)
        {
            if (TryGetBodyAnchorLocalPose(out Pose bodyAnchorLocalPose))
            {
                usingInitialHeadReferenceThisFrame = false;
                debugHasBodyAnchor = true;
                debugBodyAnchorLocalPose = bodyAnchorLocalPose;
                bool reacquiredAfterDropout = WasBodyAnchorReacquiredAfterDropout();

                if (!TryGetBodyPlanarSignalLocal(bodyAnchorLocalPose, headLocalPos, out Vector3 signalLocalPos, out string signalName))
                {
                    MarkBodyAnchorUnavailable();
                    MarkAdaptiveCalibrationWaitingForBody();
                    LogBodyAnchorStatus(false);
                    return Vector3.zero;
                }

                debugPlanarSignalPointLocal = signalLocalPos;
                debugPlanarSignalAnchorLocal = bodyAnchorLocalPose.position;
                debugPlanarSignalName = signalName;

                Vector3 bodyReferencedHeadOffset = ComputeBodyReferencedOffsetLocal(
                    signalLocalPos,
                    bodyAnchorLocalPose,
                    out bool currentUsesYaw,
                    out Quaternion bodyYawLocal);
                debugBodyAnchorUsesYaw = currentUsesYaw;
                debugBodyYawLocal = bodyYawLocal;
                debugBodyLocalHeadOffset = bodyReferencedHeadOffset;
                bool calibrationActive = ProcessAdaptiveCalibrationSample(
                    bodyReferencedHeadOffset,
                    currentUsesYaw,
                    bodyAnchorLocalPose);

                if ((!hasCenterBodyAnchor || centerBodyAnchorUsesYaw != currentUsesYaw || reacquiredAfterDropout) &&
                    !calibrationActive)
                {
                    neutralBodyLocalHeadOffset = bodyReferencedHeadOffset;
                    hasCenterBodyAnchor = true;
                    centerBodyAnchorUsesYaw = currentUsesYaw;
                    hasAdaptiveNeutral = false;
                    adaptiveDeadZone = Mathf.Max(0f, planarDeadZone);
                    hasDirectionalCalibration = false;
                    ResetOnlineMaxOffsetLearningBuffers(false);

                    if (recenterHeadYawOnBodyAnchorReacquired && reacquiredAfterDropout)
                    {
                        CaptureHeadOrientationCenter();
                    }

                    hasNeutralBodyRelativeHeadYaw = TryComputeBodyRelativeHeadYawDeg(
                        bodyAnchorLocalPose,
                        target.InverseTransformDirection(head.forward),
                        out neutralBodyRelativeHeadYawDeg);
                    warnedBodyAnchorFallback = false;
                }

                wasBodyAnchorAvailable = true;
                bodyAnchorLostTime = -1f;
                LogBodyAnchorStatus(true);
                Vector3 bodyReferencedDelta = bodyReferencedHeadOffset - neutralBodyLocalHeadOffset;
                bodyReferencedDelta.y = 0f;
                debugBodyLocalMoveOffset = bodyReferencedDelta;
                UpdateOnlineMaxOffsetLearning(bodyReferencedDelta);

                if (freezeMovementDuringCalibration && IsAdaptiveCalibrationActive())
                {
                    return Vector3.zero;
                }

                if (currentUsesYaw)
                {
                    bodyReferencedDelta = bodyYawLocal * bodyReferencedDelta;
                    bodyReferencedDelta.y = 0f;
                }

                return bodyReferencedDelta;
            }

            MarkBodyAnchorUnavailable();
            debugHasBodyAnchor = false;
            debugBodyAnchorUsesYaw = false;
            debugBodyYawLocal = Quaternion.identity;
            debugBodyLocalHeadOffset = Vector3.zero;
            debugBodyLocalMoveOffset = Vector3.zero;
            debugPlanarSignalPointLocal = Vector3.zero;
            debugPlanarSignalAnchorLocal = Vector3.zero;
            debugPlanarSignalName = "Unavailable";
            MarkAdaptiveCalibrationWaitingForBody();

            if (!fallbackToInitialHeadReference)
            {
                LogBodyAnchorStatus(false);
                return Vector3.zero;
            }

            WarnBodyAnchorFallbackOnce();
            usingInitialHeadReferenceThisFrame = true;
            if (onlineMaxOffsetLearningEnabled)
            {
                debugOnlineMaxOffsetLearningStatus = "Paused during initial-head fallback.";
            }

            LogBodyAnchorStatus(false);
        }

        return headLocalPos - centerHeadLocal;
    }

    private bool TryGetBodyPlanarSignalLocal(
        Pose bodyAnchorLocalPose,
        Vector3 headLocalPos,
        out Vector3 signalLocalPos,
        out string signalName)
    {
        signalLocalPos = headLocalPos;
        signalName = "HMD-Waist";
        return true;
    }

    private bool WasBodyAnchorReacquiredAfterDropout()
    {
        return !wasBodyAnchorAvailable &&
               bodyAnchorLostTime >= 0f &&
               Time.unscaledTime - bodyAnchorLostTime >= Mathf.Max(0f, bodyAnchorReacquireRecenterDelay);
    }

    private void MarkBodyAnchorUnavailable()
    {
        if (!wasBodyAnchorAvailable)
        {
            return;
        }

        wasBodyAnchorAvailable = false;
        bodyAnchorLostTime = Time.unscaledTime;
    }

    private void CaptureHeadOrientationCenter()
    {
        if (target == null || head == null)
        {
            centerHeadOrientationLocal = Quaternion.identity;
            hasCenterHeadOrientation = false;
            return;
        }

        hasCenterHeadOrientation = TryBuildHeadForwardCenterOrientation(
            target.InverseTransformDirection(head.forward),
            out centerHeadOrientationLocal);
    }

    private bool TryGetBodyAnchorLocalPose(out Pose bodyAnchorLocalPose)
    {
        bodyAnchorLocalPose = default;

        if (planarReferenceMode != PlanarReferenceMode.BodyAnchor)
        {
            return false;
        }

        ResolveBodyAnchorProvider();
        return bodyAnchorProvider != null && bodyAnchorProvider.TryGetAnchorLocalPose(target, out bodyAnchorLocalPose);
    }

    private void ResolveBodyAnchorProvider()
    {
        if (bodyAnchorProvider != null || planarReferenceMode != PlanarReferenceMode.BodyAnchor)
        {
            return;
        }

        bodyAnchorProvider = GetComponent<BodyAnchorProvider>();
        if (bodyAnchorProvider == null && autoCreateBodyAnchorProvider)
        {
            bodyAnchorProvider = gameObject.AddComponent<BodyAnchorProvider>();
        }

        if (bodyAnchorProvider != null && bodyAnchorProvider.target == null)
        {
            bodyAnchorProvider.target = target;
        }
    }

    private void WarnBodyAnchorFallbackOnce()
    {
        if (warnedBodyAnchorFallback)
        {
            return;
        }

        string providerStatus = bodyAnchorProvider != null ? bodyAnchorProvider.LastStatus : "No BodyAnchorProvider is assigned.";
        Debug.LogWarning(
            $"{nameof(HeadOffsetLocomotion)} on '{name}' could not get a body anchor, so planar input is using the initial head reference. " +
            providerStatus,
            this);
        warnedBodyAnchorFallback = true;
    }

    private void LogBodyAnchorStatus(bool usingBodyAnchor)
    {
        if (!logBodyAnchorStatus)
        {
            return;
        }

        bool stateChanged = !hasLoggedBodyAnchorState || usingBodyAnchor != lastUsingBodyAnchor;
        if (!stateChanged && Time.unscaledTime < nextBodyAnchorStatusLogTime)
        {
            return;
        }

        string providerStatus = bodyAnchorProvider != null ? bodyAnchorProvider.LastStatus : "No BodyAnchorProvider is assigned.";
        string referenceName = usingBodyAnchor
            ? "BodyAnchor"
            : fallbackToInitialHeadReference ? "InitialHeadPosition fallback" : "No planar input";
        Debug.Log(
            $"{nameof(HeadOffsetLocomotion)} on '{name}' planar reference: {referenceName}. {providerStatus}",
            this);

        hasLoggedBodyAnchorState = true;
        lastUsingBodyAnchor = usingBodyAnchor;
        nextBodyAnchorStatusLogTime = Time.unscaledTime + Mathf.Max(0.25f, bodyAnchorStatusLogInterval);
    }

    private bool WasRecenterPressedThisFrame()
    {
        return WasKeyPressedThisFrame(recenterKey, ref warnedUnsupportedRecenterKey);
    }

    private bool WasFullCalibrationPressedThisFrame()
    {
        return WasKeyPressedThisFrame(fullCalibrationKey, ref warnedUnsupportedFullCalibrationKey);
    }

    private bool WasKeyPressedThisFrame(KeyCode keyCode, ref bool warnedUnsupportedKey)
    {
#if ENABLE_INPUT_SYSTEM
        if (keyCode == KeyCode.None)
        {
            return false;
        }

        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return false;
        }

        if (!TryMapKeyCodeToInputSystemKey(keyCode, out var mappedKey))
        {
            if (!warnedUnsupportedKey)
            {
                Debug.LogWarning(
                    $"{nameof(HeadOffsetLocomotion)} on '{name}' cannot map key '{keyCode}' to the Input System keyboard API.",
                    this);
                warnedUnsupportedKey = true;
            }

            return false;
        }

        return keyboard[mappedKey].wasPressedThisFrame;
#else
        return keyCode != KeyCode.None && Input.GetKeyDown(keyCode);
#endif
    }

    private float ComputePlanarSpeed(Vector3 planarOffsetLocal)
    {
        float offsetMagnitude = planarOffsetLocal.magnitude;
        float deadZone = GetEffectivePlanarDeadZone();
        float maxOffset = GetEffectivePlanarMaxOffset(deadZone, planarOffsetLocal);
        debugEffectivePlanarDeadZone = deadZone;
        debugEffectivePlanarMaxOffset = maxOffset;

        if (offsetMagnitude <= deadZone)
        {
            return 0f;
        }

        maxOffset = Mathf.Max(deadZone + 1e-4f, maxOffset);
        float normalized = Mathf.Clamp01((offsetMagnitude - deadZone) / (maxOffset - deadZone));
        float curved = Mathf.Pow(normalized, Mathf.Max(0.1f, planarResponseExponent));
        return curved * Mathf.Max(0f, horizontalSpeed);
    }

    private float GetEffectivePlanarDeadZone()
    {
        if (usingInitialHeadReferenceThisFrame)
        {
            return Mathf.Max(0f, planarDeadZone);
        }

        if (onlineMaxOffsetLearningEnabled && onlineDeadZoneLearningEnabled && hasOnlineDeadZoneLearning)
        {
            return ClampAdaptiveDeadZone(onlineLearnedDeadZone);
        }

        if (adaptiveCalibrationEnabled && hasAdaptiveNeutral)
        {
            return ClampAdaptiveDeadZone(adaptiveDeadZone);
        }

        return Mathf.Max(0f, planarDeadZone);
    }

    private float GetEffectivePlanarMaxOffset(float deadZone, Vector3 planarOffsetLocal)
    {
        float maxOffset = Mathf.Max(deadZone + 1e-4f, planarMaxOffset);
        if (usingInitialHeadReferenceThisFrame)
        {
            return maxOffset;
        }

        if (!hasDirectionalCalibration || (!adaptiveCalibrationEnabled && !onlineMaxOffsetLearningEnabled))
        {
            return maxOffset;
        }

        Vector3 calibrationOffset = debugBodyLocalMoveOffset.sqrMagnitude > 1e-8f
            ? debugBodyLocalMoveOffset
            : planarOffsetLocal;
        calibrationOffset.y = 0f;
        if (calibrationOffset.sqrMagnitude <= 1e-8f)
        {
            return Mathf.Max(deadZone + 1e-4f, ClampAdaptiveMaxOffset(planarMaxOffset));
        }

        Vector3 direction = calibrationOffset.normalized;
        float xMax = direction.x >= 0f ? calibratedRightMaxOffset : calibratedLeftMaxOffset;
        float zMax = direction.z >= 0f ? calibratedForwardMaxOffset : calibratedBackwardMaxOffset;
        xMax = Mathf.Max(deadZone + 1e-4f, ClampAdaptiveMaxOffset(xMax));
        zMax = Mathf.Max(deadZone + 1e-4f, ClampAdaptiveMaxOffset(zMax));

        float denom =
            (direction.x * direction.x) / (xMax * xMax) +
            (direction.z * direction.z) / (zMax * zMax);
        if (denom <= 1e-8f)
        {
            return Mathf.Max(deadZone + 1e-4f, ClampAdaptiveMaxOffset(planarMaxOffset));
        }

        maxOffset = 1f / Mathf.Sqrt(denom);
        return Mathf.Max(deadZone + 1e-4f, maxOffset);
    }

    private float ClampAdaptiveDeadZone(float value)
    {
        float min = Mathf.Max(0f, minAdaptiveDeadZone);
        float max = Mathf.Max(min + 1e-4f, maxAdaptiveDeadZone);
        return Mathf.Clamp(Mathf.Max(0f, value), min, max);
    }

    private float ClampAdaptiveMaxOffset(float value)
    {
        float min = Mathf.Max(1e-4f, minAdaptiveMaxOffset);
        float max = Mathf.Max(min + 1e-4f, maxAdaptiveMaxOffset);
        return Mathf.Clamp(Mathf.Max(0f, value), min, max);
    }

    private Vector3 ComputePlanarDirectionLocal(Vector3 planarOffsetLocal)
    {
        Vector3 leanDirLocal = planarOffsetLocal.sqrMagnitude > 1e-8f
            ? planarOffsetLocal.normalized
            : Vector3.zero;

        if (headDirectionBlend <= 1e-5f)
        {
            return leanDirLocal;
        }

        Vector3 headForwardLocal = GetControlHeadForwardLocal();
        Vector3 headPlanarLocal = new Vector3(headForwardLocal.x, 0f, headForwardLocal.z);
        if (headPlanarLocal.sqrMagnitude > 1e-8f)
        {
            headPlanarLocal.Normalize();
        }

        if (leanDirLocal == Vector3.zero && headPlanarLocal == Vector3.zero)
        {
            return Vector3.zero;
        }

        if (leanDirLocal == Vector3.zero)
        {
            return headPlanarLocal;
        }

        if (headPlanarLocal == Vector3.zero)
        {
            return leanDirLocal;
        }

        Vector3 blended = Vector3.Lerp(leanDirLocal, headPlanarLocal, headDirectionBlend);
        return blended.sqrMagnitude > 1e-8f ? blended.normalized : Vector3.zero;
    }

    private float ComputeYawRate(Vector3 headForwardLocal, Vector2 planarCommand)
    {
        float headYaw = Mathf.Atan2(headForwardLocal.x, headForwardLocal.z);

        switch (orientationMode)
        {
            case OrientationControlMode.Static:
            {
                float threshold = staticYawThresholdDeg * Mathf.Deg2Rad;
                float maxRate = staticYawSpeedDeg * Mathf.Deg2Rad;
                return Mathf.Abs(headYaw) > threshold ? maxRate * Mathf.Sign(headYaw) : 0f;
            }
            case OrientationControlMode.Coupled:
            {
                float maxRate = coupledYawMaxSpeedDeg * Mathf.Deg2Rad;
                return Mathf.Clamp(coupledYawGain * headYaw, -maxRate, maxRate);
            }
            case OrientationControlMode.Dynamic:
            default:
            {
                // Mirrors the MATLAB dynamic yaw rule from SMHMIs.
                float gain = 0.5f;
                float maxRate = yawSpeed * Mathf.Deg2Rad;
                float thMin = 12f * Mathf.Deg2Rad;
                float thMax = 30f * Mathf.Deg2Rad;
                float speedThreshold = 7f;
                float speedScale = 10f;

                float planarSpeedNormalized = Mathf.Min(1f, planarCommand.magnitude);
                float vd = planarSpeedNormalized * speedScale;
                float deltaTheta = (thMin - thMax) / (1f + Mathf.Exp(speedThreshold - vd)) + thMax;
                float lambda = 1f / (1f + Mathf.Exp(-gain * (Mathf.Abs(headYaw) - deltaTheta)));
                float scaled = Mathf.Max(0f, 2f * lambda - 1f);
                float yawRate = maxRate * scaled * Mathf.Sign(headYaw);
                return Mathf.Clamp(yawRate, -maxRate, maxRate);
            }
        }
    }

    private Vector3 GetControlHeadForwardLocal()
    {
        debugUsingBodyRelativeHeadYaw = false;
        debugHasBodyRelativeHeadYaw = false;
        debugBodyRelativeHeadYawDeg = 0f;
        debugBodyRelativeHeadYawDeltaDeg = 0f;

        Vector3 headForwardLocal = target.InverseTransformDirection(head.forward);
        if (headForwardLocal.sqrMagnitude <= 1e-8f)
        {
            return Vector3.forward;
        }

        headForwardLocal.Normalize();

        if (useBodyRelativeHeadYaw &&
            TryGetBodyAnchorLocalPose(out Pose bodyAnchorLocalPose) &&
            TryComputeBodyRelativeHeadYawDeg(bodyAnchorLocalPose, headForwardLocal, out float bodyRelativeHeadYawDeg))
        {
            if (!hasNeutralBodyRelativeHeadYaw)
            {
                neutralBodyRelativeHeadYawDeg = bodyRelativeHeadYawDeg;
                hasNeutralBodyRelativeHeadYaw = true;
            }

            Vector3 pitchForwardLocal = headForwardLocal;
            if (hasCenterHeadOrientation)
            {
                pitchForwardLocal = Quaternion.Inverse(centerHeadOrientationLocal) * pitchForwardLocal;
                if (pitchForwardLocal.sqrMagnitude > 1e-8f)
                {
                    pitchForwardLocal.Normalize();
                }
            }

            float yawDeltaRad = Mathf.DeltaAngle(neutralBodyRelativeHeadYawDeg, bodyRelativeHeadYawDeg) * Mathf.Deg2Rad;
            debugUsingBodyRelativeHeadYaw = true;
            debugHasBodyRelativeHeadYaw = true;
            debugBodyRelativeHeadYawDeg = bodyRelativeHeadYawDeg;
            debugBodyRelativeHeadYawDeltaDeg = yawDeltaRad * Mathf.Rad2Deg;

            float pitchY = Mathf.Clamp(pitchForwardLocal.y, -0.9999f, 0.9999f);
            float planarMagnitude = Mathf.Sqrt(Mathf.Max(0f, 1f - pitchY * pitchY));
            headForwardLocal = new Vector3(
                Mathf.Sin(yawDeltaRad) * planarMagnitude,
                pitchY,
                Mathf.Cos(yawDeltaRad) * planarMagnitude);
        }
        else if (hasCenterHeadOrientation)
        {
            headForwardLocal = Quaternion.Inverse(centerHeadOrientationLocal) * headForwardLocal;
            if (headForwardLocal.sqrMagnitude > 1e-8f)
            {
                headForwardLocal.Normalize();
            }
        }

        if (headForwardLocal.sqrMagnitude <= 1e-8f)
        {
            headForwardLocal = Vector3.forward;
        }

        if (invertYawDirection)
        {
            headForwardLocal.x *= -1f;
        }

        return headForwardLocal;
    }

    private Vector3 ComputeBodyReferencedOffsetLocal(
        Vector3 signalLocalPos,
        Pose bodyAnchorLocalPose,
        out bool usedBodyYaw,
        out Quaternion bodyYawLocal)
    {
        Vector3 headRelativeToBodyLocal = signalLocalPos - bodyAnchorLocalPose.position;
        headRelativeToBodyLocal.y = 0f;
        usedBodyYaw = false;
        bodyYawLocal = Quaternion.identity;

        if (normalizePlanarOffsetByBodyYaw && TryGetBodyYawLocal(bodyAnchorLocalPose, out bodyYawLocal))
        {
            headRelativeToBodyLocal = Quaternion.Inverse(bodyYawLocal) * headRelativeToBodyLocal;
            usedBodyYaw = true;
        }

        headRelativeToBodyLocal.y = 0f;
        return headRelativeToBodyLocal;
    }

    private bool TryComputeBodyRelativeHeadYawDeg(
        Pose bodyAnchorLocalPose,
        Vector3 headForwardLocal,
        out float bodyRelativeHeadYawDeg)
    {
        bodyRelativeHeadYawDeg = 0f;

        if (!useBodyRelativeHeadYaw || !TryGetBodyYawLocal(bodyAnchorLocalPose, out Quaternion bodyYawLocal))
        {
            return false;
        }

        Vector3 bodyForwardLocal = bodyYawLocal * Vector3.forward;
        if (!TryGetPlanarYawDeg(bodyForwardLocal, out float bodyYawDeg) ||
            !TryGetPlanarYawDeg(headForwardLocal, out float headYawDeg))
        {
            return false;
        }

        bodyRelativeHeadYawDeg = Mathf.DeltaAngle(bodyYawDeg, headYawDeg);
        return true;
    }

    private static bool TryGetPlanarYawDeg(Vector3 forward, out float yawDeg)
    {
        forward.y = 0f;
        if (forward.sqrMagnitude <= 1e-8f)
        {
            yawDeg = 0f;
            return false;
        }

        yawDeg = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        return true;
    }

    private static float GetYawDeg(Vector3 forward)
    {
        return TryGetPlanarYawDeg(forward, out float yawDeg) ? yawDeg : 0f;
    }

    private static float GetPitchDeg(Vector3 forward)
    {
        if (forward.sqrMagnitude <= 1e-8f)
        {
            return 0f;
        }

        forward.Normalize();
        float planarNorm = Mathf.Sqrt(forward.x * forward.x + forward.z * forward.z);
        return Mathf.Atan2(forward.y, planarNorm) * Mathf.Rad2Deg;
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Scene";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        StringBuilder builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        return builder.ToString();
    }

    private static bool TryGetBodyYawLocal(Pose bodyAnchorLocalPose, out Quaternion bodyYawLocal)
    {
        return TryGetYawRotation(bodyAnchorLocalPose.rotation, out bodyYawLocal);
    }

    private static bool TryGetYawRotation(Quaternion rotation, out Quaternion yawRotation)
    {
        Vector3 forward = rotation * Vector3.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude <= 1e-8f)
        {
            Vector3 right = rotation * Vector3.right;
            right.y = 0f;
            if (right.sqrMagnitude <= 1e-8f)
            {
                yawRotation = Quaternion.identity;
                return false;
            }

            forward = Vector3.Cross(right.normalized, Vector3.up);
        }

        yawRotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        return true;
    }

    private static bool TryBuildHeadForwardCenterOrientation(Vector3 forwardLocal, out Quaternion orientationLocal)
    {
        if (forwardLocal.sqrMagnitude <= 1e-8f)
        {
            orientationLocal = Quaternion.identity;
            return false;
        }

        Vector3 forward = forwardLocal.normalized;
        Vector3 upProjected = Vector3.ProjectOnPlane(Vector3.up, forward);

        if (upProjected.sqrMagnitude <= 1e-8f)
        {
            Vector3 fallbackUp = Mathf.Abs(Vector3.Dot(forward, Vector3.forward)) > 0.999f ? Vector3.right : Vector3.forward;
            upProjected = Vector3.ProjectOnPlane(fallbackUp, forward);
            if (upProjected.sqrMagnitude <= 1e-8f)
            {
                orientationLocal = Quaternion.identity;
                return false;
            }
        }

        // Use only HMD forward for neutral orientation: yaw and pitch are recentered, roll is ignored.
        orientationLocal = Quaternion.LookRotation(forward, upProjected.normalized);
        return true;
    }

    private float ComputeVerticalSpeed(Vector3 headForwardLocal)
    {
        float planarNorm = Mathf.Sqrt(headForwardLocal.x * headForwardLocal.x + headForwardLocal.z * headForwardLocal.z);
        float headPitchDeg = Mathf.Atan2(headForwardLocal.y, planarNorm) * Mathf.Rad2Deg;

        switch (orientationMode)
        {
            case OrientationControlMode.Static:
                if (headPitchDeg >= staticPitchUpThresholdDeg)
                {
                    return staticPitchUpSpeed;
                }
                if (headPitchDeg <= -staticPitchDownThresholdDeg)
                {
                    return -staticPitchDownSpeed;
                }
                return 0f;

            case OrientationControlMode.Coupled:
            {
                float refDeg = Mathf.Max(coupledPitchReferenceDeg, 1f);
                float maxSpeed = coupledPitchMaxSpeed;
                return Mathf.Clamp((headPitchDeg / refDeg) * maxSpeed, -maxSpeed, maxSpeed);
            }

            case OrientationControlMode.Dynamic:
            default:
            {
                float lambda;
                float delta;
                float signFactor;

                if (headPitchDeg >= 0f)
                {
                    lambda = 0.3f;
                    delta = 18f;
                    signFactor = 1f;
                }
                else
                {
                    lambda = -0.4f;
                    delta = -18f;
                    signFactor = -1f;
                }

                float logistic = 1f / (1f + Mathf.Exp(-lambda * (headPitchDeg - delta)));
                logistic = Mathf.Clamp01(logistic);
                return signFactor * logistic * Mathf.Max(0f, verticalSpeed);
            }
        }
    }

    private static Vector3 ClampPosition(Vector3 position, Vector3 limits)
    {
        return new Vector3(
            Mathf.Clamp(position.x, -limits.x, limits.x),
            Mathf.Clamp(position.y, -limits.y, limits.y),
            Mathf.Clamp(position.z, -limits.z, limits.z));
    }

#if ENABLE_INPUT_SYSTEM
    private static bool TryMapKeyCodeToInputSystemKey(KeyCode keyCode, out Key key)
    {
        switch (keyCode)
        {
            case KeyCode.Backspace:
                key = Key.Backspace;
                return true;
            case KeyCode.Tab:
                key = Key.Tab;
                return true;
            case KeyCode.Return:
            case KeyCode.KeypadEnter:
                key = Key.Enter;
                return true;
            case KeyCode.Escape:
                key = Key.Escape;
                return true;
            case KeyCode.Space:
                key = Key.Space;
                return true;
            case KeyCode.LeftShift:
                key = Key.LeftShift;
                return true;
            case KeyCode.RightShift:
                key = Key.RightShift;
                return true;
            case KeyCode.LeftControl:
                key = Key.LeftCtrl;
                return true;
            case KeyCode.RightControl:
                key = Key.RightCtrl;
                return true;
            case KeyCode.LeftAlt:
                key = Key.LeftAlt;
                return true;
            case KeyCode.RightAlt:
                key = Key.RightAlt;
                return true;
            case KeyCode.LeftArrow:
                key = Key.LeftArrow;
                return true;
            case KeyCode.RightArrow:
                key = Key.RightArrow;
                return true;
            case KeyCode.UpArrow:
                key = Key.UpArrow;
                return true;
            case KeyCode.DownArrow:
                key = Key.DownArrow;
                return true;
            case KeyCode.Alpha0:
                key = Key.Digit0;
                return true;
            case KeyCode.Alpha1:
                key = Key.Digit1;
                return true;
            case KeyCode.Alpha2:
                key = Key.Digit2;
                return true;
            case KeyCode.Alpha3:
                key = Key.Digit3;
                return true;
            case KeyCode.Alpha4:
                key = Key.Digit4;
                return true;
            case KeyCode.Alpha5:
                key = Key.Digit5;
                return true;
            case KeyCode.Alpha6:
                key = Key.Digit6;
                return true;
            case KeyCode.Alpha7:
                key = Key.Digit7;
                return true;
            case KeyCode.Alpha8:
                key = Key.Digit8;
                return true;
            case KeyCode.Alpha9:
                key = Key.Digit9;
                return true;
        }

        if (Enum.TryParse(keyCode.ToString(), true, out key))
        {
            return true;
        }

        key = default;
        return false;
    }
#endif
}
