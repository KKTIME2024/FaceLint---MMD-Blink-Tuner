# FaceLint — 技术笔记（工程踩坑 / 已验证事实）

> 本文件记录**已验证过的事实**与**踩坑结论**，供后续开发直接引用，避免重复调查。
> 每条都标注了验证方式（源码核对 / 实测 / 推测）。设计决策见 `DESIGN_SCULPT_BASE.md`。

---

## 1. FBX 二进制解析（RINDO.fbx 实测，2026-08-15）

**结论：FBX 7400 文件的记录头是 u32 版（13 字节），不是文档常见的 u64 版（17 字节）。**

```
[EndOffset u32][numProps u32][plen u32][nameLen u8][name][props...]
```

- 文件头：23 字节 `"Kaydara FBX Binary  \x00\x1a\x00"` + u32 版本号（7400）+ 首记录（无 null record）
- **EndOffset 是排除式**（节点数据结束位置 = 下一个兄弟的起始）
- 每个节点的子列表以 null record（全 0 的 13 字节）终止；**解析器遇到 null record 必须 break**，不能继续
- 文件尾部：最后的 null record（13 字节）+ 167 字节 footer（含经典 magic `FA BC AB 09 D0 C8 D4 66 B1 76 FB 83 1C F7 26 7E`）
- 属性类型码：`Y/C/I/F/D/L/S/R` + 数组 `f/d/i/l/b/c`；zlib 压缩数组：`arrLen u32 + enc u32 + compLen u32 + [uncompLen u32] + data`，enc=1 时用 zlib（.NET 的 `ZLibStream` 直接解）
- ⚠️ 字符串属性存在**非标准填充**：RINDO.fbx 里 `"Armature"` 的 S 属性长度字段为 23（含后续字节），plen 字段与标准 props 长度对不上——**手写解析器在深层嵌套会失准**，shape 级 delta 数据未能可靠提取（验证方式：逐字节人工核对 + 解析器顶层对齐、深层错位）
- 混叠特征：顶层结构（FBXHeaderExtension→FileId→CreationTime→Creator→GlobalSettings→Documents→References→Definitions→Objects→Connections→Takes）全部对齐验证 ✓

**教训**：手写 FBX 解析器性价比低。下次需要 shape 级数据时，优先用 Unity 的 FBX SDK / Python fbxpye，或在 Unity 工程里用 `AssetDatabase.LoadAllAssetsAtPath` 直接读导入后的 mesh（`mesh.GetBlendShapeFrameVertices`）。

---

## 2. Unity / NDMF / VRC SDK API（对照源码逐项核实）

| 事实 | 验证方式 |
|---|---|
| NDMF 1.11 `Pass<T>`：`protected abstract void Execute(BuildContext context)`，`DisplayName` 属性 | `nadena.dev.ndmf/Editor/API/Fluent/Pass.cs` 源码 |
| `BuildContext.AvatarDescriptor`（VRCAvatarDescriptor）存在 | `VRChatBuildContextExtensions.cs` 源码 |
| `VRCAvatarDescriptor.baseAnimationLayers`（CustomAnimLayer[]）、`customizeAnimationLayers`、`CustomAnimLayer.animatorController`（RuntimeAnimatorController）、`isDefault` | `com.vrchat.avatars` SDK 3.10.2 Editor 源码用法核实 |
| VRChat 层约定：0=Base 1=Additive 2=Gesture 3=Action 4=FX；Custom Expressions 开启时 FX 属商品 | SDK 惯例 + 设计决策 |
| `Object.Instantiate(controller)` + 按需 `Object.Instantiate(clip)` 克隆改写 = AAO Write Defaults pass 同款模式，构建上下文可用 | AAO 先例 |
| `Mesh.ClearBlendShapes()` 只清 shape 数据，**不动 vertices**——烤入顶点后重建 shape 是安全的 | Unity API 语义 |
| `BlendShapePreviewRenderer.Render` 用 `AnimationMode.SampleAnimationClip` 驱动权重，**不触碰渲染器真实权重**——校准器扫描不需要先归零权重 | 代码审查 |

