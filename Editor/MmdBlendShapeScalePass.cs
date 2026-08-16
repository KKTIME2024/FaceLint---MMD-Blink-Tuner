using System;
using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;   // resolve CS0104 ambiguity with System.Object

namespace MmdBlendShapeScaler
{
    /// <summary>
    /// FaceLint build pipeline (v0.6):
    ///
    ///   Pass A — Sculpt Freeze: bakes nonzero renderer base weights (the user's face sculpt)
    ///            into the cloned mesh's vertices/normals/tangents. WD-immune: the sculpt
    ///            lives in vertex space, animator writes (Write Defaults ON included) can't touch it.
    ///   Pass B — Driver Scaling: every "driver channel" (referenced by any clip in the final
    ///            animators ∪ MMD database ∪ vrc.blink family) gets α = 1 − Σ b_j·(D_j·D_i)/|D_i|²,
    ///            so any driver at weight 100 lands exactly on the author's peak displacement.
    ///            Manual scaler.scales entries override the automatic α.
    ///            v0.6.2: α is gated — it only applies to drivers that are the same channel as
    ///            the sculpt (max of cosine & driver-projection ≥ 0.95, e.g. the blink family
    ///            including single-eye variants). The model's own expressions (e.g. ">_<") that
    ///            merely overlap the sculpt's direction stay at 100% (additive, editor-consistent)
    ///            instead of being uniformly weakened by the projection.
    ///            v0.6.3: scaling inside the gate is PER-VERTEX: α(v) = 1 − Σ b_j·(D_j(v)·D_i(v))/|D_i(v)|².
    ///            A scalar can't express "left ×0.9, right ×1.0" for asymmetric sculpts
    ///            (eye_close_L = 10 → right eye must stay untouched).
    ///   Pass C — Controller Neutralization: rewrites the avatar's OWN baked base face
    ///            (constant curves in Base/Gesture states) to 0, so the vertex-baked sculpt
    ///            is not double-counted at runtime under Write Defaults ON. (MmdControllerNeutralizer)
    ///
    /// Backward compatible: no sculpt weights and no manual scales → no-op; manual scales only → old behavior.
    /// </summary>
    public class MmdBlendShapeScalePass : Pass<MmdBlendShapeScalePass>
    {
        public override string DisplayName => "VRC Avatar MMD & Blink Fixer";

        private HashSet<string> _driverCache;

