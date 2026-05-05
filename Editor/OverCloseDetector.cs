using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MMDBlendShapeChecker
{
    public enum 检测严重程度
    {
        正常,
        注意,
        警告,
        严重
    }

    public class 检测结果
    {
        public string 形状名称;
        public string 中文说明;
        public MmdShapeCategory 分类;
        public 检测严重程度 严重程度;
        public List<string> 问题详情 = new List<string>();
        public float 主数值;
        public string 匹配原始形状;
        public float 有效权重;
        public float 原始基值;
        public List<string> 影响基值列表 = new List<string>();
    }

    public class 全面检测报告
    {
        public List<检测结果> 所有结果 = new List<检测结果>();
        public List<string> 缺失形状列表 = new List<string>();
        public string 网格名称;
        public string 错误信息;
        public int 总形状数;
        public int MMD形状数;
        public int 存在基值的形状数;
    }

    public static class OverCloseDetector
    {
        private const float 最大检测权重 = 250f;

        public static 全面检测报告 执行全面检测(SkinnedMeshRenderer faceRenderer)
        {
            var report = new 全面检测报告();
            var mesh = faceRenderer.sharedMesh;
            if (mesh == null || mesh.blendShapeCount == 0)
            {
                report.错误信息 = "无法读取网格或网格无blendshape";
                return report;
            }

            report.网格名称 = mesh.name;
            report.总形状数 = mesh.blendShapeCount;

            var neutralV = mesh.vertices;
            var neutralN = mesh.normals;
            int vCount = neutralV.Length;

            // 第1步: 分类所有形状
            var mmdIndices = new List<int>();
            var nativeIndices = new List<int>();
            var nativeWeights = new Dictionary<int, float>();
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string name = mesh.GetBlendShapeName(i);
                float w = faceRenderer.GetBlendShapeWeight(i);
                if (MmdShapeDatabase.名称到信息映射.ContainsKey(name))
                {
                    mmdIndices.Add(i);
                }
                else
                {
                    nativeIndices.Add(i);
                    if (w > 0.001f)
                    {
                        nativeWeights[i] = w;
                        report.存在基值的形状数++;
                    }
                }
            }
            report.MMD形状数 = mmdIndices.Count;

            // 第2步: 计算基值变形 (所有非MMD形状在当前权重下的叠加)
            var baseDeformedV = new Vector3[vCount];
            var baseDeformedN = new Vector3[vCount];
            System.Array.Copy(neutralV, baseDeformedV, vCount);
            System.Array.Copy(neutralN, baseDeformedN, vCount);

            var deltaV = new Vector3[vCount];
            var deltaN = new Vector3[vCount];
            var deltaT = new Vector3[vCount];

            foreach (var kv in nativeWeights)
            {
                int idx = kv.Key;
                float w = kv.Value;
                if (mesh.GetBlendShapeFrameCount(idx) < 1) continue;
                mesh.GetBlendShapeFrameVertices(idx, 0, deltaV, deltaN, deltaT);
                float scale = w / 100f;
                for (int v = 0; v < vCount; v++)
                {
                    baseDeformedV[v].x += deltaV[v].x * scale;
                    baseDeformedV[v].y += deltaV[v].y * scale;
                    baseDeformedV[v].z += deltaV[v].z * scale;
                    baseDeformedN[v].x += deltaN[v].x * scale;
                    baseDeformedN[v].y += deltaN[v].y * scale;
                    baseDeformedN[v].z += deltaN[v].z * scale;
                }
            }

            // 第3步: 用 eye_close / mouth_a 等自适应识别面部区域
            var eyeRegion = 自适应识别眼部区域(mesh, neutralV, baseDeformedV);
            var mouthRegion = 自适应识别嘴部区域(mesh, neutralV, baseDeformedV);
            var browRegion = 自适应识别眉部区域(mesh, neutralV, baseDeformedV, eyeRegion);

            // 第4步: 对每个MMD形状执行检测
            for (int mmdIdx = 0; mmdIdx < mmdIndices.Count; mmdIdx++)
            {
                int i = mmdIndices[mmdIdx];
                string name = mesh.GetBlendShapeName(i);

                if (!MmdShapeDatabase.名称到信息映射.TryGetValue(name, out var info)) continue;
                if (mesh.GetBlendShapeFrameCount(i) < 1) continue;

                mesh.GetBlendShapeFrameVertices(i, 0, deltaV, deltaN, deltaT);

                var result = new 检测结果
                {
                    形状名称 = name,
                    中文说明 = info.中文说明,
                    分类 = info.分类,
                    严重程度 = 检测严重程度.正常
                };

                // 匹配原始形状
                (string matchedName, int matchedIdx, float matchedWeight) = 匹配原始形状(
                    mesh, deltaV, nativeIndices, faceRenderer);
                result.匹配原始形状 = matchedName;
                result.原始基值 = matchedWeight;
                result.有效权重 = 100f + matchedWeight;

                // 收集影响基值
                foreach (var kv in nativeWeights)
                {
                    int ni = kv.Key;
                    if (ni == matchedIdx) continue;
                    string nname = mesh.GetBlendShapeName(ni);
                    float nw = kv.Value;
                    // 只记录同区域的
                    if ((info.分类 & MmdShapeCategory.眼部) != 0 && eyeRegion.包含顶点(ni)) continue; // 太粗糙, 直接用名称匹配
                    result.影响基值列表.Add($"{nname}={nw:F0}");
                }

                // 执行区域检测
                if ((info.分类 & MmdShapeCategory.眼部) != 0 && info.是闭合类)
                {
                    检测眼部(baseDeformedV, baseDeformedN, deltaV, deltaN,
                        info, result, eyeRegion, result.有效权重);
                }

                if ((info.分类 & MmdShapeCategory.嘴部) != 0)
                {
                    检测嘴部(baseDeformedV, baseDeformedN, deltaV, deltaN,
                        info, result, mouthRegion, result.有效权重);
                }

                if ((info.分类 & MmdShapeCategory.眉毛) != 0 && (info.分类 & MmdShapeCategory.眼部) == 0)
                {
                    检测眉毛(baseDeformedV, deltaV, info, result, browRegion, result.有效权重);
                }

                if (result.问题详情.Count == 0)
                    result.问题详情.Add("未检测到异常");

                report.所有结果.Add(result);
            }

            检测缺失形状(mesh, report);
            return report;
        }

        // ===== 自适应区域识别 =====

        private class 眼部区域
        {
            public List<int> 左眼上睑 = new List<int>();
            public List<int> 左眼下睑 = new List<int>();
            public List<int> 右眼上睑 = new List<int>();
            public List<int> 右眼下睑 = new List<int>();
            public List<int> 所有眼睑 = new List<int>();
            public float 中性眼裂;
            public float 眼角宽度;
        }

        private class 嘴部区域
        {
            public List<int> 上唇 = new List<int>();
            public List<int> 下唇 = new List<int>();
            public float 中性唇距;
        }

        private class 眉部区域
        {
            public List<int> 眉顶点 = new List<int>();
        }

        private static 眼部区域 自适应识别眼部区域(Mesh mesh, Vector3[] neutral, Vector3[] baseDeformed)
        {
            var eye = new 眼部区域();

            // 在native形状中找 eye_close 或相似名称
            int refIdx = 查找关键形状(mesh, new[] { "eye_close", "eye_close_L", "eye close",
                "vrc.blink (3.0)", "vrc.blink", "vrc_blink" });
            if (refIdx < 0)
                refIdx = 查找关键形状(mesh, new[] { "vrc.v_blink", "blink", "eye_joy" });

            if (refIdx < 0) return eye;

            float[] mags = new float[neutral.Length];
            mesh.GetBlendShapeFrameVertices(refIdx, 0,
                new Vector3[neutral.Length], new Vector3[neutral.Length], new Vector3[neutral.Length]);
            // Re-read after GetBlendShapeFrameVertices overwrites
            var refDelta = new Vector3[neutral.Length];
            mesh.GetBlendShapeFrameVertices(refIdx, 0, refDelta, new Vector3[neutral.Length], new Vector3[neutral.Length]);

            for (int v = 0; v < neutral.Length; v++)
                mags[v] = refDelta[v].magnitude;

            // 取位移最大的前 15% 顶点作为眼部候选
            float threshold = 取百分位阈值(mags, 0.85f);
            var eyeCandidates = new List<int>();
            for (int v = 0; v < neutral.Length; v++)
                if (mags[v] > threshold) eyeCandidates.Add(v);

            if (eyeCandidates.Count < 10) return eye;

            // 按X分左右眼
            float cx = neutral[eyeCandidates[0]].x;
            foreach (int v in eyeCandidates) cx += neutral[v].x;
            cx /= eyeCandidates.Count;

            foreach (int v in eyeCandidates)
            {
                float x = baseDeformed[v].x;
                float y = baseDeformed[v].y;

                // Y中位数分割上下睑
                var list = x < cx ? (y > 取区域中位数(baseDeformed, eyeCandidates, v => v < cx, v2 => baseDeformed[v2].y)
                    ? eye.左眼上睑 : eye.左眼下睑)
                    : (y > 取区域中位数(baseDeformed, eyeCandidates, v2 => v2 >= cx, v2 => baseDeformed[v2].y)
                    ? eye.右眼上睑 : eye.右眼下睑);

                list.Add(v);
                eye.所有眼睑.Add(v);
            }

            eye.中性眼裂 = (计算眼裂(baseDeformed, eye.左眼上睑, eye.左眼下睑)
                          + 计算眼裂(baseDeformed, eye.右眼上睑, eye.右眼下睑)) / 2f;
            eye.眼角宽度 = 计算眼角宽度(baseDeformed, eye);

            return eye;
        }

        private static 嘴部区域 自适应识别嘴部区域(Mesh mesh, Vector3[] neutral, Vector3[] baseDeformed)
        {
            var mouth = new 嘴部区域();

            int refIdx = 查找关键形状(mesh, new[] { "mouth_a", "mouth_a (no tooth)", "あ",
                "vrc.v_aa", "vrc_v_aa", "mouth_o", "お" });
            if (refIdx < 0) return mouth;

            var refDelta = new Vector3[neutral.Length];
            mesh.GetBlendShapeFrameVertices(refIdx, 0, refDelta, new Vector3[neutral.Length], new Vector3[neutral.Length]);

            float[] mags = new float[neutral.Length];
            for (int v = 0; v < neutral.Length; v++) mags[v] = refDelta[v].magnitude;

            float threshold = 取百分位阈值(mags, 0.85f);
            var candidates = new List<int>();
            for (int v = 0; v < neutral.Length; v++)
                if (mags[v] > threshold) candidates.Add(v);

            if (candidates.Count < 6) return mouth;

            float medianY = 取区域中位数(baseDeformed, candidates, _ => true, v => baseDeformed[v].y);
            foreach (int v in candidates)
            {
                if (baseDeformed[v].y > medianY)
                    mouth.上唇.Add(v);
                else
                    mouth.下唇.Add(v);
            }

            mouth.中性唇距 = 计算眼裂(baseDeformed, mouth.上唇, mouth.下唇);
            return mouth;
        }

        private static 眉部区域 自适应识别眉部区域(Mesh mesh, Vector3[] neutral, Vector3[] baseDeformed, 眼部区域 eye)
        {
            var brow = new 眉部区域();

            int refIdx = 查找关键形状(mesh, new[] { "brow_joy", "brow_anger", "brow_up",
                "上", "怒り", "brow_surprised", "brow_down" });
            if (refIdx < 0) return brow;

            var refDelta = new Vector3[neutral.Length];
            mesh.GetBlendShapeFrameVertices(refIdx, 0, refDelta, new Vector3[neutral.Length], new Vector3[neutral.Length]);

            float[] mags = new float[neutral.Length];
            for (int v = 0; v < neutral.Length; v++) mags[v] = refDelta[v].magnitude;

            float threshold = 取百分位阈值(mags, 0.85f);
            for (int v = 0; v < neutral.Length; v++)
                if (mags[v] > threshold && !eye.所有眼睑.Contains(v))
                    brow.眉顶点.Add(v);

            return brow;
        }

        private static int 查找关键形状(Mesh mesh, string[] candidates)
        {
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string name = mesh.GetBlendShapeName(i).ToLower().Replace(" ", "_");
                foreach (var c in candidates)
                    if (name == c.ToLower().Replace(" ", "_") && mesh.GetBlendShapeFrameCount(i) >= 1)
                        return i;
            }
            return -1;
        }

        private static float 取百分位阈值(float[] values, float percentile)
        {
            var sorted = new List<float>(values);
            sorted.Sort();
            int idx = Mathf.Clamp((int)(sorted.Count * percentile), 0, sorted.Count - 1);
            return sorted[idx];
        }

        private static float 取区域中位数(Vector3[] positions, List<int> indices,
            System.Func<int, bool> filter, System.Func<int, float> selector)
        {
            var vals = new List<float>();
            foreach (int i in indices)
                if (filter(i)) vals.Add(selector(i));
            if (vals.Count == 0) return 0;
            vals.Sort();
            return vals[vals.Count / 2];
        }

        private static float 计算眼裂(Vector3[] verts, List<int> upper, List<int> lower)
        {
            if (upper.Count < 1 || lower.Count < 1) return 0;
            float uMin = float.MaxValue;
            foreach (int i in upper) if (verts[i].y < uMin) uMin = verts[i].y;
            float lMax = float.MinValue;
            foreach (int i in lower) if (verts[i].y > lMax) lMax = verts[i].y;
            return uMin - lMax;
        }

        private static float 计算眼角宽度(Vector3[] verts, 眼部区域 eye)
        {
            float lMin = float.MaxValue, lMax = float.MinValue;
            float rMin = float.MaxValue, rMax = float.MinValue;
            foreach (int i in eye.左眼上睑.Concat(eye.左眼下睑))
            {
                if (verts[i].x < lMin) lMin = verts[i].x;
                if (verts[i].x > lMax) lMax = verts[i].x;
            }
            foreach (int i in eye.右眼上睑.Concat(eye.右眼下睑))
            {
                if (verts[i].x < rMin) rMin = verts[i].x;
                if (verts[i].x > rMax) rMax = verts[i].x;
            }
            return ((lMax - lMin) + (rMax - rMin)) / 2f;
        }

        // ===== 形状匹配 =====

        private static (string name, int index, float weight) 匹配原始形状(
            Mesh mesh, Vector3[] mmdDelta, List<int> nativeIndices, SkinnedMeshRenderer faceRenderer)
        {
            var nativeDelta = new Vector3[mmdDelta.Length];
            var bestIdx = -1;
            float bestOverlap = 0f;

            foreach (int ni in nativeIndices)
            {
                if (mesh.GetBlendShapeFrameCount(ni) < 1) continue;
                mesh.GetBlendShapeFrameVertices(ni, 0, nativeDelta, new Vector3[mmdDelta.Length], new Vector3[mmdDelta.Length]);

                int matchingVerts = 0;
                for (int v = 0; v < mmdDelta.Length; v++)
                {
                    float dx = mmdDelta[v].x - nativeDelta[v].x;
                    float dy = mmdDelta[v].y - nativeDelta[v].y;
                    float dz = mmdDelta[v].z - nativeDelta[v].z;
                    if (dx * dx + dy * dy + dz * dz < 0.000001f) matchingVerts++;
                }
                float overlap = (float)matchingVerts / mmdDelta.Length;
                if (overlap > bestOverlap && overlap > 0.95f)
                {
                    bestOverlap = overlap;
                    bestIdx = ni;
                }
            }

            if (bestIdx >= 0)
            {
                string name = mesh.GetBlendShapeName(bestIdx);
                float w = faceRenderer.GetBlendShapeWeight(bestIdx);
                return (name, bestIdx, w);
            }
            return ("", -1, 0);
        }

        // ===== 眼睑配对 =====

        private struct 眼睑配对 { public int 上; public int 下; public float 上Y; public float 下Y; }

        private static List<眼睑配对> 建立配对(Vector3[] verts, List<int> upper, List<int> lower, int buckets)
        {
            var pairs = new List<眼睑配对>();
            if (upper.Count < 2 || lower.Count < 2) return pairs;

            var all = new List<int>(); all.AddRange(upper); all.AddRange(lower);
            float minX = float.MaxValue, maxX = float.MinValue;
            foreach (int i in all) { if (verts[i].x < minX) minX = verts[i].x; if (verts[i].x > maxX) maxX = verts[i].x; }
            float range = maxX - minX;
            if (range < 0.0001f) return pairs;

            for (int b = 0; b < buckets; b++)
            {
                float lo = minX + range * b / buckets;
                float hi = minX + range * (b + 1) / buckets;

                int bestU = -1; float uMin = float.MaxValue;
                foreach (int i in upper)
                    if (verts[i].x >= lo && (verts[i].x < hi || b == buckets - 1))
                        if (verts[i].y < uMin) { uMin = verts[i].y; bestU = i; }

                int bestL = -1; float lMax = float.MinValue;
                foreach (int i in lower)
                    if (verts[i].x >= lo && (verts[i].x < hi || b == buckets - 1))
                        if (verts[i].y > lMax) { lMax = verts[i].y; bestL = i; }

                if (bestU >= 0 && bestL >= 0)
                    pairs.Add(new 眼睑配对 { 上 = bestU, 下 = bestL, 上Y = uMin, 下Y = lMax });
            }
            return pairs;
        }

        // ===== 眼部检测 =====

        private static void 检测眼部(Vector3[] baseV, Vector3[] baseN,
            Vector3[] deltaV, Vector3[] deltaN,
            MmdShapeInfo info, 检测结果 result, 眼部区域 eye, float effectiveWeight)
        {
            if (eye.左眼上睑.Count < 3 || eye.左眼下睑.Count < 3) return;
            if (eye.右眼上睑.Count < 3 || eye.右眼下睑.Count < 3) return;

            int vCount = baseV.Length;
            var currentV = new Vector3[vCount];
            var currentN = new Vector3[vCount];

            float safeDist = eye.中性眼裂;
            if (safeDist < 0.0001f) safeDist = 0.01f;

            float maxNegDepth = 0f;
            float maxStrain = 0f;
            int negPairs = 0, totalPairs = 0;
            int extrapNegMax = 0;

            // 在 base_deformed + mmd_delta*(w/100) 状态下检测
            float[] testWeights = { effectiveWeight, effectiveWeight * 1.2f, effectiveWeight * 1.5f, effectiveWeight * 2.0f };
            for (int tw = 0; tw < testWeights.Length; tw++)
            {
                float w = Mathf.Min(testWeights[tw], 最大检测权重);
                float scale = w / 100f;

                for (int v = 0; v < vCount; v++)
                {
                    currentV[v].x = baseV[v].x + deltaV[v].x * scale;
                    currentV[v].y = baseV[v].y + deltaV[v].y * scale;
                    currentV[v].z = baseV[v].z + deltaV[v].z * scale;
                }

                var lPairs = 建立配对(currentV, eye.左眼上睑, eye.左眼下睑, 8);
                var rPairs = 建立配对(currentV, eye.右眼上睑, eye.右眼下睑, 8);

                int wNeg = 0;
                float wDepth = 0f;

                foreach (var p in lPairs.Concat(rPairs))
                {
                    if (tw == 0) totalPairs++;
                    float gap = currentV[p.上].y - currentV[p.下].y;
                    if (gap < 0)
                    {
                        if (tw == 0) { negPairs++; }
                        wNeg++;
                        float d = -gap;
                        if (d > wDepth) wDepth = d;
                        if (tw == 0 && d > maxNegDepth) maxNegDepth = d;
                    }
                }
                if (wNeg > extrapNegMax) extrapNegMax = wNeg;

                // 应变
                if (tw == 0)
                {
                    float sumMag = 0f;
                    foreach (int i in eye.所有眼睑) sumMag += deltaV[i].magnitude * scale;
                    maxStrain = sumMag / eye.所有眼睑.Count / safeDist;
                }
            }

            // 法线反转 (at effectiveWeight)
            {
                float scale = effectiveWeight / 100f;
                int flipped = 0;
                foreach (int i in eye.所有眼睑)
                {
                    float nx = baseN[i].x + deltaN[i].x * scale;
                    float ny = baseN[i].y + deltaN[i].y * scale;
                    float nz = baseN[i].z + deltaN[i].z * scale;
                    float dot = baseN[i].x * nx + baseN[i].y * ny + baseN[i].z * nz;
                    if (dot < 0) flipped++;
                }
                if (flipped > 3)
                {
                    result.严重程度 = 检测严重程度.严重;
                    result.问题详情.Add($"法线反转: {flipped} 个顶点法线翻转");
                }
            }

            // 侧向挤压
            float baseWidth = eye.眼角宽度;
            {
                float scale = effectiveWeight / 100f;
                float lMin = float.MaxValue, lMax = float.MinValue;
                float rMin = float.MaxValue, rMax = float.MinValue;
                foreach (int i in eye.左眼上睑.Concat(eye.左眼下睑))
                {
                    float x = baseV[i].x + deltaV[i].x * scale;
                    if (x < lMin) lMin = x; if (x > lMax) lMax = x;
                }
                foreach (int i in eye.右眼上睑.Concat(eye.右眼下睑))
                {
                    float x = baseV[i].x + deltaV[i].x * scale;
                    if (x < rMin) rMin = x; if (x > rMax) rMax = x;
                }
                float defWidth = ((lMax - lMin) + (rMax - rMin)) / 2f;
                float lateral = baseWidth > 0.0001f ? (baseWidth - defWidth) / baseWidth : 0;
                if (lateral > 0.15f)
                {
                    if (result.严重程度 < 检测严重程度.警告) result.严重程度 = 检测严重程度.警告;
                    result.问题详情.Add($"眼角侧向挤压: {lateral * 100f:F0}%");
                }
            }

            result.主数值 = maxStrain;

            if (negPairs > 0)
            {
                result.严重程度 = 检测严重程度.严重;
                result.问题详情.Add($"眼睑穿透: {negPairs}/{totalPairs} 检测点, 深度 {maxNegDepth * 1000f:F1}mm");
            }

            if (extrapNegMax > 0 && negPairs == 0)
            {
                if (result.严重程度 < 检测严重程度.警告) result.严重程度 = 检测严重程度.警告;
                result.问题详情.Add($"超限穿透: 权重>{effectiveWeight:F0}% 时最多 {extrapNegMax} 处穿透");
            }

            if (maxStrain > 1.5f)
            {
                if (result.严重程度 < 检测严重程度.警告) result.严重程度 = 检测严重程度.警告;
                result.问题详情.Add($"应变超标: {maxStrain:F1}x (阈值 1.5x)");
            }
            else if (maxStrain > 1.2f)
            {
                if (result.严重程度 < 检测严重程度.注意) result.严重程度 = 检测严重程度.注意;
                result.问题详情.Add($"应变偏高: {maxStrain:F1}x");
            }
        }

        // ===== 嘴部检测 =====

        private static void 检测嘴部(Vector3[] baseV, Vector3[] baseN,
            Vector3[] deltaV, Vector3[] deltaN,
            MmdShapeInfo info, 检测结果 result, 嘴部区域 mouth, float effectiveWeight)
        {
            if (mouth.上唇.Count < 3 || mouth.下唇.Count < 3) return;

            int vCount = baseV.Length;
            var currentV = new Vector3[vCount];
            float scale = effectiveWeight / 100f;
            for (int v = 0; v < vCount; v++)
            {
                currentV[v].x = baseV[v].x + deltaV[v].x * scale;
                currentV[v].y = baseV[v].y + deltaV[v].y * scale;
                currentV[v].z = baseV[v].z + deltaV[v].z * scale;
            }

            var pairs = 建立配对(currentV, mouth.上唇, mouth.下唇, 7);
            int negCount = 0;
            float maxDepth = 0f;
            foreach (var p in pairs)
            {
                float gap = currentV[p.上].y - currentV[p.下].y;
                if (gap < 0) { negCount++; float d = -gap; if (d > maxDepth) maxDepth = d; }
            }

            // 法线
            int flipped = 0;
            foreach (int i in mouth.上唇.Concat(mouth.下唇))
            {
                float nx = baseN[i].x + deltaN[i].x * scale;
                float ny = baseN[i].y + deltaN[i].y * scale;
                float nz = baseN[i].z + deltaN[i].z * scale;
                if (baseN[i].x * nx + baseN[i].y * ny + baseN[i].z * nz < 0) flipped++;
            }

            result.主数值 = maxDepth;

            if (flipped > 3)
            {
                result.严重程度 = 检测严重程度.严重;
                result.问题详情.Add($"法线反转: {flipped} 个嘴部顶点翻转");
            }

            if (info.是闭合类 && negCount > 0)
            {
                result.严重程度 = 检测严重程度.严重;
                result.问题详情.Add($"嘴唇穿透: {negCount}/{pairs.Count} 检测点, 深度 {maxDepth * 1000f:F1}mm");
            }

            // 外推
            int extrapNeg = 0;
            for (float ew = effectiveWeight * 1.5f; ew <= 最大检测权重; ew += 50f)
            {
                float es = ew / 100f;
                for (int v = 0; v < vCount; v++)
                {
                    currentV[v].x = baseV[v].x + deltaV[v].x * es;
                    currentV[v].y = baseV[v].y + deltaV[v].y * es;
                    currentV[v].z = baseV[v].z + deltaV[v].z * es;
                }
                foreach (var p in pairs)
                    if (currentV[p.上].y < currentV[p.下].y) extrapNeg++;
            }
            if (extrapNeg > 0 && negCount == 0)
            {
                if (result.严重程度 < 检测严重程度.警告) result.严重程度 = 检测严重程度.警告;
                result.问题详情.Add($"超限穿透: 权重>{effectiveWeight:F0}%时 {extrapNeg} 处穿透");
            }

            if (result.问题详情.Count == 0)
                result.问题详情.Add($"嘴部变形正常");
        }

        // ===== 眉毛检测 =====

        private static void 检测眉毛(Vector3[] baseV, Vector3[] deltaV,
            MmdShapeInfo info, 检测结果 result, 眉部区域 brow, float effectiveWeight)
        {
            if (brow.眉顶点.Count < 3) return;

            float scale = effectiveWeight / 100f;
            float maxMag = 0f;
            foreach (int i in brow.眉顶点)
            {
                float m = deltaV[i].magnitude * scale;
                if (m > maxMag) maxMag = m;
            }

            // 用 bounding box 参考
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (int i in brow.眉顶点) { if (baseV[i].y < minY) minY = baseV[i].y; if (baseV[i].y > maxY) maxY = baseV[i].y; }
            float h = maxY - minY;
            float ratio = h > 0.0001f ? maxMag / h * 100f : 0;
            result.主数值 = maxMag;

            if (ratio > 25f)
            {
                result.严重程度 = 检测严重程度.警告;
                result.问题详情.Add($"眉毛位移超标: {ratio:F0}% 区域高度");
            }
            else if (result.问题详情.Count == 0)
                result.问题详情.Add($"眉毛位移正常");
        }

        // ===== 缺失检测 =====

        private static void 检测缺失形状(Mesh mesh, 全面检测报告 report)
        {
            var names = new HashSet<string>();
            for (int i = 0; i < mesh.blendShapeCount; i++) names.Add(mesh.GetBlendShapeName(i));
            foreach (var info in MmdShapeDatabase.标准形状列表)
                if (!names.Contains(info.日文名))
                    report.缺失形状列表.Add($"{info.日文名} ({info.中文说明})");
        }
    }
}
