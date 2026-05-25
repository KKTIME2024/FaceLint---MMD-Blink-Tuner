# FaceLint - MMD & Blink Tuner

[中文] [English](README_EN.md) [日本語](README_JA.md)

> Like a linter for your avatar's face. Scan to visually detect over-closed eyes and exaggerated MMD expressions, then tune them with a slider. NDMF 非破坏构建时自动应用。

---
## 使用方法

1. 添加组件：选择你的模型 → **Add Component** → **FaceLint → MMD & Blink Tuner**
2. 将avatar的面部拖入 Target Renderer 字段（自动识别**SkinnedMeshRenderer**组件）
3. 点击 **Open Calibrator**，或使用菜单 **Tools → FaceLint - MMD & Blink Tuner**
4. 可以切换语言
5. 点击 **Scan MMD Shapes** 找到该网格上的所有 MMD 形态键
6. 点击任意缩略图进入详情视图
7. 拖动滑块（0–200%）——Scene View 显示实时预览，自动保存
8. 使用 ← Back / ◀ 上一个 / 下一个 ▶ 导航，返回或者移至上/下一个形态键
9. 快速应用：之前确认的值会作为快捷按钮显示在滑块下方
10. 调整完毕后，点击 **Re Scan**——缩略图会以当前缩放值重新生成，让你在一屏内预览所有调整后的效果
11. 完成后关闭窗口，**Build & Upload**——NDMF 会自动应用所有缩放

## 原理

MMD 世界以 weight=100 驱动形态键。如果你的模型在原生形态键上有基础值（例如 `eye_close=20`），会导致眼睛过度闭合。

本工具可让你在unity中预览，将每个 MMD 形态键缩小（或放大）并在scene实时观察效果。修改在构建时通过克隆网格并缩放顶点增量来应用——原始网格资产不会被修改。

## 安装

**方法 A（推荐）：** 从 [Releases](https://github.com/KKTIME2024/vrc-avatar-mmd-blink-fixer/releases) 下载 `.unitypackage`，拖入 Unity Project 窗口，全部导入。

**方法 B（手动）：** 将 `Runtime/` 和 `Editor/` 复制到 `Assets/VrcAvatarMmdBlinkFixer/`。

## 要求

- Unity 2022.3+
- VRCSDK3 (com.vrchat.avatars >= 3.7.0)
- NDMF (nadena.dev.ndmf >= 1.7.4)

## 构建时处理

上传时，NDMF pass 会：

1. 克隆目标网格（`Object.Instantiate`）
2. 清除所有形态键
3. 重新添加每一帧——已配置形态键的顶点增量被缩放，法线/切线保持不变
4. 将克隆网格分配给渲染器
5. 销毁配置组件（它是 `IEditorOnly`，不会进入构建资产）

## 验证

在 Build & Upload 期间检查 Unity Console：

```
[MMDBlinkFixer] Execute started. Found 1 scaler(s) on avatar.
[MMDBlinkFixer] Processed 334 blendshapes for 'Body'. Scaled: 12.
[MMDBlinkFixer] Execute finished.
```

## License

内测阶段，请勿分发。
