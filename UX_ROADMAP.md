# UX Roadmap — Independent New Features

> 记录 `feature/link-blink-shapes` 分支范围之外的独立 UX 改进。按优先级排列。
> 每个条目包含：目标、设计思路、预估工作量、前置条件。

---

## P0 — 窗口内嵌实时 3D 预览

**状态：** ✅ 已实现（`feature/embedded-preview` commit `f8d07bc`）

**实现内容：**
- 详情视图右侧 200×200 RenderTexture 实时 3D 预览
- 鼠标拖拽旋转（yaw/pitch）+ 滚轮缩放
- 滑块变化实时反映在预览中
- Prev/Next 导航时 Camera 资源保持，不重建
- 离开详情视图时自动销毁 Camera + RT

---

## P1 — 网格视图搜索/过滤

**当前问题：** 64 个 shape 分布在 4 个分类中，用户需要视觉扫描 + 滚动。

**目标：** 顶部搜索框 + 快速过滤。

**设计方案：**
- 搜索框在网格视图顶部：`EditorGUILayout.TextField(S.Search, _searchFilter, EditorStyles.toolbarSearchField)`
- 过滤逻辑：`_entries.Where(e => string.IsNullOrEmpty(_searchFilter) || e.name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))`
- 额外过滤按钮："只显示已调节"（`_showOnlyModified` toggle）

**交互：**
- 输入即时过滤（每次 Repaint 时执行 LINQ Where）
- 清空搜索框 → 显示全部
- 搜索高亮：匹配到的文字在 shape name 上加色

**工作量：** S（< 半天）

**前置条件：** 无。

---

## P1 — A/B 对比切换

**当前问题：** 用户调节后无法直观对比"100% vs 77%"的效果差异。

**目标：** 在详情视图添加长按切换功能，临时切换回 100% 查看原始效果。

**设计方案：**

方案 A（推荐 — 最简单）：
- 在 slider 旁边加一个锁按钮 "100%"：按住鼠标时 `PreviewOnMesh` 显示 100%，松开恢复当前值
- Unity IMGUI 原生不支持 `Button` 的按下/释放分离，但可以用 `Repaint` + `Event.current.type` 检测

方案 B：
- 快捷键 `B` 键切换：在 `OnGUI` 中检查 `Event.current.keyCode == KeyCode.B`，切换 `_isComparing` 状态
- 比较模式时，slider 标签和缩略图显示对比值

**工作量：** S（方案 A < 2 小时）

**前置条件：** 无。

---

## P2 — 惰性缩略图渲染 + 缓存

**当前问题：** 修改缩略图大小/zoom 后触发全量重新扫描，进度条阻塞 Editor 约 3 秒。

**目标：** 只渲染视口可见的 shape，滚动/参数变化时增量更新。

**设计方案：**
- 扫描时只记录条目，不渲染缩略图
- 在 `DrawCategoryGrid` 中，只对在 `_scrollPos` 可视范围内的 cell 调用 `EnsureThumbnail(entry)`
- `EnsureThumbnail`：如果 `entry.thumbnail == null`，渲染并缓存
- 缓存失效：缩略图大小/zoom 变化时，清空所有 `entry.thumbnail`（但保持 `_entries` 列表不变）

```csharp
private void EnsureThumbnail(ShapeEntry entry)
{
    if (entry.thumbnail != null) return;
    // 只渲染这一个，不阻塞
    entry.thumbnail = BlendShapePreviewRenderer.Render(
        _faceRenderer, entry.meshIndex, entry.sliderValue, _thumbnailSize);
}
```

**优化：** 滚动过程中如果有新 entry 进入视口但来不及同步渲染，先显示一个占位色块。

**工作量：** M（1 天）

**前置条件：** 扫描流程需要拆分为"扫描 + 渲染"两个独立阶段。

---

## P2 — Slider 标尺刻度

**当前问题：** Slider 是一条白线，用户不知道 0表示完全关闭 MMD 驱动，100是原始 MMD 值。

**目标：** 在 Slider 轨道上做视觉区分。

