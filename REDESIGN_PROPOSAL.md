# MMD BlendShape 缩放调节器 — 重构方案 v2

**日期**: 2026-05-18  
**状态**: ✅ 已实现 — 见 `Runtime/` 和 `Editor/` 目录  
**参考**: make-it-mmd 非破坏性架构 (`E:\21_CodeRepos\make-it-mmd`)

---

## 0. 需求澄清

### 关键纠正（v1 → v2）

| v1 误解 | v2 正确理解 |
|---------|-------------|
| 从 avatar blendshape（如 `vrc.blink`）创建 MMD blendshape（如 `まばたき`） | **Avatar 上已经有 MMD blendshape**（まばたき、あ、い...），只需要缩放它 |
| 需要"源匹配"来找到对应关系 | 不需要匹配。`まばたき` 就是 `まばたき`，但 delta 需要缩放 |
| 与 make-it-mmd 功能重叠 | 完全独立。make-it-mmd **创建** MMD blendshape，本工具**缩放**已有的 |

### 使用场景

```
Avatar 上有 MMD blendshape「まばたき」(weight=100 时 delta = 闭眼 5mm)
用户捏脸后，Renderer 上 eye_close 基值 = 20
MMD 世界驱动 まばたき weight=100 时：
  实际效果 = eye_close(20) + まばたき(100) → 过度闭合 7mm

解决方案：把 まばたき 的 delta 缩放到 80%，即最终 delta = 5mm × 0.8 = 4mm
结果：eye_close(20) + 缩放的まばたき(100) → 刚好闭合 5mm ✓
```

---

## 1. 架构概览

沿用 make-it-mmd 的 NDMF 模式，但操作对象是**已有 blendshape 的 delta 数据**：

```
┌──────────────────────────┐     ┌──────────────────────────────────┐
│  MmdBlendShapeScaler      │     │  MmdBlendShapeScalePass (NDMF)    │
│  (IEditorOnly, 存缩放配置) │ ──→ │  克隆 mesh →                     │
│                          │     │  读取所有 blendshape →             │
│  まばたき → scale: 0.8   │     │  ClearBlendShapes →               │
│  笑い    → scale: 0.9   │     │  逐个 AddBlendShapeFrame →         │
│  あ      → scale: 1.0   │     │  (需要缩放的, delta × scale)       │
│                          │     │  赋值回 renderer → 销毁组件         │
└──────────────────────────┘     └──────────────────────────────────┘
```

### 为什么必须重新创建所有 blendshape

Unity Mesh API 没有 `ReplaceBlendShape` 或 `ModifyBlendShapeFrame`。只能：
1. `ClearBlendShapes()` — 清空所有
2. `AddBlendShapeFrame()` — 逐个添加

所以即使只想缩放 1 个 blendshape，也必须重写全部 blendshape。

### 非破坏性链

```
1. 用户配置阶段：MmdBlendShapeScaler 组件存 scale 值，不碰 mesh
2. 构建阶段：   Object.Instantiate(mesh) → 克隆 → 修改克隆 → 赋值
3. 清理阶段：   Object.DestroyImmediate(scaler) — 组件不进包
```

---

## 2. 文件规划

```
Assets/MmdBlendShapeScaler/
├── Runtime/
│   ├── MmdBlendShapeScaler.asmdef
│   └── MmdBlendShapeScaler.cs           # IEditorOnly 组件
│
├── Editor/
│   ├── MmdBlendShapeScaler.Editor.asmdef
│   ├── MmdBlendShapeScalerPlugin.cs     # NDMF Plugin 注册
│   ├── MmdBlendShapeScalePass.cs        # NDMF Pass: 克隆 + 缩放 + 重建
│   ├── MmdBlendShapeScalerEditor.cs     # CustomEditor: 摘要 + 一键打开
│   ├── MmdCalibratorWindow.cs           # EditorWindow (改造自旧工具)
│   ├── MmdShapeDatabase.cs              # 64 个标准 MMD 名 + 分类 (迁移)
│   └── BlendShapePreviewRenderer.cs     # 缩略图渲染 (迁移)
│
├── Vendor/                               # 从 make-it-mmd 引入差异高亮
│   ├── BlendshapeViewerDiffCompute.cs
│   ├── BlendshapeViewerGenerator.cs
│   ├── HaiBlendshapeViewer.shader
│   ├── HaiBlendshapeViewerRectOnly.shader
│   └── DiffCompute.compute
│
└── Tests/
    ├── MmdBlendShapeScaler.Tests.asmdef
    ├── MmdBlendShapeScalePassTests.cs
    └── MmdBlendShapeScalerTests.cs
```

---

## 3. 核心类设计

### 3.1 Runtime: `MmdBlendShapeScaler`

