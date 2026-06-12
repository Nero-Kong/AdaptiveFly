#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public sealed class BodyTrackingVectorVisualizerWindow : EditorWindow
{
    private HeadOffsetLocomotion locomotion;
    private BodyAnchorProvider provider;
    private Transform target;
    private Transform head;
    private Vector2 scroll;
    private Vector2 lastMousePosition;
    private bool dragging;
    private float viewYaw = 35f;
    private float viewPitch = 18f;
    private float viewDistance = 2.4f;
    private float vectorScale = 0.25f;
    private bool autoFind = true;
    private bool showLabels = true;
    private bool showGrid = true;
    private bool hasWaistPose;
    private Pose waistPoseLocal;
    private string waistStatus = "No waist controller pose.";

    [MenuItem("AdaptiveFly/Debug/Body Tracking 3D Vector Visualizer")]
    public static void Open()
    {
        BodyTrackingVectorVisualizerWindow window = GetWindow<BodyTrackingVectorVisualizerWindow>();
        window.titleContent = new GUIContent("Waist Vectors 3D");
        window.minSize = new Vector2(640f, 420f);
        window.Show();
    }

    private void OnEnable()
    {
        EditorApplication.update += Repaint;
        ResolveReferences();
    }

    private void OnDisable()
    {
        EditorApplication.update -= Repaint;
    }

    private void OnGUI()
    {
        DrawToolbar();

        Rect viewport = new Rect(0f, 24f, position.width, Mathf.Max(160f, position.height - 118f));
        HandleViewportInput(viewport);
        ResolveReferences();
        RefreshWaistPose();
        DrawViewport(viewport);

        Rect panel = new Rect(0f, viewport.yMax, position.width, position.height - viewport.yMax);
        DrawReadoutPanel(panel);
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        autoFind = GUILayout.Toggle(autoFind, "Auto Find", EditorStyles.toolbarButton, GUILayout.Width(72f));
        showLabels = GUILayout.Toggle(showLabels, "Labels", EditorStyles.toolbarButton, GUILayout.Width(62f));
        showGrid = GUILayout.Toggle(showGrid, "Grid", EditorStyles.toolbarButton, GUILayout.Width(48f));
        GUILayout.Label("Vector Scale", GUILayout.Width(82f));
        vectorScale = GUILayout.HorizontalSlider(vectorScale, 0.05f, 0.8f, GUILayout.Width(120f));
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Reset View", EditorStyles.toolbarButton, GUILayout.Width(78f)))
        {
            viewYaw = 35f;
            viewPitch = 18f;
            viewDistance = 2.4f;
        }
        EditorGUILayout.EndHorizontal();
    }

    private void ResolveReferences()
    {
        if (!autoFind && locomotion != null)
        {
            provider = locomotion.bodyAnchorProvider;
            target = locomotion.target;
            head = locomotion.head;
            return;
        }

        if (locomotion == null)
        {
            locomotion = FindAnyObjectByType<HeadOffsetLocomotion>();
        }

        if (locomotion != null)
        {
            provider = locomotion.bodyAnchorProvider;
            target = locomotion.target;
            head = locomotion.head;
        }

        if (provider == null)
        {
            provider = FindAnyObjectByType<BodyAnchorProvider>();
        }

        if (target == null && provider != null)
        {
            target = provider.target;
        }

        if (head == null && Camera.main != null)
        {
            head = Camera.main.transform;
        }
    }

    private void RefreshWaistPose()
    {
        hasWaistPose = false;
        waistPoseLocal = default;
        waistStatus = "No BodyAnchorProvider.";

        if (provider == null || target == null)
        {
            return;
        }

        hasWaistPose = provider.TryGetAnchorLocalPose(target, out waistPoseLocal);
        waistStatus = provider.LastStatus;
    }

    private void DrawViewport(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.045f, 0.05f, 0.06f));

        Vector3 hmd = GetHmdLocalPosition();
        Vector3 waist = hasWaistPose ? waistPoseLocal.position : Vector3.zero;
        Vector3 focus = hasWaistPose && head != null ? Vector3.Lerp(waist, hmd, 0.5f) : waist;

        if (showGrid)
        {
            DrawGrid(rect, focus);
        }

        DrawAxes(rect, focus);
        DrawWaistAndHmd(rect, focus, waist, hmd);
        DrawLocomotionVectors(rect, focus, waist, hmd);

        if (!Application.isPlaying)
        {
            DrawCenteredMessage(rect, "Enter Play Mode to stream live XR controller and HMD data.");
        }
        else if (locomotion == null)
        {
            DrawCenteredMessage(rect, "No HeadOffsetLocomotion found in the active scene.");
        }
    }

    private void DrawGrid(Rect rect, Vector3 focus)
    {
        Color gridColor = new Color(1f, 1f, 1f, 0.11f);
        float range = 1.2f;
        for (int i = -6; i <= 6; i++)
        {
            float v = i * 0.2f;
            DrawWorldLine(rect, focus, new Vector3(v, 0f, -range), new Vector3(v, 0f, range), gridColor, 1f);
            DrawWorldLine(rect, focus, new Vector3(-range, 0f, v), new Vector3(range, 0f, v), gridColor, 1f);
        }
    }

    private void DrawAxes(Rect rect, Vector3 focus)
    {
        DrawArrow(rect, focus, Vector3.zero, Vector3.right * 0.35f, new Color(1f, 0.25f, 0.25f), "X");
        DrawArrow(rect, focus, Vector3.zero, Vector3.up * 0.35f, new Color(0.35f, 1f, 0.35f), "Y");
        DrawArrow(rect, focus, Vector3.zero, Vector3.forward * 0.35f, new Color(0.35f, 0.55f, 1f), "Z");
    }

    private void DrawWaistAndHmd(Rect rect, Vector3 focus, Vector3 waist, Vector3 hmd)
    {
        if (hasWaistPose)
        {
            DrawPoint(rect, focus, waist, new Color(0.1f, 0.85f, 1f), 7f, "Waist controller");

            Vector3 waistForward = waistPoseLocal.rotation * Vector3.forward;
            waistForward.y = 0f;
            if (waistForward.sqrMagnitude > 1e-6f)
            {
                DrawArrow(rect, focus, waist, waist + waistForward.normalized * 0.22f, new Color(0.3f, 1f, 1f), "waist fwd");
            }
        }

        if (head != null && target != null)
        {
            DrawPoint(rect, focus, hmd, new Color(1f, 0.2f, 1f), 7f, "HMD");
            Vector3 hmdForward = target.InverseTransformDirection(head.forward);
            if (hmdForward.sqrMagnitude > 1e-6f)
            {
                DrawArrow(rect, focus, hmd, hmd + hmdForward.normalized * 0.22f, new Color(1f, 0.55f, 1f), "HMD fwd");
            }
        }
    }

    private void DrawLocomotionVectors(Rect rect, Vector3 focus, Vector3 waist, Vector3 hmd)
    {
        if (locomotion == null)
        {
            return;
        }

        if (hasWaistPose)
        {
            DrawArrow(rect, focus, waist, hmd, new Color(1f, 0.2f, 1f), $"current HMD-waist {HorizontalMagnitude(hmd - waist):0.000}m");

            Quaternion bodyYaw = locomotion.DebugBodyAnchorUsesYaw ? locomotion.DebugBodyYawLocal : Quaternion.identity;
            Vector3 neutralEnd = waist + bodyYaw * locomotion.DebugNeutralBodyLocalHeadOffset;
            DrawArrow(rect, focus, waist, neutralEnd, new Color(1f, 0.85f, 0.25f), $"neutral from C {HorizontalMagnitude(neutralEnd - waist):0.000}m");
        }

        Vector3 moveOffset = new Vector3(locomotion.DebugPlanarOffsetLocal.x, 0f, locomotion.DebugPlanarOffsetLocal.z);
        DrawArrow(rect, focus, waist, waist + moveOffset, new Color(1f, 0.45f, 0.05f), $"translation input {moveOffset.magnitude:0.000}m");

        Vector3 velocity = new Vector3(locomotion.DebugSmoothedPlanarVelocityLocal.x, 0f, locomotion.DebugSmoothedPlanarVelocityLocal.z);
        DrawArrow(rect, focus, waist, waist + velocity * vectorScale, new Color(0.1f, 0.75f, 1f), $"planar velocity {velocity.magnitude:0.00}m/s");

        Vector3 yawForward = locomotion.DebugControlHeadForwardLocal;
        yawForward.y = 0f;
        if (yawForward.sqrMagnitude > 1e-6f)
        {
            yawForward.Normalize();
            DrawArrow(rect, focus, hmd, hmd + yawForward * 0.28f, new Color(0.75f, 0.45f, 1f), $"yaw input {GetYawDeg(yawForward):0.0}deg");
        }

        DrawYawRateArc(rect, focus, waist, locomotion.DebugYawRateDegPerSecond);
    }

    private void DrawYawRateArc(Rect rect, Vector3 focus, Vector3 center, float yawRate)
    {
        float maxRate = locomotion != null ? Mathf.Max(1f, locomotion.yawSpeed) : 180f;
        float signedArc = Mathf.Clamp(yawRate / maxRate, -1f, 1f) * 140f;
        if (Mathf.Abs(signedArc) < 0.1f)
        {
            return;
        }

        float radius = 0.23f;
        int segments = 18;
        Vector3 previous = center + new Vector3(0f, 0.08f, radius);
        Color color = new Color(1f, 0.18f, 0.18f);
        for (int i = 1; i <= segments; i++)
        {
            float t = i / (float)segments;
            float angle = signedArc * t * Mathf.Deg2Rad;
            Vector3 current = center + new Vector3(Mathf.Sin(angle) * radius, 0.08f, Mathf.Cos(angle) * radius);
            DrawWorldLine(rect, focus, previous, current, color, 2.2f);
            previous = current;
        }

        DrawPoint(rect, focus, previous, color, 4f, $"yaw rate {yawRate:0.0}deg/s");
    }

    private void DrawReadoutPanel(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.08f, 0.085f, 0.095f));
        GUILayout.BeginArea(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, rect.height - 10f));
        scroll = GUILayout.BeginScrollView(scroll);

        string source = provider != null ? provider.source.ToString() : "No provider";
        GUILayout.Label($"Source: {source}  Status: {waistStatus}");

        if (locomotion != null)
        {
            Vector3 hmd = locomotion.DebugHeadLocalPosition;
            Vector3 waist = hasWaistPose ? waistPoseLocal.position : Vector3.zero;
            Vector3 current = hmd - waist;
            Vector3 neutral = locomotion.DebugNeutralBodyLocalHeadOffset;
            Vector3 move = locomotion.DebugPlanarOffsetLocal;
            Vector3 velocity = locomotion.DebugSmoothedPlanarVelocityLocal;
            string referenceMode = locomotion.DebugUsingInitialHeadReferenceFallback ? "HMD fallback" : "Waist controller";

            GUILayout.Label($"Reference: {referenceMode}   HMD local: {FormatVector(hmd)}   Waist local: {(hasWaistPose ? FormatVector(waist) : "missing")}");
            GUILayout.Label($"Current HMD-waist XZ: {FormatXZ(current)} |mag| {HorizontalMagnitude(current):0.000} m   Neutral XZ: {FormatXZ(neutral)}");
            GUILayout.Label($"Translation input: {FormatXZ(move)} |mag| {HorizontalMagnitude(move):0.000} m   Planar velocity: {FormatXZ(velocity)} |mag| {HorizontalMagnitude(velocity):0.000} m/s");
            GUILayout.Label($"Yaw input: {GetYawDeg(locomotion.DebugControlHeadForwardLocal):0.0} deg   Yaw rate: {locomotion.DebugYawRateDegPerSecond:0.0} deg/s   Vertical: {locomotion.DebugVerticalCommand:0.00} m/s");
            GUILayout.Label($"Deadzone/max offset: {locomotion.DebugEffectivePlanarDeadZone:0.000} / {locomotion.DebugEffectivePlanarMaxOffset:0.000} m   Learned DZ: {(locomotion.DebugHasOnlineDeadZoneLearning ? locomotion.DebugOnlineLearnedDeadZone.ToString("0.000") : "--")} m   Body yaw normalized: {locomotion.DebugBodyAnchorUsesYaw}");
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private Vector3 GetHmdLocalPosition()
    {
        if (target == null || head == null)
        {
            return Vector3.zero;
        }

        return target.InverseTransformPoint(head.position);
    }

    private void HandleViewportInput(Rect rect)
    {
        Event e = Event.current;
        if (!rect.Contains(e.mousePosition))
        {
            return;
        }

        if (e.type == EventType.ScrollWheel)
        {
            viewDistance = Mathf.Clamp(viewDistance + e.delta.y * 0.08f, 0.6f, 8f);
            e.Use();
        }

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            dragging = true;
            lastMousePosition = e.mousePosition;
            e.Use();
        }

        if (e.type == EventType.MouseUp)
        {
            dragging = false;
        }

        if (dragging && e.type == EventType.MouseDrag)
        {
            Vector2 delta = e.mousePosition - lastMousePosition;
            lastMousePosition = e.mousePosition;
            viewYaw += delta.x * 0.35f;
            viewPitch = Mathf.Clamp(viewPitch - delta.y * 0.35f, -75f, 75f);
            e.Use();
        }
    }

    private bool Project(Rect rect, Vector3 focus, Vector3 point, out Vector2 screen)
    {
        Quaternion viewRotation = Quaternion.Euler(viewPitch, viewYaw, 0f);
        Vector3 cameraSpace = Quaternion.Inverse(viewRotation) * (point - focus) + Vector3.forward * viewDistance;
        if (cameraSpace.z <= 0.02f)
        {
            screen = default;
            return false;
        }

        float focalLength = Mathf.Min(rect.width, rect.height) * 0.55f;
        float scale = focalLength / cameraSpace.z;
        screen = new Vector2(rect.center.x + cameraSpace.x * scale, rect.center.y - cameraSpace.y * scale);
        return true;
    }

    private void DrawPoint(Rect rect, Vector3 focus, Vector3 point, Color color, float radius, string label)
    {
        if (!Project(rect, focus, point, out Vector2 screen))
        {
            return;
        }

        Handles.BeginGUI();
        Handles.color = color;
        Handles.DrawSolidDisc(screen, Vector3.forward, radius);
        Handles.color = Color.black;
        Handles.DrawWireDisc(screen, Vector3.forward, radius);
        Handles.EndGUI();

        if (showLabels)
        {
            GUI.Label(new Rect(screen.x + radius + 3f, screen.y - 10f, 220f, 20f), label);
        }
    }

    private void DrawWorldLine(Rect rect, Vector3 focus, Vector3 from, Vector3 to, Color color, float width)
    {
        if (!Project(rect, focus, from, out Vector2 a) || !Project(rect, focus, to, out Vector2 b))
        {
            return;
        }

        Handles.BeginGUI();
        Handles.color = color;
        Handles.DrawAAPolyLine(width, a, b);
        Handles.EndGUI();
    }

    private void DrawArrow(Rect rect, Vector3 focus, Vector3 from, Vector3 to, Color color, string label)
    {
        if (!Project(rect, focus, from, out Vector2 a) || !Project(rect, focus, to, out Vector2 b))
        {
            return;
        }

        Handles.BeginGUI();
        Handles.color = color;
        Handles.DrawAAPolyLine(2.4f, a, b);
        Vector2 dir = b - a;
        if (dir.sqrMagnitude > 0.001f)
        {
            dir.Normalize();
            Vector2 n = new Vector2(-dir.y, dir.x);
            float headSize = 9f;
            Handles.DrawAAPolyLine(2.4f, b, b - dir * headSize + n * headSize * 0.55f);
            Handles.DrawAAPolyLine(2.4f, b, b - dir * headSize - n * headSize * 0.55f);
        }
        Handles.EndGUI();

        if (showLabels)
        {
            Vector2 labelPos = Vector2.Lerp(a, b, 0.65f);
            GUI.Label(new Rect(labelPos.x + 4f, labelPos.y - 10f, 260f, 20f), label);
        }
    }

    private void DrawCenteredMessage(Rect rect, string message)
    {
        GUIStyle style = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(1f, 1f, 1f, 0.75f) }
        };
        GUI.Label(rect, message, style);
    }

    private static float HorizontalMagnitude(Vector3 value)
    {
        return Mathf.Sqrt(value.x * value.x + value.z * value.z);
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

    private static string FormatVector(Vector3 value)
    {
        return $"({value.x:0.000}, {value.y:0.000}, {value.z:0.000})";
    }

    private static string FormatXZ(Vector3 value)
    {
        return $"({value.x:0.000}, {value.z:0.000})";
    }
}
#endif
