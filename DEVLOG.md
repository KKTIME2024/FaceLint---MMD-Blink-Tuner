# MmdBlendShapeScaler — 开发日志

## v0.6.5 — 预算恢复全量：烘焙与补偿一致（2026-08-15 改码，2026-08-16 实测通过 ✅）

**状态**: ✅ 用户 Unity 实测通过，接受标准达成 —— "一次捏脸，无需修复任何眨眼/MMD/表情"。

### 用户实测（RINDO 完整日志 + blendshape 导出）

- 21 个基值 Σ=5.52，其中 15 个在眼部族且日志 cos=1.000（delta 同形，基值叠在同一批顶点上）
- **右眼仍差一点**：v0.6.4 预算门把"没过门的捏脸项"排除在 α 外，但 Pass A 烤入了全部 21 项——
  **烤了却不补偿**。例：`eye_open` 基值（撑眼，与闭眼反向）残留 → 眨眼时右眼差"那一点"
- `Sculpt frozen: 21, Scaled: 112`；`>_<` 与表情不再被清零（v0.6.4 生效）✓

### 修复 — 全量预算（撤销 v0.6.4 的门）

α 预算恢复为**所有捏脸项**（逐顶点）：

- 反向项（eye_open）点积为负 → **α(v) > 1 自动补回撑开量** ✓
- 跨通道项只在它们真正触及的顶点上影响 α（逐顶点局部性）✓
- v0.6.2 的"19 个清零"是**标量公式伪影**（全局 |D_i|² 被 21 项全局点积撑爆）；逐顶点不会互相拖累 ✓
- 驱动级"跳过"判定降级为日志信息（`[no same-channel sculpt; per-vertex only]`）

### 实测结果（2026-08-16，用户 Unity 构建验证）

- **右眼完全闭合**：`eye_open` 残留消失，眨眼左右对称 ✅
- **表情全部正常**：eye_happy/じと目/ｷﾘｯ 等驱动到 100 有反应，不再清零 ✅
- **`>_<` 无回归** ✅
- 接受标准达成：一次捏脸，无需修复任何眨眼/MMD/表情 ✅

---

## v0.6.4 — 预算只收同通道捏脸项：模型自带基值不再清零表情（2026-08-15，未编译验证）

**状态**: 🚧 代码已改，待 Unity 编译/实测。

### 用户实测（RINDO 构建日志）

- `Sculpt frozen: 21` —— **RINDO 自带 21 个基值权重**（作者烘焙的默认脸），工具把整张默认脸当成了"用户的捏脸"
- **19 个表情 shape 被 `clamped to 0`**（delta 清零）：eye_happy/eye_angry/eye_sad/じと目/ｷﾘｯ/eyelid_* 等，
  驱动到 100 时完全没反应；`Scaled: 73`
- `>_<` 被门挡住（cos=0.275）✓；Pass C 3 clips ✓
- 注意：该日志来自旧构建（v0.6.2 标量+双指标）；v0.6.3 逐顶点+三指标尚未生效

### 根因（结构性）

α 的预算求和 `Σ_j b_j·(D_j·D_i)` 遍历**全部**捏脸项。门只决定"驱动是否缩放"，
却没有过滤"哪些捏脸项进预算"——21 个基值里大量部分重叠项（eye_doya 与闭眼 ~30% 重叠等）
在同一个驱动上叠加，Σ b_j·proj > 1 → α≤0 → 表情清零。

### 修复 — 预算门（budget gating）

预算只从**与驱动同通道（max(A,B,C) ≥ 0.95）**的捏脸项收取：

- eye_happy 只抵自己的基值（α = 1 − b_happy ≈ 0.95），不再被 21 项清零 ✓
- 眨眼族照旧：预算 = 同通道族（eye_close/eye_close_L/eye_close_R/blink/まばたき）的 Σb ✓
- 跨通道部分重叠（eye_doya 对 eye_close 的 30%）不进预算 ✓
- 方向相反的基值（如"开眼"shape）gdot<0 → 指标为负 → 不进预算 ✓

### 待验证（请用最新代码重建）

- 19 个表情全部恢复：日志中 `clamped to 0` 应消失（除非同通道族 Σb>1）
- `Scaled` 数应大幅下降（只有同通道族被缩放）
- 非对称捏脸（eye_close_L=10）+ 逐顶点 α：左眼 1.0 / 右眼 1.0
- `>_<` 回归：仍被门挡

---

## v0.6.3 — 逐顶点 α：非对称捏脸（2026-08-15，未编译验证）

**状态**: 🚧 代码已改，待 Unity 编译/实测。

