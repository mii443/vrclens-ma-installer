# Changelog

## 0.1.3

- LyraでVRCLensを移動・改名すると、Toggle型カスタマイズのCustomメニューが元の階層へ別に生成される問題を修正。
- VRCFury FullControllerに加え、Toggleのメニュー追加先もビルド用コピー上で追従。元のカスタマイズPrefab・Lyraレイアウトは変更しません。
- FarClipPlaneを含むSDK回帰テストと、バッチモードでの互換テスト選択に対応。

## 0.1.2

- 専用リポジトリ `mii443/vrclens-ma-installer` でソースとReleaseを管理。
- VCCリポジトリの案内URLを `https://vpm.mii.dev/index.json` に更新。
- 許可リストによる配布ZIPの作成とGitHub ActionsでのRelease公開に対応。
- Installer本体のC#コード・Prefabの動作変更はありません。

## 0.1.1

- Lyra Menu Manager用Item Proxyの設定ボタンを追加。
- LyraでVRCLensを移動・改名した後も、カスタマイズのVRCFuryメニュー追加先を追従。
- 共有サブメニューを含むVRCLensメニューの追跡に対応。
- Prefab Variantへの参照・プレビュー変更の保存を明示化。
- InstallerとアバターのVariant、Lyra、カスタマイズ併用のSDK統合テストを追加。

## 0.1.0

- VRCLens 1.10.0をNDMFビルド時に自動適用。
- 元のメニュー・パラメーター・アニメーションの複製と一時生成物の移管。
- 空のInstaller Prefab、設定Inspector、カスタマイズ配置先を追加。
- 有料アセットを含めない許可リスト方式の配布ZIP生成。
