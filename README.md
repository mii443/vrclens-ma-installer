# VRCLens MA Installer

VRCLensをアバターの子として設定し、ビルド時に自動導入するNDMFプラグインです。
アバターの元のメニュー、Expression Parameters、FXなどのPlayable Layersを複製してから、購入済みVRCLensのApply処理を呼び出します。

## 必要なもの

- Unity 2022.3（開発環境: 2022.3.22f1）
- VRChat Avatars SDK 3.7以降
- Modular Avatar 1.18.7以降 / NDMF 1.14.8以降
- 別途購入・インポートした **VRCLens 1.10.0**
- カスタマイズする場合は、別途購入・インポートしたVRCLens_Customと、その製品が要求する依存ツール

VRCLensの内部APIを利用するため、対応バージョン以外では明示的に停止します。VRCLens_Custom本体やVRCFuryはこのパッケージの必須依存ではありません。

## インストール

VCCのSettings → Packages → Add Repositoryで、次のURLを追加します。

`https://vpm.mii.dev/index.json`

プロジェクトのManage Projectから **VRCLens MA Installer** を追加してください。[配布ページ](https://vpm.mii.dev/)のAdd to VCCボタンも利用できます。Modular Avatar / NDMFの配布元リポジトリも登録しておいてください。VRCLens本体と任意のカスタマイズは別途購入・インポートが必要です。

ZIPで直接導入する場合:

配布ZIPを展開し、`package.json`が`Packages/dev.mii.vrclens-ma-installer/package.json`になるようフォルダーを配置してください。依存パッケージはALCOM / VCC等で先に導入してください。

## 使用手順

1. 通常Applyをまだ行っていないアバターを用意します。
2. Projectの `Packages > VRCLens MA Installer > Prefabs > VRCLens MA Installer` をアバター直下へドラッグします。
3. Inspectorの **設定用VRCLensを作成** を押します。利用者のプロジェクトにある購入済みPrefabから、設定用オブジェクトを作成します。
4. InstallerのInspectorで設定を編集します。**設定をシーンのプレビューに反映** でカメラモデル等を確認できます。
5. `VRCLens Settings` 直下の `HeadMountPoint`、`SelfieFocusPoint`、`FocusObject`、`CamBase` の位置を調整します。Inspectorの各選択ボタンも使えます。回転はVRCLens SettingsのCamera Rotation / Focus Pen Rotationで設定します。
6. 任意のカスタマイズPrefabを **Customizations直下** に置きます。
7. 通常どおりアップロードします。元のVRCLensの **Apply VRCLensは押しません**。

Hierarchyでアバターを選択し、`GameObject > VRCLens MA > Installerを追加` を使うと、2〜3を一括で行えます。

```text
Avatar
└─ VRCLens MA Installer
   ├─ VRCLens Settings  ← このプロジェクト内で生成（配布物には含まれない）
   │  ├─ HeadMountPoint
   │  ├─ SelfieFocusPoint
   │  ├─ FocusObject
   │  └─ CamBase ...
   └─ Customizations
      └─ 購入済みのカスタマイズPrefab（任意）
```

Target Avatarはビルド用の親アバターへ自動設定されます。アバターを複製しても、別のシーン上のアバターへApplyされません。
Installerを無効化すると、その設定用オブジェクトもビルドから除かれます。導入を取りやめる場合はInstaller全体を削除します。

## Prefab Variant

InstallerのPrefab Variantをアバター直下に配置して使用できます。Installerを含むアバター自体をPrefab Variantとして保存する構成も検証しています。設定用VRCLensを作成した後は、シーンまたは自分用のVariantへ設定を保存してください。生成済みの設定用オブジェクトには購入済みアセットが含まれるため、配布用パッケージには入れないでください。

## Lyra Menu Managerとの併用

Lyra Menu Manager 1.2.1で検証しています。Lyraは任意の依存で、導入されているときだけ連携ボタンが表示されます。

1. Installerの設定用VRCLensを作成します。
2. InstallerのInspectorで **Lyra Menu Manager用Proxyを設定** を押します。
3. シーンを保存し、Lyra Menu Managerでアバターのメニューを再読み込みします。
4. 表示されたVRCLensを希望のサブメニューへ移動し、Lyraのレイアウトを保存します。表示名も変更できます。
5. 通常どおりビルドします。

Proxyはビルド時に生成されるVRCLensメニュー全体を表します。VRCLens内部の全項目を編集画面に展開する機能ではありません。Target Sub Menuを後から変更した場合は、Proxy設定ボタンをもう一度押してください。

カスタマイズのVRCFuryメニューには`VRCLens/Custom`等の追加先が設定されています。LyraはNDMFのTransformingで移動処理を行い、その後のSDKコールバックでVRCFuryがメニューを統合するため、固定パスのままでは本体とCustomが分離します。本ツールは、Lyra処理前後で追加先のメニュー項目を追跡し、ビルド用コピーの追加先パスだけを更新します。例えば`Cameras/My Lens/Custom`へ追従します。元のカスタマイズPrefabとLyraレイアウトは変更しません。

追従の対象は、本Installerで導入したVRCLens配下のVRCFury FullControllerメニューです。追加先をLyraのインベントリへ移して非表示にするなど、移動先が消えたり一意に決まらなくなった場合はビルドを停止します。

## 非破壊性とビルド順序

NDMFのFirstChanceで処理し、Modular Avatarのメニュー統合・Animator仮想化に先行します。
元のメニューグラフ（共有・循環参照を含む）、Expression Parameters、Playable LayersのAnimatorグラフを複製し、ビルド用アバター内の参照を差し替えます。MAのメニュー追加先参照も同じ複製へ対応付けます。

VRCLensの通常処理はファイルを必要とするため、実行中のみVRCLensの`User/NDMF_<ランダムID>`に複製と生成物を置きます。既存のUserフォルダー内のファイルは使用しません。完了後、参照される生成物をNDMFのアセット保存機構へ移し、この一時フォルダーを削除します。失敗時も一時フォルダーを削除し、元のアバター名を復元します。

ビルド用アバターではCustomizationsの子を生成された`Avatar/VRCLens`直下へ移動し、設定用オブジェクトとInstallerを除去します。VRCLens_Custom自体の処理は、その製品が提供するビルドフックに任せます。

MenuFavoritesは通常のNDMF処理より早いSDKフックでメニューを読むため、配置されている場合だけSDK順序-21000で同じ導入処理を先行実行します。この場合もNDMFのBuildContextと複製アセットを使用し、出力はメモリー上で保持した後、通常のNDMF処理で保存します。元製品のコールバックを重複実行することはありません。

## 制約

- アバター直下に有効なInstallerを1つ置きます。通常Apply済みのアバターとの重複導入は停止します。既に改変された元FXやメニューを自動で復元するツールではありません。
- 有効なHumanoid、必要な頭・手・足のボーン、正の均等スケールが必要です。
- AnimatorOverrideControllerは未対応です。
- メニューの空きとVRCLens単体追加時の同期パラメーター容量を確認します。MAなどによる追加後の総容量はSDK側でも確認してください。
- 各カスタマイズの依存関係・対応VRCLensバージョンは元製品に従います。NDMFの処理だけを実行するプレビューでは、SDKビルドフック専用のカスタマイズ処理が走らない場合があります。
- VRChat内の撮影結果は実機で確認してください。

## 配布について

このパッケージには独自コード、空のInstaller Prefab、説明書のみを含めます。VRCLens / VRCLens_Customのコード、Prefab、シェーダー、マテリアル、テクスチャ、アニメーションは含めません。型・メソッド・設定名による接続情報だけを持ち、インストール済み製品を実行時に探します。

開発プロジェクト全体、生成済みVRCLens Settings、ビルド生成物を配布物へ入れないでください。ルートの`Tools/build_release.py`は明示的なファイル許可リストからZIPを作り、Prefabに外部GUID参照がないことも検証します。UnityのInclude Dependencies付きExportは使いません。

本ツールはVRCLens / Modular Avatarの非公式アダプターです。元製品のライセンスはそれぞれの製品に適用されます。

## ソース管理と更新の公開

ソースコードは [mii443/vrclens-ma-installer](https://github.com/mii443/vrclens-ma-installer) で管理しています。リポジトリのルートがUnityパッケージのルートです。開発時は、依存ツールと購入済みアセットを導入したローカルのUnityプロジェクトに、このリポジトリを `Packages/dev.mii.vrclens-ma-installer` として配置してください。Git管理するのはこのパッケージフォルダーだけです。

1. パッケージのコードを編集し、ローカルのUnityで必要な検証を行います。
2. `package.json` の `version` と `url` のバージョンをそろえ、CHANGELOGを更新してcommit・pushします。
3. パッケージのルートで `python Tools/build_release.py` を実行すると、許可リストから配布ZIP・SHA-256・manifestを `dist/` に生成できます。
4. GitHubの **Actions → Build Release → Run workflow** を実行すると、同じ処理をクリーンな環境で行い、`v<version>` タグのReleaseを公開します。`v<version>` タグのpushでも実行できます。公開済みバージョンは上書きしません。
5. [vpm-repo](https://github.com/mii443/vpm-repo) の `source.json` に新しいReleaseのZIP URLを追加してpushすると、`https://vpm.mii.dev/index.json` が更新されます。旧バージョンのReleaseとURLは保持してください。

VPMパッケージとして配布するファイルはReleaseに添付された `dev.mii.vrclens-ma-installer-<version>.zip` です。GitHubの自動生成するSource code ZIPには開発用テスト等も含まれるため、VPMのインストールには使用しません。

## 開発・検証

`Tests/Editor/SmokeTests.cs`は利用者が用意したHumanoid Prefabで、通常適用、NDMF/MA併用、位置保存、循環メニュー、重複・無効化、元AssetsファイルのSHA-256一致を検証します。`CompatibilityTests.cs`はInstallerとアバターのPrefab Variantの保存・再読込、Lyraでの移動・改名とカスタマイズのメニュー配置を検証します。開発用テストにはLyraも必要です。テスト用有料アセットは配布しません。

```powershell
Unity.exe -batchmode -nographics -projectPath <project> -executeMethod Micro.VRCLensMA.Tests.SmokeTests.Run -vrclensTestAvatar <avatar.prefab> -logFile <test.log>
python Tools/build_release.py
```

テストは配布ZIPには含みません。プロジェクトが既にUnityで開かれている場合、`Library/VRCLensMAInstaller.test-request.txt`にテスト対象Prefabのプロジェクト相対パスを書くと、別の一時シーンで実行し、`Logs/vrclens-ma-live-results.txt`へ結果を出します。

リクエストファイルの2行目に`compat`を書くとVariant・Lyra併用テスト、`lyra`を書くとLyra併用テストだけを実行します。SDKテストはローカルのアップロード前処理を実行し、サーバーへのアップロードは行いません。カスタマイズ側の一時出力先もテスト専用フォルダーへ一時的に切り替え、完了後に復元します。

0.1.1の互換テスト（2026-10-25）では、Unity 2022.3.22f1、VRCLens 1.10.0、MA 1.18.7、NDMF 1.14.8、Lyra 1.2.1、VRCFury 1.1434.0、SDK 3.10.5で1,916チェックが成功しました。InstallerとアバターのVariantを保存・再読込してNDMF処理を行い、4つの位置と設定・追加オブジェクトの保持を確認しています。

SDK前処理ではMenuExtra、MoveDroneVertical、MenuFavorites、SmoothZoomを併用し、Lyraによる`Cameras/My Lens`への移動・改名、Customメニューの追従、ルートへの重複生成がないことを確認しました。元Assetsの1,888ファイルとLyraレイアウトのハッシュは不変でした。全カスタマイズ個別の機能やVRChat内での撮影動作までを検証したものではありません。

参考: [NDMF BuildPhase](https://ndmf.nadena.dev/api/nadena.dev.ndmf.BuildPhase.html)、[NDMF](https://ndmf.nadena.dev/)。
