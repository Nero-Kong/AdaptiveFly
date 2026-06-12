using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.HighDefinition;
using Unity.XR.CoreUtils;

/// <summary>
/// One-shot utility that copies the XR Origin hierarchy from Ocean.unity into
/// HeavyStation_demo.unity and reconfigures cameras to preserve the Heavy
/// Station lighting/post-processing setup without camera conflicts.
/// </summary>
[InitializeOnLoad]
public static class HeavyStationXROriginImporter
{
    private const string SourceScenePath = "Assets/VRLocomotion/Ocean.unity";
    private const string TargetScenePath = "Assets/VRLocomotion/HeavyStation_demo.unity";
    private const string TriggerPath = "Assets/VRLocomotion/Editor/HeavyStationXROriginImport.trigger.txt";
    private const string RigName = "XR Origin (VR)";
    private const string MainCameraName = "Main Camera";
    private const string CamPlayerName = "Cam Player";
    private const string CamCinematicName = "Cam Cinematic";

    static HeavyStationXROriginImporter()
    {
        EditorApplication.delayCall += TryRunFromTrigger;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem("Tools/VR/Copy Ocean XR Origin To Heavy Station")]
    public static void RunFromMenu()
    {
        RunMigration(removeTriggerOnSuccess: false);
    }

    private static void TryRunFromTrigger()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            return;
        }

        if (!File.Exists(TriggerPath))
        {
            return;
        }

        RunMigration(removeTriggerOnSuccess: true);
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.delayCall += TryRunFromTrigger;
        }
    }

    private static void RunMigration(bool removeTriggerOnSuccess)
    {
        Scene activeSceneBefore = SceneManager.GetActiveScene();
        bool openedSourceScene = false;
        bool openedTargetScene = false;

        try
        {
            Scene sourceScene = GetOrOpenScene(SourceScenePath, ref openedSourceScene);
            Scene targetScene = GetOrOpenScene(TargetScenePath, ref openedTargetScene);

            GameObject sourceRig = FindInScene(sourceScene, RigName);
            if (sourceRig == null)
            {
                throw new InvalidOperationException($"Could not find '{RigName}' in {SourceScenePath}.");
            }

            GameObject existingTargetRig = FindInScene(targetScene, RigName);
            if (existingTargetRig != null)
            {
                UnityEngine.Object.DestroyImmediate(existingTargetRig);
            }

            GameObject clonedRig = UnityEngine.Object.Instantiate(sourceRig);
            clonedRig.name = RigName;
            SceneManager.MoveGameObjectToScene(clonedRig, targetScene);

            GameObject targetCamPlayer = FindInScene(targetScene, CamPlayerName);
            GameObject targetCamCinematic = FindInScene(targetScene, CamCinematicName);

            XROrigin xrOrigin = clonedRig.GetComponent<XROrigin>();
            if (xrOrigin == null || xrOrigin.Camera == null)
            {
                throw new InvalidOperationException("Cloned XR Origin does not contain a configured XROrigin camera.");
            }

            Camera xrMainCamera = xrOrigin.Camera;
            Camera heavyReferenceCamera = targetCamPlayer != null ? targetCamPlayer.GetComponent<Camera>() : null;

            if (heavyReferenceCamera != null)
            {
                CopyCameraSettings(heavyReferenceCamera, xrMainCamera);
                CopyHdCameraSettings(
                    targetCamPlayer.GetComponent<HDAdditionalCameraData>(),
                    xrMainCamera.GetComponent<HDAdditionalCameraData>());
                AlignRigToCameraPose(clonedRig.transform, xrMainCamera.transform, heavyReferenceCamera.transform);
            }

            EnsureMainCameraSetup(xrMainCamera);
            DisableLegacySceneCameras(targetCamPlayer, targetCamCinematic, xrMainCamera);
            DisableLegacySceneControllers(targetScene);

            EditorSceneManager.MarkSceneDirty(targetScene);
            EditorSceneManager.SaveScene(targetScene);

            Debug.Log(
                $"HeavyStationXROriginImporter: copied '{RigName}' from '{SourceScenePath}' to '{TargetScenePath}' and disabled conflicting legacy cameras.",
                clonedRig);

            if (removeTriggerOnSuccess && AssetDatabase.DeleteAsset(TriggerPath))
            {
                AssetDatabase.Refresh();
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"HeavyStationXROriginImporter failed: {ex}");
        }
        finally
        {
            RestoreActiveScene(activeSceneBefore);
            CloseSceneIfNeeded(SourceScenePath, openedSourceScene);
            CloseSceneIfNeeded(TargetScenePath, openedTargetScene);
        }
    }

    private static Scene GetOrOpenScene(string path, ref bool openedScene)
    {
        Scene scene = SceneManager.GetSceneByPath(path);
        if (scene.IsValid() && scene.isLoaded)
        {
            return scene;
        }

        openedScene = true;
        return EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
    }

    private static void CloseSceneIfNeeded(string path, bool openedScene)
    {
        if (!openedScene)
        {
            return;
        }

        Scene scene = SceneManager.GetSceneByPath(path);
        if (scene.IsValid() && scene.isLoaded)
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void RestoreActiveScene(Scene previousActiveScene)
    {
        if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
        {
            SceneManager.SetActiveScene(previousActiveScene);
        }
    }

    private static GameObject FindInScene(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform match = root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == name);
            if (match != null)
            {
                return match.gameObject;
            }
        }

        return null;
    }

    private static void CopyCameraSettings(Camera source, Camera target)
    {
        if (source == null || target == null)
        {
            return;
        }

        EditorUtility.CopySerialized(source, target);
    }

    private static void CopyHdCameraSettings(HDAdditionalCameraData source, HDAdditionalCameraData target)
    {
        if (source == null || target == null)
        {
            return;
        }

        EditorUtility.CopySerialized(source, target);
    }

    private static void AlignRigToCameraPose(Transform rigRoot, Transform xrCamera, Transform referenceCamera)
    {
        Vector3 currentPlanarForward = Vector3.ProjectOnPlane(xrCamera.forward, Vector3.up);
        Vector3 targetPlanarForward = Vector3.ProjectOnPlane(referenceCamera.forward, Vector3.up);

        if (currentPlanarForward.sqrMagnitude > 1e-6f && targetPlanarForward.sqrMagnitude > 1e-6f)
        {
            Quaternion yawDelta = Quaternion.FromToRotation(currentPlanarForward.normalized, targetPlanarForward.normalized);
            rigRoot.rotation = yawDelta * rigRoot.rotation;
        }

        Vector3 cameraToRoot = rigRoot.position - xrCamera.position;
        rigRoot.position = referenceCamera.position + cameraToRoot;
    }

    private static void EnsureMainCameraSetup(Camera xrMainCamera)
    {
        xrMainCamera.gameObject.tag = "MainCamera";

        foreach (AudioListener listener in xrMainCamera.GetComponents<AudioListener>())
        {
            listener.enabled = true;
        }
    }

    private static void DisableLegacySceneCameras(GameObject camPlayer, GameObject camCinematic, Camera xrMainCamera)
    {
        if (camPlayer != null)
        {
            DisableCameraHierarchy(camPlayer);
        }

        if (camCinematic != null)
        {
            DisableCameraHierarchy(camCinematic);
        }

        foreach (AudioListener listener in xrMainCamera.GetComponentsInChildren<AudioListener>(true))
        {
            listener.enabled = listener.gameObject == xrMainCamera.gameObject;
        }
    }

    private static void DisableCameraHierarchy(GameObject cameraRoot)
    {
        cameraRoot.tag = "Untagged";

        foreach (Camera camera in cameraRoot.GetComponentsInChildren<Camera>(true))
        {
            camera.enabled = false;
        }

        foreach (AudioListener listener in cameraRoot.GetComponentsInChildren<AudioListener>(true))
        {
            listener.enabled = false;
        }

        foreach (CameraOrbit orbit in cameraRoot.GetComponentsInChildren<CameraOrbit>(true))
        {
            orbit.enabled = false;
        }
    }

    private static void DisableLegacySceneControllers(Scene scene)
    {
        List<GameObject> roots = new List<GameObject>();
        scene.GetRootGameObjects(roots);

        foreach (GameObject root in roots)
        {
            foreach (CameraSwitchKey cameraSwitch in root.GetComponentsInChildren<CameraSwitchKey>(true))
            {
                cameraSwitch.enabled = false;
            }

            foreach (StandaloneInputModule standaloneInput in root.GetComponentsInChildren<StandaloneInputModule>(true))
            {
                standaloneInput.enabled = false;
            }
        }
    }
}
