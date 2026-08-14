[简体中文](README.md) | [English](README_EN.md) | [日本語](README_JA.md)

# PhantomSystem

PhantomSystem は、VRChat アバターに 1 体以上の Humanoid 分身（Phantom Avatar）を
追加する NDMF ベースのシステムです。ビルド時にアニメーション、パラメーター、メニュー、
および操作に必要な構成を自動的に準備します。

生成された Expression Menu から、分身の追従や固定、身体のポーズ、スケール変更、分身位置
からのビュー表示などを操作できます。分身元アバターのメニューや一般的なアニメーション制御も
引き継ぐことができます。

> Inspector と生成される Expression Menu の表示は現在英語のみです。

## 主な機能

### 複数の分身を個別に操作

- 1 つのアバターに複数の Slot を設定し、それぞれ異なる分身元を割り当てられます。
- 分身ごとに有効化、固定、Position Lock を個別に切り替えられます。
- 初期生成位置と、分身元メニューを最終メニューへ含めるかどうかを指定できます。

### 分身元のアニメーションとメニューを統合

- 分身元の FX、Gesture、Action、Expression Parameters、Expression Menu を Prebake
  して統合します。
- 一般的な Humanoid アニメーション、Avatar Mask、BlendTree、Animator Override
  Controller、Root Motion、Mirror アニメーションに対応します。
- Animator Tracking Control を分身向けの身体部位同期へ変換できます。
- 分身元の制御が不要な場合は、PhantomSystem の制御だけを残すこともできます。

### Phantom Grabbing

- 固定中の分身の Hips をハンドジェスチャーで移動できます。
- 身体用 PhysBone Proxy を生成し、接触への反応やポーズ操作を可能にします。
- 位置を確認しやすい簡易ボーン表示を使用できます。この表示は VRChat のミラーやカメラには
  映りません。

### スケールとミラー

- Slot ごとに全体スケールを調整し、既定値へリセットできます。
- Slot のローカル X 軸に沿って分身全体を反転できます。

### Phantom View

- 分身の頭部から見たローカル専用のステレオビューを表示します。
- ステレオの強さと中央ビューマスクの大きさを調整できます。
- Advanced の Camera Near Clip は Phantom Scale に合わせて変化し、拡大した分身の顔が
  ビューを遮る場合に調整できます。
- ビューが重ならないよう、同時に表示される Phantom View は 1 つの Slot だけです。

### パラメーター管理とビルド前チェック

- 分身元パラメーターを名前空間化し、Animator、メニュー、Contact、PhysBone、VRCRaycast、
  Play Audio などの参照を一貫して更新します。
- 互換性のある同名パラメーターは共有し、互換性のない競合は自動的に別名へ変更します。
- Inspector で Slot ごとの同期コスト、共有による節約、最終パラメーター名を確認できます。
- **Review Any Alerts** で Humanoid ボーン、Slot 名、パラメーター競合、Missing Script、
  および互換性を確認できないコンポーネントをビルド前に検出します。

## 必要環境

- Unity 2022.3
- VRChat SDK - Avatars 3.10.3 以降
- NDMF 1.14.0 以降
- Modular Avatar 1.15.0 以降

## インストール

VCC に次の VPM リポジトリを追加し、アバタープロジェクトへ **PhantomSystem** を追加して
ください。

```text
https://mpcct.github.io/VRC-PhantomSystem-NDMF/index.json
```

## クイックセットアップ

1. 分身元を、本体アバター階層の外に独立した Avatar Root として配置します。
2. 本体 Avatar Root を右クリックし、`PhantomSystem > Setup PhantomSystem` を選択します。
3. 生成された `PhantomSystem` 子オブジェクトを選択し、Slot の **Phantom Avatar** に
   分身元の `VRCAvatarDescriptor` を指定します。
4. 必要に応じて Phantom Grabbing、Scale Control、Phantom View、分身元メニューを
   有効にします。
5. **Review Any Alerts** を確認し、Error を修正して必要な Warning を確認します。
6. 通常どおり VRChat SDK から Build & Test またはアップロードを実行します。分身元は
   メインビルドの前に自動で Prebake されます。

確認用の Manual Bake には、コンポーネントの **Bake Avatar with PhantomSystem** を使用
してください。通常の Modular Avatar Manual Bake では、PhantomSystem に必要な分身元の
Prebake は実行されません。

## 主なオプション

- **Install Phantom Menu**：PhantomSystem の Expression Menu を生成して追加します。
- **Slot Name**：Slot の識別名と既定のパラメータープレフィックスを設定します。最終的な
  Slot 名は重複できません。