        protected override void Execute(BuildContext context)
        {
            var scalers = context.AvatarRootObject
                .GetComponentsInChildren<MmdBlendShapeScaler>(includeInactive: true);

            Debug.Log($"[MMDBlinkFixer] Execute started. Found {scalers.Length} scaler(s) on avatar.");

            // Pass C bookkeeping: sculpted shape names per renderer path, collected across scalers
            var sculptByPath = new Dictionary<string, List<string>>();
            bool anyNeutralize = false;

            foreach (var scaler in scalers)
            {
                if (scaler == null || !scaler.IsValid)
                {
                    Debug.Log($"[MMDBlinkFixer] Skipping scaler: {(scaler == null ? "null" : "invalid (targetRenderer or mesh null)")}");
                    continue;
                }
                if (scaler.Count == 0 && !HasSculptWeights(scaler.targetRenderer))
                {
                    Debug.Log("[MMDBlinkFixer] Skipping scaler: no manual scales and no sculpt base weights.");
                    continue;
                }

                var renderer = scaler.targetRenderer;
                var originalMesh = renderer.sharedMesh;
                var meshCopy = Object.Instantiate(originalMesh);

                // ═══ Pass A: sculpt freeze (bake base weights into vertices) ═══
                var sculpt = CollectSculpt(renderer);
                if (sculpt.Count > 0)
                {
                    BakeSculpt(meshCopy, sculpt);

                    // Zero the sculpt weights on the build renderer (runtime starts clean)
                    foreach (var s in sculpt)
                        renderer.SetBlendShapeWeight(s.index, 0f);

                    string rp = GetRendererPath(context, renderer);
                    if (!sculptByPath.TryGetValue(rp, out var names))
                    {
                        names = new List<string>();
                        sculptByPath[rp] = names;
                    }
                    foreach (var s in sculpt)
                        if (!names.Contains(s.name)) names.Add(s.name);

                    anyNeutralize |= scaler.neutralizeBakedFace;
                }

                // ═══ Pass B: streaming rebuild with driver scaling ═══
                var drivers = GetDriverShapes(context);
                int blendShapeCount = originalMesh.blendShapeCount;
                int vertexCount = originalMesh.vertexCount;

                // Reusable working arrays (overwritten each frame)
                var deltaV = new Vector3[vertexCount];
                var deltaN = new Vector3[vertexCount];
                var deltaT = new Vector3[vertexCount];

                // Per-vertex α workspaces (double precision, reused across shapes)
                var workLen2 = new double[vertexCount];
                var workDot = new double[vertexCount];

                // Scaled vertex array (lazy allocation, only when needed)
                Vector3[] scaledV = null;

                meshCopy.ClearBlendShapes();

                int scaledCount = 0;
                for (int i = 0; i < blendShapeCount; i++)
                {
                    string name = originalMesh.GetBlendShapeName(i);
                    int frameCount = originalMesh.GetBlendShapeFrameCount(i);

                    // Manual config overrides the automatic α.
                    bool manual = scaler.scales.TryGetValue(name, out float manualScale);
                    bool autoCandidate = !manual && drivers.Contains(name) && sculpt.Count > 0;
                    float scale = manual ? manualScale : 1f;

                    // v0.6.3: per-vertex α array (null = no scaling). See ComputeAlpha.
                    float[] alpha = null;

                    for (int f = 0; f < frameCount; f++)
                    {
                        float weight = originalMesh.GetBlendShapeFrameWeight(i, f);
                        originalMesh.GetBlendShapeFrameVertices(i, f, deltaV, deltaN, deltaT);

                        // α computed on frame 0 data (standard single-frame weight-100 format)
                        if (f == 0 && autoCandidate)
                            alpha = ComputeAlpha(deltaV, sculpt, vertexCount, name, workLen2, workDot);

                        bool needsScale = (manual && Mathf.Abs(scale - 1.0f) > 0.001f) || alpha != null;
                        if (needsScale && scaledV == null)
                            scaledV = new Vector3[vertexCount];

                        if (needsScale)
                        {
                            if (alpha != null)
                            {
                                // Per-vertex: left/right halves of a driver can differ — a scalar
                                // cannot say "left ×0.9, right ×1.0" (asymmetric sculpt case).
                                for (int v = 0; v < vertexCount; v++)
                                    scaledV[v] = deltaV[v] * alpha[v];
                            }
                            else
                            {
                                // Manual scale: uniform scalar (Only scale vertices. Normals/tangents
                                // are NOT position deltas — scaling them causes shading exaggeration.)
                                for (int v = 0; v < vertexCount; v++)
                                    scaledV[v] = deltaV[v] * scale;
                            }

                            meshCopy.AddBlendShapeFrame(name, weight, scaledV, deltaN, deltaT);
                            if (f == 0) scaledCount++;
                        }
                        else
                        {
                            meshCopy.AddBlendShapeFrame(name, weight, deltaV, deltaN, deltaT);
                        }
                    }
                }

                // Assign clone and destroy component
                renderer.sharedMesh = meshCopy;
                Debug.Log($"[MMDBlinkFixer] Processed {blendShapeCount} blendshapes for '{renderer.name}'. " +
                          $"Sculpt frozen: {sculpt.Count}, Scaled: {scaledCount}.");
                Object.DestroyImmediate(scaler);
            }

            // ═══ Pass C: neutralize baked base face in the avatar's own controllers ═══
            if (anyNeutralize && sculptByPath.Count > 0)
            {
                try
                {
                    int rewritten = MmdControllerNeutralizer.Neutralize(context, sculptByPath);
                    Debug.Log($"[MMDBlinkFixer] Pass C (controller neutralization): {rewritten} clip(s) rewritten.");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[MMDBlinkFixer] Pass C failed — build continues without it: {e.Message}\n{e.StackTrace}");
                }
            }

            Debug.Log("[MMDBlinkFixer] Execute finished.");
        }

        // ══════════════════════════════════════════════
        //  Pass A — Sculpt Freeze
        // ══════════════════════════════════════════════

        private class SculptEntry
        {
            public int index;
            public string name;
            public float b;              // base weight / 100
            public double len2;          // |D_j|² (precomputed for α / similarity)
            public Vector3[] delta;      // frame-0 position delta (cached for α computation)
            public Vector3[] deltaN;
            public Vector3[] deltaT;
        }