### 用户实测（RINDO）：非对称捏脸暴露标量 α 的结构性缺陷

- 第一轮（纯余弦门）：`eye_close_L` 捏 10 → **右眼正常、左眼过度**（α=1，过闭 1.1）
- 第二轮（双指标门）：**左眼正常（1.0）、右眼留缝（0.9）**——"缝的大小刚好是左眼多出来的量"
- 结论：驱动双眼的 shape 拿到了 α=0.9，但捏脸预算只在左眼——**一个标量无法表达
  "左半边 ×0.9、右半边 ×1.0"**。该 shape 恰好坐在 0.95 门限悬崖边（两轮 cos/proj 各在 0.949/0.951 附近翻转）

### 修复 — 门保留 + 过门后逐顶点缩放 + C 指标（雕塑侧覆盖）

- **门保留**：`>_<` 等表情的行为（用户已确认正常）完全不动
- **门补 C 指标**：模型有 `eye_close`（双眼）+ `eye_close_L/R` 三件套（用户确认"很多形态键也有额外的 L,R"）。
  旧双指标缺"雕塑侧覆盖"：双眼驱动 vs 单眼捏脸时 A=0.707、B=0.5 都不过——但捏脸能量 100% 落在驱动里，
  必须缩放。新增 `C = (D_j·D_i)/|D_j|²`（sculpt-side coverage），门 = max(A,B,C) ≥ 0.95：
  - A 管同向（孪生）、B 管驱动侧覆盖（单眼驱动 vs 双眼捏脸）、**C 管雕塑侧覆盖（双眼驱动 vs 单眼捏脸）**
- **过门后**：`α(v) = 1 − Σ_j b_j·(D_j(v)·D_i(v)) / |D_i(v)|²`（clamp 0..2，|D_i(v)|²≈0 处 α=1）
  - 有捏脸预算的顶点（左眼）→ 0.9；预算不触及的顶点（右眼）→ **1.0，缝消失**
  - 对称捏脸 → 逐顶点退化回均匀 1−Σb，行为不变
  - 工作数组 `workLen2/workDot`（double，跨 shape 复用）
- 日志：`α for 'X': mean=0.95 (per-vertex, cos=..., driverCov=..., sculptCov=...)`；全顶点 α≈1 时返回 null（不重建）

### 待验证

- 非对称捏脸（eye_close_L=10）：左眼 1.0 / 右眼 1.0（无缝）——双眼驱动经 C=1.0 过门，逐顶点只缩左半边
- 对称捏脸回归：与 v0.6.2 完全一致
- `>_<` 回归：仍被门挡（A,B,C 均 <0.95），行为不变
- **请贴构建日志**（所有 `α for` 行）——确认 eye_close 的 cos/driverCov/sculptCov 实际值

---

## v0.6.2 — 模型自己的表情不再被投影公式削弱（2026-08-15，未编译验证）

**状态**: 🚧 代码已改，待 Unity 编译/实测。

### 用户实测反馈（RINDO 测试工程）

> `m_BlendShapeWeights.Array.data[76]`（模型自己的 `>_<` 眼部表情）在构建后"显著不足"。
> 静态下捏完脸拉到 100 正常；编译完这个值明显不够。**这不是 MMD 形状，是模型自己的表情。**

### 根因

`>_<` 被模型自己的表情系统（clip binding）驱动 → 进入驱动通道名单 → v0.6 投影公式
`α = 1 − Σ b_j·(D_j·D_i)/|D_i|²` 按它与捏脸方向（eye_close 族）的重叠度**整体缩小它的 delta**。
但 `>_<` 不是眨眼通道——它的 delta 里除了闭眼分量还有表情特征（眉/颊/眼形），
均匀缩放把整个表情压弱了 → "显著不足"。编辑器里（加性：捏脸 + 表情全量）才是用户认可的样子。

### 修复 — 共线门（collinearity gate）

`ComputeAlpha` 增加通道判定：驱动通道与**任一**捏脸 shape 满足
**max(余弦, 驱动能量沿捏脸占比) ≥ 0.95** 才应用 α，否则 α = 1。

两个指标互补（缺一不可）：
- **余弦** `(D_j·D_i)/(|D_j|·|D_i|)`：对幅度不敏感 → 同向不同幅的眨眼孪生 shape 依然 α ✓
- **投影占比** `(D_j·D_i)/|D_i|²`：对子集敏感 → **单眼 shape（眼L）vs 双眼捏脸（eye_close）时
  余弦只有 0.707，但它的能量 100% 沿捏脸方向（占比=1.0）→ 必须缩放**，α = 1−b 正好修复单眼过闭

