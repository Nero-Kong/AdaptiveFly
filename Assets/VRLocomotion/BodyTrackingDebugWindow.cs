using System;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class BodyTrackingDebugWindow : MonoBehaviour
{
    [Header("References")]
    public HeadOffsetLocomotion locomotion;
    public BodyAnchorProvider provider;
    public Transform target;
    public Transform head;

    [Header("Window")]
    public bool visible = false;
    public KeyCode toggleKey = KeyCode.F8;
    public float refreshInterval = 0.05f;
    public Rect windowRect = new Rect(20f, 20f, 520f, 300f);
    public float mapHalfRangeMeters = 0.8f;

    [Header("Gizmos")]
    public bool drawSceneGizmos = true;
    public float gizmoSize = 0.035f;

    private const int WindowId = 719438;

    private bool hasWaistPose;
    private Pose waistPoseLocal;
    private string waistStatus = "Waist anchor has not been queried.";
    private float nextRefreshTime;
    private GUIStyle smallStyle;
    private GUIStyle boldStyle;
    private GUIStyle statusStyle;
    private Texture2D whiteTexture;
    private bool warnedUnsupportedToggleKey;

    private void OnEnable()
    {
        ResolveReferences();
        RefreshWaistPose();
    }

    private void Update()
    {
        if (WasTogglePressedThisFrame())
        {
            visible = !visible;
        }

        if (Time.unscaledTime >= nextRefreshTime)
        {
            RefreshWaistPose();
            nextRefreshTime = Time.unscaledTime + Mathf.Max(0.01f, refreshInterval);
        }
    }

    private void OnGUI()
    {
        if (!visible)
        {
            return;
        }

        EnsureGuiResources();
        windowRect = GUI.Window(WindowId, windowRect, DrawWindow, "AdaptiveFly Waist Anchor");
    }

    private void OnDrawGizmos()
    {
        if (!drawSceneGizmos || target == null || !hasWaistPose)
        {
            return;
        }

        Gizmos.matrix = target.localToWorldMatrix;
        Gizmos.color = new Color(0.2f, 0.8f, 1f);
        Gizmos.DrawSphere(waistPoseLocal.position, gizmoSize);

        if (head != null)
        {
            Vector3 headLocal = target.InverseTransformPoint(head.position);
            Gizmos.color = new Color(1f, 0.2f, 1f);
            Gizmos.DrawSphere(headLocal, gizmoSize);
            Gizmos.DrawLine(waistPoseLocal.position, headLocal);
        }

        if (locomotion != null)
        {
            Gizmos.color = new Color(1f, 0.45f, 0.05f);
            Gizmos.DrawRay(waistPoseLocal.position, locomotion.DebugPlanarOffsetLocal);
        }
    }

    private void DrawWindow(int id)
    {
        ResolveReferences();

        GUILayout.Label("Source", boldStyle);
        GUILayout.Label(provider != null ? provider.source.ToString() : "No BodyAnchorProvider", statusStyle);
        GUILayout.Label(waistStatus, smallStyle);

        GUILayout.Space(6f);
        DrawHeadWaistAndOffset();
        DrawLocomotionYawDebug();

        GUILayout.Space(8f);
        Rect mapRect = GUILayoutUtility.GetRect(490f, 150f);
        DrawTopDownMap(mapRect);

        GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
    }

    private void DrawHeadWaistAndOffset()
    {
        if (target == null || head == null)
        {
            GUILayout.Label("Head or target is not resolved.", smallStyle);
            return;
        }

        Vector3 headLocal = target.InverseTransformPoint(head.position);
        float headYaw = GetYawDeg(target.InverseTransformDirection(head.forward));
        GUILayout.Label($"HMD local pos {FormatVector(headLocal)} yaw {headYaw,6:0.0} deg", smallStyle);

        if (!hasWaistPose)
        {
            string fallback = locomotion != null && locomotion.DebugUsingInitialHeadReferenceFallback
                ? " Using initial HMD offset fallback."
                : string.Empty;
            GUILayout.Label($"Waist controller anchor is not valid, so HMD-waist offset is unavailable.{fallback}", smallStyle);
            return;
        }

        Vector3 headMinusWaist = headLocal - waistPoseLocal.position;
        headMinusWaist.y = 0f;
        Quaternion waistYaw = GetYawRotation(waistPoseLocal.rotation);
        Vector3 waistLocalHeadOffset = Quaternion.Inverse(waistYaw) * headMinusWaist;
        float waistYawDeg = waistYaw.eulerAngles.y;
        float relativeHeadYaw = Mathf.DeltaAngle(waistYawDeg, headYaw);

        GUILayout.Label(
            $"HeadXZ - WaistXZ {FormatVectorXZ(headMinusWaist)}  waist-local {FormatVectorXZ(waistLocalHeadOffset)}",
            smallStyle);
        GUILayout.Label($"Waist yaw {waistYawDeg,6:0.0} deg  HMD yaw - Waist yaw {relativeHeadYaw,6:0.0} deg", smallStyle);

        if (locomotion != null)
        {
            GUILayout.Label(
                $"Move offset {FormatVectorXZ(locomotion.DebugPlanarOffsetLocal)}  velocity {FormatVectorXZ(locomotion.DebugSmoothedPlanarVelocityLocal)}",
                smallStyle);
        }
    }

    private void DrawLocomotionYawDebug()
    {
        if (locomotion == null)
        {
            return;
        }

        GUILayout.Label(
            $"Yaw input {GetYawDeg(locomotion.DebugControlHeadForwardLocal),6:0.0} deg  rate {locomotion.DebugYawRateDegPerSecond,7:0.0} deg/s  vertical {locomotion.DebugVerticalCommand,6:0.00} m/s",
            smallStyle);
        GUILayout.Label(
            $"Deadzone {locomotion.DebugEffectivePlanarDeadZone:0.000}m  learned DZ {(locomotion.DebugHasOnlineDeadZoneLearning ? locomotion.DebugOnlineLearnedDeadZone.ToString("0.000") : "--")}m",
            smallStyle);
    }

    private void DrawTopDownMap(Rect rect)
    {
        Color previousColor = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(rect, whiteTexture);
        GUI.color = new Color(1f, 1f, 1f, 0.25f);
        DrawLine(new Vector2(rect.xMin, rect.center.y), new Vector2(rect.xMax, rect.center.y), GUI.color, 1f);
        DrawLine(new Vector2(rect.center.x, rect.yMin), new Vector2(rect.center.x, rect.yMax), GUI.color, 1f);

        Vector3 origin = hasWaistPose ? waistPoseLocal.position : Vector3.zero;
        if (hasWaistPose)
        {
            DrawMapPoint(rect, origin, waistPoseLocal.position, "Waist", new Color(0.2f, 0.8f, 1f), 8f);
        }

        if (target != null && head != null)
        {
            Vector3 headLocal = target.InverseTransformPoint(head.position);
            if (hasWaistPose)
            {
                DrawLine(LocalToMap(rect, waistPoseLocal.position - origin), LocalToMap(rect, headLocal - origin), new Color(1f, 0.2f, 1f, 0.85f), 2f);
            }

            DrawMapPoint(rect, origin, headLocal, "HMD", new Color(1f, 0.2f, 1f), 8f);
        }

        if (locomotion != null && hasWaistPose)
        {
            Vector3 waistLocal = waistPoseLocal.position;
            Vector3 moveEnd = waistLocal + new Vector3(locomotion.DebugPlanarOffsetLocal.x, 0f, locomotion.DebugPlanarOffsetLocal.z);
            Vector3 velocityEnd = waistLocal + new Vector3(locomotion.DebugSmoothedPlanarVelocityLocal.x, 0f, locomotion.DebugSmoothedPlanarVelocityLocal.z) * 0.08f;
            DrawLine(LocalToMap(rect, waistLocal - origin), LocalToMap(rect, moveEnd - origin), new Color(1f, 0.45f, 0.05f), 3f);
            DrawLine(LocalToMap(rect, waistLocal - origin), LocalToMap(rect, velocityEnd - origin), new Color(0.1f, 0.75f, 1f), 3f);
        }

        GUI.color = previousColor;
        GUI.Label(new Rect(rect.x + 6f, rect.y + 5f, rect.width - 12f, 18f), $"Top-down XZ, centered on waist controller, +/- {mapHalfRangeMeters:0.00} m", smallStyle);
    }

    private void DrawMapPoint(Rect rect, Vector3 origin, Vector3 localPosition, string label, Color color, float size)
    {
        Vector2 point = LocalToMap(rect, localPosition - origin);
        Color previousColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(new Rect(point.x - size * 0.5f, point.y - size * 0.5f, size, size), whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(point.x + 5f, point.y - 10f, 70f, 18f), label, smallStyle);
        GUI.color = previousColor;
    }

    private Vector2 LocalToMap(Rect rect, Vector3 localOffset)
    {
        float halfRange = Mathf.Max(0.05f, mapHalfRangeMeters);
        float x = Mathf.Clamp(localOffset.x / halfRange, -1f, 1f);
        float z = Mathf.Clamp(localOffset.z / halfRange, -1f, 1f);
        return new Vector2(
            rect.center.x + x * rect.width * 0.5f,
            rect.center.y - z * rect.height * 0.5f);
    }

    private void RefreshWaistPose()
    {
        ResolveReferences();
        hasWaistPose = provider != null && provider.TryGetAnchorLocalPose(target, out waistPoseLocal);
        waistStatus = provider != null ? provider.LastStatus : "No provider.";
    }

    private void ResolveReferences()
    {
        if (locomotion == null)
        {
            locomotion = GetComponent<HeadOffsetLocomotion>();
        }

        if (locomotion == null)
        {
            locomotion = FindAnyObjectByType<HeadOffsetLocomotion>();
        }

        if (provider == null && locomotion != null)
        {
            provider = locomotion.bodyAnchorProvider;
        }

        if (provider == null)
        {
            provider = GetComponent<BodyAnchorProvider>();
        }

        if (provider == null)
        {
            provider = FindAnyObjectByType<BodyAnchorProvider>();
        }

        if (provider == null && locomotion != null)
        {
            provider = locomotion.gameObject.GetComponent<BodyAnchorProvider>();
            if (provider == null)
            {
                provider = locomotion.gameObject.AddComponent<BodyAnchorProvider>();
            }

            locomotion.bodyAnchorProvider = provider;
        }

        if (target == null && locomotion != null)
        {
            target = locomotion.target;
        }

        if (target == null && provider != null)
        {
            target = provider.target;
        }

        if (target == null)
        {
            target = transform;
        }

        if (provider != null && provider.target == null)
        {
            provider.target = target;
        }

        if (head == null && locomotion != null)
        {
            head = locomotion.head;
        }

        if (head == null && Camera.main != null)
        {
            head = Camera.main.transform;
        }
    }

    private void EnsureGuiResources()
    {
        whiteTexture = Texture2D.whiteTexture;

        if (smallStyle == null)
        {
            smallStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                wordWrap = false,
                clipping = TextClipping.Clip
            };
        }

        if (boldStyle == null)
        {
            boldStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold
            };
        }

        if (statusStyle == null)
        {
            statusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.5f, 0.9f, 1f) }
            };
        }
    }

    private bool WasTogglePressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return false;
        }

        if (!TryMapKeyCodeToInputSystemKey(toggleKey, out Key mappedKey))
        {
            if (!warnedUnsupportedToggleKey)
            {
                Debug.LogWarning($"{nameof(BodyTrackingDebugWindow)} cannot map toggle key '{toggleKey}'.", this);
                warnedUnsupportedToggleKey = true;
            }

            return false;
        }

        return keyboard[mappedKey].wasPressedThisFrame;
