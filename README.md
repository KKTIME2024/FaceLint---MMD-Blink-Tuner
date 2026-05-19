# VRC Avatar MMD & Blink Fixer

Fix over-closed eyes and exaggerated expressions on VRChat MMD avatars. Scale MMD blend shape deltas per shape, auto-applied non-destructively at build time via NDMF.

## How It Works

MMD worlds drive blend shapes at weight=100. If your avatar has base values on native shapes (e.g. `eye_close=20`), the actual result is `20 + 100 = 120` — over-closed eyes, over-stretched mouth.

This tool lets you scale each MMD blend shape down (or up) so the final result looks natural. Changes are applied at build time by cloning the mesh and scaling vertex deltas — your original mesh asset is never touched.

## Requirements

- Unity 2022.3+
- VRCSDK3 (com.vrchat.avatars >= 3.7.0)
- NDMF (nadena.dev.ndmf >= 1.7.4)

## Install

**Method A (recommended):** Download `.unitypackage` from [Releases](https://github.com/KKTIME2024/vrc-avatar-mmd-blink-fixer/releases), drag into Unity Project window, import all.

**Method B (manual):** Copy `Runtime/` and `Editor/` into `Assets/VrcAvatarMmdBlinkFixer/`.

## Usage

1. Add component: select your avatar → **Add Component** → **VRC Avatar MMD & Blink Fixer**
2. Drag your face/body **SkinnedMeshRenderer** into the Target Renderer field
3. Click **Open Calibrator**, or use menu **Tools → VRC Avatar MMD & Blink Fixer**
4. Click **Scan MMD Shapes** to find all MMD blend shapes on that mesh
5. Click a thumbnail to enter detail view
6. Drag the slider (0–200%) — Scene View shows live preview
7. Navigate with ← Back / ◀ Prev / Next ▶ to save and move to the next shape
8. Quick-apply: previously confirmed values appear as shortcut buttons below the slider
9. When done, close the window and **Build & Upload** — NDMF applies all scales automatically

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