- 模型自己的表情（`>_<` 带眉/颊特征）：两个指标都 ~0.8 < 0.95 → α = 1 原样保留（纯加性 = 编辑器所见）✓
- 手动 scales 配置不受影响（仍可手调任何 shape）✓
- 日志新增：`α for '>_<' skipped: direction differs from sculpt (cos=0.83, proj=0.83)` 便于验证

### 实测确认（用户反馈，2026-08-15）

- `>_<` 是**模型自己的表情 blendshape**，基值确实为 0——`data[76]=0` 属正常，运行时重量由 rescery Dynamic expression（一组商品动画 clip）写入
- 用户确认：**更早版本（纯投影公式）下单眼 shape 是正常的**——与公式推演一致：投影公式对"子集"（单眼 ⊂ 双眼捏脸）天然正确（`α = 1 − b·(|眼L|²/|眼L|²) = 0.8`），只对"带正交特征"（`>_<`）过度缩放
- **门限指标迭代**：第一版只算余弦 → 单眼 subset 被误挡（cos=0.707 < 0.95）→ 加入 B 指标（驱动能量占比，`dot/|D_i|²`）→ 单眼 B=1.0 放行，且 α 与旧版逐字节一致（无回归）

### 调试过程副产品（FBX 解析，未入库）

RINDO.fbx（FBX 7400，11.1MB）确认：**该文件用 u32 EndOffset 的 13 字节记录头**
（标准文档是 u64/17 字节）；顶层结构全部对齐验证；字符串属性存在非标准填充，
纯 PowerShell 解析在深层嵌套处失准，未拿到 shape 级 delta 数据——不影响修复结论
（门限 0.95 对"表情 vs 眨眼族"的区分是稳健的）。

---

## v0.6.1 — 代码审查修复（2026-08-15，未编译验证）

**状态**: 🚧 纯代码审查 + 修复，Roslyn 语法检查通过，待 Unity 编译/实测。

### 审查结论：v0.6 三 Pass 设计成立，但校准器有一个数据破坏 bug

**🔴 关键 bug — 校准器清空捏脸基值**

`RestoreAllWeights()` / `DeselectCurrent()` / `SelectEntry()` 把所有 blendshape 权重**归零且不恢复**。
v0.6 的雕塑输入就是渲染器基值权重（Pass A 构建时读取）——用户开一次校准器再关掉，捏脸就被清掉；
v0.5 时代同样会清掉模型作者烘焙的基值（如 `まばたき=20`）。

**修复**（`MmdCalibratorWindow.cs`）：
- 新增 `_baseWeights` 快照：Scan 时 / 渲染器切换时基线化全部权重
- `RestoreAllWeights` 恢复快照（而非归零）；`SelectEntry`/`DeselectCurrent` 恢复条目基值
- 快照绑定 renderer InstanceID，防旧索引写入新渲染器
- 扫描前不再归零（缩略图用 AnimationMode 采样，本来就不碰真实权重）；扫描后恢复基值

**其他修复**：

| 文件 | 修复 |
|---|---|
| `MmdCalibratorWindow.cs` | 缩略图跳过标记从 `description.Length==0` 改为显式 `hasThumbnail`（防数据库空描述误跳） |
| `MmdBlendShapeScalePass.cs` | 驱动检测补全：支持 `AnimatorOverrideController` + `VRCAvatarDescriptor` 自定义动画层（`customizeAnimationLayers` 时扫 `baseAnimationLayers` 非默认层），修局限 #4 的 Pass B 半边 |
| `MmdControllerNeutralizer.cs` | Pass C 同样处理 descriptor 自定义动画层（默认层走 Animator 组件已覆盖，跳过），修局限 #4 的 Pass C 半边 |
| `MmdBlendShapeScaler.cs` | 修正误导性注释（Pass C 开关被写成 "Sculpt Freeze"） |

### 审查中确认无问题的点（设计成立）

- NDMF 1.11 API 与本地 SDK 源码逐项核对：`BuildContext.AvatarDescriptor`、`Pass<T>.Execute` ✓
- `Object.Instantiate(controller/clip)` 克隆 + 改写模式与 AAO Write Defaults pass 同款 ✓
- 投影 α 公式（`1 − Σ b_j·(D_j·D_i)/|D_i|²`）逐项推演：同 delta 退化 `1−Σb_j`、正交通道 α≈1、`Σb_j>1` clamp 0 ✓
- `baseAnimationLayers` / `CustomAnimLayer.animatorController` / `isDefault` 字段名与 SDK 3.10.2 源码核对 ✓

### 遗留（未改，见 v0.6 已知局限）

