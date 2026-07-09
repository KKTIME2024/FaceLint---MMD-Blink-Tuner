# CLAUDE.md — FaceLint

Unity 2022.3+ editor tool for VRChat avatar MMD blendshape tuning.

## Project structure
```
Runtime/   — MmdBlendShapeScaler component (IEditorOnly), asmdef
Editor/    — EditorWindow, NDMF Pass, thumbnail renderer, strings, asmdef
```

## Key conventions
- **Trilingual UI**: `Strings.cs` has ~60 fields × 3 languages (EN/ZH/JA). Always update all 4 locations: field declaration + En + ZhCN + Ja.
- **asmdef split**: Runtime asmdef must NOT reference Editor types. Editor asmdef references Runtime.
- **NDMF pattern**: Component is `IEditorOnly` — stored on prefab, destroyed at build time by `MmdBlendShapeScalePass`.
- **Sliders are 0-200 int** (displayed as %), stored as `float scale = sliderValue / 100f` in `[0.0, 2.0]`.
- **Blendshape preview**: `SetBlendShapeWeight` in Edit Mode is transient — always restore to 0 on exit. See `RestoreAllWeights()`.
- **Commit style**: `feat:`, `fix:`, `docs:`, `chore:` prefixes. Co-authored footer.

## Branch flow
- `master` — stable, merge via feature branches
- `feature/xxx` — one branch per feature, merge back to master

## Dependencies
- Unity 2022.3+
- `nadena.dev.ndmf >= 1.7.4`
- `com.vrchat.avatars >= 3.7.0`

## UX principles for this tool
- Two-view pattern: grid (browse) → detail (tune)
- Auto-confirm: slider changes save on navigation, no explicit "save" step
- Preview zero tolerance: blendshape weights always 0 outside detail view
- Orbitable 3D preview in detail view (RenderTexture + instance Camera)
