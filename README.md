[简体中文](README.md) | [English](README_EN.md) | [日本語](README_JA.md)

# PhantomSystem

PhantomSystem 是一个基于 NDMF 的 VRChat Avatar 分身系统。它可以把一个或多个 Humanoid
Avatar 作为分身加入本体，并在构建时自动准备动画、参数、菜单和控制结构。

通过生成的 Expression Menu，可以让分身跟随本体、冻结在场景中、抓取和摆放身体、调整大小，
也可以从分身的位置观察周围。分身源原有的菜单与常用动画控制也可以一并保留。

> Inspector 和生成的 Expression Menu 目前使用英文界面。

## 主要功能

### 多分身与独立控制

- 一个 Avatar 可以配置多个独立 Slot，并为每个 Slot 指定不同的分身源。
- 每个分身可以单独启用、冻结和切换位置锁定方式。
- 可以指定分身的初始生成位置，并选择是否把分身源菜单加入最终菜单。

### 保留分身源的动画与菜单

- 自动 Prebake 分身源，并整合其 FX、Gesture、Action、Expression Parameters 和 Expression
  Menu。
- 支持常见 Humanoid 动画、Avatar Mask、BlendTree、Animator Override Controller、Root
  Motion 和镜像动画。
- 可尝试把 Animator Tracking Control 转换为适用于分身的身体部位同步控制。
- 不需要分身源控制时，可以只保留 PhantomSystem 自身的控制菜单。

### Phantom Grabbing

- 分身冻结时，可以通过手势抓取并移动其 Hips。
- 自动生成身体 PhysBone 代理，使分身身体可以被碰触和摆姿势。
- 可显示简化骨骼，方便确认和操作身体位置；骨骼显示不会出现在 VRChat 镜子和相机中。

### 缩放与镜像

- 每个 Slot 可以独立调整整体大小，并一键恢复默认比例。
- 可以沿 Slot 本地 X 轴镜像整个分身。

### Phantom View

- 从分身头部生成仅本地可见的立体视角。
- 可以调整立体强度和中心视野遮罩大小。
- 可以在 Advanced 中调整相机 Near Clip；该距离会跟随分身缩放，避免放大后的脸部遮挡视野。
- 同一时间只显示一个 Slot 的 Phantom View，避免视野相互覆盖。

### 参数管理与构建检查

- 自动为分身参数添加命名空间，并同步处理 Animator、菜单、Contact、PhysBone、VRCRaycast
  和 Play Audio 等参数引用。
- 兼容的同名参数可以共享；不兼容的冲突参数会自动改名。
- Inspector 会预览每个 Slot 的同步参数占用、共享节省和最终参数名称。
- **Review Any Alerts** 会在构建前检查 Humanoid 骨骼、Slot 名称、参数冲突、Missing
  Script 和无法确认兼容性的组件。

## 环境要求

- Unity 2022.3
- VRChat SDK - Avatars 3.10.3 或更高
- NDMF 1.14.0 或更高
- Modular Avatar 1.15.0 或更高

## 安装

在 VCC 中添加以下 VPM 仓库，然后将 **PhantomSystem** 添加到 Avatar 工程：

```text
https://mpcct.github.io/VRC-PhantomSystem-NDMF/index.json
```

## 快速配置

1. 将分身源作为独立的 Avatar 根节点放在场景中，不要放进本体 Avatar 的层级。
2. 右键本体 Avatar 根节点，选择 `PhantomSystem > Setup PhantomSystem`。
3. 选中生成的 `PhantomSystem` 子物体，在 Slot 的 **Phantom Avatar** 中指定分身源的
   `VRCAvatarDescriptor`。
4. 根据需要启用 Phantom Grabbing、Scale Control、Phantom View 或分身源菜单。
5. 检查 **Review Any Alerts**，修正 Error，并确认需要关注的 Warning。
6. 正常使用 VRChat SDK 执行 Build & Test 或上传。分身源会在构建前自动 Prebake。

如需生成供检查使用的手动 Bake，请使用组件中的 **Bake Avatar with PhantomSystem**。
普通 Modular Avatar Manual Bake 不会执行 PhantomSystem 所需的分身源 Prebake。

## 常用选项

