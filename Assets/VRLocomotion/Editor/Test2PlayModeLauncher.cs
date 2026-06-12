using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class Test2PlayModeLauncher
{
    private const string ScenePath = "Assets/VRLocomotion/test2.unity";
    private static double _enterPlayModeAfter;

    [MenuItem("Tools/VR/Open Test2 And Enter Play Mode")]
    public static void OpenTest2AndEnterPlayMode()
    {
        Debug.Log("[Test2PlayModeLauncher] Opening " + ScenePath);
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
            Debug.Log("[Test2PlayModeLauncher] Entering Play Mode.");
            EditorApplication.EnterPlaymode();
        }
    }
}
