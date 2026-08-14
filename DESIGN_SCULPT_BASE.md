# FaceLint — 捏脸基值通道重排（Sculpt Base Relay）设计

**状态**: 设计定稿，v0.6 代码已落地（`MmdBlendShapeScalePass.cs` + `MmdControllerNeutralizer.cs`），待 Unity 编译验证
**日期**: 2026-05-20
**前置讨论**: 用户目标与多轮推演收敛（见会话记录，核心结论已并入本文）

---

## 0. 目标（验收标准）

> 在 mesh blendshape 上**一次捏脸**（设置基值），构建后**无需修改/修复任何**：
> 眨眼（内置/MMD）、MMD 世界驱动、单帧表情、多帧动态表情（商品）。

**一句话不变量**：

```
你的脸(w) = 你的捏脸 + (1 − b)·作者脸(w)
```

- w = 0（静止）→ 恰好是你的捏脸
- w = 100（任何驱动方峰值）→ 恰好是作者调好的表情峰值
- 覆盖 / 叠加 / MMD 世界驱动三种语义全部收敛到同一终点

---

## 1. 核心方程

所有 blendshape 共享同一份顶点预算，任何时刻的位移为：

```
V(w) = Σ_i w_i · D_i          （D_i = shape i 的 delta，含法线/切线帧）
```

- **捏脸预算**：`B = Σ_{j∈sculpt} b_j · D_j`（b_j = 用户捏脸基值 / 100）
- **驱动通道缩放**（对每个被动画驱动的 shape i）：

```
α_i = 1 − Σ_j b_j · (D_j·D_i) / |D_i|²      （投影公式）
```

- 三个 delta 完全相同时退化为 `α = 1 − Σb_j`（例：eye_close=20 → α=0.8）
- 所有 b=0 时 α=1 —— **退化为现有 `MmdBlendShapeScalePass` 的行为，向后兼容**
- 投影残差 `B − proj(B on D_i)` 是 mesh 侧不可修的"delta 不适配"部分（如眉毛转 45 度），保留并如实报告
- `Σb_j > 1`（过度捏脸）→ α clamp 到 0

**关键性质**：α 只依赖"哪些 shape 被捏了、捏了多少"，与商品无关——所以**一次捏脸后所有商品自动正确**。

---

## 2. 两个 Pass（全部在 NDMF Transforming 阶段，MA 合并之后）

### Pass A — 顶点烤（Sculpt Freeze，唯一架构）

> **硬约束：WD OFF 不考虑**——WD ON 是绝大多数商品与 MA 系工具的默认，要求 WD OFF 等于切割生态。
> 因此私有频道方案（依赖"没有任何 clip 写它"）作废；雕塑必须活在顶点空间，动画层永远碰不到。

对每个捏脸 shape j（基值 b_j > 0，无论是否驱动通道）：

1. 克隆 mesh 后，`vertices += b_j · deltaPos_j`，`normals += b_j · deltaNorm_j`（逐顶点归一化），`tangents += b_j · deltaTan_j`（相对新法线正交化）
2. **禁止 `Mesh.RecalculateNormals()`**——会毁掉卡通模型的 edited normals；delta 法线/切线累加是 AAO FreezeBlendShape 已验证的路线
3. shape **保留**（商品仍驱动它），渲染器权重归零
4. 零基值 shape 不烤、不动

**WD 免疫原理**：雕塑在顶点里；WD ON 状态进场把未动画属性写回烘焙默认值（通常 0），0 权重 = 已烤的中性脸，怎么写都碰不到雕塑。

### Pass B — 驱动缩放（现有 pass 的扩展）

对全部驱动通道按 §1 的 α_i 缩放 delta。现有 `MmdBlendShapeScalePass.cs` 的流式克隆/重建机制原样复用，改动点：

1. `scales` 字典从"64 个 MMD 名"放开到"任意 shape 名"（MMD 名仅作 UI 预分组）
2. 读 renderer 基值 → 计算 B → 逐通道投影 → α_i
3. 执行顺序：Pass A（烤雕塑）→ Pass B（缩驱动）→ Pass C（中和 controller）

### Pass C — Controller 中和（WD ON 的 default face 帧）

WD ON 状态下，各状态会写回**烘焙在 controller 里的默认值**。avatar 自己的 Base/Gesture 层往往烘焙了原版基脸（用户的"default face 帧"痛点）；顶点烤之后这些值仍会写回，叠在雕塑上（b + c·α）。

