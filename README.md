# PhantomSystem

PhantomSystem is an early-test NDMF/VPM package for creating and controlling
prebaked phantom avatars in VRChat.

Build flow:

1. Before NDMF starts processing the main avatar, every unique phantom source is processed through a complete, separate NDMF build.
2. The main avatar's NDMF build clones only those processed results.
3. PhantomSystem installs humanoid constraints, the generated control FX, the prebaked source FX, expressions menus, and namespaced parameters.

The prebake hook runs automatically for VRChat SDK builds, Build & Test, uploads, and VRC Apply on Play preprocessing. A failed prebake stops the build; there is no raw-avatar compatibility fallback.

Current scope:

- Automatic per-build phantom source prebaking.
- Build-time cloning of prebaked phantoms under the target avatar.
- Basic humanoid bone constraints.
- Activate, Freeze, and Position Lock controllers.
- Optional per-slot Phantom Grabbing for contact-driven Hips movement, PhysBone body posing, and local x-ray proxy-bone display while frozen.
- Optional per-slot Scale Control with radial 0.2x-1.8x scaling, reset, and X-axis mirroring.
- Prebaked FX, menu, and parameter integration through Modular Avatar.
- Final phantom animation binding diagnostics before Avatar Optimizer.
- Multi-slot Inspector cards with validation, parameter-sharing controls, and persistent foldout state.

Not yet implemented:

- Phantom View.
- Built-in localization.
- VRC Tracking Control translation.

Version `0.1.0` is an early test release and is still undergoing Unity validation
against complex third-party NDMF avatars.

