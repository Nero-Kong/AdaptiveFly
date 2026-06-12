using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds Assets/VRLocomotion/test2.unity for the UDP/MPEG-TS receiver path:
/// Edge2/X5 MPEG-TS UDP -> ffmpeg raw BGRA -> GPU dual-fisheye stitch -> HDRP sky dome.
/// </summary>
public static class Test2SceneBuilder
{
    private const string SceneAssetPath = "Assets/VRLocomotion/test2.unity";
    private const string ComputeShaderPath = "Assets/VRLocomotion/Shaders/DualFisheyeStitch.compute";

    [MenuItem("Tools/VR/Create Test2 UDP Skybox Scene")]
    public static void CreateFromMenu()
    {
        CreateOrUpdateScene();
    }

    public static void CreateOrUpdateScene()
    {
        Scene previousActiveScene = SceneManager.GetActiveScene();
        bool canRestorePreviousScene = previousActiveScene.IsValid() && !string.IsNullOrEmpty(previousActiveScene.path);
        Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        try
        {
            SceneManager.SetActiveScene(newScene);

            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = Vector3.zero;
            cameraObject.transform.rotation = Quaternion.identity;

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.fieldOfView = 75f;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 1000f;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<HDAdditionalCameraData>();

            GameObject lightObject = new GameObject("Soft Reference Light");
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.2f;

            GameObject sphereObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphereObject.name = "UDP GPU Stitched Sky Dome";
            sphereObject.transform.position = Vector3.zero;
            sphereObject.transform.localScale = Vector3.one * 100f;
            Object.DestroyImmediate(sphereObject.GetComponent<Collider>());

            GameObject pipelineObject = new GameObject("UDP MPEG-TS Skybox Pipeline");
            UdpVideoStreamReceiver receiver = pipelineObject.AddComponent<UdpVideoStreamReceiver>();
            receiver.streamUrl = "udp://@:5600";
            receiver.forcedWidth = 3840;
            receiver.forcedHeight = 1920;
            receiver.outputWidthOverride = 0;
            receiver.outputHeightOverride = 0;
            receiver.networkCachingMs = 30;
            receiver.ffmpegInputOptions = "-fflags nobuffer+discardcorrupt -flags low_delay -probesize 262144 -analyzeduration 100000";
            receiver.decodeBackend = UdpVideoStreamReceiver.DecodeBackend.D3D11VA;
            receiver.autoFindOrCreateProjectionSphere = false;
            receiver.createRuntimePlaybackMaterial = false;

            X5ImuStabilizer stabilizer = pipelineObject.AddComponent<X5ImuStabilizer>();
            stabilizer.listenPort = 5601;
            stabilizer.compensateRollPitch = true;
            stabilizer.dampYawJitter = true;

            DualFisheyeStitcher stitcher = pipelineObject.AddComponent<DualFisheyeStitcher>();
            stitcher.receiver = receiver;
            stitcher.imuStabilizer = stabilizer;
            stitcher.enableImuStabilization = true;
            stitcher.sphereRenderer = sphereObject.GetComponent<Renderer>();
            stitcher.equirectWidth = 3840;
            stitcher.equirectHeight = 1920;
            stitcher.fovDeg = 200f;
            stitcher.backFlipX = true;
            stitcher.drawEquirectOverlay = false;
            stitcher.noSignalDebugColor = new Color(0.02f, 0.025f, 0.035f, 1f);

            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputeShaderPath);
            if (compute != null)
            {
                stitcher.computeShader = compute;
            }
            else
            {
                Debug.LogWarning("[Test2SceneBuilder] Missing compute shader: " + ComputeShaderPath);
            }

            EditorSceneManager.SaveScene(newScene, SceneAssetPath);
            AssetDatabase.Refresh();
            Debug.Log("[Test2SceneBuilder] Created UDP/MPEG-TS test2 skybox scene at " + SceneAssetPath);
        }
        finally
        {
            if (canRestorePreviousScene)
            {
                EditorSceneManager.OpenScene(previousActiveScene.path, OpenSceneMode.Single);
            }
        }
    }
}
