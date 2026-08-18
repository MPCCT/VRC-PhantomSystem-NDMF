# Changelog

## 0.3.0

### Added

- Added a project-local Humanoid pose bake cache under `Library`. Repeated Gesture and Action conversion can reuse compatible adaptive-sampling results while final motions continue to use the current source Clip's non-Humanoid curves.
- Added Humanoid bake cache statistics and clearing controls to **Tools > PhantomSystem** and the Global Settings window. Invalid or damaged cache entries safely fall back to a normal bake.

### Changed

- Reorganized Humanoid animation conversion into separate binding analysis, pose sampling, adaptive processing, curve output, and VirtualClip output stages. Cache hits now rebuild converted motions directly as VirtualClips to avoid repeating Unity scene sampling and temporary Clip materialization.
- Unified Core and source parameter planning across the Inspector, NDMF Parameter Provider, menu generation, and avatar build. Source State Behaviour translation is now separated from playable-controller preparation so both pipelines share one consistent result and diagnostic path.
- Generated Prebake assets are now removed after a successful VRC build, after PhantomSystem Manual Bake, and when leaving Apply on Play. Stale assets are cleared before a later Prebake, and empty `Assets/PhantomSystemGenerated` directories are removed automatically.
- The PhantomSystem Inspector now refreshes parameter and validation previews through precise NDMF dependency tracking instead of listening to every hierarchy change.
- Moved the component's Add Component entry from `MPCCT/PhantomSystem` to the top-level `PhantomSystem` entry.
- Disabled Modular Avatar MMD World Support only on temporary phantom Prebake clones, without changing the source avatar's setting.

### Fixed

- Animator Layer Control targets are now resolved while NDMF Animator Services still retains stable virtual layer identities, then verified after final controller generation. This prevents renamed or removed intermediate layers from invalidating Converted Action weights and cross-playable Layer Controls.
- Fixed selecting or editing PhantomSystem dependencies repeatedly dirtying the scene. Relevant source Avatar, parameter, menu, controller, component, and Humanoid-rig changes still refresh the Inspector automatically.

## 0.2.2

### Changed

- Retained source Gesture, Action, and FX controllers are now all merged into the final FX Controller in a deterministic order. Their conversion, per-layer Avatar Masks, and logical roles remain separate, while affected Animator Layer Controls and binary Action Playable Layer Controls are retargeted to the final FX layers.
- Refactored Scale Control around a positive Slot scale root and a separate X-axis Mirror root. Scale and Mirror now use independent 1D BlendTrees inside one Direct BlendTree; Mirror remains a synced Bool and is expanded to Float only inside the Animator.
- The Phantom View camera capture root now follows a scale-aware Head anchor while remaining outside the mirrored subtree. Its capture position and stereo eye separation inherit the Slot's overall scale, while Near Clip continues to receive explicit world-space scale compensation.

### Fixed

- Preserved Animator parameter curves embedded in Humanoid clips instead of treating them as unsupported Humanoid bindings during Gesture and Action conversion.
- Removed Humanoid, Root Motion, and skeletal Transform pose curves from retained source FX clips while preserving parameter, BlendShape, material, object-reference, and other component animation. A duration-preserving dummy binding is added when pose curves are removed so the result does not become an empty clip.
- Added a lower-priority neutral pose for each animation driver skeleton and completed missing rotations in converted Override Gesture and Action clips within their effective Avatar Masks. Partial Humanoid clips no longer leave omitted bones in an unrelated previous pose or T-pose.
- Fixed Layer Control resolution after Gesture and Action layers are moved into the final FX Controller, including multi-layer Action enable and disable controls.

## 0.2.1

### Added

- Added a per-Slot **Phantom View Near Clip** setting under Advanced options. The configured distance represents the value at 1x scale and follows Phantom Scale automatically, allowing enlarged phantoms to clip nearby facial geometry without changing the capture viewpoint.

### Changed

- Source FX, Gesture, and Action conversion is now isolated per Slot through NDMF Animator Services. Multiple Slots can reference the same source avatar while retaining independent layers, motions, State Behaviours, Avatar Masks, Tracking Control conversion, and parameter targets.
- Freeze now preserves the phantom's current pose instead of switching its tracking rig back to live base-avatar motion.
- Source parameter ownership is collected before generated Rig components are added. Known source references are remapped consistently, while unknown names remain unchanged and produce one non-blocking summary warning per Slot instead of receiving an automatic `Original/` prefix.
- Generated PhantomSystem Animator layers now include their Slot name, making multi-Slot controllers easier to inspect and diagnose.
- **Override PhysBone Immobile Type** is enabled by default for newly created Slots so physbones on frozen phantoms are less likely to inherit unintended base-avatar movement. Existing serialized Slot settings are unchanged.
- Raised the minimum VRChat SDK - Avatars dependency to `3.10.3`.

### Fixed

- Fixed multiple Slots referencing the same source avatar sharing converted playable state, which could leave Tracking Control generated only once or targeting the first Slot.
- Fixed parameter remapping for retained source Contacts, PhysBones, VRCRaycast components, Animator Parameter Drivers, transitions, BlendTrees, and `VRCAnimatorPlayAudio`. 
- Fixed standalone Phantom View and Tracking controllers temporarily referencing Core parameters that were not declared in those controllers.
- Fixed `VRC.Core.PipelineManager` being reported as an unknown source component by the compatibility check.

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
