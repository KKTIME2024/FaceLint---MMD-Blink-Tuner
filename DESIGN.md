# MMD BlendShape 调节器 — 设计文档

**版本**: v6  
**日期**: 2026-05-18  
**状态**: 当前版本

---

## 1. 目的

为 VRChat Avatar 制作者提供 MMD BlendShape 缩放因子的可视化调节工具。

**解决的问题**: MMD 世界驱动会将 BlendShape 推至 weight=100。但 Avatar 的 native 形状（eye_close 等）已有非零基值，两者叠加导致过度闭合或其他不自然效果。需要为每个 MMD 形状确定合适的缩放因子（如まばたき→80%），供 make-it-mmd 插件使用。

**不做的事**: 不自动检测"过闭合"。不修改 Avatar 的 Mesh 资产或 Renderer 权重。

---

## 2. UX 设计

### 2.1 两视图架构

| 视图 | 入口 | 功能 |
|---|---|---|
| **网格视图** | 默认 | 所有 MMD 形状的 100% 缩略图，按眼/嘴/眉分组显示。每个缩略图下方显示形状名、中文说明、当前缩放值 |
| **详情视图** | 点击缩略图 | 选中形状的参考缩略图 + 缩放滑块 (0-200) + 预设按钮。Scene View 中实时 3D 预览 |

### 2.2 用户流程

```
打开工具 → 拖入面部 SkinnedMeshRenderer → 点击「扫描 MMD 形状」
  → 等待 ~3 秒（64 个形状的缩略图渲染，有进度条）
  → 网格视图展示所有 MMD 形状

浏览缩略图 → 发现某个形状效果不佳 → 点击缩略图
  → 进入详情视图
  → Scene View 实时显示该形状在当前缩放值下的 3D 效果
  → 拖动滑块 (0-200) 或点击预设按钮 (60%/80%/120%/150%)
  → Scene View 即时更新
  → 可旋转/缩放/平移 Scene View 从任意角度检查

点击「← 返回网格」或「上一个/下一个」→ 当前形状 Renderer 权重恢复为 0
  → 返回网格，缩放值保留在滑块中（橙色高亮 = 已调节）

重复调节其他形状

点击「确认缩放值」→ 缩放因子记录到 appliedValue
  → make-it-mmd 配置文本复制到剪贴板（格式: "まばたき → scale:0.80"）
  → 弹出对话框显示完整输出
```

### 2.3 视觉反馈

| 状态 | 视觉表现 |
|---|---|
| 未调节 (slider=100) | 白色文字，灰色背景 |
| 已调节 (slider≠100) | 粗体名称，橙色数值，橙色微背景 |
| 已确认 (applied=slider≠100) | 粗体名称，橙色数值（无"已改"标记） |

### 2.4 操作按钮

| 按钮 | 位置 | 功能 |
|---|---|---|
| 扫描 MMD 形状 | 顶部 | 读取网格，生成 64 个 100% 缩略图 |
| 确认缩放值 | 网格底部 / 详情底部 | 记录当前 scale，输出 make-it-mmd 配置到剪贴板 |
| 全部重置为 100% | 网格底部 | 所有 slider 恢复 100 |
| 重新扫描 | 网格底部 | 重新生成缩略图（未确认的更改丢失） |
| ← 返回网格 | 详情顶部 | 退出详情视图 |
| ◀ 上一个 / 下一个 ▶ | 详情顶部 | 快速切换形状 |
| 重置 100% | 详情 | 当前形状恢复 100 |
| 60%/80%/120%/150% | 详情 | 快捷预设值 |

---

## 3. 技术架构

### 3.1 文件清单

```
Assets/MMDBlendShapeChecker/Editor/
├── MMDBlendShapeChecker.asmdef          # Assembly Definition (Editor only)
├── MmdShapeDatabase.cs                  # 64 个标准 MMD 形状名 + 分类 + 中英文
├── BlendShapeCalibrator.cs              # v4-v5 自动校准算法 (已退役，保留参考)
├── BlendShapePreviewRenderer.cs         # 缩略图渲染引擎
└── MMDBlendShapeCheckerWindow.cs        # 调节器 UI (EditorWindow)
```

### 3.2 类结构

```
MmdShapeDatabase (static)
├── MmdShapeCategory (Flags enum: 眼部 | 嘴部 | 眉毛)
├── MmdShapeInfo (日文名, 中文说明, 分类, 是否闭合类)
├── 标准形状列表: List<MmdShapeInfo> (64)
└── 名称到信息映射: Dictionary<string, MmdShapeInfo>

BlendShapePreviewRenderer (static)
└── Render(SkinnedMeshRenderer, int blendshapeIndex, float weight, int size) → Texture2D
    ├── 拷贝 Scene View 相机位姿 → 创建临时 Camera
    ├── 创建 AnimationClip (blendShape.{name} = weight)
    ├── AnimationMode.SampleAnimationClip → 临时激活
    ├── Camera.Render → RenderTexture → ReadPixels → Texture2D
    └── 清理临时对象

MMDBlendShapeCheckerWindow : EditorWindow
├── ShapeEntry (内部类): name, description, category, meshIndex, sliderValue, appliedValue, thumbnail
├── List<ShapeEntry> _entries
├── ShapeEntry _selectedEntry (null = grid 模式, non-null = detail 模式)
└── 核心方法:
    ├── ScanAndGenerateThumbnails() — 扫描网格 + 生成 64 个缩略图
    ├── DrawGridView() / DrawDetailView() — 两视图渲染
    ├── SelectEntry() / DeselectCurrent() — 选中/取消
    ├── PreviewOnMesh() — SetBlendShapeWeight(临时值)
    ├── RestoreAllWeights() — 所有 MMD 形状恢复为 0
    └── ApplyAllChanges() — 记录 scale + 输出剪贴板
```