1. Pass C 不修"无 motion 状态"（纯 WD 默认值），仅日志
2. 多帧 blendshape 的 α 用 frame-0 数据（设计意图：整条曲线仿射映射）
3. 法线/切线不随驱动缩放（沿用 v7 决策）
4. ⚠️ Pass C 对自定义层是"克隆并替换"——`customizeAnimationLayers` 时 animator 组件上的默认层控制器仍会被处理（运行时不用，无害但冗余）

---

## v0.6 — Sculpt Freeze + 自动 α + Pass C (代码已落地, 待 Unity 验证 2026-05-20)

**状态**: 🚧 代码完成，未在 Unity 中编译/验证。设计见 `DESIGN_SCULPT_BASE.md`。

### 目标（用户验收标准）

> 在 mesh blendshape 里一次捏脸（设置基值），无需修改/修复任何眨眼、MMD、单帧表情、多帧动态表情。
> 不变量：`你的脸(w) = 捏脸 + (1−b)·作者脸(w)` — 任何驱动到 100 = 作者峰值。

### 实现

- **Pass A — Sculpt Freeze** (`MmdBlendShapeScalePass.cs`)：构建时读取渲染器基值权重（>0.5%），
  把 Σ b_j·delta 烤进克隆 mesh 的 vertices + normals + tangents（delta 法线/切线累加，**不**用 `RecalculateNormals`），
  基值权重归零。WD 免疫：雕塑活在顶点空间。
- **Pass B — 驱动缩放**：驱动通道 = 最终 animator 所有 clip 的 `blendShape.*` binding ∪ 64 个 MMD 名 ∪
  `vrc.blink` 家族。α_i = 1 − Σ_j b_j·(D_j·D_i)/|D_i|²（投影公式，同 delta 时退化为 1−Σb_j）。
  手动 `scales` 配置覆盖自动 α。无雕塑时 α=1 → 完全向后兼容。
- **Pass C — Controller 中和** (`MmdControllerNeutralizer.cs`, v0.1 实验性)：
  克隆 avatar 自己的控制器，把雕塑 shape 的**常量曲线**（烘焙基脸）改写为 0；
  动画曲线留给仿射映射；customExpressions 时跳过 FX 层（商品领域）；无 motion 的状态仅日志。
- **UI**：校准器新增「包含全部形态键（非 MMD）」开关（商品 shape 可调滑块，无缩略图仅名称）；
  Inspector 新增 Pass C 开关。

### 已知局限（今晚测试重点）

1. Pass C 只修"状态有 motion 且 clip 含常量曲线"的烘焙基脸；无 motion 状态（纯 WD 默认值）未修
2. 多帧 blendshape 的 α 用 frame-0 数据（沿用单帧 weight-100 假设）
3. 法线/切线未随驱动缩放（沿用 v7 决策，只缩 vertices）
4. Pass C 未处理 `VRCAvatarDescriptor.playableLayers` 自定义控制器

### 预期构建日志

```
[MMDBlinkFixer] Execute started. Found 1 scaler(s) on avatar.
[MMDBlinkFixer] Processed 334 blendshapes for 'Body'. Sculpt frozen: 1, Scaled: 3.
[MMDBlinkFixer] Pass C (controller neutralization): 5 clip(s) rewritten.
[MMDBlinkFixer] Execute finished.
```

---

## v0.2.0 — 三语 UI 支持 (已实现 2026-05-20)

**状态**: ✅ 已实现。新增 `Editor/Strings.cs`（字典式三语方案），README 同步更新为三语。

### 实现

- `Strings.cs`：`enum UILang { En, ZhCN, Ja }` + `Strings` 类，~40 个翻译字段，`Strings.For(lang)` 工厂，语言持久化到 `EditorPrefs`
- `MmdCalibratorWindow.cs`：顶部 `EnumPopup` 语言切换器，所有 UI 标签改用 `Strings.Current.XXX`
- `MmdBlendShapeScalerEditor.cs`：Inspector 标签全部接入三语
- `README.md`：日本語 / English / 中文 三个完整章节
- Undo 标签和 Debug.Log 保留英文（开发者面向，Unity 惯例）

### 其他 v0.2 候选

- Prev/Next 导航时保持在详情视图（不跳回网格）
- 差异高亮（接入 Vendor/ 里已有的 DiffCompute）
- 单元测试

---

## v7.1 — 构建不生效：3 个隐藏 Bug 排查与修复 (2026-05-19)

**状态**: ✅ 已修复并实测通过

### 背景

v7 架构完成后，Editor 中 Calibrator 窗口正常工作，Scene View 预览正常，
但 **Build & Upload 后 blendshape 缩放完全没生效**。排查发现 3 个隐藏 bug，
形成连锁故障——前两个 bug 掩盖了第三个。

