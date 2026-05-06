using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MMDBlendShapeChecker
{
    public class 校准结果
    {
        public string 形状名称;
        public string 中文说明;
        public MmdShapeCategory 分类;
        public float 推荐值;          // α* × 100, 如 78
        public float 原始基值贡献;     // 匹配原始形状的基值
        public string 匹配形状;
        public List<(string 名称, float 贡献)> 主要影响因素 = new List<(string, float)>();
        public string 建议操作;
    }

    public class 校准报告
    {
        public List<校准结果> 所有结果 = new List<校准结果>();
        public List<string> 缺失形状列表 = new List<string>();
        public string 网格名称;
        public string 错误信息;
        public int 总形状数;
        public int MMD形状数;
        public int 非零基值数;
    }

    public static class BlendShapeCalibrator
    {
        public static 校准报告 执行校准(SkinnedMeshRenderer faceRenderer)
        {
            var report = new 校准报告();
            var mesh = faceRenderer.sharedMesh;
            if (mesh == null || mesh.blendShapeCount == 0)
            {
                report.错误信息 = "网格无效";
                return report;
            }

            report.网格名称 = mesh.name;
            report.总形状数 = mesh.blendShapeCount;

            int vCount = mesh.vertexCount;
            var neutral = mesh.vertices;
            var deltaV = new Vector3[vCount];
            var deltaN = new Vector3[vCount];
            var deltaT = new Vector3[vCount];

            // 分类 shapes
            var mmdIndices = new List<int>();
            var nativeIndices = new List<int>();
            var nativeDeltas = new List<Vector3[]>();
            var nativeWeights = new List<float>();
            var nativeNames = new List<string>();

            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string name = mesh.GetBlendShapeName(i);
                if (MmdShapeDatabase.名称到信息映射.ContainsKey(name))
                {
                    mmdIndices.Add(i);
                }
                else
                {
                    float w = faceRenderer.GetBlendShapeWeight(i);
                    if (w > 0.001f && mesh.GetBlendShapeFrameCount(i) >= 1)
                    {
                        var nDelta = new Vector3[vCount];
                        mesh.GetBlendShapeFrameVertices(i, 0, nDelta, new Vector3[vCount], new Vector3[vCount]);
                        nativeIndices.Add(i);
                        nativeDeltas.Add(nDelta);
                        nativeWeights.Add(w);
                        nativeNames.Add(name);
                    }
                }
            }

            report.MMD形状数 = mmdIndices.Count;
            report.非零基值数 = nativeNames.Count;

            // 对每个 MMD shape 执行校准
            for (int m = 0; m < mmdIndices.Count; m++)
            {
                int mi = mmdIndices[m];
                string mmdName = mesh.GetBlendShapeName(mi);
                if (!MmdShapeDatabase.名称到信息映射.TryGetValue(mmdName, out var info)) continue;
                if (mesh.GetBlendShapeFrameCount(mi) < 1) continue;

                mesh.GetBlendShapeFrameVertices(mi, 0, deltaV, deltaN, deltaT);

                var result = new 校准结果
                {
                    形状名称 = mmdName,
                    中文说明 = info.中文说明,
                    分类 = info.分类
                };

                // 选择受 M 显著影响的顶点 (top 20% magnitude)
                float[] magSq = new float[vCount];
                for (int v = 0; v < vCount; v++)
                    magSq[v] = deltaV[v].x * deltaV[v].x + deltaV[v].y * deltaV[v].y + deltaV[v].z * deltaV[v].z;

                var sorted = new List<(int idx, float mag)>(vCount);
                for (int v = 0; v < vCount; v++)
                    if (magSq[v] > 0) sorted.Add((v, magSq[v]));
                sorted.Sort((a, b) => b.mag.CompareTo(a.mag));

                int topCount = Mathf.Max(sorted.Count / 5, 10); // top 20%, min 10
                var affectedVerts = new HashSet<int>();
                for (int i = 0; i < topCount && i < sorted.Count; i++)
                    affectedVerts.Add(sorted[i].idx);

                // 计算 Σ(|M|²) on affected vertices
                float mDotM = 0;
                foreach (int v in affectedVerts)
                    mDotM += magSq[v];

                if (mDotM < 0.000001f) continue;

                // 计算每个 native shape 对 M 的贡献
                float totalShift = 0;
                var contributions = new List<(string name, float contrib)>();

                for (int n = 0; n < nativeIndices.Count; n++)
                {
                    float bDotM = 0;
                    var nDelta = nativeDeltas[n];
                    foreach (int v in affectedVerts)
                        bDotM += nDelta[v].x * deltaV[v].x + nDelta[v].y * deltaV[v].y + nDelta[v].z * deltaV[v].z;

                    float contrib = Mathf.Abs(bDotM / mDotM) * nativeWeights[n] / 100f;
                    totalShift += bDotM / mDotM * nativeWeights[n] / 100f;

                    if (contrib > 0.01f)
                        contributions.Add((nativeNames[n], contrib));
                }

                float alpha = 1f - totalShift;
                alpha = Mathf.Clamp(alpha, 0.1f, 2f);
                result.推荐值 = alpha * 100f;
                result.原始基值贡献 = totalShift;

                contributions.Sort((a, b) => b.contrib.CompareTo(a.contrib));
                result.主要影响因素 = contributions.Take(5).ToList();

                // 匹配原始形状
                float bestMatch = 0;
                for (int n = 0; n < nativeIndices.Count; n++)
                {
                    float bDotM = 0, bDotB = 0;
                    var nDelta = nativeDeltas[n];
                    foreach (int v in affectedVerts)
                    {
                        bDotM += nDelta[v].x * deltaV[v].x + nDelta[v].y * deltaV[v].y + nDelta[v].z * deltaV[v].z;
                        bDotB += nDelta[v].x * nDelta[v].x + nDelta[v].y * nDelta[v].y + nDelta[v].z * nDelta[v].z;
                    }
                    float sim = mDotM > 0 && bDotB > 0 ? bDotM / Mathf.Sqrt(mDotM * bDotB) : 0;
                    if (sim > bestMatch && sim > 0.95f)
                    {
                        bestMatch = sim;
                        result.匹配形状 = nativeNames[n];
                    }
                }

                result.建议操作 = result.推荐值 > 99.5f && result.推荐值 < 100.5f
                    ? "无需调整"
                    : $"make-it-mmd 中设置 {mmdName} → {mmdName}(scale:{result.推荐值 / 100f:F2})";

                report.所有结果.Add(result);
            }

            // 缺失检测
            {
                var names = new HashSet<string>();
                for (int i = 0; i < mesh.blendShapeCount; i++) names.Add(mesh.GetBlendShapeName(i));
                foreach (var info in MmdShapeDatabase.标准形状列表)
                    if (!names.Contains(info.日文名))
                        report.缺失形状列表.Add($"{info.日文名} ({info.中文说明})");
            }

            return report;
        }
    }
}