1. 构建时克隆 avatar 自己的 Base/Gesture 控制器
2. 对每个状态：雕塑用到的 shape 的烘焙默认值改写为 0（雕塑已替代原版基脸）
3. **商品 FX 层不碰**——保留商品自己的 look，让其经过仿射映射自然缩放
4. 技术：读取/改写 WD 烘焙默认值 = AAO Write Defaults pass 同款序列化技巧，已被验证可行

### 驱动通道检测

Transforming 阶段遍历 avatar 上全部 AnimatorController（此时 MA 已合并商品 FX）：

- 收集所有 clip 的 `blendShape.X` binding → 驱动名单
- 并入：`MmdShapeDatabase` 64 标准名 + `vrc.blink` / `vrc.blink_l` / `vrc.blink_r` 家族

---

## 3. 四类驱动方的验收映射

| 驱动方 | 触发 | 现状问题 | 构建后 |
|---|---|---|---|
| 眨眼（内置/商品） | vrc.blink 或 blink → 100 | 0.2 + 1.0 = 1.2 过闭 | 顶点 0.2 + 0.8 = 1.0 ✓ |
| MMD | まばたき → 100 | 同上 | → 1.0 ✓ |
| 单帧表情 | gesture clip 单帧 key | 覆盖弹脸 / 叠加过闭 | 基值在私有频道不被覆盖；→ 1.0 ✓ |
| 多帧动态表情 | 商品包逐帧 clip 0→100 | 每帧偏、峰值最严重 | 整条曲线映射 0.2+0.8w，峰值收敛，**零逐帧修改** ✓ |

**同 shape 场景**（捏脸与商品驱动同一个 shape，如三胞胎 eye_close/blink/まばたき）：Pass A 把 20 烤进顶点 + Pass B 把三个驱动 delta 都 ×0.8。静止 = 你的 20；任一驱动到 100 = 作者闭眼。

---

## 4. 诚实边界（验收条件，非缺陷）

1. **WD ON 为硬约束**（用户决策：WD OFF 切割 90% 商品生态，不考虑）。雕塑必须烤进顶点——WD ON 写 0 碰不到顶点，只有 Pass C 覆盖不到的"controller 烘焙基脸"需要中和。
2. **中间帧语义**：多帧表情的中间帧 = "从你的脸单调走到作者峰值"，非作者原曲线的中间帧。这是捏脸者想要的语义，但需在文档中明示。
3. **骨骼驱动表情**不在覆盖范围（本方案是 blendshape 领域）。
4. **商品自身的 look**（其状态烘焙的非零默认值）不中和，走仿射映射自然缩放——保留商品作者的设计，这是 Pass C 只碰 avatar 自己控制器的原因。

---

## 5. 对 repo 的改动清单

| 文件 | 改动 |
|---|---|
| `Editor/MmdBlendShapeScalePass.cs` | + 驱动检测（扫 clip bindings）；+ Pass A 顶点烤（pos+normal+tangent 累加）；+ 投影 α 计算 |
| `Editor/`（新） | Pass C：controller 克隆 + WD 烘焙默认值改写（参考 AAO Write Defaults pass） |
| `Editor/MmdCalibratorWindow.cs` | 扫描放开到全部 blendshape，MMD 名仅作预分组，新增"其他/商品"分类 |
| `Runtime/MmdBlendShapeScaler.cs` | scales 语义从"MMD 名"扩为"shape 名"；雕塑输入 = 渲染器基值（构建时读取） |
| `DESIGN.md` / `DEVLOG.md` | 代码落地后补记 |

---

## 6. 未决问题

- [ ] Pass C 的序列化改写细节：WD 烘焙默认值在 AnimatorController 中的位置与 API（参照 AAO Write Defaults pass 实现）
- [ ] Pass C 的"avatar 自己 vs 商品"控制器判定（默认按 Descriptor 的 Base/Gesture 归属 + MA 合并来源）
- [ ] 投影公式在 60k 顶点 × 数百 shape 下的性能（可预计算 `|D_i|²`，O(shapes²) 次点积，一次性）
- [ ] 烤入顶点后与 viseme/blink 系统的交互验证（预期无冲突：雕塑是常量位移，驱动通道照常工作）
