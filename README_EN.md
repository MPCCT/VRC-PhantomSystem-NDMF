[简体中文](README.md) | [English](README_EN.md) | [日本語](README_JA.md)

# PhantomSystem

PhantomSystem is an NDMF-based phantom avatar system for VRChat. It adds one or
more Humanoid avatars to a base avatar and automatically prepares their
animation, parameters, menus, and controls during the build.

The generated Expression Menu can make a phantom follow the base avatar, freeze
it in the scene, pose its body, change its scale, or show a view from its
position. The source avatar's menus and common animation controls can also be
retained.

> The Inspector and generated Expression Menus currently use English interface
> text only.

## Main features

### Multiple independent phantoms

- Configure multiple Slots on one avatar and assign a different phantom source
  to each Slot.
- Activate, freeze, and change position locking independently for each phantom.
- Choose an initial spawn transform and whether to include the source avatar's
  menu.

### Source animation and menu integration

- Prebakes and integrates source FX, Gesture, Action, Expression Parameters,
  and Expression Menu content.
- Supports common Humanoid animation, Avatar Masks, BlendTrees, Animator
  Override Controllers, Root Motion, and mirrored animation.
- Can translate Animator Tracking Control into body-part synchronization suited
  to a phantom.
- Source controls can be omitted when only the PhantomSystem controls are
  needed.

### Phantom Grabbing

- Move a frozen phantom's Hips with a hand gesture.
- Generates PhysBone body proxies so the phantom can react to touches and be
  posed.
- Shows a simplified bone display for positioning. The display is hidden from
  VRChat mirrors and cameras.

### Scale and mirror

- Adjust each Slot's overall scale independently and reset it to the default.
- Mirror the whole phantom along the Slot's local X axis.

### Phantom View

- Displays a local-only stereo view captured from the phantom's head.
- Adjusts stereo strength and the size of the central view mask.
- Provides an Advanced camera Near Clip setting that follows Phantom Scale to
  keep an enlarged phantom's face from obscuring the view.
- Only one Slot's Phantom View is shown at a time to prevent overlapping views.

### Parameter management and validation

- Namespaces source parameters and consistently updates references used by
  Animators, menus, Contacts, PhysBones, VRCRaycast, and Play Audio behaviours.
- Shares compatible same-name parameters and automatically renames incompatible
  collisions.
- Previews each Slot's synchronization cost, sharing savings, and final parameter
  names in the Inspector.
- **Review Any Alerts** checks Humanoid bones, Slot names, parameter conflicts,
  missing scripts, and components whose compatibility cannot be verified before
  the build.

## Requirements

- Unity 2022.3
- VRChat SDK - Avatars 3.10.3 or newer
- NDMF 1.14.0 or newer
- Modular Avatar 1.15.0 or newer

## Installation

Add the following VPM repository in VCC, then add **PhantomSystem** to the avatar
project:

```text
https://mpcct.github.io/VRC-PhantomSystem-NDMF/index.json
```

## Quick setup

1. Place each phantom source in the scene as a separate avatar root outside the
   base avatar hierarchy.
2. Right-click the base avatar root and select
   `PhantomSystem > Setup PhantomSystem`.
3. Select the generated `PhantomSystem` child. In its Slot, assign the source
   `VRCAvatarDescriptor` to **Phantom Avatar**.
4. Enable Phantom Grabbing, Scale Control, Phantom View, or the source menu as
   needed.
5. Review **Review Any Alerts**, resolve Errors, and check relevant Warnings.
6. Build, test, or upload through the VRChat SDK normally. Phantom sources are
   prebaked automatically before the main build.

For an inspectable manual bake, use **Bake Avatar with PhantomSystem** on the
component. A regular Modular Avatar Manual Bake does not run the source-avatar
prebake required by PhantomSystem.

## Common options

- **Install Phantom Menu**: Generates and installs the PhantomSystem Expression
  Menu.
- **Slot Name**: Sets the Slot identity and default parameter prefix. Final Slot
  names must be unique.
- **Spawn Override**: Sets the phantom's initial position and rotation.
- **Include Phantom Menu**: Adds the source avatar's final Expression Menu to the
  Slot menu.
- **Enable Phantom Grabbing**: Enables grabbing, body proxies, and the bone
  display.
- **Enable Scale Control**: Enables overall scale, reset, and mirror controls.
- **Enable Phantom View**: Enables the local-only view from the phantom.
- **Namespace Phantom Parameters**: Places source parameters in an independent
  namespace.
- **Same-name Parameter Sharing**: Selects compatible parameters that may remain
  shared with the base avatar.
- **Remove Source Controls**: Excludes source FX, Action, Gesture, parameters,
  and menu while retaining PhantomSystem controls.
- **Use Rotation Constraint**: May improve following when the base and phantom
  skeleton proportions or orientations differ slightly.
- **Override PhysBone Immobile Type**: Sets the phantom's PhysBones to All
  Motion to reduce unintended base-avatar movement while frozen.
- **Try Convert Animator Tracking Control**: Attempts to retain body-part
  Tracking controls from the source avatar.
- **Phantom View Near Clip (Advanced)**: Sets the camera's near clipping
  distance at 1x scale. It follows the phantom's size when Scale Control is
  enabled.

New Slots enable Phantom Grabbing, Scale Control, Phantom View, and Tracking
Control conversion by default, and also override the PhysBone Immobile Type.

## Global Settings

Open project-wide settings with **Open Global Settings** on the component or
`Tools > PhantomSystem > Global Settings`:

- **Phantom View Texture Size** sets the render resolution shared by phantom
  views.
- **Humanoid Animation Conversion** sets the maximum sampling rate and position
  and rotation error tolerances.

The defaults suit most projects. Adjust them only when animation detail, Clip
size, or Phantom View performance needs tuning.

## Generated menu controls

- **Activate** enables or disables the phantom.
- **Freeze** stops normal bone following and holds the current state.
- **Position Lock** changes the generated position-lock behavior.
- **Scale / Reset Scale / Mirror** resizes, resets, or mirrors the whole Slot.
- **Bone Display** shows the simplified poseable skeleton while frozen.
- **Settings > Phantom View** enables the view and adjusts Stereo Strength and
  Mask Size.

## Limitations

- The base avatar and all phantom sources must be valid Humanoid avatars with
  the required Humanoid bones.
- Avatars whose final FX Controller uses **Write Defaults Off** are not currently
  supported. VRChat evaluates FX after Gesture, and Unity may let an unmasked
  WD Off FX Controller claim ordinary Transform properties even when no FX Clip
  animates them. This can prevent the Phantom Animation Driver from receiving
  source Gesture/Action bone animation and may leave the phantom in its static
  pose or a T-pose while an action plays. Affected avatars can use an FX setup
  compatible with WD On, or enable **Remove Source Controls** to omit source
  animation controls.
- A base avatar can contain only one PhantomSystem component.
- A phantom source must remain outside the base avatar hierarchy and cannot
  contain another PhantomSystem.
- Some Animator State Behaviours apply only to the player avatar and cannot run
  directly on a phantom. PhantomSystem converts supported behaviours and reports
  removed or partially converted content during the build.
- Parameter-driven Animator State Mirror changes are not supported at runtime.
  The State's default Mirror value is baked and a build warning is reported.
- Use `Tools > PhantomSystem > Delete Prebake Assets` to remove generated
  prebake assets.

## License

PhantomSystem is available under the [MIT License](LICENSE). Menu icons are from
[Tabler Icons](https://github.com/tabler/tabler-icons), also under the MIT
License.

See the [changelog](Packages/com.mpcct.phantom-system/CHANGELOG.md) for release
history.
