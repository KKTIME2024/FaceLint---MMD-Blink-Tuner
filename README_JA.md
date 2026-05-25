# FaceLint - MMD & Blink Tuner

[中文](../README.md) [English](README_EN.md) [日本語]

> コードリンターのようにアバターの顔をスキャン — 目の閉じすぎやMMD表情の誇張を視覚的に検出し、スライダーで微調整。NDMFで非破壊ビルド時適用。

---

## 使い方

1. コンポーネントを追加：アバターを選択 → **Add Component** → **FaceLint → MMD & Blink Tuner**
2. アバターの顔を Target Renderer フィールドにドラッグ（**SkinnedMeshRenderer**を自動認識）
3. **Open Calibrator** をクリック、またはメニュー **Tools → FaceLint - MMD & Blink Tuner** を使用
4. 必要に応じて言語を切り替え
5. **Scan MMD Shapes** をクリックしてメッシュ上の全MMDブレンドシェイプを検出
6. 任意のサムネイルをクリックして詳細ビューに入る
7. スライダー（0–200%）をドラッグ — Scene Viewにライブプレビューが表示され、自動保存
8. ← Back / ◀ 前へ / 次へ ▶ で戻るか前/次のシェイプに移動
9. クイック適用：確認済みの値がスライダーの下にショートカットボタンとして表示されます
10. 調整後、**Re Scan** をクリック — 現在の倍率でサムネイルが再生成され、全調整を一覧で確認できます
11. 完了したらウィンドウを閉じて **Build & Upload** — NDMFがすべての倍率を自動適用します

## 仕組み

MMDワールドはブレンドシェイプを weight=100 で駆動します。アバターのネイティブシェイプに基準値（例：`eye_close=20`）があると、目が閉じすぎる結果になります。

このツールではUnity内でプレビューしながら、各MMDブレンドシェイプを縮小（または拡大）して、Sceneビューでリアルタイムに効果を確認できます。変更はビルド時にメッシュをクローンして頂点デルタを拡大縮小することで適用され、元のメッシュアセットは一切変更されません。

## インストール

**方法A（推奨）:** [Releases](https://github.com/KKTIME2024/vrc-avatar-mmd-blink-fixer/releases) から `.unitypackage` をダウンロードし、Unity Project ウィンドウにドラッグしてすべてインポート。

**方法B（手動）:** `Runtime/` と `Editor/` を `Assets/VrcAvatarMmdBlinkFixer/` にコピー。

## 要件

- Unity 2022.3+
- VRCSDK3 (com.vrchat.avatars >= 3.7.0)
- NDMF (nadena.dev.ndmf >= 1.7.4)

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
