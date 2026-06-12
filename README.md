# AdaptiveFly HDRP

Unity HDRP / OpenXR prototype for AdaptiveFly-style body-lean locomotion.

## Versioned scope

This repository intentionally tracks the project code, locomotion scenes, XR/OpenXR settings, project settings, and embedded packages that are needed for the current development work.

Tracked core areas:

- `Assets/VRLocomotion`
- `Assets/XR`
- `Assets/Settings`
- `Assets/Plugins`
- `ProjectSettings`
- `Packages`

Large imported art/environment packs are excluded from Git and should be restored separately on machines that need the full visual scenes:

- `Assets/_DLNK`
- `Assets/Davis3D`
- `Assets/Sci-Fi_Submarine 1`
- `Assets/CrestShowcase`

Unity generated folders such as `Library`, `Logs`, and `UserSettings` are also excluded.

## Notes

The tracked locomotion implementation currently uses a waist/controller anchor plus HMD-relative motion and XR Hands hand mesh visualization. Large binary assets in the tracked scope are stored through Git LFS.