        private static bool HasSculptWeights(SkinnedMeshRenderer renderer)
        {
            var mesh = renderer.sharedMesh;
            if (mesh == null) return false;
            for (int i = 0; i < mesh.blendShapeCount; i++)
                if (renderer.GetBlendShapeWeight(i) > 0.5f) return true;
            return false;
        }

        private static List<SculptEntry> CollectSculpt(SkinnedMeshRenderer renderer)
        {
            var mesh = renderer.sharedMesh;
            var result = new List<SculptEntry>();
            int vc = mesh.vertexCount;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                float w = renderer.GetBlendShapeWeight(i);
                if (w <= 0.5f) continue;                    // ignore float noise
                if (mesh.GetBlendShapeFrameCount(i) == 0) continue;

                var dV = new Vector3[vc];
                var dN = new Vector3[vc];
                var dT = new Vector3[vc];
                mesh.GetBlendShapeFrameVertices(i, 0, dV, dN, dT);

                double len2 = 0;
                for (int v = 0; v < vc; v++)
                {
                    var p = dV[v];
                    len2 += (double)p.x * p.x + (double)p.y * p.y + (double)p.z * p.z;
                }

                result.Add(new SculptEntry
                {
                    index = i,
                    name = mesh.GetBlendShapeName(i),
                    b = w / 100f,
                    len2 = len2,
                    delta = dV,
                    deltaN = dN,
                    deltaT = dT
                });
            }
            return result;
        }

        /// <summary>
        /// Bakes Σ b_j·delta_j into the cloned mesh's base vertices, normals and tangents.
        /// delta normals/tangents are accumulated (NOT RecalculateNormals — that would
        /// destroy the author's edited normals used by toon shaders).
        /// </summary>
        private static void BakeSculpt(Mesh meshCopy, List<SculptEntry> sculpt)
        {
            int vc = meshCopy.vertexCount;
            var vertices = meshCopy.vertices;
            var normals = meshCopy.normals;
            var tangents = meshCopy.tangents;

            bool hasNormals = normals != null && normals.Length == vc;
            bool hasTangents = tangents != null && tangents.Length == vc;

            // Accumulate sculpt displacement per vertex
            var accV = new Vector3[vc];
            var accN = hasNormals ? new Vector3[vc] : null;
            var accT = hasTangents ? new Vector3[vc] : null;

            foreach (var s in sculpt)
            {
                for (int v = 0; v < vc; v++)
                {
                    accV[v] += s.delta[v] * s.b;
                    if (accN != null) accN[v] += s.deltaN[v] * s.b;
                    if (accT != null) accT[v] += s.deltaT[v] * s.b;
                }
            }

            for (int v = 0; v < vc; v++)
            {
                vertices[v] += accV[v];

                if (accN != null)
                {
                    var n = normals[v] + accN[v];
                    normals[v] = n.sqrMagnitude > 1e-12f ? n.normalized : n;
                }

                if (accT != null)
                {
                    var n = accN != null ? normals[v] : Vector3.up;
                    var t3 = (Vector3)tangents[v] + accT[v];
                    t3 -= n * Vector3.Dot(n, t3);           // Gram-Schmidt against new normal
                    if (t3.sqrMagnitude > 1e-12f)
                        tangents[v] = new Vector4(t3.normalized.x, t3.normalized.y, t3.normalized.z, tangents[v].w);
                }
            }

            meshCopy.vertices = vertices;
            if (accN != null) meshCopy.normals = normals;
            if (accT != null) meshCopy.tangents = tangents;
            meshCopy.RecalculateBounds();
        }

        // ══════════════════════════════════════════════
        //  Pass B — Driver scaling (projection α)
        // ══════════════════════════════════════════════

