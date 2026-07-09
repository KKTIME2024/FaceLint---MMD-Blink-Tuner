# UX Roadmap — Independent New Features

> 状态：**全部完成** ✅（`feature/embedded-preview` branch）
> 已合入 master。

---

## ✅ P0 — 窗口内嵌实时 3D 预览

- 详情视图 200×200 RenderTexture 实时 3D 预览
- 鼠标拖拽旋转（yaw/pitch）+ 滚轮缩放
- 滑块变化实时反映
- Prev/Next 导航时 Camera 资源复用，不重建
- 离开详情视图时自动销毁 Camera + RT

---

## ✅ P1 — 网格视图搜索/过滤

- 顶部搜索框（editor toolbar style）
- 即时过滤：case-insensitive name match
- "仅已修改" toggle

---

## ✅ P1 — A/B 对比切换

- "A/B" toggle 按钮在 MMD Strength label 旁
- 切换后预览和 Scene View 都显示 100% 原始值
- 蓝色提示文字 "A/B: showing 100% (original)"

---

## ✅ P2 — 惰性缩略图渲染

- Scan() 不再渲染缩略图，只建 entries 列表
- DrawGridCell 按需渲染（EnsureThumbnail）
- 消除 3s 阻塞进度条

---

## ✅ P2 — Slider 标尺刻度

- Slider 轨道底部彩色区域：绿色（0–100），橙色（100–200）
- 100% 处灰色刻度线

---

## ✅ P3 — 概念模型文案优化

- "缩放系数 / Scale Factor / 拡大率" → "MMD 驱动强度 / MMD Strength / MMD強度"

---

## ✅ P3 — 分类级重置按钮

- 每个折叠头加 ↺ 按钮（有修改时显示）
- 弹出确认对话框，reset 该分类所有 shape 至 100%

---

## ✅ P3 — 本地 Preset 导入导出

- Export: SaveFilePanel → JSON（只有 modified 条目）
- Import: OpenFilePanel → 解析 JSON → 按 name 匹配 → 写入 component
- 支持 Undo

---

## ✅ P4 — 首次引导

- 首次进入详情视图时显示一次性 HelpBox 提示
- 用 SessionState 记录（每次 Unity session 重置）
- "💡 拖动滑块调节 MMD 驱动强度，可在预览区旋转查看效果。"

---

## Commits

| Commit | Feature |
|--------|---------|
| `f8d07bc` | Embedded 3D preview |
| `4cebafb` | Search & filter |
| `fa07528` | A/B compare |
| `be8e596` | Slider zone marks |
| `635c82e` | Lazy thumbnail rendering |
| `26964b7` | Category reset |
| `fc490d3` | Preset import/export |
| `2b7fde5` | Copy + first-time guide |