### Bug #1：MmdBlendShapeScalerPlugin.cs 文件损坏

**现象**：文件底部（第 39 行 namespace 闭合后）有重复的孤儿代码——
`Configure()` 方法和 `MmdScalerDiagnostics` 类被复制粘贴到了 namespace 之外。
`Configure()` 不在任何 class 体内，这是 C# 编译错误。

**影响**：Editor 程序集编译失败 → NDMF Plugin 无法注册 → 构建时 pass 永不执行。

**修复**：删除孤儿代码，保留一份干净的 `Plugin<T>` 子类。

### Bug #2：缺少 [assembly: ExportsPlugin] 属性

**现象**：修好 Bug #1 后，Unity Console 中仍然没有任何 `[MmdScaler]` 日志。
对比 make-it-mmd 源码（`E:\21_CodeRepos\make-it-mmd`）后发现，make-it-mmd 有
`Editor/AssemblyInfo.cs` 文件，内含：

```csharp
[assembly: ExportsPlugin(typeof(NonDestructiveMmdPlugin))]
```

这是 NDMF 发现插件的**标准入口**。之前的代码试图用 `InitializeOnLoadMethod` +
`RuntimeHelpers.RunClassConstructor` 暴力触发静态构造函数来注册，这个 hack 不可靠。

**修复**：
- 新建 `Editor/AssemblyInfo.cs`，添加 `[assembly: ExportsPlugin(typeof(MmdBlendShapeScalerPlugin))]`
- `Editor/MmdBlendShapeScaler.Editor.asmdef` 设置 `"autoReferenced": false`（与 make-it-mmd 一致）
- 删除 `MmdScalerDiagnostics` 的 `InitializeOnLoadMethod` hack

### Bug #3：Pass 静态构造函数访问 Instance 导致递归崩溃

**现象**：修好 Bug #2 后，NDMF 成功发现了 Plugin，`Configure()` 被调用，
但构建立即崩溃：

```
InvalidOperationException: ValueFactory attempted to access the Value property...
MmdBlendShapeScalePass..cctor () (at MmdBlendShapeScalePass.cs:12)
```

**根因**：`MmdBlendShapeScalePass` 的静态构造函数里写了：
```csharp
Debug.Log("Instance is: " + (Instance != null ? "valid" : "null"));
```

`Instance` 是 `Pass<T>` 基类的属性，内部用 `Lazy<T>` + `Activator.CreateInstance<T>()`
来创建实例。但在静态构造函数**内部**访问 `Instance` 会再次触发 `Activator.CreateInstance<T>()`，
导致递归调用。`Lazy<T>` 检测到 `ValueFactory` 里又访问 `Value`，直接抛异常。

**为什么之前不暴露**：Bug #1 和 #2 导致 `Configure()` 从未被调用，
`MmdBlendShapeScalePass.Instance` 从未被访问，静态构造函数从未执行。
这是一个经典的"被上游 bug 掩盖的 bug"。

**修复**：静态构造函数中不访问 `Instance`，改为无参 Debug.Log。

### 最终验证日志

```
[MmdScaler] Configure called — registering pass
[MmdScaler] Execute started. Found 1 scaler(s) on avatar.
[MmdScaler] Processed 334 blendshapes for 'Body'. Scaled: 12.
[MmdScaler] Execute finished.
```

334 个 blendshape 中 12 个被按配置缩放，构建成功，上传后效果生效。

### 教训

1. **总是用 `[assembly: ExportsPlugin]` 注册 NDMF 插件**——这是 NDMF 的标准发现机制，
   不要依赖 `InitializeOnLoadMethod` hack。
2. **不要在 `Pass<T>` 的静态构造函数中访问 `Instance`**——`Lazy<T>` 不允许递归访问。
3. **对比参考实现**——make-it-mmd 的 `AssemblyInfo.cs` + `autoReferenced: false` 模式
   是 NDMF 插件的最佳实践。

---

## v7 — NDMF 非破坏性架构 (2026-05-19)

**状态**: ✅ 完成
**分支**: v7-ndmf-rewrite

### 背景

v1-v6 的 `MMDBlendShapeChecker` 是一个纯预览工具：扫描 → 调节滑块 → 复制剪贴板 → 手动到 make-it-mmd 配置。工作流断裂，不是"一步到位"。

v7 采用 make-it-mmd 的非破坏性 NDMF 架构，将工具从"纯预览"升级为"预览 + 构建时自动应用"。

### 架构变更

