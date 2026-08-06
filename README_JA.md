[简体中文](README.md) | [English](README_EN.md) | [日本語](README_JA.md)

# PhantomSystem

PhantomSystem は、VRChat アバターに操作可能な分身（ファントム）アバターを追加する
NDMF ベースのシステムです。ビルド時に分身元アバターを Prebake し、操作に必要な
メニュー、パラメーター、Humanoid ボーン Constraint を自動生成します。

現在、Inspector と生成される Expression Menu はまだローカライズされておらず、
インターフェースのテキストは英語のみです。

> [!NOTE]
> この日本語 README は AI によって翻訳されています。表現や用語に誤りが含まれる
> 可能性があります。

## 必要環境

- Unity 2022.3
- VRChat SDK - Avatars 3.10.0 以降
- NDMF 1.14.0 以降
- Modular Avatar 1.15.0 以降

## インストール

VCC に次の VPM リポジトリを追加し、アバタープロジェクトに
**PhantomSystem** を追加してください。

```text
https://mpcct.github.io/VRC-PhantomSystem-NDMF/index.json
```

## 基本設定

1. 分身元アバターを、本体アバターの外側に独立したルートとして配置します。
2. Hierarchy で本体アバターのルートを右クリックし、
   `PhantomSystem > Setup PhantomSystem` を選択します。
3. 生成された `PhantomSystem` 子オブジェクトを選択し、Slot の
   **Phantom Avatar** に分身元の `VRCAvatarDescriptor` を指定します。
4. Slot を設定し、**Review Any Alerts** に表示されたエラーを解消します。
5. 通常どおり VRChat SDK から Build & Test またはアップロードを実行します。
   分身元はメインビルド前に自動で Prebake されます。

NDMF の手動 Bake を行う場合は、Modular Avatar の通常の Manual Bake ではなく、
コンポーネントの **Bake Avatar with PhantomSystem** を使用してください。

## Inspector オプション

### System Options

- **Install Phantom Menu**：PhantomSystem の Expression Menu を生成して追加します。
- **Select Core Menu Location**：本体メニュー内の追加先を選択します。
- **Bake Avatar with PhantomSystem**：全分身元を Prebake してから手動 Bake を実行します。

### Slot

- **Slot Name**：Slot 名と既定のパラメーター名前空間です。各 Slot には異なる名前が必要です。
- **Phantom Avatar**：分身元として使用する Humanoid アバターです。
- **Spawn Override**：分身の初期位置と回転を指定します。未指定時は本体ルートを使用します。
- **Include Phantom Menu**：分身元の最終 Expression Menu を Slot メニューに追加します。
- **Enable Phantom Grabbing**：Hips の Grab、PhysBone ボディ Proxy、ボーン表示を生成します。
- **Enable Scale Control**：スケール、リセット、X 軸ミラー操作を追加します。

新しい Slot では Phantom Grabbing と Scale Control が既定で有効です。

### Parameter Settings

- **Parameter Prefix**：既定の `PhantomSystem/<Slot Name>` プレフィックスを上書きします。
- **Namespace Phantom Parameters**：分身元パラメーターを名前空間化し、
  `_IsGrabbed` や `_IsPosed` などの PhysBone 派生パラメーターも処理します。
- **Same-name Parameter Sharing**：互換性のある選択済みパラメーターを、
  本体の同名パラメーターと共有します。

### Advanced

- **Remove Original FX**：分身元の FX、パラメーター、メニューを除外し、
  PhantomSystem の操作だけを残します。
- **Use Rotation Constraint**：Hips 以外のボーンで Parent Constraint の代わりに
  Rotation Constraint を使用します。本体と分身のスケルトン構造や比率がわずかに
  異なるアバターに有効です。
- **Rotation Solve In World Space**：本体と分身でボーンの向きが異なる場合に、
  上記 Rotation Constraint をワールド空間で計算します。有効にすると、分身は
  本体とは独立した向きを維持できなくなります。
- **Override PhysBone Immobile Type**：Slot 内の PhysBone を `All Motion` に変更します。
  分身元の PhysBone の挙動が変わる可能性があります。
- **Try Convert Animator Tracking Control**：対応する Tracking Control を
  分身ボーングループの同期制御へ変換します。まぶた、Viseme、顔の BlendShape は変換されません。

## Expression Menu

- **Activate**：分身を有効または無効にします。
- **Freeze**：通常のボーン追従を停止し、分身を保持します。
- **Position Lock**：生成された位置固定方式を切り替えます。
- **Scale**：Slot 全体を `0.2x` から `1.8x` まで拡縮します。
- **Reset Scale**：スケールを `1.0x` に戻します。
- **Mirror**：Slot のローカル X 軸で分身を反転します。
- **Bone Display**：Freeze 中に生成された八面体ボーン Mesh を表示します。

## 注意事項

- 本体と分身元は、どちらも有効な Humanoid アバターである必要があります。
- 本体アバターに配置できる PhantomSystem コンポーネントは 1 つだけです。
- 分身元は本体階層の外に置き、別の PhantomSystem を含めないでください。
- 分身 FX で安全に実行できないアバター全体向け State Behavior は削除され、
  NDMF Console に結果が表示されます。
- 生成済み Prebake アセットは
  `Tools > PhantomSystem > Delete Prebake Assets` から削除できます。

## License

PhantomSystem は [MIT License](LICENSE) で提供されます。メニューアイコンには、
同じく MIT License の [Tabler Icons](https://github.com/tabler/tabler-icons) を使用しています。

更新履歴は [CHANGELOG](Packages/com.mpcct.phantom-system/CHANGELOG.md) を参照してください。