- **Spawn Override**：分身の初期位置と回転を指定します。
- **Include Phantom Menu**：分身元の最終 Expression Menu を Slot メニューへ追加します。
- **Enable Phantom Grabbing**：Grab、身体 Proxy、ボーン表示を有効にします。
- **Enable Scale Control**：全体スケール、リセット、Mirror 操作を有効にします。
- **Enable Phantom View**：分身からのローカル専用ビューを有効にします。
- **Namespace Phantom Parameters**：分身元パラメーターに独立した名前空間を使用します。
- **Same-name Parameter Sharing**：本体と共有できる互換パラメーターを選択します。
- **Remove Source Controls**：分身元の FX、Action、Gesture、パラメーター、メニューを除外し、
  PhantomSystem の制御だけを残します。
- **Use Rotation Constraint**：本体と分身のボーン比率や向きが少し異なる場合に、追従を改善
  できることがあります。
- **Override PhysBone Immobile Type**：分身の PhysBone を All Motion に設定し、Freeze 中も
  本体の移動を引き継いでしまう現象を軽減します。
- **Try Convert Animator Tracking Control**：分身元の身体部位 Tracking 制御の維持を
  試みます。
- **Phantom View Near Clip (Advanced)**：1 倍スケール時のカメラ近接クリップ距離を設定します。
  Scale Control が有効な場合は分身の大きさに合わせて変化します。

新しい Slot では Phantom Grabbing、Scale Control、Phantom View、Tracking Control
変換、および PhysBone Immobile Type の上書きが既定で有効です。

## Global Settings

コンポーネントの **Open Global Settings**、または
`Tools > PhantomSystem > Global Settings` からプロジェクト共通設定を開けます。

- **Phantom View Texture Size**：分身ビューで共有する描画解像度を設定します。
- **Humanoid Animation Conversion**：最大サンプリングレートと位置・回転の誤差許容値を
  設定します。

既定値は一般的なプロジェクト向けです。アニメーションの精度、Clip サイズ、または Phantom
View のパフォーマンスを調整したい場合だけ変更してください。

## 生成されるメニュー操作

- **Activate**：分身の表示を有効または無効にします。
- **Freeze**：通常のボーン追従を止め、現在の状態を保持します。
- **Position Lock**：生成された位置固定方式を切り替えます。
- **Scale / Reset Scale / Mirror**：Slot 全体のサイズ変更、リセット、反転を行います。
- **Bone Display**：Freeze 中にポーズ操作用の簡易ボーンを表示します。
- **Settings > Phantom View**：ビューを有効にし、Stereo Strength と Mask Size を
  調整します。

## 制限事項

- 本体とすべての分身元は、必要な Humanoid ボーンを持つ有効な Humanoid Avatar である
  必要があります。
- 最終的な FX Controller が **Write Defaults Off** を使用する Avatar には、現在対応して
  いません。VRChat では FX が Gesture の後に評価されるため、FX Clip が Transform を
  アニメーションしていない場合でも、マスクされていない WD Off FX が通常の Transform の
  所有権を取得することがあります。その結果、Phantom Animation Driver が分身元の
  Gesture/Action ボーンアニメーションを受け取れず、アクション再生中の分身が静止ポーズや
  T-Pose になる場合があります。該当する Avatar では WD On と互換性のある FX 構成を使用するか、
  **Remove Source Controls** を有効にして分身元のアニメーション制御を除外してください。
- 1 つの本体アバターに配置できる PhantomSystem コンポーネントは 1 つだけです。
- 分身元は本体階層の外に配置し、別の PhantomSystem を含めないでください。
- プレイヤー本体専用の Animator State Behaviour の一部は、分身上で直接実行できません。
  対応する Behaviour は変換され、削除または一部変換された内容はビルド時に報告されます。
- パラメーター駆動の Animator State Mirror は、実行時の変化に対応していません。State の
  既定 Mirror 値を Bake し、ビルド時に Warning を表示します。
- 生成された Prebake アセットは
  `Tools > PhantomSystem > Delete Prebake Assets` から削除できます。

## License

PhantomSystem は [MIT License](LICENSE) で提供されます。メニューアイコンには、同じく MIT
License の [Tabler Icons](https://github.com/tabler/tabler-icons) を使用しています。

更新履歴は [CHANGELOG](Packages/com.mpcct.phantom-system/CHANGELOG.md) を参照してください。