| | v1-v6 (MMDBlendShapeChecker) | v7 (MmdBlendShapeScaler) |
|---|---|---|
| 配置存储 | EditorWindow 内部状态 | IEditorOnly Runtime 组件 |
| 持久化 | 无（关窗丢失） | Unity 序列化 |
| Mesh 修改 | 不修改（= 无产出） | NDMF Pass 自动克隆 + 缩放 |
| 预览 | SetBlendShapeWeight 临时 | 保留 + 差异高亮 |
| 工作流 | 4 步（扫描→调节→复制→粘贴） | 2 步（调节→Build） |

### 新增文件

- `Runtime/MmdBlendShapeScaler.cs` — public targetRenderer + scales 字典
- `Editor/MmdBlendShapeScalerPlugin.cs` — NDMF Transforming 阶段注册
- `Editor/MmdBlendShapeScalePass.cs` — 流式 delta 缩放 (仅 vertices)
- `Editor/MmdBlendShapeScalerEditor.cs` — Inspector 摘要 + 一键打开
- `Editor/MmdCalibratorWindow.cs` — 网格 + 详情两视图 (607 行)
- `Editor/Vendor/` — 差异高亮 shader + compute (从 blendshape-viewer 适配)

### 删除文件

- `Editor/MMDBlendShapeChecker.asmdef`
- `Editor/MMDBlendShapeCheckerWindow.cs`
- `Editor/BlendShapeCalibrator.cs`
- `Editor/MmdShapeDatabase.cs` (旧命名空间版)
- `Editor/BlendShapePreviewRenderer.cs` (旧命名空间版)

### 关键技术决策

| 决策 | 理由 |
|------|------|
| targetRenderer 存组件上 | 不依赖 VisemeSkinnedMesh — MMD blendshape 可能在任意 mesh |
| 只缩放 vertices | normals/tangents 不是 position delta，缩放导致 shading artifact |
| 流式处理 | 不缓存全部 FrameData，内存从数百 MB 降到 ~3 个数组 |
| 多 scaler 支持 | GetComponentsInChildren(true) 支持多 face mesh avatar |
| #if 守卫全部移除 | versionDefines 导致 NullReferenceException，Editor 代码无条件编译 |
| includePlatforms 从 Editor 改为空 | IEditorOnly 组件必须编译到 Standalone 才能在场景中存在 |
| internal → public | 跨 asmdef 引用需要 public |

### 已知局限

1. 差异高亮 toggle 已 stub，待接入 MmdBlendShapeViewerGenerator
2. 无单元测试
3. 滑块 MouseUp 检测在 IMGUI 中不完全精确（不影响功能）
4. 多帧 blendshape 假定使用标准权重 100 格式

---

## v1-v6 — 旧架构 (保留参考)

# MMD BlendShape 过闭合检测器 — 开发思路演变

## 背景

VRChat 玩家在调整面部 blendshape 后，MMD 世界驱动会按 weight=100 执行，但玩家已经通过基值权重修改过对应的原生形状（如 eye_close=20, eye_jito=15 等），导致实际效果相当于 weight=120 甚至更高，引发过度闭合。

## v1 - 单 shapes 独立检测 (已废弃)

**思路**: 对每个 MMD blendshape 单独分析，检查 weight=100 时的顶点形变。

**问题**:
- 忽略了 SkinnedMeshRenderer 上已有的基值权重
- 实际运行时: `actual = neutral + eye_close×0.20 + eye_jito×0.15 + まばたき×1.00`，而非单纯 `neutral + まばたき×1.00`

## v2 - 分桶配对 + 超限外推 (已废弃)

**新增**: 8桶X轴配对顶点替代全局min/max; 模拟120/150/200%外推; 法线翻转检测; 侧向挤压检测

**问题**:
- 仍然以 neutral mesh 为基准，未纳入基值
- 硬编码 Y-ratio 阈值做区域分类 (50-72%=眼部, 25-48%=嘴部)，受 bounding box 内含非面部顶点影响失效
- "闭合率 = 1 - 变形眼裂/中性眼裂" 在完全闭合后永远是 100%，无法区分刚好闭合和过度挤压

## v3 - 基值叠加 + 自适应区域 (当前版本，用户认为思路有问题)

**核心改动**:
1. 读取 `faceRenderer.GetBlendShapeWeight(i)` 获取所有基值
2. 计算 `base_deformed = neutral + sum(native_delta * weight/100)`
3. 用 `eye_close` / `mouth_a` / `brow_joy` 等参考形状的 delta 自适应识别面部区域
4. MMD 形状与原始形状自动匹配 (delta 逐顶点比较)
5. 有效权重 = 100 + 匹配原始形状基值
6. 在 `base_deformed` 状态下运行分桶配对/穿透/法线/应变检测

