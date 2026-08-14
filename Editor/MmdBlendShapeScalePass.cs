using System;
using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

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

                    for (int f = 0; f < frameCount; f++)
                    {
                        float weight = originalMesh.GetBlendShapeFrameWeight(i, f);
                        originalMesh.GetBlendShapeFrameVertices(i, f, deltaV, deltaN, deltaT);

                        // α computed on frame 0 data (standard single-frame weight-100 format)
                        if (f == 0 && autoCandidate)
                            scale = ComputeAlpha(deltaV, sculpt, vertexCount, name);

                        bool needsScale = Mathf.Abs(scale - 1.0f) > 0.001f;
                        if (needsScale && scaledV == null)
                            scaledV = new Vector3[vertexCount];

                        if (needsScale)
                        {
                            // Only scale vertices. Normals/tangents are NOT position deltas —
                            // scaling them causes shading exaggeration and specular artifacts.
                            for (int v = 0; v < vertexCount; v++)
                                scaledV[v] = deltaV[v] * scale;

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

                result.Add(new SculptEntry
                {
                    index = i,
                    name = mesh.GetBlendShapeName(i),
                    b = w / 100f,
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
        /// α_i = 1 − Σ_j b_j·(D_j·D_i)/|D_i|²  — the sculpt budget projected onto this driver's delta.
        /// Same-delta triplet (eye_close/blink/まばたき identical): α = 1 − Σb_j.
        /// </summary>
        private static float ComputeAlpha(Vector3[] deltaV, List<SculptEntry> sculpt, int vertexCount, string shapeName)
        {
            double len2 = 0;
            for (int v = 0; v < vertexCount; v++)
            {
                var p = deltaV[v];
                len2 += (double)p.x * p.x + (double)p.y * p.y + (double)p.z * p.z;
            }
            if (len2 < 1e-12) return 1f;    // empty delta — nothing to scale

            double sum = 0;
            foreach (var s in sculpt)
            {
                double dot = 0;
                var sd = s.delta;
                for (int v = 0; v < vertexCount; v++)
                {
                    dot += (double)sd[v].x * deltaV[v].x
                         + (double)sd[v].y * deltaV[v].y
                         + (double)sd[v].z * deltaV[v].z;
                }
                sum += s.b * dot / len2;
            }

            float alpha = (float)(1.0 - sum);
            if (alpha < 0f)
            {
                Debug.Log($"[MMDBlinkFixer] α for '{shapeName}' clamped to 0 (sculpt budget exceeds this driver's displacement).");
                return 0f;
            }
            return Mathf.Clamp(alpha, 0f, 2f);
        }

        /// <summary>
        /// Driver channels = shapes referenced by any clip in the final (MA-merged) animators
        /// ∪ the 64 standard MMD shapes ∪ VRChat built-in blink family.
        /// Cached per Execute.
        /// </summary>
        private HashSet<string> GetDriverShapes(BuildContext context)
        {
            if (_driverCache != null) return _driverCache;

            var drivers = new HashSet<string> { "vrc.blink", "vrc.blink_l", "vrc.blink_r" };
            foreach (var info in MmdShapeDatabase.标准形状列表)
                drivers.Add(info.日文名);

            foreach (var animator in context.AvatarRootObject.GetComponentsInChildren<Animator>(true))
            {
                if (!(animator.runtimeAnimatorController is AnimatorController ctrl)) continue;
                foreach (var clip in ctrl.animationClips)
                {
                    if (clip == null) continue;
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (binding.type == typeof(SkinnedMeshRenderer) &&
                            binding.propertyName.StartsWith("blendShape."))
                        {
                            drivers.Add(binding.propertyName.Substring("blendShape.".Length));
                        }
                    }
                }
            }

            _driverCache = drivers;
            return drivers;
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