### 3.3 数据模型

```
ShapeEntry
├── name: string               # 日文名 (e.g. "まばたき")
├── description: string        # 中文说明 (e.g. "眨眼/Blink")
├── category: MmdShapeCategory # 眼部 | 嘴部 | 眉毛
├── meshIndex: int             # 在 sharedMesh.blendShapeCount 中的索引
├── sliderValue: float         # 当前滑块值 (0-200), 默认 100
├── appliedValue: float        # 上次确认的值, 默认 100
├── thumbnail: Texture2D       # 100% 缩略图
├── isModified: bool           # sliderValue ≠ appliedValue (未确认的更改)
└── isDirty: bool              # sliderValue ≠ 100 (有调节)
```

### 3.4 渲染管线

```
BlendShapePreviewRenderer.Render(renderer, index, weight=100, size=100)
│
├─ 1. 读取 SceneView.lastActiveSceneView.camera
│     └─ 拷贝: position, rotation, FOV, orthographic, clip planes
│
├─ 2. 创建临时 Camera (HideFlags.HideAndDontSave)
│     └─ GameObject("__MMD_BS_Preview_Cam__")
│
├─ 3. 创建 AnimationClip (HideFlags.HideAndDontSave)
│     └─ EditorCurveBinding: type=SkinnedMeshRenderer, property="blendShape.{name}"
│     └─ AnimationCurve.Constant(0, 1/60f, weight)
│
├─ 4. AnimationMode 临时采样
│     ├─ StartAnimationMode()
│     ├─ SampleAnimationClip(gameObject, clip, 1/60f)
│     └─ (此时 renderer 临时处于目标权重)
│
├─ 5. 渲染到纹理
│     ├─ RenderTexture.GetTemporary(size, size, 24)
│     ├─ camera.targetTexture = rt
│     ├─ camera.Render()
│     ├─ new Texture2D(size, size, RGB24)
│     ├─ ReadPixels(rt) + Apply()
│     └─ RenderTexture.ReleaseTemporary(rt)
│
├─ 6. 清理
│     ├─ AnimationMode.StopAnimationMode()
│     ├─ DestroyImmediate(clip)
│     └─ DestroyImmediate(camGo)
│
└─ 返回 Texture2D (调用者负责 DestroyImmediate)
```

### 3.5 Scene View 实时预览

```
详情视图中:
  slider 变化 → PreviewOnMesh(entry)
    → _faceRenderer.SetBlendShapeWeight(entry.meshIndex, entry.sliderValue)
    → SceneView.RepaintAll()

选中切换 / 返回网格 / 关闭窗口:
  → _faceRenderer.SetBlendShapeWeight(entry.meshIndex, 0f)
  → Renderer 恢复到 Avatar 默认状态

关键不变式:
  "Renderer 上 MMD 形状的权重永远为 0（除非正在预览）"
```

### 3.6 非破坏性保证

| 操作 | 是否修改 Renderer | 是否修改 Mesh 资产 |
|---|---|---|
| 预览 (slider 拖动) | 是 (临时, 立即恢复) | 否 |
| 确认缩放值 (Apply) | 否 | 否 |
| 全部重置 | 否 | 否 |
| 关闭窗口 | 恢复所有 MMD → 0 | 否 |

---

## 4. 当前局限

1. **缩略图依赖 Scene View**: 如果 Scene View 未打开或没有活跃相机，`BlendShapePreviewRenderer.Render` 返回 null，网格不显示缩略图
2. **缩略图是静态快照**: 以 100% 渲染，不随滑块变化。详情视图中的大缩略图也是 100% 参考，实时预览依赖 Scene View
3. **无 Scene View 自动对焦**: 预览时不自动调整 Scene View 相机角度去框住面部区域
4. **输出仅支持 make-it-mmd 格式**: 剪贴板输出的文本格式硬编码为 `{name} → scale:{value:F2}`
5. **缩放因子与 Renderer 权重解耦**: 工具的 scale 值不会应用到 Renderer。用户需要在 make-it-mmd 中手动配置这些 scale 值
6. **无配置持久化**: 关闭窗口后 scale 数据丢失。重新扫描会重置所有 slider 为 100

---

## 5. 依赖

- Unity Editor API (`UnityEditor`)
- `AnimationMode` — 临时 BlendShape 采样
- `SceneView.lastActiveSceneView` — 相机参数来源
- 无第三方依赖
- 无 Runtime 代码（Editor-only, asmdef: Editor platform）