- **Install Phantom Menu**：生成并安装 PhantomSystem 的 Expression Menu。
- **Slot Name**：设置 Slot 身份和默认参数前缀；多个 Slot 的最终名称必须唯一。
- **Spawn Override**：指定分身的初始位置和旋转。
- **Include Phantom Menu**：把分身源最终生成的 Expression Menu 加入 Slot 菜单。
- **Enable Phantom Grabbing**：启用抓取、身体代理和骨骼显示。
- **Enable Scale Control**：启用整体缩放、重置和镜像。
- **Enable Phantom View**：启用仅本地可见的分身视角。
- **Namespace Phantom Parameters**：为分身源参数使用独立命名空间。
- **Same-name Parameter Sharing**：选择可以与本体共享的兼容参数。
- **Remove Source Controls**：排除分身源的 FX、Action、Gesture、参数和菜单，只保留
  PhantomSystem 控制。
- **Use Rotation Constraint**：本体与分身骨架比例或朝向略有差异时，可尝试改善骨骼跟随。
- **Override PhysBone Immobile Type**：把分身 PhysBone 的 Immobile Type 设为 All Motion，减少
  Freeze 后仍随本体移动的情况。
- **Try Convert Animator Tracking Control**：尝试保留分身源的身体部位 Tracking 控制。
- **Phantom View Near Clip (Advanced)**：设置 1 倍大小时的相机近裁剪距离；启用 Scale
  Control 时会自动随分身大小变化。

新建 Slot 默认启用 Phantom Grabbing、Scale Control、Phantom View、Tracking Control 转换和
PhysBone Immobile Type 覆盖。

## Global Settings

通过组件中的 **Open Global Settings**，或菜单
`Tools > PhantomSystem > Global Settings` 打开项目级设置：

- **Phantom View Texture Size**：设置所有分身视角使用的渲染分辨率。
- **Humanoid Animation Conversion**：设置动画转换的最高采样率和位置、旋转误差容限。

默认值适合一般项目。只有在动画细节不足、Clip 体积过大或 Phantom View 性能不足时，才建议调整。

## 生成的菜单控制

- **Activate**：显示或关闭分身。
- **Freeze**：停止正常骨骼跟随并保持当前状态。
- **Position Lock**：切换位置锁定方式。
- **Scale / Reset Scale / Mirror**：调整大小、恢复比例或镜像整个 Slot。
- **Bone Display**：在 Freeze 时显示可操作的简化骨骼。
- **Settings > Phantom View**：启用分身视角并调整 Stereo Strength 与 Mask Size。

## 使用限制

- 本体和所有分身源都必须是有效的 Humanoid Avatar，并能解析必要的 Humanoid 骨骼。
- 当前不支持最终 FX Controller 使用 **Write Defaults Off** 的 Avatar。VRChat 会在 Gesture
  之后计算 FX，而 Unity 可能让未遮罩的 WD Off FX 获得普通 Transform 的所有权，即使 FX
  Clip 没有动画这些 Transform。这会阻止 Phantom Animation Driver 接收分身源的
  Gesture/Action 骨骼动画，并可能使播放动作的分身变为静态姿态或 T-Pose。受影响的 Avatar
  可以改用兼容 WD On 的 FX，或启用 **Remove Source Controls**，不保留分身源动画控制。
- 一个本体只能包含一个 PhantomSystem 组件。
- 分身源必须位于本体层级外，并且不能包含另一个 PhantomSystem。
- 部分只适用于玩家本体的 Animator State Behaviour 无法直接用于分身；PhantomSystem 会转换
  支持的行为，并对被移除或部分转换的内容给出构建警告。
- 参数驱动的 Animator State Mirror 暂不支持运行时变化；构建时会使用该 State 的默认 Mirror
  值并给出警告。
- 可通过 `Tools > PhantomSystem > Delete Prebake Assets` 清理生成的 Prebake 资产。

## License

PhantomSystem 使用 [MIT License](LICENSE)。菜单图标来自同为 MIT 协议的
[Tabler Icons](https://github.com/tabler/tabler-icons)。

版本记录参见 [CHANGELOG](Packages/com.mpcct.phantom-system/CHANGELOG.md)。
