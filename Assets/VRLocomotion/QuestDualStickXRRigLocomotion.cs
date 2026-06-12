using UnityEngine;
using Unity.XR.CoreUtils;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Quest-style dual-stick locomotion for an XR Origin.
/// Left stick: up/down on Y, yaw on X.
/// Right stick: forward/back on Y, strafe on X.
/// </summary>
public class QuestDualStickXRRigLocomotion : MonoBehaviour
{
    public enum MovementReferenceSpace
    {
        Rig,
        Head,
        World
    }

    [Header("Rig")]
    public XROrigin xrOrigin;
    public Transform head;

    [Header("Speeds")]
    public float forwardSpeed = 4f;
    public float strafeSpeed = 4f;
    public float verticalSpeed = 2.5f;
    public float turnSpeedDegrees = 90f;

    [Header("Input")]
    [Range(0f, 0.95f)]
    public float stickDeadZone = 0.15f;
    [Tooltip("Disable this when the left controller is strapped to the waist as a tracker.")]
    public bool useLeftStickInput = true;
    public bool useRightStickInput = true;

    [Header("Direction")]
    [Tooltip("Rig = move in XR rig forward/right. Head = move where the HMD looks. World = fixed world axes.")]
    public MovementReferenceSpace movementReferenceSpace = MovementReferenceSpace.Rig;

#if ENABLE_INPUT_SYSTEM
    private InputAction leftStickAction;
    private InputAction rightStickAction;
#endif

    private void Awake()
    {
        CreateInputActions();
    }

    private void OnEnable()
    {
        if (!ResolveReferences())
        {
            enabled = false;
            return;
        }

#if ENABLE_INPUT_SYSTEM
        leftStickAction?.Enable();
        rightStickAction?.Enable();
#else
        Debug.LogWarning($"{nameof(QuestDualStickXRRigLocomotion)} requires the Input System package.", this);
        enabled = false;
#endif
    }

    private void OnDisable()
    {
#if ENABLE_INPUT_SYSTEM
        leftStickAction?.Disable();
        rightStickAction?.Disable();
#endif
    }

    private void OnDestroy()
    {
#if ENABLE_INPUT_SYSTEM
        leftStickAction?.Dispose();
        rightStickAction?.Dispose();
#endif
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        Vector2 leftStick = useLeftStickInput
            ? ApplyDeadZone(leftStickAction.ReadValue<Vector2>())
            : Vector2.zero;
        Vector2 rightStick = useRightStickInput
            ? ApplyDeadZone(rightStickAction.ReadValue<Vector2>())
            : Vector2.zero;

        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f)
        {
            return;
        }

        float yawDelta = leftStick.x * turnSpeedDegrees * deltaTime;
        if (Mathf.Abs(yawDelta) > 1e-4f)
        {
            transform.Rotate(Vector3.up, yawDelta, Space.World);
        }

        Vector3 forward = GetPlanarForward();
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 translation =
            forward * (rightStick.y * forwardSpeed) +
            right * (rightStick.x * strafeSpeed) +
            Vector3.up * (leftStick.y * verticalSpeed);

        if (translation.sqrMagnitude > 0f)
        {
            transform.position += translation * deltaTime;
        }
#endif
    }

    private bool ResolveReferences()
    {
        if (xrOrigin == null)
        {
            xrOrigin = GetComponent<XROrigin>();
        }

        if (head == null)
        {
            if (xrOrigin != null && xrOrigin.Camera != null)
            {
                head = xrOrigin.Camera.transform;
            }
            else if (Camera.main != null)
            {
                head = Camera.main.transform;
            }
        }

        if (xrOrigin != null && head != null)
        {
            return true;
        }

        Debug.LogWarning(
            $"{nameof(QuestDualStickXRRigLocomotion)} on '{name}' requires an {nameof(XROrigin)} and head transform.",
            this);
        return false;
    }

    private Vector3 GetPlanarForward()
    {
        Vector3 forward;

        switch (movementReferenceSpace)
        {
            case MovementReferenceSpace.Head:
            {
                Transform basis = head != null ? head : transform;
                forward = Vector3.ProjectOnPlane(basis.forward, Vector3.up);
                break;
            }
            case MovementReferenceSpace.World:
            {
                forward = Vector3.forward;
                break;
            }
            case MovementReferenceSpace.Rig:
            default:
            {
                forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                break;
            }
        }

        return forward.sqrMagnitude < 1e-6f ? Vector3.forward : forward.normalized;
    }

    private Vector2 ApplyDeadZone(Vector2 stickValue)
    {
        float magnitude = stickValue.magnitude;
        if (magnitude <= stickDeadZone)
        {
            return Vector2.zero;
        }

        float scaledMagnitude = (magnitude - stickDeadZone) / (1f - stickDeadZone);
        return stickValue.normalized * Mathf.Clamp01(scaledMagnitude);
    }

    private void CreateInputActions()
    {
#if ENABLE_INPUT_SYSTEM
        if (leftStickAction == null)
        {
            leftStickAction = new InputAction("LeftStick", InputActionType.Value, expectedControlType: "Vector2");
            AddThumbstickBindings(leftStickAction, "LeftHand");
        }

        if (rightStickAction == null)
        {
            rightStickAction = new InputAction("RightStick", InputActionType.Value, expectedControlType: "Vector2");
            AddThumbstickBindings(rightStickAction, "RightHand");
        }
#endif
    }

#if ENABLE_INPUT_SYSTEM
    private static void AddThumbstickBindings(InputAction action, string handUsage)
    {
        action.AddBinding($"<XRController>{{{handUsage}}}/{{Primary2DAxis}}");
        action.AddBinding($"<MetaQuestTouchPlusController>{{{handUsage}}}/thumbstick");
        action.AddBinding($"<MetaQuestTouchProController>{{{handUsage}}}/thumbstick");
        action.AddBinding($"<OculusTouchController>{{{handUsage}}}/thumbstick");
    }
#endif
}