        /// <summary>
        /// Per-vertex α(v) = clamp(1 − Σ_j b_j·(D_j(v)·D_i(v)) / |D_i(v)|², 0, 2).
        /// Returns null when no vertex needs scaling (gate failed, or all α ≈ 1).
        ///
        /// v0.6.2 — collinearity gate (kept): α only applies when driver and sculpt are the same
        /// channel. THREE metrics, take the max:
        ///   A = cosine  (D_j·D_i)/(|D_j|·|D_i|)  — direction agreement (twin shapes)
        ///   B = (D_j·D_i)/|D_i|²                 — driver-side coverage: a single-eye DRIVER vs a
        ///                                           both-eyes sculpt (subset driver) has A=0.71 but B=1.0
        ///   C = (D_j·D_i)/|D_j|²                 — sculpt-side coverage: a both-eyes DRIVER vs a
        ///                                           single-eye sculpt has A=0.71, B=0.5 but C=1.0
        ///                                           (the whole sculpt budget lives inside the driver)
        /// Gate passes when max(A, B, C) ≥ 0.95. A model's OWN expression (e.g. squint ">_<") whose
        /// delta carries features orthogonal to the sculpt gets A,B,C < 0.95 → untouched (additive,
        /// exactly the editor look).
        ///
        /// v0.6.3 — per-vertex scaling inside the gate: a SINGLE scalar cannot express
        /// "left half ×0.9, right half ×1.0". Asymmetric sculpt (eye_close_L = 10) with a
        /// both-eyes driver needs exactly that: vertices carrying the sculpt budget get 0.9,
        /// vertices the sculpt never touches stay 1.0. The per-vertex form degenerates to the
        /// uniform 1−Σb for symmetric sculpts — no behavior change there.
        ///
        /// v0.6.4 — budget gating: only same-channel sculpt entries feed the budget. Models
        /// ship with many baked base weights (RINDO: 21 frozen); the old code summed ALL of
        /// them (incl. partial overlaps) into every driver's budget — 19 expression shapes got
        /// α≤0 and were wiped. Each driver now compensates only its own channel's budget.
        ///
        /// v0.6.5 — budget gating REMOVED again: the gate made bake (all sculpts) inconsistent
        /// with budget (passing sculpts only). RINDO 实测：eye_open 的基值（撑眼，与闭眼反向）
        /// 被烤入却不被补偿 → 眨眼时右眼差一点。全量预算下反向项贡献负点积 → α(v) > 1 自动补回；
        /// 逐顶点局部性让跨通道项只影响它们触及的顶点（v0.6.2 的零清是标量伪影）。
        ///
        /// workspaces: workLen2[v] = |D_i(v)|², workDot[v] = Σ_j b_j·(D_j(v)·D_i(v)) — reused
        /// arrays, caller-owned.
        /// </summary>
        private static float[] ComputeAlpha(Vector3[] deltaV, List<SculptEntry> sculpt, int vertexCount,
            string shapeName, double[] workLen2, double[] workDot)
        {
            double totalLen2 = 0;
            for (int v = 0; v < vertexCount; v++)
            {
                var p = deltaV[v];
                double l2 = (double)p.x * p.x + (double)p.y * p.y + (double)p.z * p.z;
                workLen2[v] = l2;
                totalLen2 += l2;
            }
            if (totalLen2 < 1e-12) return null;    // empty delta — nothing to scale

            // Per-vertex budget: ALL sculpt entries contribute, at the vertices they touch.
            //
            // v0.6.5: removed the v0.6.4 budget gate. RINDO 实测：预算门把"没过门的捏脸项"
            // 排除在 α 之外，但 Pass A 把它们全部烤进了顶点 —— 烤了却不补偿 → 例如 eye_open
            // 的基值（撑开眼睛，与闭眼方向相反）在眨眼时残留 → 右眼差一点。全量预算下：
            // 方向相反的项点积为负 → α(v) > 1（自动补回撑开量）；不同通道的项只在它们真正
            // 触及的顶点上影响 α。v0.6.2 的"19 个表情清零"是标量公式的伪影（全局 |D_i|² 被
            // 21 项全局点积撑爆），逐顶点公式是局部的，不会互相拖累。
            double maxA = 0;
            double maxB = 0;
            double maxC = 0;
            Array.Clear(workDot, 0, vertexCount);
            foreach (var s in sculpt)
            {
                var sd = s.delta;
                double gdot = 0;
                for (int v = 0; v < vertexCount; v++)
                {
                    double d = (double)sd[v].x * deltaV[v].x
                             + (double)sd[v].y * deltaV[v].y
                             + (double)sd[v].z * deltaV[v].z;
                    gdot += d;
                    workDot[v] += s.b * d;
                }

                if (s.len2 > 1e-12)
                {
                    double simA = gdot / Math.Sqrt(s.len2 * totalLen2);   // cosine (twin shapes)
                    double simB = gdot / totalLen2;                        // driver-side coverage (subset driver)
                    double simC = gdot / s.len2;                           // sculpt-side coverage (subset sculpt)
                    if (simA > maxA) maxA = simA;
                    if (simB > maxB) maxB = simB;
                    if (simC > maxC) maxC = simC;
                }
            }

            // Informational: whether any sculpt is same-channel with this driver.
            bool sameChannel = Math.Max(maxA, Math.Max(maxB, maxC)) >= 0.95;

            var alpha = new float[vertexCount];
            bool any = false;
            double mean = 0;
            for (int v = 0; v < vertexCount; v++)
            {
                double a;
                if (workLen2[v] < 1e-12)
                {
                    a = 1.0;                     // no driver displacement here — nothing to scale
                }
                else
                {
                    a = 1.0 - workDot[v] / workLen2[v];
                    if (a < 0) a = 0;
                    else if (a > 2) a = 2;
                }
                alpha[v] = (float)a;
                mean += a;
                if (Math.Abs(a - 1.0) > 0.001) any = true;
            }

            if (!any) return null;               // budget doesn't reach this shape — untouched

            string channel = sameChannel ? "" : " [no same-channel sculpt; per-vertex only]";
            Debug.Log($"[MMDBlinkFixer] α for '{shapeName}': mean={mean / vertexCount:F3} (per-vertex, cos={maxA:F3}, driverCov={maxB:F3}, sculptCov={maxC:F3}){channel}.");
            return alpha;
        }

