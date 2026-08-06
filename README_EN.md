[简体中文](README.md) | [English](README_EN.md) | [日本語](README_JA.md)

# PhantomSystem

PhantomSystem is an NDMF-based system for adding a controllable Phantom Avatar
to a VRChat avatar. During the build, it prebakes each phantom source and
generates the menus, parameters, and Humanoid bone constraints required to
control it.

The Inspector and generated Expression Menus are not localized yet and currently
use English interface text only.

## Requirements

- Unity 2022.3
- VRChat SDK - Avatars 3.10.0 or newer
- NDMF 1.14.0 or newer
- Modular Avatar 1.15.0 or newer

## Installation

Add the following VPM repository in VCC, then add **PhantomSystem** to the avatar
project:

```text
https://mpcct.github.io/VRC-PhantomSystem-NDMF/index.json
```

## Basic setup

1. Place each phantom source in the scene as a separate avatar root, outside the
   base avatar hierarchy.
2. Right-click the base avatar root and select
   `PhantomSystem > Setup PhantomSystem`.
3. Select the generated `PhantomSystem` child. In the Slot, assign the source
   `VRCAvatarDescriptor` to **Phantom Avatar**.
4. Configure the Slot and resolve errors under **Review Any Alerts**.
5. Build or upload through the VRChat SDK normally. Phantom sources are prebaked
   automatically before the main build.

For an NDMF manual bake, use **Bake Avatar with PhantomSystem** on the component
instead of Modular Avatar's regular Manual Bake command.

## Inspector options

### System Options

- **Install Phantom Menu**: Generates and installs the PhantomSystem Expression
  Menu.
- **Select Core Menu Location**: Selects its installation location in the base
  avatar menu.
- **Bake Avatar with PhantomSystem**: Prebakes all phantom sources and then runs
  a manual avatar bake.

### Slot

- **Slot Name**: Identifies the Slot and its default parameter namespace. Slot
  names must be unique.
- **Phantom Avatar**: The Humanoid avatar used as the phantom source.
- **Spawn Override**: Overrides the initial position and rotation. When empty,
  the base avatar root is used.
- **Include Phantom Menu**: Adds the source avatar's final Expression Menu to the
  Slot menu.
- **Enable Phantom Grabbing**: Generates Hips grabbing, PhysBone body proxies,
  and the bone display.
- **Enable Scale Control**: Adds scale, reset, and X-axis mirror controls.

New Slots enable Phantom Grabbing and Scale Control by default.

### Parameter Settings

- **Parameter Prefix**: Overrides the default `PhantomSystem/<Slot Name>` prefix.
- **Namespace Phantom Parameters**: Namespaces source parameters, including
  PhysBone-derived parameters such as `_IsGrabbed` and `_IsPosed`.
- **Same-name Parameter Sharing**: Allows selected compatible parameters to
  remain shared with same-name parameters on the base avatar.

### Advanced

- **Remove Original FX**: Excludes the source FX, parameters, and menu while
  retaining PhantomSystem controls.
- **Use Rotation Constraint**: Uses Rotation Constraints instead of Parent
  Constraints for non-Hips bones. This is useful when the base and phantom
  avatars have slightly different skeleton structures or proportions.
- **Rotation Solve In World Space**: Solves those Rotation Constraints in world
  space to handle different bone orientations between the base and phantom.
  When enabled, the phantom can no longer maintain an orientation independent
  of the base avatar.
- **Override PhysBone Immobile Type**: Changes PhysBones in the Slot to `All
  Motion`. This may alter the source avatar's intended PhysBone behavior.
- **Try Convert Animator Tracking Control**: Converts supported source Tracking
  Control values into phantom bone-group synchronization. Eyelids, visemes, and
  facial blend shapes are not converted.

## Expression Menu

- **Activate**: Enables or disables the phantom.
- **Freeze**: Stops normal bone following and holds the phantom.
- **Position Lock**: Switches the generated position-lock behavior.
- **Scale**: Scales the entire Slot from `0.2x` to `1.8x`.
- **Reset Scale**: Restores `1.0x` scale.
- **Mirror**: Mirrors the phantom on the Slot's local X axis.
- **Bone Display**: Shows the generated octahedral bone mesh while frozen.

## Notes

- The base avatar and every phantom source must be valid Humanoid avatars.
- A base avatar can contain only one PhantomSystem component.
- A phantom source must remain outside the base avatar hierarchy and cannot
  contain another PhantomSystem.
- Avatar-global State Behaviors that cannot safely run in a phantom FX are
  removed and reported in the NDMF Console.
- Use `Tools > PhantomSystem > Delete Prebake Assets` to remove generated prebake
  assets.

## License

PhantomSystem is available under the [MIT License](LICENSE). Menu icons are from
[Tabler Icons](https://github.com/tabler/tabler-icons), also under the MIT
License.

See the [changelog](Packages/com.mpcct.phantom-system/CHANGELOG.md) for release
history.
