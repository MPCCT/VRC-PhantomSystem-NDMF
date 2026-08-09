# Changelog

## 0.2.0

### Added

- Added conversion and integration of source **Gesture** and **Action** playable controllers alongside source FX. Humanoid animation, Avatar Masks, Animator Override Controllers, BlendTrees, Root Motion, mirrored animation, and supported State Behaviours can now continue to drive each phantom independently.
- Added a per-slot animation driver skeleton. Converted source bone animation is isolated from the visible phantom and transferred through the generated rig, avoiding conflicts between source animation and PhantomSystem constraints.
- Added adaptive Humanoid animation sampling. Original key times are retained while additional samples are inserted only where the configured position or rotation accuracy requires them.
- Added **Tools > PhantomSystem > Global Settings**, also accessible from the component Inspector, for Phantom View texture resolution and Humanoid conversion accuracy.
- Added deterministic parameter resolution across the base avatar and all Slots. Compatible parameters can be shared, while incompatible source parameters are automatically moved to a unique Slot namespace.
- Added shared authoring validation for the Inspector, Prebake, and NDMF build. Diagnostics now use stable `PHS` codes and cover avatar rigs, Slot identity, parameter conflicts, missing scripts, and component compatibility.
- Added an Editor test assembly covering Slot validation, parameter resolution, Humanoid conversion, BlendTree handling, build error lifecycle, and validation consistency.

### Changed

- Renamed **Remove Original FX** to **Remove Source Controls**. It now excludes the source FX, Action, Gesture, parameters, and final Expression Menu while retaining PhantomSystem controls.
- Enabled **Try Convert Animator Tracking Control** by default for newly created Slots.
- Normalized Slot identity consistently across hierarchy names, animation paths, layers, menus, and parameters. Empty names resolve to `Slot1`, comparisons are case-sensitive, and unsafe hierarchy-name or Core-prefix collisions are reported before build.
- Hardened component compatibility classification: VRC and NDMF components are now recognized by their owning assemblies rather than type namespace, while other `MonoBehaviour` types remain non-blocking `PHS021` warnings.
- Inspector-visible authoring warnings are no longer repeated in the Console during build. Warnings that can only be discovered while converting or inspecting the built avatar are still reported.
- Build failures now abort PhantomSystem processing once, report accumulated root causes once, and prevent later PhantomSystem passes from continuing to modify the avatar.

### Fixed

- Fixed cloned phantom armatures conflicting with same-named base-avatar hierarchy objects. Affected armatures are renamed and their animation paths are remapped automatically.
- Fixed Phantom View capturing its own output and producing visual feedback in VR.
- Fixed synchronization budget reporting so Phantom Grabbing, Scale Control, source parameters, compatible sharing, and automatic renames are reflected in the displayed total; Phantom View remains local-only.
- Fixed authoring and build failures being reported repeatedly or allowing later PhantomSystem passes to continue. Null Slots are handled safely, collapsed Slot headers include Warning counts, Missing Script alerts can select affected objects, and Core Menu installation failures use the correct severity.

### Known limitations

- Parameter-driven Animator State mirroring is not evaluated at runtime. PhantomSystem bakes the State's default Mirror value and reports a build warning when `mirrorParameterActive` is used.

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