```csharp
namespace MmdBlendShapeScaler
{
    /// <summary>
    /// 存储 MMD blendshape 的缩放因子。
    /// 只记录 scale ≠ 1.0 的条目。scale = 1.0 的条目不需要存储。
    /// 
    /// ★ 关键设计：显式引用目标 SkinnedMeshRenderer。
    ///    不依赖 VRCAvatarDescriptor.VisemeSkinnedMesh —
    ///    因为 MMD blendshape 可能在 Body/Face/Head 等任意 mesh 上。
    ///    Editor Window 和 Build Pass 使用同一引用，保证数据一致性。
    /// </summary>
    [AddComponentMenu("MMD/MmdBlendShapeScaler")]
    [DisallowMultipleComponent]
    public class MmdBlendShapeScaler : MonoBehaviour, VRC.SDKBase.IEditorOnly, ISerializationCallbackReceiver
    {
        public const int CURRENT_DATA_VERSION = 1;
        
        // === 目标 Renderer（★ 关键：不依赖 VisemeSkinnedMesh）===
        [SerializeField] internal SkinnedMeshRenderer targetRenderer;
        
        // === 序列化存储 ===
        [SerializeField] internal List<MmdScaleEntry> _entries = new List<MmdScaleEntry>();
        [SerializeField] internal int dataVersion = CURRENT_DATA_VERSION;
        
        // === 运行时字典（从 _entries 还原）===
        internal Dictionary<string, float> scales; // mmdName → scale (0.0-2.0)
        
        // === API ===
        public void SetScale(string mmdName, float scale);
        public float GetScale(string mmdName);     // 默认 1.0
        public bool HasScale(string mmdName);
        public void RemoveScale(string mmdName);
        public void RemoveAll();
        public int Count { get; }
        public IEnumerable<MmdScaleEntry> GetModifiedEntries();
        public bool IsValid { get; }                // targetRenderer != null && sharedMesh != null
        
        // === 序列化回调 ===
        void OnBeforeSerialize()  { /* scales → _entries, 过滤 scale=1.0 */ }
        void OnAfterDeserialize() { /* _entries → scales */ }
    }
    
    [Serializable]
    public class MmdScaleEntry
    {
        public string name;       // "まばたき"
        public float scale;       // 0.0 - 2.0
    }
}
```

**设计要点**：
- `targetRenderer` 是架构一致性关键：Editor Window 和 Build Pass 读写同一个引用，不依赖 `VisemeSkinnedMesh`
- 只存储 `scale ≠ 1.0` 的条目，`GetScale()` 默认返回 1.0
- 数据版本号保留扩展点
- `DisallowMultipleComponent` 但 **Pass 层用 `GetComponentsInChildren(true)` 支持多个 scaler**（见 §3.2）

### 3.2 Editor: `MmdBlendShapeScalePass`

```csharp
public class MmdBlendShapeScalePass : Pass<MmdBlendShapeScalePass>
{
    public override string DisplayName => "Scale MMD BlendShapes";
    
    protected override void Execute(BuildContext context)
    {
        // ★ 遍历所有 MmdBlendShapeScaler，支持多 mesh / 多 face mesh avatar
        var scalers = context.AvatarRootObject
            .GetComponentsInChildren<MmdBlendShapeScaler>(includeInactive: true);
        
        foreach (var scaler in scalers)
        {
            if (scaler == null || scaler.Count == 0) continue;
            if (!scaler.IsValid) continue; // targetRenderer 为空或 sharedMesh 为空
            
            var renderer = scaler.targetRenderer;
            var originalMesh = renderer.sharedMesh;
            
            // ═══ 步骤 1: 克隆 mesh ═══
            // ★ 非破坏性：操作克隆体，原始 mesh 不受影响
            var meshCopy = Object.Instantiate(originalMesh);
            
            // ═══ 步骤 2: 流式处理 —— 边读边写 ═══
            // ★ 修复：不再缓存全部 FrameData。
            //    而是：清空 → 逐 blendshape 读帧 → scale if needed → 立即 AddFrame。
            //    内存：只需 3 个 working array + 可选 scaled temp。
            int blendShapeCount = originalMesh.blendShapeCount;
            int vertexCount = originalMesh.vertexCount;
            
            // 工作数组（复用，每帧覆盖）
            var deltaV = new Vector3[vertexCount];
            var deltaN = new Vector3[vertexCount];
            var deltaT = new Vector3[vertexCount];
            
            // scaled 分支使用（仅在需要缩放时分配）
            Vector3[] scaledV = null;
            
            meshCopy.ClearBlendShapes();
            
            for (int i = 0; i < blendShapeCount; i++)
            {
                string name = originalMesh.GetBlendShapeName(i);
                int frameCount = originalMesh.GetBlendShapeFrameCount(i);
                
                bool needsScale = scaler.scales.TryGetValue(name, out float scale) 
                               && Mathf.Abs(scale - 1.0f) > 0.001f;
                
                // ★ 只在需要缩放时分配 scaled 数组（惰性分配）
                if (needsScale && scaledV == null)
                    scaledV = new Vector3[vertexCount];
                
                for (int f = 0; f < frameCount; f++)
                {
                    float weight = originalMesh.GetBlendShapeFrameWeight(i, f);
                    originalMesh.GetBlendShapeFrameVertices(i, f, deltaV, deltaN, deltaT);
                    
                    if (needsScale)
                    {
                        // ★ 默认只缩放 vertices。
                        //    normals/tangents 不是 position delta —— 缩放它们
                        //    可能导致 shading exaggeration 和 specular artifact。
                        //    仅当用户通过高级选项显式开启时才缩放 normals/tangents。
                        for (int v = 0; v < vertexCount; v++)
                            scaledV[v] = deltaV[v] * scale;
                        
                        meshCopy.AddBlendShapeFrame(name, weight, scaledV, deltaN, deltaT);
                    }
                    else
                    {
                        // ★ 直接回写（deltaV 是本次 GetBlendShapeFrameVertices 的正确数据）
                        meshCopy.AddBlendShapeFrame(name, weight, deltaV, deltaN, deltaT);
                    }
                }
            }
            
            // ★ 不添加分隔线（不创建新 blendshape，不留 marker）
            
            renderer.sharedMesh = meshCopy;
            Object.DestroyImmediate(scaler);
        }
    }
}
```

