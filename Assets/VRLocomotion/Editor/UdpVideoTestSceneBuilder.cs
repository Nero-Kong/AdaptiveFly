using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Creates a minimal UDP video test scene under Assets/VRLocomotion/test.unity.
/// The scene contains a camera, a directional light, an inside-out sphere, and a
/// UdpVideoStreamReceiver component already bound to that sphere.
/// </summary>
[InitializeOnLoad]
public static class UdpVideoTestSceneBuilder
{
    private const string SceneAssetPath = "Assets/VRLocomotion/test.unity";
    private const string TriggerAssetPath = "Assets/VRLocomotion/Editor/CreateUdpVideoTestScene.trigger.txt";

    static UdpVideoTestSceneBuilder()
    {
        EditorApplication.delayCall += TryCreateFromTrigger;
    }

    [MenuItem("Tools/VR/Create UDP Video Test Scene")]
    public static void CreateFromMenu()
    {
        CreateOrUpdateScene();
    }

    private static void TryCreateFromTrigger()
    {
        if (!File.Exists(GetAbsoluteProjectPath(TriggerAssetPath)))
        {
            return;
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += TryCreateFromTrigger;
            return;
        }

        try
        {
            CreateOrUpdateScene();
        }
        finally
        {
            AssetDatabase.DeleteAsset(TriggerAssetPath);
            AssetDatabase.Refresh();
        }
    }

    private static void CreateOrUpdateScene()
    {
        Scene previousActiveScene = SceneManager.GetActiveScene();
        Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

        try
        {
            SceneManager.SetActiveScene(newScene);

            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 1.5f, -4f);
            cameraObject.transform.rotation = Quaternion.identity;

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.05f, 0.08f, 1f);
            cameraObject.AddComponent<AudioListener>();
            cameraObject.transform.position = Vector3.zero;

            if (cameraObject.GetComponent<HDAdditionalCameraData>() == null)
            {
                cameraObject.AddComponent<HDAdditionalCameraData>();
            }

            GameObject lightObject = new GameObject("Directional Light");
            lightObject.transform.rotation = Quaternion.Euler(40f, -25f, 0f);
            Light directionalLight = lightObject.AddComponent<Light>();
            directionalLight.type = LightType.Directional;
            directionalLight.intensity = 3f;

            GameObject screenObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            screenObject.name = "UDP Video Sphere";
            screenObject.transform.position = cameraObject.transform.position;
            screenObject.transform.localScale = new Vector3(10f, 10f, 10f);

            Collider sphereCollider = screenObject.GetComponent<Collider>();
            if (sphereCollider != null)
            {
                Object.DestroyImmediate(sphereCollider);
            }

            Renderer screenRenderer = screenObject.GetComponent<Renderer>();

            GameObject receiverObject = new GameObject("UDP Video Receiver");
            UdpVideoStreamReceiver receiver = receiverObject.AddComponent<UdpVideoStreamReceiver>();
            receiver.targetRenderer = screenRenderer;
            receiver.targetTextureProperty = "_BaseColorMap";
            receiver.streamUrl = "udp://@:5600";
            receiver.projectOnInnerSurface = true;

            EditorSceneManager.SaveScene(newScene, SceneAssetPath);
            AssetDatabase.Refresh();

            Debug.Log($"Created UDP video test scene at '{SceneAssetPath}'.");
        }
        finally
        {
            EditorSceneManager.CloseScene(newScene, true);

            if (previousActiveScene.IsValid())
            {
                SceneManager.SetActiveScene(previousActiveScene);
            }
        }
    }

    private static string GetAbsoluteProjectPath(string assetPath)
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
    }
}
