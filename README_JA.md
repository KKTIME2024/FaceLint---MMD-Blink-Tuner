# VRC Avatar MMD & Blink Fixer

[中文](../README.md) [English](README_EN.md) [日本語]

> VRChatのMMDアバターで、目が閉じすぎたり表情が誇張される問題を修正します。MMDブレンドシェイプのデルタを形状ごとに拡大縮小し、NDMFでビルド時に非破壊的に自動適用します。

---

## 仕組み

MMDワールドはブレンドシェイプを weight=100 で駆動します。アバターのネイティブシェイプに基準値（例：`eye_close=20`）があると、実際の結果は `20 + 100 = 120` になり、目が閉じすぎたり口が伸びすぎたりします。

このツールでは各MMDブレンドシェイプを縮小（または拡大）して、最終的な見た目を自然に調整できます。変更はビルド時にメッシュをクローンして頂点デルタを拡大縮小することで適用され、元のメッシュアセットは一切変更されません。

## 要件

- Unity 2022.3+
- VRCSDK3 (com.vrchat.avatars >= 3.7.0)
- NDMF (nadena.dev.ndmf >= 1.7.4)

## インストール

**方法A（推奨）:** [Releases](https://github.com/KKTIME2024/vrc-avatar-mmd-blink-fixer/releases) から `.unitypackage` をダウンロードし、Unity Project ウィンドウにドラッグしてすべてインポート。

**方法B（手動）:** `Runtime/` と `Editor/` を `Assets/VrcAvatarMmdBlinkFixer/` にコピー。

## 使い方

1. コンポーネントを追加：アバターを選択 → **Add Component** → **VRC Avatar MMD & Blink Fixer**
2. 顔/体の **SkinnedMeshRenderer** を Target Renderer フィールドにドラッグ
3. **Open Calibrator** をクリック、またはメニュー **Tools → VRC Avatar MMD & Blink Fixer** を使用
4. **Scan MMD Shapes** をクリックしてメッシュ上の全MMDブレンドシェイプを検出
5. サムネイルをクリックして詳細ビューに入る
6. スライダー（0–200%）をドラッグ — Scene View にライブプレビューが表示されます
7. ← Back / ◀ 前へ / 次へ ▶ で保存して次のシェイプに移動
8. クイック適用：確認済みの値がスライダーの下にショートカットボタンとして表示されます
9. 完了したらウィンドウを閉じて **Build & Upload** — NDMFがすべての倍率を自動適用します

## ビルド時の処理

アップロード時にNDMFパスが以下を実行します：

1. 対象メッシュをクローン (`Object.Instantiate`)
2. すべてのブレンドシェイプをクリア
3. 全フレームを再追加 — 設定されたシェイプの頂点デルタが拡大縮小され、法線/接線はそのまま
4. クローンをレンダラーに割り当て
5. 設定コンポーネントを破棄（`IEditorOnly` のためビルドには含まれない）

## 確認方法

Build & Upload 中に Unity Console で以下を確認してください：

```
[MMDBlinkFixer] Execute started. Found 1 scaler(s) on avatar.
[MMDBlinkFixer] Processed 334 blendshapes for 'Body'. Scaled: 12.
[MMDBlinkFixer] Execute finished.
```

## ライセンス

内部テスト段階 — 再配布禁止。