**修复清单**（v1 方案中的 6 个 bug）：

| Bug | 原因 | 修复 |
|-----|------|------|
| **#1 FrameData 引用复用** | `deltaV/deltaN/deltaT` 是同一个复用数组，所有 frame 引用最后一次读取的数据 | 改为**流式处理**：读一帧 → scale → 立刻 `AddFrame`。不需要缓存 FrameData |
| **#2 Renderer 依赖 VisemeSkinnedMesh** | MMD blendshape 不一定在 viseme mesh 上 | 改用 `scaler.targetRenderer`（§3.1 已加字段） |
| **#3 GetComponentInChildren 单例** | 多 mesh / nested prefab 可能有多个 scaler | 改为 `GetComponentsInChildren(true)` 逐个处理 |
| **#4 分隔线 blendshape** | §9 已决定不需要，但代码还留着 | 移除 |
| **#5 normals/tangents 缩放** | `deltaN/tangents * scale` 不是 position delta，缩放它们导致 shading artifact | **默认只缩 vertices**。保留 normals/tangents 原值。高级选项可开启 |
| **#6 内存膨胀** | 缓存全部 FrameData → 60k 顶点 × 80 blendshape × 3 数组 = 数百 MB | **流式处理**：clear → 逐 blendshape 读帧 → 立即写。只需 3-4 个 working array |

**Preview vs Build 一致性说明**（第 7 个问题）：当前预览用 `SetBlendShapeWeight(index, sliderValue)`，假设 MMD blendshape 使用标准单帧 weight=100 格式。对于绝大多数 MMD avatar 这是成立的。多帧 blendshape 属边缘情况，已知局限，文档标注即可。

### 3.3 Editor: `MmdCalibratorWindow`（改造对照表）

| 旧行为 | 新行为 |
|--------|--------|
| 操作 `ShapeEntry` 内部列表（`sliderValue`, `appliedValue`） | 操作 `MmdBlendShapeScaler` 组件（`SetScale(name, scale/100f)`） |
| "确认缩放值" → 复制到剪贴板 | "确认缩放值" → 写入组件 + Undo |
| 无持久化 | 序列化到 prefab（通过组件） |
| 输出: `まばたき → scale:0.80` | 输出: 组件上已存 `scale:0.80`，无需剪贴板 |
| `meshIndex` 从扫描获取 | MMD blendshape 本身在扫描时确定 index，但存的是 name（名字不变） |

保留的：
- 两视图（网格 + 详情）
- 滑块 0-200，整数步进
- `BlendShapePreviewRenderer` 缩略图渲染
- Scene View 实时预览（`SetBlendShapeWeight(index, sliderValue)`）
- 分组折叠

新增的：
- **差异高亮**（Q5 选了"需要"）：集成 make-it-mmd 的 DiffCompute + HaiBlendshapeViewer shader
- 写入组件时调用 `Undo.RecordObject(scaler, "Set MMD Scale")` + `EditorUtility.SetDirty(scaler)`

### 3.4 差异高亮集成 + Vendor 冲突防护

从 make-it-mmd 引入以下文件到 `Vendor/`，**必须做防冲突处理**：

```
Vendor/
├── BlendshapeViewerDiffCompute.cs   # namespace: enitimeago... → MmdBlendShapeScaler.Vendor
├── BlendshapeViewerGenerator.cs     # 同上
├── MmdDiffViewer.shader              # ★ 重命名：HaiBlendshapeViewer → MmdDiffViewer
├── MmdDiffViewerRectOnly.shader      # ★ 重命名
└── MmdDiffCompute.compute           # ★ 重命名：DiffCompute → MmdDiffCompute
```

**必须执行的防冲突处理**：

| 冲突风险 | 确保措施 |
|----------|----------|
| 用户同时安装 make-it-mmd 和本工具 | ✅ 不共用 |
| Shader 名冲突 | 重命名为 `MmdDiffViewer` / `MmdDiffViewerRectOnly`，Shader.Find 使用新名称 |
| ComputeShader GUID 冲突 | Unity 中 Reimport → 生成新 `.meta` GUID |
| Namespace 冲突 | 从 `enitimeago.NonDestructiveMMD.vendor.BlendshapeViewer` 改为 `MmdBlendShapeScaler.Vendor` |
| asmdef 引用 | Vendor 文件编译在 Editor asmdef 内，不需要独立 asmdef |

改造 `BlendShapePreviewRenderer` 以支持 diff 模式：
- 渲染 neutral（所有 blendshape 权重 = 0）
- 对每个 MMD blendshape 渲染 100%
- 用 compute shader 找 AABB，shader 高亮包围盒
- 缩略图显示变化区域 + 淡红色标注

---

## 4. UX 交互路径详解

### 4.0 前置：组件初始化

用户在 Avatar 上 `Add Component → MmdBlendShapeScaler`。组件本身没有 Inspector UI 字段，只有一个"打开调节器"按钮（由 `MmdBlendShapeScalerEditor.cs` 提供）。

