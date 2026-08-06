# Changelog

## 0.1.3

- Added an optional local Phantom View for each Slot. It captures from the phantom's descriptor View Position and displays a head-constrained view for the local player.
- Added `Settings > Phantom View` in expression menu controls for enabling the view, adjusting local **Stereo Strength**, and adjusting the angular **Mask Size**.
- Added a projection-aware, angle-based center mask with proportional feathering, and per-eye FOV remapping for VR rendering.
- Excluded the Phantom Grabbing Bone Display from VRChat mirrors, face mirrors, handheld cameras, and screenshots.
- Fixed Animator Tracking Control conversion on Write Defaults Off avatars by merging its Direct BlendTrees through a dedicated Write Defaults On controller.

## 0.1.2

- Added optional Animator Tracking Control translation into per-slot phantom bone-group synchronization.
- Removed unsupported avatar-global state behaviors from phantom FX controllers, including Locomotion Control, Temporary Pose Space, Playable Layer Control, and non-FX Layer Control behaviors.
- Fixed Scale Control so scale and mirror transforms apply to the entire slot and Phantom Grabbing proxy bones remain aligned.
- Made the Phantom Grabbing bone display control network-synced so other users can see it while the phantom is frozen.
- Enabled Phantom Grabbing and Scale Control by default for newly created slots.
- Fixed PhysBone parameter prefix remapping so cloned PhysBones and their generated parameters, such as `_IsGrabbed` and `_IsPosed`, use the slot namespace consistently.
  
## 0.1.1

- Added `GameObject > PhantomSystem > Setup PhantomSystem` for quickly installing PhantomSystem on a selected VRChat avatar root.
- Changed VPM dependencies to minimum-version ranges instead of requiring exact package versions.

## 0.1.0

- Initial early test release.