**架构**:
```
Input: SkinnedMeshRenderer (Body)

Step 1: 分类 shapes → MMD / native
Step 2: base_deformed = neutral + Σ(native_delta × weight_i/100)
Step 3: 参考形状自适应区域识别 (eye_close → 眼部, mouth_a → 嘴部)
Step 4: MMD shapes → 匹配原始形状 → 有效权重
Step 5: 在 base_deformed + mmd_delta × effectiveWeight/100 状态下运行检测
Step 6: 报告穿透/法线/应变/侧向挤压
```

**检测维度**:
- 8桶配对顶点眼裂 (保留负值)
- 法线翻转 (dot<0)
- 侧向挤压 (眼角宽度收缩)
- 应变比 (位移/安全闭合距离)
- 100%/120%/150%/200% 权重外推
- 64项标准 MMD 形状缺失检测

**判定层级**: 正常 → 注意 → 警告 → 严重 (4级)

## 用户反馈 (当前)

v3 思路仍然不对。用户认为根本的假设可能有问题，需要重新思考检测的本质。

## 文件结构

```
Assets/MMDBlendShapeChecker/Editor/
├── MMDBlendShapeChecker.asmdef
├── MmdShapeDatabase.cs              (64个标准MMD形状 + 分类)
├── BlendShapeCalibrator.cs          (v4-v5 自动校准算法, 已退役)
├── BlendShapePreviewRenderer.cs     (v6 Camera + AnimationMode 缩略图渲染)
└── MMDBlendShapeCheckerWindow.cs    (v6 手动调节器 UI)
```

## v4 - 最小二乘校准 (当前版本)

### 关键洞察

v1-v3 都在做"碰撞检测"——判断顶点是否穿透、法线是否翻转。但古法修复根本不是检测，而是**校准**。

古法流程:
1. 对每个 MMD shape 拉到 100，肉眼观察是否闭过头（比如まばたき 100 + eye_close基值20 = 实际120，闭过头了）
2. 手动拖到"刚好合适"的位置（如 80）
3. 去 make-it-mmd 插件设 scale=0.8

### 数学模型

默认状态(全基值=0)的终态:
```
V_default = neutral + M        (M 为 weight=100 的 delta)
```

用户捏脸后的终态:
```
V_current = neutral + B + αM   (B 为基值形变向量)
```

最小化当前终态与默认终态的差异:
```
minimize ||V_default - V_current||²
→ minimize ||M(1-α) - B||²

解:
α* = 1 - Σ(Bᵢ · Mᵢ) / Σ(|Mᵢ|²)
```

推荐值 = α* × 100（例如 α*=0.78 → 推荐 78%）

对应 make-it-mmd 操作: `まばたき → まばたき(scale:0.78)`

### 与 v3 的根本区别

| v3 (检测) | v4 (校准) |
|-----------|-----------|
| "这个形状有问题需要修" | "这个形状的推荐驱动值是 78%" |
| 判断严重/警告/正常 | 输出数值和对应操作 |
| 需要定义什么是"过度" | 只需要参考答案 (默认终态) |
| 区域分类/配对/法线/应变 | 最小二乘投影 |
| 无法确定修到多少才好 | α* 精确解 |

### 已知遗留问题

1. 影响基值列表可能过长 (列出所有非零基值而非相关项)
2. 区域识别依赖特定参考形状名 (若缺失则回退失败)
3. ~~检测本质上是"碰撞检测"而非"形变意图检测"~~ → 已废弃 v3，改用校准范式

---

## v5 — 逐顶点偏差检测 (当前版本, 2026-05-17)

### v4 的死因

v4 只取 top 20% 顶点做全局 dot product 投影，然后线性外推一个 scale。这导致：
- **从未真正识别过"哪些顶点在穿模"**——dot product ≈ 0 时输出"无需调整"，但实际问题仍然存在
- **线性投影假设在闭合问题上失效**——眼睛闭合是阈值物理现象，投影给出的是数学拟合值而非物理临界点
- **区域识别靠猜**——依赖"碰巧有 eye_close/mouth_a 这个名字"来划分面部区域

### v5 核心改动

**范式：从"全局投影公式"变为"逐顶点比较 + 取最保守方案"。**

#### 算法流程