点击后打开 `MmdCalibratorWindow`。

---

### 4.1 状态 0：未扫描（初始状态）

```
┌──────────────────────────────────────────────────────────────────┐
│  MMD BlendShape 缩放调节器                                        │
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│  [面部网格: ○ (None)                        ]  [扫描 MMD 形状]    │
│                                                                  │
│  ┌──────────────────────────────────────────────────────────────┐│
│  │ ℹ 请拖入面部 SkinnedMeshRenderer（通常是 Body 网格），         ││
│  │   然后点击"扫描 MMD 形状"生成缩略图                            ││
│  └──────────────────────────────────────────────────────────────┘│
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

**UI 元素**：

| 元素 | 类型 | 行为 |
|------|------|------|
| 面部网格 | `EditorGUILayout.ObjectField` | 拖入或点选 `SkinnedMeshRenderer`。接收任何 SMR，但预期是 `VRCAvatarDescriptor.VisemeSkinnedMesh`（即 Body 网格） |
| 扫描 MMD 形状 | `GUILayout.Button` | 灰显（disabled）直到面部网格非空。点击触发扫描（见 §4.2） |

**窗口关闭行为**：状态 0 没有任何数据，直接关闭不产生任何效果。

---

### 4.2 扫描过程

用户拖入 Face Renderer 后「扫描 MMD 形状」按钮变为可用。点击后：

```
1. 验证 _faceRenderer.sharedMesh != null
2. 遍历 sharedMesh.blendShapeCount:
   - 对每个 blendshapeName，查 MmdShapeDatabase.名称到信息映射
   - 命中的加入 _entries 列表（记录 meshIndex、name、category、description）
   - 未命中的忽略（不是 MMD 形状）
3. 按 category + name 排序
4. 生成缩略图:
   - 显示进度条 "渲染缩略图 まばたき (1/35)..."
   - 逐个调用 BlendShapePreviewRenderer.Render(renderer, meshIndex, 100f, thumbnailSize)
     - 如果 showDifferences=true: 先渲染 neutral，再渲染 100%，调用 DiffCompute + diff shader 生成差异高亮缩略图
     - 如果 showDifferences=false: 直接渲染 100% 缩略图
5. 从组件加载已有 scale 值:
   - 遍历 _entries，对每个 entry.name 调用 scaler.GetScale(name)
   - 默认 100（即 1.0f），如果组件上已有值则读取
   - entry.sliderValue = savedScale × 100
6. 进入状态 1（网格视图）
```

**扫描失败处理**：
- Face Renderer 无 sharedMesh → HelpBox "所选对象没有 Mesh"
- 扫描到 0 个 MMD 形状 → HelpBox "未检测到 MMD BlendShape。该 Avatar 可能尚未进行 MMD 适配。请先使用 make-it-mmd 创建 MMD 形状后再使用本工具缩放。"

---

### 4.3 状态 1：网格视图

```
┌──────────────────────────────────────────────────────────────────┐
│  MMD BlendShape 缩放调节器                          [语言 ▼]     │
├──────────────────────────────────────────────────────────────────┤
│  [面部网格: ● Body (SkinnedMeshRenderer)    ]  [重新扫描]        │
│                                                                  │
│  共 35 个 MMD 形状  │  已调节: 3 个  │  [缩略图大小: ●━━━○ 150] │
│  [✓ 显示差异]  [□ 自动确认(调节即写入)]                           │
│                                                                  │
│  ┌─ ▼ 眼部 (18) [已调: 2] ────────────────────────────────────┐ │
│  │ ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐              │ │
│  │ │██████│ │ 图片  │ │ 图片  │ │ 图片  │ │ 图片  │              │ │
│  │ │██████│ │      │ │      │ │      │ │      │              │ │
│  │ └──────┘ └──────┘ └──────┘ └──────┘ └──────┘              │ │
│  │ まばたき   笑い     はぅ    ウインク  ウインク右              │ │
│  │ **80%**    90%     100%    100%    100%                    │ │
│  └────────────────────────────────────────────────────────────┘ │
│                                                                  │
│  ┌─ ▼ 嘴部 (11) [已调: 1] ────────────────────────────────────┐ │
│  │ ┌──────┐ ┌──────┐ ┌──────┐ ...                            │ │
│  │ │ 图片  │ │ 图片  │ │ 图片  │                               │ │
│  │ └──────┘ └──────┘ └──────┘                                 │ │
│  │ あ       い       う                                        │ │
│  │ 100%     100%    **70%**                                    │ │
│  └────────────────────────────────────────────────────────────┘ │
│                                                                  │
│  ┌─ ▼ 眉毛 (5) ───────────────────────────────────────────────┐ │
│  │ ...                                                         │ │
│  └────────────────────────────────────────────────────────────┘ │
│                                                                  │
│  ┌─ ▼ 其他 (1) ───────────────────────────────────────────────┐ │
│  │ ...                                                         │ │
│  └────────────────────────────────────────────────────────────┘ │
│                                                                  │
├──────────────────────────────────────────────────────────────────┤
│  [全部重置为 100%]                              [重新扫描]        │
└──────────────────────────────────────────────────────────────────┘
```

**UI 元素详解**：

| 元素 | 类型 | 初始值 | 行为 |
|------|------|--------|------|
| 面部网格 | ObjectField | 保持上次值 | 修改后不自动重新扫描，需手动点「重新扫描」 |
| 重新扫描 | Button | — | 弹出确认对话框（如有未写入的修改），然后回到 §4.2 |
| 共 X 个 MMD 形状 | Label | 扫描结果 | 纯显示，如"共 35 个 MMD 形状" |
| 已调节: Y 个 | Label | 0 | 统计 `sliderValue ≠ 100` 的条目数。橙色文字 |
| 缩略图大小 | IntSlider (100-300) | 150 | 拖动时**不立即重渲染**。松开后调用 `RegenerateThumbnails()` |
| 显示差异 | Toggle | ✓ 勾选 | 勾选时缩略图含红色差异高亮。切换时 `RegenerateThumbnails()` |
| 自动确认 | Toggle | □ 不勾选 | **新增选项**：勾选后调节滑块时**立即写入组件**（`SetScale` + `Undo`），不再需要手动确认。默认不勾选，保持传统"先调后确认"模式 |

**分类折叠区**（眼部/嘴部/眉毛/其他）：

每个分类是一个 `Foldout`，标题格式为 `"眼部 (18) [已调: 2]"`（方括号仅在 `已调 > 0` 时显示）。

**缩略图单元格**：

```
┌──────────────┐
│              │  ← 缩略图 (thumbnailSize × thumbnailSize)
│   [图片]     │     作为 Button 渲染
│              │     diff 模式下：差异区红色半透明蒙层
└──────────────┘
  まばたき       ← 名称 (未调节: 普通字体, 已调节: Bold)
  眨眼/Blink    ← 中文说明 (小字)
  **80%**       ← 当前 scale 值 (未调节=100%: 灰色, 已调节≠100: 橙色 Bold)
