# FaceLint - MMD & Blink Tuner

[中文](../README.md) [English] [日本語](README_JA.md)

> Like a linter for your avatar's face. Scan to visually detect over-closed eyes and exaggerated MMD expressions, then tune them with a slider. Non-destructive build-time via NDMF.

---

## Usage

1. Add component: select your avatar → **Add Component** → **FaceLint → MMD & Blink Tuner**
2. Drag the avatar's face into the Target Renderer field (auto-detects **SkinnedMeshRenderer**)
3. Click **Open Calibrator**, or use menu **Tools → FaceLint - MMD & Blink Tuner**
4. Switch language if needed
5. Click **Scan MMD Shapes** to find all MMD blend shapes on that mesh
6. Click any thumbnail to enter detail view
7. Drag the slider (0–200%) — Scene View shows live preview, auto-saves
8. Navigate with ← Back / ◀ Prev / Next ▶ to go back or move to prev/next shape
9. Quick-apply: previously confirmed values appear as shortcut buttons below the slider
10. After adjustments, click **Re Scan** — thumbnails regenerate at current scale values, letting you preview all changes at a glance
11. When done, close the window and **Build & Upload** — NDMF applies all scales automatically

## How It Works

MMD worlds drive blend shapes at weight=100. If your avatar has base values on native shapes (e.g. `eye_close=20`), the result is over-closed eyes.

This tool lets you preview in Unity, scale each MMD blend shape down (or up) and observe the result in real time in the Scene View. Changes are applied at build time by cloning the mesh and scaling vertex deltas — your original mesh asset is never touched.

## Install

**Method A (recommended):** Download `.unitypackage` from [Releases](https://github.com/KKTIME2024/vrc-avatar-mmd-blink-fixer/releases), drag into Unity Project window, import all.

**Method B (manual):** Copy `Runtime/` and `Editor/` into `Assets/VrcAvatarMmdBlinkFixer/`.

## Requirements

- Unity 2022.3+
- VRCSDK3 (com.vrchat.avatars >= 3.7.0)
- NDMF (nadena.dev.ndmf >= 1.7.4)

## Build-Time Processing

During upload, the NDMF pass:

1. Clones the target mesh (`Object.Instantiate`)
2. Clears all blend shapes
3. Re-adds every frame — vertex deltas are scaled for configured shapes, normals/tangents kept as-is
4. Assigns the clone to the renderer
5. Destroys the config component (it's `IEditorOnly`, never enters the built asset)

## Verify

Check Unity Console during Build & Upload for:

```
[MMDBlinkFixer] Execute started. Found 1 scaler(s) on avatar.
[MMDBlinkFixer] Processed 334 blendshapes for 'Body'. Scaled: 12.
[MMDBlinkFixer] Execute finished.
```

## License

Internal testing phase — not for redistribution.
