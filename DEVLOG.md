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