```

**单元格交互**：
- **点击缩略图** → 进入状态 2（详情视图），以该条目为 `_selectedEntry`
- **右键缩略图** → 弹出右键菜单：
  - "重置为 100%" — 将该形状 sliderValue 设为 100
  - "设为 80%" — 快捷值
  - "复制名称" — 复制 MMD 名称到剪贴板

**底部操作栏**：

| 按钮 | 行为 |
|------|------|
| 全部重置为 100% | 所有条目 `sliderValue = 100`。如果自动确认开启，同时写入组件。弹出确认对话框。 |
| 重新扫描 | 同顶部。会丢失未写入的 slider 修改（除非自动确认开启）。 |

**视觉反馈**：
- `sliderValue ≠ 100` → 名称 Bold + 数值橙色
- `sliderValue = 100` → 名称普通 + 数值灰色 100%
- 鼠标悬停缩略图 → 边框高亮 | **（style: _hasValueBackground 蓝底，类似 make-it-mmd 的设计）** — 表示该 MMD 形状存在于当前网格上

**数据流（网格视图期间）**：
- `sliderValue` 修改 → 只影响窗口内部的 `_entries` 列表，**不写入组件**
- 除非「自动确认」开启：`sliderValue` 修改 → `scaler.SetScale(name, value/100f)` + `Undo.RecordObject(scaler)` + `SetDirty`
- 关闭窗口 → 如有未确认修改，弹出对话框提示"有 X 个未确认的调节"，提供"确认并关闭"/"放弃并关闭"/"取消"

---

### 4.4 状态 2：详情视图

点击网格视图中的缩略图进入：

```
┌──────────────────────────────────────────────────────────────────┐
│  MMD BlendShape 缩放调节器                                        │
├──────────────────────────────────────────────────────────────────┤
│  [← 返回网格]  まばたき  眨眼/Blink  [◀ 上一个] [下一个 ▶]       │
│                                                                  │
│  ┌─────────────────────┐    ┌──────────────────────────────────┐ │
│  │                     │    │ 缩放因子  [━━━━━●━━━] 80 %  [80] │ │
│  │   200×200 大缩略图   │    │ 0                         200    │ │
│  │   (差异高亮)         │    │                                  │ │
│  │                     │    │ [重置 100%] [60%] [80%]           │ │
│  └─────────────────────┘    │         [120%] [150%]             │ │
│                              │                                  │ │
│  ↑ 上方是 100% 参考缩略图    │ 💡 Scene View 实时预览中          │ │
│    红色区域 = 受影响顶点     │    当前缩放: **80%** (默认 100%)  │ │
│                              │    可在 Scene View 旋转查看       │ │
│                              └──────────────────────────────────┘ │
├──────────────────────────────────────────────────────────────────┤
│            [✓ 确认缩放]  [✗ 取消]                                 │
└──────────────────────────────────────────────────────────────────┘
```

**导航栏**：

| 按钮 | 条件 | 行为 |
|------|------|------|
| ← 返回网格 | 始终可用 | 恢复当前形状预览为 0 → 返回状态 1。**不保存** sliderValue（除非自动确认模式） |
| ◀ 上一个 | `curIdx > 0` | 恢复当前形状 → 选中 `_entries[curIdx - 1]` → 预览新形状 |
| 下一个 ▶ | `curIdx < _entries.Count - 1` | 同上，选中 `_entries[curIdx + 1]` |

**大缩略图区域（左侧）**：
- 200×200（或 thumbnailSize 的 2 倍）缩略图
- 显示 100% 权重的差异高亮效果
- 红色半透明层覆盖在变化顶点区域（通过 DiffCompute + HaiBlendshapeViewer shader）
- **静态参考图**：不随滑块变化。始终显示 100% 效果，帮助用户对比"默认 MMD 驱动"和"缩放后 MMD 驱动"

**调节面板（右侧）**：

| 元素 | 类型 | 值 | 行为 |
|------|------|-----|------|
| 缩放因子标签 | Label | — | "缩放因子" |
| 滑块 | `GUILayout.HorizontalSlider` | sliderValue (0-200) | 拖动时：实时调用 `SetBlendShapeWeight(meshIndex, sliderValue)` → `SceneView.RepaintAll()` |
| 百分比显示 | Label | `"{sliderValue:F0} %"` | sliderValue≠100 时橙色 Bold |
| 数值输入框 | `GUILayout.TextField` | `"{sliderValue:F0}"` | 输入后 `float.TryParse`，clamp 到 0-200，触发同滑块效果 |

**预设按钮**：

| 按钮 | 设置值 | 说明 |
|------|--------|------|
| 重置 100% | 100 | 恢复默认 |
| 60% | 60 | 常见缩眼值 |
| 80% | 80 | 常见弱化值 |
| 120% | 120 | 常见增强值 |
| 150% | 150 | 极端增强 |

点击任意预设 → `sliderValue = value` → `SetBlendShapeWeight(meshIndex, value)` → `Repaint()`

**Scene View 实时预览**：
- 进入详情视图时：`SetBlendShapeWeight(meshIndex, sliderValue)` → 仅当前这一个 MMD 形状被临时设置
- 滑块拖动/预设点击/数值输入 → 即时更新 Renderer 权重 → `SceneView.RepaintAll()`
- 用户可以在 Scene View 中旋转、缩放、平移，从任意角度观察效果
- 退出详情视图时：`SetBlendShapeWeight(meshIndex, 0)` — 恢复为 0

**关键不变式**（从旧工具保留）：
> Renderer 上 MMD 形状的 BlendShapeWeight 永远为 0，除非详情视图正在预览。退出详情/关闭窗口时恢复为 0。

**底部操作栏**：

| 按钮 | 快捷键 | 行为 |
|------|--------|------|
| ✓ 确认缩放 | Enter / Ctrl+Enter | 将当前 sliderValue 写入组件：`scaler.SetScale(name, sliderValue/100f)` + `Undo.RecordObject(scaler)` + `EditorUtility.SetDirty(scaler)`。返回状态 1 |
| ✗ 取消 | Escape | 恢复 sliderValue 为进入详情前的值（或组件上的值）。不写入。返回状态 1 |

**确认时的数据流**：
```
确认按钮
  → scaler.SetScale("まばたき", 0.8f)
     → scales["まばたき"] = 0.8f
  → Undo.RecordObject(scaler, "Set MMD Scale まばたき=80%")
  → EditorUtility.SetDirty(scaler)
  → 返回网格视图
  → 该缩略图下方显示 **80%** (橙色 Bold)