```
Step 1: 分类 shapes → MMD / native (非零基值)
Step 2: 预计算 B[v] = Σ(native_delta_i[v] × weight_i/100)  基值形变向量
Step 3: 对每个 MMD shape:
  3a. 取 M_delta[v]
  3b. 对 M_delta 非零的每个顶点 v:
      - 计算 dot(B[v], M_delta[v])
      - 若 > 0: 基值推动方向与 MMD 相同 → 潜在过闭合
      - 若 |B[v]| > 0.05mm: 偏差不可忽略
      - 计算 α_v = 1 - dot(B[v], M_delta[v]) / |M_delta[v]|²
  3c. 推荐值 = min(α_v) × 100  (取最保守值，确保没有任何顶点过闭合)
  3d. 报告: 受影响顶点数、过闭顶点数、最大偏差(mm)、平均偏差(mm)
```

#### 和 v4 的本质区别

| | v4 | v5 |
|---|---|---|
| 顶点选择 | top 20% (丢弃80%) | M_delta 非零的全部顶点 |
| 计算方法 | 全局 dot sum 投影 | 逐顶点 α_v，取 min |
| 检测结果 | 可能漏报 (dot sum ≈ 0) | 有偏差就有偏差 |
| 输出信息 | 仅推荐 scale | scale + 顶点统计 + 偏差分布 |
| 区域识别 | 不需要猜 — M_delta 自然标记了受影响顶点 |

#### 已知局限

1. 取 min(α_v) 是保守策略 — 可能过度降低某些形状 (但不会漏报)
2. 匹配原始形状的余弦相似度计算 O(M×N×V) 可能慢，已预计算 native magnitudes 优化
3. 偏差阈值 0.05mm 为硬编码常量，大规模/小规模模型可能需要调整

#### 验证计划

- [ ] 在测试场景中对已知有问题的 Avatar 运行，确认能识别出过闭合顶点
- [ ] 对比 v4 输出，确认 v5 不会漏报 v4 漏掉的形状
- [ ] 用推荐的 scale 值实际应用到 make-it-mmd，验证穿模消失

---

## v6 — 手动可视化调节器 (当前版本, 2026-05-18)

### 为什么要放弃自动校准

v1-v5 都在尝试用算法回答"这个形状做得好不好"。但这是死路：

- **闭眼/张嘴 → 物理问题**，顶点检测勉强能做
- **眉毛角度/嘴角弧度 → 审美问题**，数学永远给不出"好不好看"的答案
- **不同玩家对"正常"的定义不同**，没有客观标准

结论：**把判断权还给用户。** 工具只负责让用户快速看到效果 + 高效调节。

### 核心设计

**两视图架构：**

| 视图 | 功能 |
|---|---|
| **网格视图** | 所有 MMD 形状的 100% 缩略图，分组显示。一眼扫出哪个不喜欢。 |
| **详情视图** | 点击缩略图进入。大图参考 + 滑块 (0-200) + Scene View 实时 3D 预览。 |

**调节流程：**
1. 拖入面部 SkinnedMeshRenderer → 点击「扫描」
2. 64 个 MMD 形状的 100% 缩略图渲染完成（基于 blendshape-viewer 方案）
3. 看到有问题的形状 → 点击缩略图
4. Scene View 实时显示该形状在滑块值下的效果
5. 调节滑块 (0-200) → 在 Scene View 中 360° 旋转检查
6. 调到满意 → 点击下一个形状（上一个自动恢复）
7. 全部调完 → 「应用所有更改」→ 持久化到 SkinnedMeshRenderer

**非破坏性：**
- 通过 `Undo.RecordObject` + `SetBlendShapeWeight` + `SetDirty` 修改组件数据
- 不修改 mesh 资产
- 支持 Prefab Instance 的 property modification 记录
- 可随时「全部重置为 100%」

### 新增文件

| 文件 | 功能 |
|---|---|
| `BlendShapePreviewRenderer.cs` | Camera + AnimationMode 渲染单个 BlendShape 为 Texture2D |

### 预览渲染方案

参考 make-it-mmd / blendshape-viewer (Hai~) 的方案：
1. 拷贝 Scene View 当前相机位姿 → 创建临时 Camera
2. 创建 `AnimationClip`，用 `AnimationUtility.SetEditorCurve` 设置 `blendShape.{name}` 为目标 weight
3. `AnimationMode.SampleAnimationClip` → 临时激活
4. `Camera.Render` → `RenderTexture` → `ReadPixels` → `Texture2D`
5. 清理临时对象

64 个缩略图 × ~50ms ≈ 3 秒，有进度条。

### 已知局限

1. Scene View 预览依赖 `SetBlendShapeWeight` 直接修改渲染器，无独立预览窗口
2. 没有"当前值是否好看"的客观检测 — 这是设计意图，不是缺陷
3. 缩略图渲染需几秒钟，非实时生成

