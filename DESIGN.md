# MmdBlendShapeScaler — 设计文档

**版本**: v7 (NDMF 非破坏性架构)  
**日期**: 2026-05-19  

---

## 1. 目的

为 VRChat Avatar 制作者提供 **非破坏性 MMD BlendShape 缩放工具**。

**解决的问题**: 捏脸后 Avatar 的 MMD blendshape（まばたき、あ、い等）在 MMD 世界驱动到 weight=100 时，与捏脸基值叠加导致过度闭合或不自然效果。需要将 MMD blendshape 的 delta 缩放到合适的比例。

**核心能力**:
- 可视化预览所有 MMD blendshape（缩略图 + Scene View 实时 3D）
- 针对每个 MMD 形状独立设置缩放因子（0-200%）
- 构建时非破坏性应用缩放（NDMF Pass 自动克隆 mesh + 缩放 delta）

**架构**: Runtime IEditorOnly 组件 + NDMF Build Pass，完全非破坏性。

---

## 2. 用户流程

```
1. Avatar 上 Add Component → MmdBlendShapeScaler
2. Inspector 中点「Open MMD BlendShape Calibrator」
3. 拖入 Face Renderer → 点击「Scan MMD Shapes」
4. 网格视图浏览缩略图 → 点击进入详情
5. 拖动滑块 (0-200) → Scene View 实时预览
6. 调节到满意 → 关闭窗口（数据自动保存在组件上）
7. Build & Upload → NDMF 自动克隆 mesh + 缩放 delta
```

---

## 3. 技术架构

### 3.1 文件清单

```
Runtime/
  MmdBlendShapeScaler.asmdef       # 程序集定义 (Editor + Standalone)
  MmdBlendShapeScaler.cs           # IEditorOnly 组件, 存 targetRenderer + scales

Editor/
  MmdBlendShapeScaler.Editor.asmdef # 程序集定义 (Editor only)
  MmdBlendShapeScalerPlugin.cs      # NDMF Plugin 注册 (Transforming 阶段)
  MmdBlendShapeScalePass.cs         # NDMF Pass: 流式读取 → 缩放 vertices → 回写
  MmdBlendShapeScalerEditor.cs      # CustomEditor: Inspector 摘要 + 一键打开
  MmdCalibratorWindow.cs            # EditorWindow: 网格视图 + 详情视图
  MmdShapeDatabase.cs               # 64 个标准 MMD blendshape 名称数据库
  BlendShapePreviewRenderer.cs      # AnimationMode + Camera 缩略图渲染
  Vendor/                           # 差异高亮 (从 blendshape-viewer 适配)
    MmdDiffCompute.cs               # GPU AABB 差异检测
    MmdBlendShapeViewerGenerator.cs # 差异渲染引擎
    MmdDiffViewer.shader            # 差异着色器
    MmdDiffViewerRectOnly.shader    # 配合 compute shader 版
    MmdDiffCompute.compute           # ComputeShader 源码
```

### 3.2 核心类设计

```
MmdBlendShapeScaler (MonoBehaviour, IEditorOnly)
├── targetRenderer: SkinnedMeshRenderer   ← 显式引用目标 mesh
├── scales: Dictionary<string, float>     ← mmdName → scale (0.0-2.0)
├── SetScale() / GetScale() / RemoveAll()
└── ISerializationCallbackReceiver (过滤 scale=1.0)

MmdBlendShapeScalePass (NDMF Pass<>, Transforming 阶段)
├── GetComponentsInChildren(true)   ← 多 scaler 支持
├── Object.Instantiate(mesh)        ← 非破坏性克隆
├── 流式处理: 读一帧 → scale vertices → 立即 AddFrame
├── 只缩放 vertices (不缩放 normals/tangents)
└── DestroyImmediate(scaler)        ← 组件自毁

MmdCalibratorWindow (EditorWindow)
├── 网格视图: 分类折叠 + 缩略图 + 橙色标记已调节
├── 详情视图: 大缩略图 + 滑块 0-200 + 预设按钮 + Scene View 实时预览
├── 生命周期: OnDisable/OnDestroy/PlayModeChanged/AssemblyReload 全部归零
└── 自动确认模式: MouseUp 时一次性 Undo
```

### 3.3 非破坏性保证

| 保证项 | 实现 |
|--------|------|
| 原始 mesh 不修改 | `Object.Instantiate(originalMesh)` |
| 配置持久化 | Unity 序列化到 prefab |
| 构建时清理 | Pass 执行后 `DestroyImmediate(scaler)` |
| 组件不进包 | `IEditorOnly` — VRChat SDK 构建时剥离 |
| 撤销支持 | `Undo.RegisterCompleteObjectUndo` |
| 预览残留零容忍 | 6 种生命周期事件全部归零 |

### 3.4 从 make-it-mmd 学到的技术

| 技术 | 来源 | 本工具采用 |
|------|------|-----------|
| IEditorOnly 标记组件 | make-it-mmd | ✓ |
| NDMF Pass 模式 | make-it-mmd | ✓ |
| Object.Instantiate 克隆 | make-it-mmd | ✓ |
| 缩略图预览 | blendshape-viewer (Haï) | ✓ |
| 差异高亮 DiffCompute | blendshape-viewer | ✓ |
| 流式 delta 缩放 | BlendShapeMappingsPass | ✓ (改进: 只缩 vertices) |

---

## 4. 依赖

- Unity 2022.3+
- nadena.dev.ndmf >= 1.7.4
- com.vrchat.avatars >= 3.7.0 (VRC.SDKBase + VRCSDK3)