**设计方案：**
- 最简实现：在 Slider 前后加 label 注释
  - 左侧小字 "0 — 关闭 MMD"
  - 右侧小字 "200 — 增强"
  - 100 处做标记
- 进阶：用 `EditorGUI.DrawRect` 在 Slider 轨道上画分色
  - 0–100: 半透明绿（安全区）
  - 100–200: 半透明橙（增强区）

**工作量：** S（< 2 小时）

**前置条件：** 无。

---

## P3 — 概念模型文案优化

**当前问题：** "缩放因子" + "100%" 对非技术用户不直观。用户知道"眼睛闭太过了"，但不懂"缩放 delta"。

**目标：** 用更贴近用户认知的文案描述操作语义。

**具体改动：**
- "缩放因子" → "MMD 驱动强度" / "MMD Strength"
- 百分比显示增加语义标签：
  - 0% → "Muted (0%)"
  - 50% → "Half (50%)"
  - 100% → "Original (100%)"
  - 150% → "Strong (150%)"

**影响范围：** `Strings.cs` 中的 `ScaleFactor` 字段。

**工作量：** XS（改一行字符串）

**前置条件：** 需要确认是否支持三种语言各自调整。

---

## P3 — 分类级重置按钮

**当前问题：** 只有"全部重置为 100%"的核选项。

**目标：** 每个分类折叠头加一个重置按钮。

**设计方案：**
```
┌─ ▼ 眼部 (18) [已调: 2]    [↺] ─────────────────┐
```

点击 `↺` 弹出确认："重置所有眼部 MMD 形状至 100%？"

**代码位置：** `DrawCategoryGrid` 中的 foldout label 行，在右侧加一个小按钮。

**工作量：** XS（< 1 小时）

**前置条件：** 无。

---

## P3 — 本地 Preset 导入导出

**当前问题：** 多个 avatar 需要各自的配置，无法复用。

**目标：** 支持将当前 MMD 缩放配置导出为 JSON，导入到另一个 avatar。

**设计方案：**
- 序列化格式：`List<MmdScaleEntry>` 的 JSON（`JsonUtility.ToJson`）
- 导出按钮在底部栏："导出配置..."
- 导入按钮在底部栏："导入配置..."
- 导入时校验 name 是否存在于当前 mesh 的 blendshape 列表中

```json
[
    {"name": "まばたき", "scale": 0.77},
    {"name": "ウィンク", "scale": 0.77},
    {"name": "あ", "scale": 1.0}
]
```

**工作量：** S（< 半天）

**前置条件：** 无。

---

## P4 — Undo 预览 / 首次引导

**当前问题：** Preview 状态不在 Undo 系统中，误操作不能 Ctrl+Z。

**设计方案：**
- 滑块操作在鼠标松开时记录当前值到 `_lastSavedSliderValue`
- Escape 键恢复 `_lastSavedSliderValue`

**首次引导：**
- 扫描完成后的第一次进入详情视图时，显示一次性的 tooltip：
  "💡 拖动滑块调节 MMD 驱动强度，在 Scene View 中观察效果"
- 用 `SessionState` 或 `EditorPrefs` 记录是否已展示

**工作量：** S（< 半天）

**前置条件：** 无。

---

## 汇总

| 优先级 | 功能 | 工作量 | 依赖 |
|--------|------|--------|------|
| P0 | 窗口内嵌 3D 预览 | M | 无 |
| P1 | 搜索/过滤 | S | 无 |
| P1 | A/B 对比切换 | S | 无 |
| P2 | 惰性缩略图渲染 | M | 扫描 → 渲染分阶段 |
| P2 | Slider 标尺 | S | 无 |
| P3 | 文案优化 | XS | 无 |
| P3 | 分类重置 | XS | 无 |
| P3 | Preset 导入导出 | S | 无 |
| P4 | Undo 预览 / 引导 | S | 无 |

**推荐实施顺序：** P0 → P1（任意顺序）→ P2 → P3 → P4

每个功能独立，互不依赖，可选择性实现。
