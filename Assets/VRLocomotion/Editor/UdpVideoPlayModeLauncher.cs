using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class UdpVideoPlayModeLauncher
{
    private const string ScenePath = "Assets/VRLocomotion/test.unity";
    private static double _enterPlayModeAfter;

    [MenuItem("Tools/VR/Open UDP Video Test And Enter Play Mode")]
    public static void OpenUdpVideoTestAndEnterPlayMode()
    {
        Debug.Log("[UdpVideoPlayModeLauncher] Opening " + ScenePath);
        EditorSceneManager.OpenScene(ScenePath);

        _enterPlayModeAfter = EditorApplication.timeSinceStartup + 2.0;
        EditorApplication.update -= EnterPlayModeWhenReady;
        EditorApplication.update += EnterPlayModeWhenReady;
    }

    private static void EnterPlayModeWhenReady()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            return;
        }

        if (EditorApplication.timeSinceStartup < _enterPlayModeAfter)
        {
            return;
        }

        EditorApplication.update -= EnterPlayModeWhenReady;

        if (!EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.Log("[UdpVideoPlayModeLauncher] Entering Play Mode.");
            EditorApplication.EnterPlaymode();
        }
    }
}
