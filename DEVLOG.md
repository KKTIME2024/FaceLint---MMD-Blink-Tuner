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
├── MmdShapeDatabase.cs       (64个标准MMD形状 + 分类)
├── OverCloseDetector.cs      (检测算法)
└── MMDBlendShapeCheckerWindow.cs (UI)
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