        /// <summary>
        /// Driver channels = shapes referenced by any clip in the final (MA-merged) animators
        /// ∪ the avatar's custom playable layers ∪ the 64 standard MMD shapes ∪ VRChat built-in
        /// blink family. Cached per Execute.
        /// </summary>
        private HashSet<string> GetDriverShapes(BuildContext context)
        {
            if (_driverCache != null) return _driverCache;

            var drivers = new HashSet<string> { "vrc.blink", "vrc.blink_l", "vrc.blink_r" };
            foreach (var info in MmdShapeDatabase.标准形状列表)
                drivers.Add(info.日文名);

            foreach (var animator in context.AvatarRootObject.GetComponentsInChildren<Animator>(true))
            {
                if (animator.runtimeAnimatorController is AnimatorController ctrl)
                    CollectControllerBindings(ctrl, drivers);
                else if (animator.runtimeAnimatorController is AnimatorOverrideController over)
                    CollectOverrideBindings(over, drivers);
            }

            // Custom playable layers (VRCAvatarDescriptor) are not attached to any Animator
            // component — their clips drive shapes just the same and must be in the driver set.
            var descriptor = context.AvatarDescriptor;
            if (descriptor != null && descriptor.customizeAnimationLayers)
            {
                foreach (var layer in descriptor.baseAnimationLayers)
                {
                    if (layer.isDefault) continue;
                    if (layer.animatorController is AnimatorController ctrl)
                        CollectControllerBindings(ctrl, drivers);
                    else if (layer.animatorController is AnimatorOverrideController over)
                        CollectOverrideBindings(over, drivers);
                }
            }

            _driverCache = drivers;
            return drivers;
        }

        private static void CollectControllerBindings(AnimatorController ctrl, HashSet<string> drivers)
        {
            if (ctrl == null) return;
            foreach (var clip in ctrl.animationClips)
                CollectClipBindings(clip, drivers);
        }

        private static void CollectOverrideBindings(AnimatorOverrideController over, HashSet<string> drivers)
        {
            if (over == null) return;
            foreach (var clip in over.animationClips)
                CollectClipBindings(clip, drivers);
        }

        private static void CollectClipBindings(AnimationClip clip, HashSet<string> drivers)
        {
            if (clip == null) return;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.type == typeof(SkinnedMeshRenderer) &&
                    binding.propertyName.StartsWith("blendShape."))
                {
                    drivers.Add(binding.propertyName.Substring("blendShape.".Length));
                }
            }
        }

        /// <summary>Path of the renderer relative to the avatar root (used for clip binding matching in Pass C).</summary>
        private static string GetRendererPath(BuildContext context, SkinnedMeshRenderer renderer)
        {
            var root = context.AvatarRootObject.transform;
            var t = renderer.transform;
            if (t == root) return "";
            string path = t.name;
            while (t.parent != null && t.parent != root)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }
    }
}
