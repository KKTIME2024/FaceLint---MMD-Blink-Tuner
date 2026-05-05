using System;
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
    }

    public class 全面检测报告
    {
        public List<检测结果> 所有结果 = new List<检测结果>();
        public List<string> 缺失形状列表 = new List<string>();
        public string 网格名称;
        public string 错误信息;
        public int 总形状数;
        public int MMD形状数;
    }

    public static class OverCloseDetector
    {
        private const float 最大检测权重 = 200f;
        private const float 外推步进 = 20f;

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
            var faceBounds = 计算面部边界(neutralV);

            var 眼部区域 = new 眼部区域信息();
            var 嘴部区域 = new 嘴部区域信息();
            var 眉部区域 = new 眉部区域信息();

            分析顶点区域(neutralV, faceBounds, 眼部区域, 嘴部区域, 眉部区域);

            var deltaV = new Vector3[neutralV.Length];
            var deltaN = new Vector3[neutralV.Length];
            var deltaT = new Vector3[neutralV.Length];

            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string name = mesh.GetBlendShapeName(i);

                if (MmdShapeDatabase.名称到信息映射.TryGetValue(name, out var info))
                {
                    report.MMD形状数++;
                    int frameCount = mesh.GetBlendShapeFrameCount(i);
                    if (frameCount < 1) continue;

                    mesh.GetBlendShapeFrameVertices(i, 0, deltaV, deltaN, deltaT);

                    var result = new 检测结果
                    {
                        形状名称 = name,
                        中文说明 = info.中文说明,
                        分类 = info.分类,
                        严重程度 = 检测严重程度.正常
                    };

                    if ((info.分类 & MmdShapeCategory.眼部) != 0)
                    {
                        检测眼部(neutralV, neutralN, deltaV, deltaN, info, result, 眼部区域, faceBounds);
                    }

                    if ((info.分类 & MmdShapeCategory.嘴部) != 0)
                    {
                        检测嘴部(neutralV, neutralN, deltaV, deltaN, info, result, 嘴部区域, faceBounds);
                    }

                    if ((info.分类 & MmdShapeCategory.眉毛) != 0 && (info.分类 & MmdShapeCategory.眼部) == 0)
                    {
                        检测眉毛(neutralV, deltaV, deltaN, info, result, 眉部区域, faceBounds);
                    }

                    if (result.问题详情.Count == 0)
                    {
                        result.问题详情.Add("未检测到异常");
                    }

                    report.所有结果.Add(result);
                }
            }

            检测缺失形状(mesh, report);
            return report;
        }

        // ===== 区域分析 =====

        private static Bounds 计算面部边界(Vector3[] verts)
        {
            var b = new Bounds(verts[0], Vector3.zero);
            foreach (var v in verts) b.Encapsulate(v);
            return b;
        }

        private class 眼部区域信息
        {
            public List<int> 左眼上睑 = new List<int>();
            public List<int> 左眼下睑 = new List<int>();
            public List<int> 右眼上睑 = new List<int>();
            public List<int> 右眼下睑 = new List<int>();
            public float 中性平均眼裂;
            public float 中性眼角宽度;
        }

        private class 嘴部区域信息
        {
            public List<int> 上唇 = new List<int>();
            public List<int> 下唇 = new List<int>();
            public float 中性平均唇距;
        }

        private class 眉部区域信息
        {
            public List<int> 眉顶点 = new List<int>();
            public float 中性眉高;
        }

        private static void 分析顶点区域(Vector3[] neutral, Bounds bounds,
            眼部区域信息 eye, 嘴部区域信息 mouth, 眉部区域信息 brow)
        {
            float faceH = bounds.size.y;
            float faceW = bounds.size.x;
            float minY = bounds.min.y;
            float minX = bounds.min.x;
            float cx = bounds.center.x;
            float cz = bounds.center.z;

            for (int i = 0; i < neutral.Length; i++)
            {
                var v = neutral[i];
                float yR = (v.y - minY) / faceH;
                float xAbs = Mathf.Abs(v.x - cx);
                float xR = xAbs / (faceW * 0.5f);

                if (v.z < cz - faceW * 0.25f) continue;

                // 眉部: Y=65%~88%
                if (yR > 0.65f && yR < 0.88f && xR < 0.52f)
                    brow.眉顶点.Add(i);

                // 眼部: Y=50%~72%, X=3%~40%
                if (yR > 0.50f && yR < 0.72f && xR > 0.03f && xR < 0.40f)
                {
                    if (v.x < cx)
                    {
                        if (yR > 0.58f) eye.左眼上睑.Add(i);
                        else eye.左眼下睑.Add(i);
                    }
                    else
                    {
                        if (yR > 0.58f) eye.右眼上睑.Add(i);
                        else eye.右眼下睑.Add(i);
                    }
                }

                // 嘴部: Y=25%~48%, X<38%
                if (yR > 0.25f && yR < 0.48f && xR < 0.38f)
                {
                    if (yR > 0.36f) mouth.上唇.Add(i);
                    else mouth.下唇.Add(i);
                }
            }

            eye.中性平均眼裂 = 计算平均眼裂(neutral, eye.左眼上睑, eye.左眼下睑)
                              + 计算平均眼裂(neutral, eye.右眼上睑, eye.右眼下睑);
            eye.中性平均眼裂 /= 2f;
            eye.中性眼角宽度 = 计算眼角宽度(neutral, eye);

            mouth.中性平均唇距 = 计算平均唇距(neutral, mouth.上唇, mouth.下唇);

            if (brow.眉顶点.Count > 0)
            {
                float sum = 0;
                foreach (int i in brow.眉顶点) sum += neutral[i].y;
                brow.中性眉高 = sum / brow.眉顶点.Count;
            }
        }

        // ===== 眼部检测 =====

        private static void 检测眼部(Vector3[] neutral, Vector3[] neutralN,
            Vector3[] deltaV, Vector3[] deltaN,
            MmdShapeInfo info, 检测结果 result, 眼部区域信息 eye, Bounds faceBounds)
        {
            if (!info.是闭合类) return;

            bool hasData = (eye.左眼上睑.Count >= 3 && eye.左眼下睑.Count >= 3)
                        || (eye.右眼上睑.Count >= 3 && eye.右眼下睑.Count >= 3);
            if (!hasData) return;

            float safeCloseDist = eye.中性平均眼裂 * 1.0f;

            float maxNegDepth = 0f;
            float maxStrain = 0f;
            int maxNegBucketCount = 0;
            int pairTotal = 0;
            int negPairCount = 0;

            // 分桶配对检测：按X轴等分为8个桶
            int bucketCount = 8;

            var leftPairs = 建立眼睑配对(neutral, eye.左眼上睑, eye.左眼下睑, bucketCount);
            var rightPairs = 建立眼睑配对(neutral, eye.右眼上睑, eye.右眼下睑, bucketCount);

            foreach (var pairs in new[] { leftPairs, rightPairs })
            {
                foreach (var pair in pairs)
                {
                    if (pair.上Y < pair.下Y) continue; // 无效配对
                    float neutralGap = pair.上Y - pair.下Y;
                    if (neutralGap <= 0.001f) continue;

                    pairTotal++;
                    float upperDisplaced = neutral[pair.上顶点].y + deltaV[pair.上顶点].y;
                    float lowerDisplaced = neutral[pair.下顶点].y + deltaV[pair.下顶点].y;
                    float gap100 = upperDisplaced - lowerDisplaced;

                    if (gap100 < 0)
                    {
                        negPairCount++;
                        float depth = -gap100;
                        if (depth > maxNegDepth) maxNegDepth = depth;
                    }
                }
            }

            // 100% 权重应变计算
            {
                float totalDisp = 0f;
                int dispCount = 0;
                var allEyeVerts = eye.左眼上睑.Concat(eye.右眼上睑).Concat(eye.左眼下睑).Concat(eye.右眼下睑);
                foreach (var idx in allEyeVerts)
                {
                    totalDisp += deltaV[idx].magnitude;
                    dispCount++;
                }
                if (dispCount > 0 && safeCloseDist > 0.0001f)
                {
                    maxStrain = (totalDisp / dispCount) / safeCloseDist;
                }
            }

            // 外推检测: 120%, 150%, 200%
            float[] extrapWeights = { 120f, 150f, 200f };
            foreach (float w in extrapWeights)
            {
                float scale = w / 100f;
                int wNegCount = 0;

                foreach (var pairs in new[] { leftPairs, rightPairs })
                {
                    foreach (var pair in pairs)
                    {
                        if (pair.上Y < pair.下Y) continue;
                        float neutralGap = pair.上Y - pair.下Y;
                        if (neutralGap <= 0.001f) continue;

                        float upperExtrap = neutral[pair.上顶点].y + deltaV[pair.上顶点].y * scale;
                        float lowerExtrap = neutral[pair.下顶点].y + deltaV[pair.下顶点].y * scale;
                        float gapExtrap = upperExtrap - lowerExtrap;

                        if (gapExtrap < 0) wNegCount++;
                    }
                }

                if (wNegCount > maxNegBucketCount) maxNegBucketCount = wNegCount;

                float totalDisp = 0f;
                int dispCount = 0;
                foreach (var idx in eye.左眼上睑.Concat(eye.右眼上睑).Concat(eye.左眼下睑).Concat(eye.右眼下睑))
                {
                    totalDisp += deltaV[idx].magnitude * scale;
                    dispCount++;
                }
                if (dispCount > 0 && safeCloseDist > 0.0001f)
                {
                    float strain = (totalDisp / dispCount) / safeCloseDist;
                    if (strain > maxStrain) maxStrain = strain;
                }
            }

            // 法线反转检测 (at weight 100)
            int flippedNormals = 0;
            foreach (var idx in eye.左眼上睑.Concat(eye.右眼上睑).Concat(eye.左眼下睑).Concat(eye.右眼下睑))
            {
                var origN = neutralN[idx].normalized;
                var deformedN = (neutralN[idx] + deltaN[idx]).normalized;
                float dot = Vector3.Dot(origN, deformedN);
                if (dot < 0f) flippedNormals++;
            }

            // 侧向挤压检测
            float neutralCornersW = eye.中性眼角宽度;
            float deformedCornersW = 计算眼角宽度(neutral, deltaV, eye);
            float lateralStrain = neutralCornersW > 0.0001f
                ? (neutralCornersW - deformedCornersW) / neutralCornersW : 0f;

            // --- 判定 ---
            result.主数值 = maxStrain;

            // 法线反转 → 最严重
            if (flippedNormals > 3)
            {
                result.严重程度 = 检测严重程度.严重;
                result.问题详情.Add($"法线反转: {flippedNormals} 个眼睑顶点法线翻转(渲染黑斑)");
            }

            if (info.是闭合类 && pairTotal > 0 && negPairCount > 0)
            {
                result.严重程度 = Max(result.严重程度, 检测严重程度.严重);
                result.问题详情.Add($"眼睑穿透(100%权重): {negPairCount}/{pairTotal} 个检测点穿透, 最大深度 {maxNegDepth * 1000f:F1}mm");
            }

            if (info.是闭合类 && maxNegBucketCount > 0)
            {
                if (result.严重程度 < 检测严重程度.严重)
                    result.严重程度 = 检测严重程度.警告;
                result.问题详情.Add($"超限外推穿透: 权重>100%时最多 {maxNegBucketCount} 处穿透(模拟至 {最大检测权重:F0}%)");
            }

            float closureRate = pairTotal > 0 ? (float)negPairCount / pairTotal * 100f : 0f;
            if (info.是闭合类 && closureRate == 0f && pairTotal > 0)
            {
                // 检查是否接近穿透(闭合率接近100%但未侵入)
                int nearCount = 0;
                foreach (var pairs in new[] { leftPairs, rightPairs })
                {
                    foreach (var pair in pairs)
                    {
                        if (pair.上Y < pair.下Y) continue;
                        float neutralGap = pair.上Y - pair.下Y;
                        if (neutralGap <= 0.001f) continue;
                        float upperD = neutral[pair.上顶点].y + deltaV[pair.上顶点].y;
                        float lowerD = neutral[pair.下顶点].y + deltaV[pair.下顶点].y;
                        float gap = upperD - lowerD;
                        if (gap < neutralGap * 0.05f) nearCount++;
                    }
                }
                if (nearCount > pairTotal * 0.6f)
                {
                    if (result.严重程度 < 检测严重程度.警告)
                        result.严重程度 = 检测严重程度.警告;
                    result.问题详情.Add($"高度闭合: {nearCount}/{pairTotal} 检测点已闭合 (接近穿透边界)");
                }
            }

            if (maxStrain > 1.5f)
            {
                result.严重程度 = Max(result.严重程度, 检测严重程度.警告);
                result.问题详情.Add($"应变超标: 位移/安全闭合 = {maxStrain:F1}x (阈值1.5x)");
            }
            else if (maxStrain > 1.2f)
            {
                if (result.严重程度 < 检测严重程度.注意)
                    result.严重程度 = 检测严重程度.注意;
                result.问题详情.Add($"应变偏高: 位移/安全闭合 = {maxStrain:F1}x");
            }

            if (lateralStrain < -0.15f)
            {
                if (result.严重程度 < 检测严重程度.警告)
                    result.严重程度 = 检测严重程度.警告;
                result.问题详情.Add($"眼角侧向挤压: {lateralStrain * -100f:F0}%宽度收缩");
            }

            if (result.问题详情.Count == 0 && info.是闭合类)
            {
                result.问题详情.Add($"眼睑闭合正常 (检测 {pairTotal} 点, 应变 {maxStrain:F2}x)");
            }
        }

        private struct 眼睑配对
        {
            public int 上顶点;
            public int 下顶点;
            public float 上Y;
            public float 下Y;
        }

        private static List<眼睑配对> 建立眼睑配对(Vector3[] neutral,
            List<int> upperLid, List<int> lowerLid, int bucketCount)
        {
            var pairs = new List<眼睑配对>();
            if (upperLid.Count < 2 || lowerLid.Count < 2) return pairs;

            float minX = float.MaxValue, maxX = float.MinValue;
            foreach (int i in upperLid.Concat(lowerLid))
            {
                if (neutral[i].x < minX) minX = neutral[i].x;
                if (neutral[i].x > maxX) maxX = neutral[i].x;
            }
            float range = maxX - minX;
            if (range < 0.0001f) return pairs;

            for (int b = 0; b < bucketCount; b++)
            {
                float xLo = minX + range * b / bucketCount;
                float xHi = minX + range * (b + 1) / bucketCount;

                // 找到该桶内上睑最下点和下睑最上点
                int bestU = -1;
                float uMinY = float.MaxValue;
                foreach (int i in upperLid)
                {
                    float x = neutral[i].x;
                    if (x >= xLo && x < xHi || (b == bucketCount - 1 && x >= xLo && x <= xHi + 0.001f))
                    {
                        if (neutral[i].y < uMinY) { uMinY = neutral[i].y; bestU = i; }
                    }
                }

                int bestL = -1;
                float lMaxY = float.MinValue;
                foreach (int i in lowerLid)
                {
                    float x = neutral[i].x;
                    if (x >= xLo && x < xHi || (b == bucketCount - 1 && x >= xLo && x <= xHi + 0.001f))
                    {
                        if (neutral[i].y > lMaxY) { lMaxY = neutral[i].y; bestL = i; }
                    }
                }

                if (bestU >= 0 && bestL >= 0)
                {
                    pairs.Add(new 眼睑配对
                    {
                        上顶点 = bestU,
                        下顶点 = bestL,
                        上Y = uMinY,
                        下Y = lMaxY
                    });
                }
            }
            return pairs;
        }

        private static float 计算平均眼裂(Vector3[] neutral, List<int> upper, List<int> lower)
        {
            if (upper.Count < 1 || lower.Count < 1) return 0f;
            float uMin = float.MaxValue;
            foreach (int i in upper) if (neutral[i].y < uMin) uMin = neutral[i].y;
            float lMax = float.MinValue;
            foreach (int i in lower) if (neutral[i].y > lMax) lMax = neutral[i].y;
            return uMin - lMax;
        }

        private static float 计算眼角宽度(Vector3[] neutral, 眼部区域信息 eye)
        {
            float lMinX = float.MaxValue, lMaxX = float.MinValue;
            float rMinX = float.MaxValue, rMaxX = float.MinValue;
            foreach (int i in eye.左眼上睑.Concat(eye.左眼下睑))
            {
                if (neutral[i].x < lMinX) lMinX = neutral[i].x;
                if (neutral[i].x > lMaxX) lMaxX = neutral[i].x;
            }
            foreach (int i in eye.右眼上睑.Concat(eye.右眼下睑))
            {
                if (neutral[i].x < rMinX) rMinX = neutral[i].x;
                if (neutral[i].x > rMaxX) rMaxX = neutral[i].x;
            }
            return (lMaxX - lMinX + rMaxX - rMinX) / 2f;
        }

        private static float 计算眼角宽度(Vector3[] neutral, Vector3[] delta, 眼部区域信息 eye)
        {
            float lMinX = float.MaxValue, lMaxX = float.MinValue;
            float rMinX = float.MaxValue, rMaxX = float.MinValue;
            foreach (int i in eye.左眼上睑.Concat(eye.左眼下睑))
            {
                float x = neutral[i].x + delta[i].x;
                if (x < lMinX) lMinX = x;
                if (x > lMaxX) lMaxX = x;
            }
            foreach (int i in eye.右眼上睑.Concat(eye.右眼下睑))
            {
                float x = neutral[i].x + delta[i].x;
                if (x < rMinX) rMinX = x;
                if (x > rMaxX) rMaxX = x;
            }
            return (lMaxX - lMinX + rMaxX - rMinX) / 2f;
        }

        // ===== 嘴部检测 =====

        private static void 检测嘴部(Vector3[] neutral, Vector3[] neutralN,
            Vector3[] deltaV, Vector3[] deltaN,
            MmdShapeInfo info, 检测结果 result, 嘴部区域信息 mouth, Bounds faceBounds)
        {
            if (mouth.上唇.Count < 3 || mouth.下唇.Count < 3) return;

            float faceH = faceBounds.size.y;
            int bucketCount = 7;
            var pairs = 建立唇部配对(neutral, mouth.上唇, mouth.下唇, bucketCount);

            int negCount = 0;
            float maxNegDepth = 0f;
            int totalPairs = 0;
            float maxMag100 = 0f;

            foreach (var pair in pairs)
            {
                if (pair.上Y < pair.下Y) continue;
                totalPairs++;
                float upperD = neutral[pair.上顶点].y + deltaV[pair.上顶点].y;
                float lowerD = neutral[pair.下顶点].y + deltaV[pair.下顶点].y;
                float gap = upperD - lowerD;
                if (gap < 0)
                {
                    negCount++;
                    float d = -gap;
                    if (d > maxNegDepth) maxNegDepth = d;
                }
                float mag = deltaV[pair.上顶点].magnitude + deltaV[pair.下顶点].magnitude;
                if (mag > maxMag100) maxMag100 = mag;
            }

            // 位移幅度
            float dispRatio = maxMag100 / faceH * 100f;

            // 外推检测
            int extrapNegCount = 0;
            float extrapMaxDepth = 0f;
            foreach (float w in new[] { 150f, 200f })
            {
                float scale = w / 100f;
                foreach (var pair in pairs)
                {
                    if (pair.上Y < pair.下Y) continue;
                    float upperE = neutral[pair.上顶点].y + deltaV[pair.上顶点].y * scale;
                    float lowerE = neutral[pair.下顶点].y + deltaV[pair.下顶点].y * scale;
                    if (upperE < lowerE)
                    {
                        extrapNegCount++;
                        float d = lowerE - upperE;
                        if (d > extrapMaxDepth) extrapMaxDepth = d;
                    }
                }
            }

            // 法线反转
            int flipped = 0;
            foreach (int i in mouth.上唇.Concat(mouth.下唇))
            {
                var on = neutralN[i].normalized;
                var dn = (neutralN[i] + deltaN[i]).normalized;
                if (Vector3.Dot(on, dn) < 0f) flipped++;
            }

            result.主数值 = maxNegDepth;

            if (flipped > 3)
            {
                result.严重程度 = 检测严重程度.严重;
                result.问题详情.Add($"法线反转: {flipped} 个嘴部顶点法线翻转");
            }

            if (info.是闭合类 && negCount > 0)
            {
                result.严重程度 = Max(result.严重程度, 检测严重程度.严重);
                result.问题详情.Add($"嘴唇穿透(100%): {negCount}/{totalPairs} 检测点, 深度 {maxNegDepth * 1000f:F1}mm");
            }

            if (info.是闭合类 && extrapNegCount > 0)
            {
                if (result.严重程度 < 检测严重程度.警告)
                    result.严重程度 = 检测严重程度.警告;
                result.问题详情.Add($"超限穿透: 权重>100%时 {extrapNegCount} 处穿透");
            }

            if (dispRatio > 15f)
            {
                if (result.严重程度 < 检测严重程度.警告)
                    result.严重程度 = 检测严重程度.警告;
                result.问题详情.Add($"位移过大: {dispRatio:F1}%面高");
            }

            if (result.问题详情.Count == 0)
            {
                result.问题详情.Add($"嘴部变形正常 (位移 {dispRatio:F1}%面高)");
            }
        }

        private static List<眼睑配对> 建立唇部配对(Vector3[] neutral,
            List<int> upper, List<int> lower, int bucketCount)
        {
            var pairs = new List<眼睑配对>();
            if (upper.Count < 2 || lower.Count < 2) return pairs;
            float minX = float.MaxValue, maxX = float.MinValue;
            foreach (int i in upper.Concat(lower))
            {
                if (neutral[i].x < minX) minX = neutral[i].x;
                if (neutral[i].x > maxX) maxX = neutral[i].x;
            }
            float range = maxX - minX;
            if (range < 0.0001f) return pairs;
            for (int b = 0; b < bucketCount; b++)
            {
                float xLo = minX + range * b / bucketCount;
                float xHi = minX + range * (b + 1) / bucketCount;
                int bestU = -1;
                float uMinY = float.MaxValue;
                foreach (int i in upper)
                {
                    float x = neutral[i].x;
                    if (x >= xLo && x < xHi || (b == bucketCount - 1 && x >= xLo))
                    {
                        if (neutral[i].y < uMinY) { uMinY = neutral[i].y; bestU = i; }
                    }
                }
                int bestL = -1;
                float lMaxY = float.MinValue;
                foreach (int i in lower)
                {
                    float x = neutral[i].x;
                    if (x >= xLo && x < xHi || (b == bucketCount - 1 && x >= xLo))
                    {
                        if (neutral[i].y > lMaxY) { lMaxY = neutral[i].y; bestL = i; }
                    }
                }
                if (bestU >= 0 && bestL >= 0)
                    pairs.Add(new 眼睑配对 { 上顶点 = bestU, 下顶点 = bestL, 上Y = uMinY, 下Y = lMaxY });
            }
            return pairs;
        }

        private static float 计算平均唇距(Vector3[] neutral, List<int> upper, List<int> lower)
        {
            if (upper.Count < 1 || lower.Count < 1) return 0;
            float uMin = float.MaxValue;
            foreach (int i in upper) if (neutral[i].y < uMin) uMin = neutral[i].y;
            float lMax = float.MinValue;
            foreach (int i in lower) if (neutral[i].y > lMax) lMax = neutral[i].y;
            return uMin - lMax;
        }

        // ===== 眉毛检测 =====

        private static void 检测眉毛(Vector3[] neutral, Vector3[] deltaV, Vector3[] deltaN,
            MmdShapeInfo info, 检测结果 result, 眉部区域信息 brow, Bounds faceBounds)
        {
            if (brow.眉顶点.Count < 3) return;

            float faceH = faceBounds.size.y;
            float totalMag = 0;
            float maxMag = 0;
            int flipped = 0;
            float neutralAvgY = 0;

            foreach (int i in brow.眉顶点)
            {
                float mag = deltaV[i].magnitude;
                totalMag += mag;
                if (mag > maxMag) maxMag = mag;
                neutralAvgY += neutral[i].y;
            }
            neutralAvgY /= brow.眉顶点.Count;

            float maxRatio = maxMag / faceH * 100f;
            result.主数值 = maxRatio;

            // 外推
            float extrapMax = 0;
            foreach (float w in new[] { 150f, 200f })
            {
                float scale = w / 100f;
                foreach (int i in brow.眉顶点)
                {
                    float mag = deltaV[i].magnitude * scale;
                    if (mag > extrapMax) extrapMax = mag;
                }
            }
            float extrapRatio = extrapMax / faceH * 100f;

            if (maxRatio > 12f)
            {
                result.严重程度 = Max(result.严重程度, 检测严重程度.警告);
                result.问题详情.Add($"眉毛位移超标: {maxRatio:F1}%面高 (阈值12%)");
            }

            if (extrapRatio > 20f)
            {
                if (result.严重程度 < 检测严重程度.注意)
                    result.严重程度 = 检测严重程度.注意;
                result.问题详情.Add($"超限外推位移: 最大 {extrapRatio:F1}%面高");
            }

            if (result.问题详情.Count == 0)
            {
                result.问题详情.Add($"眉毛位移正常: {maxRatio:F1}%面高");
            }
        }

        // ===== 缺失检测 =====

        private static void 检测缺失形状(Mesh mesh, 全面检测报告 report)
        {
            var meshNames = new HashSet<string>();
            for (int i = 0; i < mesh.blendShapeCount; i++)
                meshNames.Add(mesh.GetBlendShapeName(i));

            foreach (var info in MmdShapeDatabase.标准形状列表)
                if (!meshNames.Contains(info.日文名))
                    report.缺失形状列表.Add($"{info.日文名} ({info.中文说明})");
        }

        private static 检测严重程度 Max(检测严重程度 a, 检测严重程度 b)
        {
            return (int)a > (int)b ? a : b;
        }
    }
}