---

## 3. C# / Unity 编译坑

- **CS0104 `Object` 二义**：`using System;` + `using UnityEngine;` 并存时 `Object.Instantiate(...)` 报 ambiguous。
  修复：`using Object = UnityEngine.Object;`（`MmdBlendShapeScalePass.cs` 已加）。这类语义错误**语法检查抓不到**，只能真编译。
- 类名用中文标识符（`MmdShapeDatabase.标准形状列表`、`MmdShapeCategory.未知`）合法且全仓一致，别改。
- `[Flags] MmdShapeCategory { 未知=0, 嘴部=1, 眼部=2, 眉毛=4 }`——网格视图用 `category == 未知` 特判（`& 未知` 永远为 0）。

---

## 4. 无 Unity 环境下的本地验证方法

**Roslyn 语法级检查**（能抓 v7.1 那类"namespace 外孤儿代码"）：
- 用本机 dotnet SDK 自带的 Roslyn：`C:\Program Files\dotnet\sdk\<ver>\Roslyn\bincore\Microsoft.CodeAnalysis.CSharp.dll`
- 小型 console 工程：`CSharpSyntaxTree.ParseText` 每个 `.cs`，报告 `DiagnosticSeverity.Error`（参考 `%TEMP%\fl-cscheck`）
- 局限：**只查语法**，查不了 API/语义错误（CS0104 就是漏网之鱼）——最终仍以 Unity 编译为准

**API 核对的正确姿势**：客户/测试工程里常有真实包源码（NDMF、VRC SDK），只读 grep 用法即可确认字段/方法存在性，无需搭工程编译。

---

## 5. 校准器权重生命周期（v0.6.1 修复，数据安全）

**背景**：渲染器 blendshape 权重 = 捏脸数据（Pass A 构建时读取）。旧代码在校准器扫描/选择/关闭时把权重**归零且不恢复**——开一次窗口捏脸就没了。

**模式（`MmdCalibratorWindow.cs`）**：
- `Scan()` 与渲染器切换时 `SnapshotBaseWeights()` 快照全部权重（绑定 renderer InstanceID）
- 所有恢复路径（关窗/播放模式/程序集重载/取消选择）`RestoreAllWeights()` **恢复快照而非归零**
- 预览（`PreviewOnMesh`）临时写权重，取消/切换时恢复该条目的基值
- 快照防串：`_baseWeightsRendererId` 不匹配时先重新快照，**绝不拿旧渲染器的索引写新渲染器**

---

## 6. Pass B 门限计算（v0.6.2，性能与数值）

- `CollectSculpt` 预计算每个捏脸 shape 的 `len2 = |D_j|²`（SculptEntry.len2），α 计算时不再重复求和
- α 的复杂度：O(捏脸数 × 顶点数) 每次驱动 shape，一次性（构建时）
- 全程 double 累加（float 在 60k 顶点求和会丢精度）
- 门限指标：`A = dot/√(len2_j·len2_i)`（余弦）、`B = dot/len2_i`（驱动能量占比），取 max ≥ 0.95
  - A 管"同向不同幅"（孪生），B 管"子集"（单眼 ⊂ 双眼）——**缺 B 会漏单眼，缺 A 会漏大幅孪生**

---

## 7. 设计决策速查（为什么这么定）

| 决策 | 原因 |
|---|---|
| 雕塑烤进顶点（Pass A），不依赖"私有频道" | WD ON 是生态默认；动画层永远碰不到顶点 |
| 渲染器权重归零（构建副本） | 烤入后权重冗余，且 WD ON 会写回 0（无害） |
| Pass C 只中和 avatar 自己的控制器，FX 在 Custom Expressions 下跳过 | FX 是商品领域，保留商品 look |
| 校准器不修 `>_<` 这类异通道表情 | RINDO 实测：均匀缩放削弱表情特征，编辑器加性结果才是用户认可的 |
| 门限 0.95 启发式 | 目前仅 RINDO 验证；边界案例按构建日志的 cos/proj 值调 |
