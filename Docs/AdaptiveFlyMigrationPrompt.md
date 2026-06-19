# AdaptiveFly Migration Prompt

Use this prompt on the new machine to continue the project without losing context.

```text
You are helping me continue a Unity HDRP/OpenXR project named AdaptiveFly.

Repository:
- https://github.com/Nero-Kong/AdaptiveFly
- Branch: main

Project purpose:
- Unity VR locomotion demo based on AdaptiveFly-style body leaning.
- Current stable tracking scheme uses HMD plus a waist/body anchor from a controller pose. The old OpenXR body/hips/chest tracking route was removed or avoided because it was unstable under Quest Link.
- The transparent hand visualization should use real XR Hands hand mesh only. Controller/tracker pose must not be used to draw fake hands.

Important current files:
- Assets/VRLocomotion/HeadOffsetLocomotion.cs
- Assets/VRLocomotion/BodyAnchorProvider.cs
- Assets/VRLocomotion/XRDeviceFilters.cs
- Assets/VRLocomotion/TransparentUpperBodyVisualizer.cs
- Assets/VRLocomotion/AdaptiveFlyDroneCommandBroadcaster.cs
- Assets/VRLocomotion/RealDrone_Z1.unity
- Assets/XR/Settings/OpenXRPackageSettings.asset
- Packages/manifest.json
- Packages/packages-lock.json

Current RealDrone_Z1 setup:
- The scene now has AdaptiveFly command calculation on XR Origin.
- HeadOffsetLocomotion is configured with applyMotionToTarget = false, so the scene computes control commands but does not virtually move/rotate the XR Origin. This avoids double motion when controlling a real drone camera feed.
- BodyAnchorProvider uses the left controller as the waist/body anchor. If that controller is unavailable, HeadOffsetLocomotion falls back to initial HMD/head offset.
- AdaptiveFlyDroneCommandBroadcaster sends UDP JSON commands at 30 Hz to 127.0.0.1:14560 by default.
- The command frame is body FRD style for drone/bridge use:
  - forward_mps: positive forward
  - right_mps: positive right
  - down_mps: positive down
  - up_mps: positive up, included for readability
  - yaw_deg_s: yaw-rate command in degrees/second
  - cmd_forward/cmd_right/cmd_down/cmd_up/cmd_yaw: normalized -1..1 command values
  - valid/has_body_anchor/using_hmd_fallback: state flags
- The THETA Z1 WebRTC receiver displays an already-stitched equirectangular panorama. RealDrone_Z1 is currently testing a simple mount correction for the Z1 top-forward setup: staticMountEulerDegrees = (-90, -90, 0), yawOffsetDegrees = 180, with Z1 IMU horizon lock disabled.

New machine setup checklist:
1. Install Git and Git LFS.
2. Clone the repo:
   git clone https://github.com/Nero-Kong/AdaptiveFly.git
   cd AdaptiveFly
   git lfs install
   git lfs pull
3. Open the project with Unity 6000.3.14f1 or a very close Unity 6 version.
4. Let Unity restore packages from Packages/manifest.json.
5. Use Meta Quest Link / OpenXR. Confirm Meta Horizon Link is the active OpenXR runtime for Quest 3.
6. Open Assets/VRLocomotion/RealDrone_Z1.unity.
7. In the THETA Z1 WebRTC Receiver, update signalingUrl if the Z1 signaling server IP changed. Current default is ws://127.0.0.1:8765.
8. In AdaptiveFlyDroneCommandBroadcaster on XR Origin, update destinationHost/destinationPort if the drone bridge is not running locally. Default is 127.0.0.1:14560.
9. Start the UDP receiver/bridge before Play. It should implement a timeout failsafe: if no packets arrive for 200-500 ms, send zero velocity / hover.
10. Press C in Play mode to recenter the HMD origin and pitch. Keep the left controller/waist tracker at the waist if using body-anchor mode.

Drone command recommendation:
- Do not directly use Unity camera absolute position/rotation to drive the drone.
- Use the broadcaster's velocity/yaw command fields:
  - forward_mps
  - right_mps
  - down_mps or up_mps
  - yaw_deg_s
- Add limits and a deadman/failsafe in the bridge or drone side before passing commands to MAVLink/ROS/flight controller.

Known repo scope:
- Large third-party environment packs such as Assets/_DLNK, Assets/Davis3D, Assets/Sci-Fi_Submarine 1, and Unity Library are intentionally excluded from Git.
- RealDrone_Z1 does not depend on those large environment packs.
- Other visual demo scenes may need those excluded asset packs restored separately.
```