```

---

### 4.5 状态流转图

```
                   ┌──────────┐
          窗口打开 →│  状态 0   │
                   │ 未扫描    │
                   └────┬─────┘
                        │ 点击「扫描 MMD 形状」
                        ▼
                   ┌──────────┐
          关闭窗口 ←│  状态 1   │ 关闭窗口
          (有未确认  │ 网格视图  │ (无未确认)
           时弹对话框)└────┬─────┘
                        │ 点击缩略图
                        ▼
                   ┌──────────┐
                   │  状态 2   │
                   │ 详情视图  │
                   └────┬─────┘
                        │ ← 返回网格 / 确认 / 取消 / 上一个 / 下一个
                        ▼
                   ┌──────────┐
                   │  状态 1   │
                   │ 网格视图  │
                   └──────────┘
```

---

### 4.6 自动确认模式的 Undo 优化

当「自动确认」Toggle 勾选时：

| 操作 | 正常模式 | 自动确认模式 |
|------|----------|-------------|
| 详情视图中每次滑块变动 | 不写入组件，等确认 | ★ **拖动期间不写**。仅在鼠标松开 (`EventType.MouseUp`) 时一次性写入 `scaler.SetScale(...)` |
| Undo 记录方式 | — | `Undo.RegisterCompleteObjectUndo(scaler, "Set MMD Scale まばたき=80%")` — 一次完整 Undo entry |
| 预设按钮点击 | 不写入组件，等确认 | 立即写入（单次操作，Undo 正常） |
| 点击 ✓ 确认 | 写入组件 | 已写入，等同于「← 返回网格」 |
| 点击 ✗ 取消 | 恢复旧值 | 用 `Undo.PerformUndo()` 撤销自动确认的操作 |
| 关闭窗口 | 弹未确认提示 | **无提示**，所有 sliderValue 已对应组件数据 |

**反模式防范**：❌ **绝不**在 `Slider` 的每帧 `OnGUI` 回调中调用 `Undo.RecordObject`。这会产生数百个 Undo entry，撑爆 Unity Undo 栈。

---

### 4.7 预览生命周期完整覆盖

| 触发事件 | 清理行为 |
|----------|----------|
| 退出详情视图（← 返回网格 / 上一个 / 下一个） | 当前预览的 blendshape → `SetBlendShapeWeight(meshIndex, 0f)` |
| 关闭窗口 (`OnDisable`) | 所有 MMD blendshape → `SetBlendShapeWeight(_, 0f)` |
| 窗口销毁 (`OnDestroy`) | 同 `OnDisable` |
| 进入 Play Mode | `EditorApplication.playModeStateChanged` → `EnteredPlayMode`: 全部归零 |
| 退出 Play Mode | `EditorApplication.playModeStateChanged` → `EnteredEditMode`: 全部归零（Editor 重启后权重已丢失，兜底） |
| Assembly Reload | `AssemblyReloadEvents.beforeAssemblyReload` → 全部归零 |
| 扫描 / 重新扫描 | 全部归零，再销毁旧缩略图 |

**关键不变式**：无论何种方式离开详情视图，Renderer 上 MMD blendshape 权重**永远恢复为 0**。这是保证"预览不影响 Avatar 正常状态"的底线。

---

### 4.7 组件 Inspector（MmdBlendShapeScalerEditor）

```
┌──────────────────────────────────────────────────────┐
│  Mmd BlendShape Scaler (Script)                      │
│                                                      │
│  已配置 3 个缩放:                                     │
│  まばたき      80%                                    │
│  笑い         90%                                    │
│  あ           70%                                    │
│                                                      │
│  [打开调节器]           [清除所有缩放]                  │
└──────────────────────────────────────────────────────┘
```

| 元素 | 行为 |
|------|------|
| 摘要列表 | 遍历 `scaler.scales`，逐行显示 `mmdName → scale*100%` |
| 打开调节器 | `MmdCalibratorWindow.ShowWindow(scaler)`，自动关联当前 SMR |
| 清除所有缩放 | `Undo.RecordObject(scaler)` + `scaler.scales.Clear()` + 确认对话框 |

如果 `scaler.scales.Count == 0`：摘要显示 "无缩放配置（所有 MMD 形状保持 100%）"。

---

### 4.8 构建时（NDMF，用户无感知）

用户执行 `Build & Upload`（VRChat SDK 操作）：

```
NDMF 管线触发 (BuildPhase.Transforming)
  │
  ├─ (其他 Plugin...)
  ├─ Modular Avatar 执行
  ├─ ★ MmdBlendShapeScalePass 执行
  │    ├─ GetComponentsInChildren<MmdBlendShapeScaler>(true)  ← 多 scaler
  │    ├─ 遍历每个 scaler:
  │    │   ├─ 验证 scaler.targetRenderer 有效
  │    │   ├─ Object.Instantiate(originalMesh)         ← 克隆
  │    │   ├─ meshCopy.ClearBlendShapes()
  │    │   ├─ 流式处理（边读边写，不缓存）:
  │    │   │   对每个 blendshape → 读帧 delta
  │    │   │   → 在 scaler.scales 中？逐顶点 scale deltaV（不缩 N/T）
  │    │   │   → 立即 AddBlendShapeFrame 回写
  │    │   ├─ renderer.sharedMesh = meshCopy
  │    │   └─ Object.DestroyImmediate(scaler)           ← 组件自毁
  ├─ (后续 Plugin...)
  └─ 上传
