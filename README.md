[简体中文](README.md) | [English](README_EN.md) | [日本語](README_JA.md)

# PhantomSystem

PhantomSystem 是一个基于 NDMF、用于为 VRChat Avatar 添加分身（Phantom Avatar）的
系统。它会在构建时预处理分身源 Avatar，将其加入本体 Avatar，并自动生成控制菜单、
参数和 Humanoid 骨骼约束。

目前插件的 Inspector 和生成的 Expression Menu 暂未进行本地化，界面文本均为英文。

## 环境要求

- Unity 2022.3
- VRChat SDK - Avatars 3.10.0 或更高
- NDMF 1.14.0 或更高
- Modular Avatar 1.15.0 或更高

## 安装

在 VCC 中添加以下 VPM 仓库，然后将 **PhantomSystem** 添加到 Avatar 工程：

```text
https://mpcct.github.io/VRC-PhantomSystem-NDMF/index.json
```

## 简单配置

1. 将分身源模型作为独立的 Avatar 根节点放在场景中，不要放进本体层级。
2. 右键本体 Avatar 根节点，选择
   `PhantomSystem > Setup PhantomSystem`。
3. 选中生成的 `PhantomSystem` 子物体，在 Slot 的 **Phantom Avatar** 中指定
   分身源模型的 `VRCAvatarDescriptor`。
4. 根据需要调整 Slot 选项，并处理 **Review Any Alerts** 中的错误。
5. 正常使用 VRChat SDK 进行 Build & Test 或上传。分身源会在构建前自动 Prebake。

如需使用 NDMF 的手动 Bake，请使用组件中的
**Bake Avatar with PhantomSystem**，不要直接使用 Modular Avatar 的普通 Manual Bake。

## Inspector 选项

### System Options

- **Install Phantom Menu**：生成并安装 PhantomSystem 的 Expression Menu。
- **Select Core Menu Location**：选择生成菜单在本体菜单中的安装位置。
- **Bake Avatar with PhantomSystem**：先 Prebake 所有分身源，再执行手动 Avatar Bake。

### Slot

- **Slot Name**：Slot 名称，同时用于默认参数命名空间。多个 Slot 必须使用不同名称。
- **Phantom Avatar**：需要生成分身的 Humanoid Avatar。
- **Spawn Override**：指定分身的初始位置和旋转；留空时使用本体根节点。
- **Include Phantom Menu**：将分身源最终生成的 Expression Menu 加入 Slot 菜单。
- **Enable Phantom Grabbing**：生成 Hips 抓取、PhysBone 身体代理和骨骼显示功能。
- **Enable Scale Control**：添加缩放、恢复缩放和 X 轴镜像控制。

新建 Slot 默认启用 Phantom Grabbing 和 Scale Control。

### Parameter Settings

- **Parameter Prefix**：覆盖默认的 `PhantomSystem/<Slot Name>` 参数前缀。
- **Namespace Phantom Parameters**：为分身源参数添加独立命名空间，同时处理
  PhysBone 的 `_IsGrabbed`、`_IsPosed` 等派生参数。
- **Same-name Parameter Sharing**：让选中的兼容参数继续与本体同名参数共享。

### Advanced

- **Remove Original FX**：不加入分身源的 FX、参数和菜单，只保留 PhantomSystem 控制。
- **Use Rotation Constraint**：非 Hips 骨骼使用 Rotation Constraint 代替 Parent
  Constraint。适合本体与分身的骨架结构或比例存在少量差异的 Avatar。
- **Rotation Solve In World Space**：让上述 Rotation Constraint 在世界空间求解，
  用于处理本体与分身骨骼定向不同的情况；启用后分身不能再保持独立于本体的朝向。
- **Override PhysBone Immobile Type**：将 Slot 内 PhysBone 的 Immobile Type 改为
  `All Motion`。这可能改变原模型的 PhysBone 表现。
- **Try Convert Animator Tracking Control**：尝试把源 FX 中的 Animator Tracking
  Control 转换为分身骨骼组同步控制。眼睑、口型和面部 BlendShape 不会被转换。

## Expression Menu

- **Activate**：显示或关闭分身。
- **Freeze**：停止分身骨骼正常跟随，使其保持在当前状态。
- **Position Lock**：切换分身的位置锁定方式。
- **Scale**：在 `0.2x` 到 `1.8x` 之间调整整个 Slot 的大小。
- **Reset Scale**：恢复到 `1.0x`。
- **Mirror**：沿 Slot 本地 X 轴镜像分身。
- **Bone Display**：Freeze 时显示生成的八面体骨骼网格。

## 注意事项

- 本体和分身源都必须是有效的 Humanoid Avatar。
- 一个本体只能包含一个 PhantomSystem 组件。
- 分身源不能位于本体层级内，也不能包含另一个 PhantomSystem。
- PhantomSystem 会移除不适合在分身 FX 中运行的 Avatar 全局 State Behavior；
  相关信息会显示在 NDMF Console 中。
- 可通过 `Tools > PhantomSystem > Delete Prebake Assets` 清理生成的 Prebake 资产。

## License

PhantomSystem 使用 [MIT License](LICENSE)。菜单图标来自同为 MIT 协议的
[Tabler Icons](https://github.com/tabler/tabler-icons)。

版本记录参见 [CHANGELOG](Packages/com.mpcct.phantom-system/CHANGELOG.md)。