#else
        return Input.GetKeyDown(toggleKey);
#endif
    }

#if ENABLE_INPUT_SYSTEM
    private static bool TryMapKeyCodeToInputSystemKey(KeyCode keyCode, out Key key)
    {
        key = Key.None;
        string keyName = keyCode.ToString();
        if (keyName.StartsWith("Alpha", StringComparison.Ordinal))
        {
            keyName = "Digit" + keyName.Substring("Alpha".Length);
        }

        if (Enum.TryParse(keyName, out key))
        {
            return key != Key.None;
        }

        return false;
    }
#endif

    private static string FormatVector(Vector3 value)
    {
        return $"({value.x,6:0.000}, {value.y,6:0.000}, {value.z,6:0.000})";
    }

    private static string FormatVectorXZ(Vector3 value)
    {
        return $"({value.x,6:0.000}, {value.z,6:0.000})";
    }

    private static float GetYawDeg(Vector3 forward)
    {
        forward.y = 0f;
        if (forward.sqrMagnitude <= 1e-8f)
        {
            return 0f;
        }

        return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
    }

    private static Quaternion GetYawRotation(Quaternion rotation)
    {
        Vector3 forward = rotation * Vector3.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 1e-8f)
        {
            Vector3 right = rotation * Vector3.right;
            right.y = 0f;
            forward = right.sqrMagnitude > 1e-8f ? Vector3.Cross(right.normalized, Vector3.up) : Vector3.forward;
        }

        return Quaternion.LookRotation(forward.normalized, Vector3.up);
    }

    private static void DrawLine(Vector2 a, Vector2 b, Color color, float thickness)
    {
        Vector2 delta = b - a;
        if (delta.sqrMagnitude <= 1e-6f)
        {
            return;
        }

        Matrix4x4 previousMatrix = GUI.matrix;
        Color previousColor = GUI.color;
        GUI.color = color;

        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        GUIUtility.RotateAroundPivot(angle, a);
        GUI.DrawTexture(new Rect(a.x, a.y - thickness * 0.5f, delta.magnitude, thickness), Texture2D.whiteTexture);

        GUI.matrix = previousMatrix;
        GUI.color = previousColor;
    }
}

public static class BodyTrackingDebugWindowBootstrap
{
#if UNITY_EDITOR
    private static readonly bool AutoCreateEditorDebugWindow = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateEditorDebugWindow()
    {
        if (!AutoCreateEditorDebugWindow)
        {
            return;
        }

        if (UnityEngine.Object.FindAnyObjectByType<BodyTrackingDebugWindow>() != null)
        {
            return;
        }

        HeadOffsetLocomotion locomotion = UnityEngine.Object.FindAnyObjectByType<HeadOffsetLocomotion>();
        if (locomotion == null)
        {
            return;
        }

        var windowObject = new GameObject("AdaptiveFly Waist Anchor Debug Window");
        BodyTrackingDebugWindow window = windowObject.AddComponent<BodyTrackingDebugWindow>();
        window.locomotion = locomotion;
        window.provider = locomotion.bodyAnchorProvider;
        window.target = locomotion.target != null ? locomotion.target : locomotion.transform;
        window.head = locomotion.head;
    }
#endif
}