```

构建后：
- 原始 mesh 文件**未修改**（因为操作的是 `Object.Instantiate` 克隆）
- `MmdBlendShapeScaler` 组件**已销毁**（不进运行时包）
- Avatar 运行时所有 MMD blendshape 的 delta 已按用户配置缩放

---

## 5. 非破坏性对比

| 层面 | 旧工具 | 新工具 |
|------|--------|--------|
| **配置存储** | EditorWindow 临时状态 | `MmdBlendShapeScaler` 组件（Unity 序列化） |
| **配置持久化** | 关窗丢失 | 保存在 prefab/scene 上 |
| **Mesh 修改时机** | 不修改 mesh（= 无产出） | 构建时克隆 mesh → 修改克隆 → 赋值（原始 mesh 不变） |
| **构建时处理** | 无。手动复制到 make-it-mmd | NDMF Pass 自动执行 |
| **组件生命周期** | 无 | IEditorOnly — 不进运行时包 |
| **撤销支持** | 无 | `Undo.RecordObject` |

---

## 6. 实现优先级

| 优先级 | 任务 | 工时估算 | 说明 |
|--------|------|----------|------|
| P0 | `MmdBlendShapeScaler` Runtime 组件 | 小 | 数据模型 + 序列化 |
| P0 | `MmdBlendShapeScalePass` | 中 | 核心逻辑：读取 → 缩放 → 重建 |
| P0 | `MmdBlendShapeScalerPlugin` | 小 | NDMF 注册，~20 行 |
| P1 | `MmdCalibratorWindow` 改造 | 中 | 从旧窗口重构，指向组件而非内部状态 |
| P1 | `MmdBlendShapeScalerEditor` | 小 | CustomEditor：显示摘要 + 打开按钮 |
| P2 | 迁移 `MmdShapeDatabase` + `BlendShapePreviewRenderer` | 小 | 命名空间变更 |
| P2 | 差异高亮集成 | 中 | 从 make-it-mmd 引入 vendor 文件 + 改造 renderer |
| P2 | 单元测试 | 中 | Pass 测试 + 序列化测试 |

---

## 7. 从旧工具迁移的改动清单

| 文件 | 改动 |
|------|------|
| `MMDBlendShapeCheckerWindow.cs` → `MmdCalibratorWindow.cs` | 重写状态管理：`ShapeEntry` → `MmdBlendShapeScaler` |
| `MmdShapeDatabase.cs` | 命名空间 `MMDBlendShapeChecker` → `MmdBlendShapeScaler` |
| `BlendShapePreviewRenderer.cs` | 增加 diff 渲染方法；命名空间迁移 |
| `BlendShapeCalibrator.cs` | 删除（v5 算法已退役） |
| `MMDBlendShapeChecker.asmdef` | 拆分为 Runtime + Editor 两个 asmdef |
| 新增 `Vendor/` | 从 make-it-mmd 拷贝 5 个文件（diff compute + shader） |

---

## 8. 依赖

```
nadena.dev.ndmf          >= 1.7.4
com.vrchat.avatars       >= 3.7.0
Unity                    2022.3+
```

---

## 9. 已确认的设计决策

| 决策 | 结论 |
|------|------|
| 源匹配方式 | 不需要匹配。MMD blendshape 本身已有（まばたき就是まばたき），只缩放其 delta |
| Scale 精度 | 整数 0-200 滑块 |
| 与 make-it-mmd 关系 | 完全独立，不依赖 make-it-mmd。依赖 NDMF（大部分用户已安装） |
| 自动推荐 scale | 不需要，用户自己判断 |
| 差异高亮 | 需要。从 make-it-mmd 引入 DiffCompute + shader |
| scale=1.0 存储 | 不存。序列化时过滤，GetScale() 默认返回 1.0 |
| 分隔线标记 | 不需要（不创建新 blendshape） |
| 批量设置闭合类 | 不需要 |

---

## 10. 从 make-it-mmd 学到的关键技术

| 技术点 | make-it-mmd 实现 | 本工具采用 |
|--------|-----------------|-----------|
| **非破坏性 mesh 克隆** | `Object.Instantiate(originalMesh)` | ✓ 相同 |
| **NDMF Pass 模式** | `Pass<T>` + `Plugin<T>` 注册 | ✓ 相同 |
| **IEditorOnly 标记组件** | 组件不进运行时包 | ✓ 相同 |
| **构建后自毁** | `DestroyImmediate(component)` | ✓ 相同 |
| **序列化回调** | `ISerializationCallbackReceiver` + `dataVersion` | ✓ 相同 |
| **缩略图预览** | `BlendshapeViewerGenerator` (AnimationMode + Camera) | ✓ 已迁移到 `BlendShapePreviewRenderer` |
| **差异高亮** | `DiffCompute.compute` + `HaiBlendshapeViewer.shader` | ✓ 引入到 Vendor/ |
| **Delta 缩放** | `BlendShapeMappingsPass` 中 `deltaV[v] *= scale` | ✓ 相同，但操作对象是已有 blendshape |
| **Undo 支持** | `Undo.RecordObject` | ✓ 所有写入操作 |
| **命名空间隔离** | `enitimeago.NonDestructiveMMD` | `MmdBlendShapeScaler` |

---

## 11. Code Review 修复汇总

v1 方案中识别到的 10 个问题及修复状态：

| # | 问题 | 严重级 | 修复 |
|---|------|--------|------|
| 1 | **FrameData 引用复用** — deltaV/N/T 复用数组，所有 frame 指向最后读取的数据 | 🔴 严重 | 改为流式处理（读一帧 → 立即 AddFrame），不再缓存 FrameData |
| 2 | **Renderer 依赖 VisemeSkinnedMesh** — MMD blendshape 不一定在 viseme mesh 上 | 🔴 严重 | `MmdBlendShapeScaler` 新增 `targetRenderer` 字段，Pass 和 Window 使用同一引用 |
| 3 | **GetComponentInChildren 单例** — 多 mesh avatar 无法扩展 | 🟡 高 | 改为 `GetComponentsInChildren<MmdBlendShapeScaler>(true)` 逐个处理 |
| 4 | **分隔线 blendshape** — 多余的 0-weight marker 污染列表 | 🟡 高 | 移除。不创建新 blendshape，不留 marker |
| 5 | **normals/tangents 缩放** — deltaN/T 不是 position delta，缩放导致 shading artifact | 🟡 高 | 默认只缩放 vertices。用户需高级选项才缩放 N/T |
| 6 | **内存膨胀** — 缓存全部 FrameData 可达数百 MB | 🟡 高 | 流式处理：3 个复用 working array + 惰性分配的 scaledV |
| 7 | **Preview vs Build 不一致** — SetBlendShapeWeight 假设单帧 weight=100 | 🟢 中 | 已知局限，文档标注。MMD 绝大多数是单帧 100，边缘情况不影响功能 |
| 8 | **Undo 栈爆炸** — 自动确认模式下每帧 Undo.RecordObject | 🟡 高 | 改为松开鼠标时一次性 `RegisterCompleteObjectUndo` |
| 9 | **预览生命周期不完整** — 缺 AssemblyReload/PlayMode/异常路径处理 | 🟡 高 | 覆盖 6 种事件：OnDisable, OnDestroy, PlayModeStateChanged, AssemblyReloadEvents |
| 10 | **Vendor 文件冲突** — 与 make-it-mmd 同时安装时 shader/namespace/GUID 冲突 | 🟢 中 | Shader 重命名 + Namespace 重命名 + GUID 重新生成 |

**方案当前状态**：所有 🔴和 🟡 问题已在方案层面修复。实现时逐条验证。
