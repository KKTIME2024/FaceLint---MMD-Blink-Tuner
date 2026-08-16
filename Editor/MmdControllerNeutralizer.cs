using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MmdBlendShapeScaler
{
    /// <summary>
    /// Pass C — Controller Neutralization (v0.1, experimental).
    ///
    /// Under Write Defaults ON, each active state writes its baked values for properties
    /// its motion does not animate. Avatars often bake the ORIGINAL base face into their
    /// Base/Gesture state motions (the "default face frame" problem). After Pass A bakes
    /// the user's sculpt into the mesh vertices, those baked values would play ON TOP of
    /// the sculpt (b + c·α double-count).
    ///
    /// This pass clones the avatar's own controller and rewrites CONSTANT-value curves of
    /// sculpted shapes to 0 — "the sculpt replaces the original base face". Rules:
    ///   - Only constant curves (baked poses) are zeroed; animated curves are left to the
    ///     affine map (your_face(w) = b + (1−b)·author_face(w)).
    ///   - The FX layer (index 4) is skipped when the avatar uses Custom Expressions —
    ///     that's product territory; product looks pass through the affine map untouched.
    ///   - States with no motion (pure WD defaults) are logged, not fixed in v0.1.
    /// </summary>
    public static class MmdControllerNeutralizer
    {
        public static int Neutralize(BuildContext context, Dictionary<string, List<string>> sculptByPath)
        {
            var descriptor = context.AvatarDescriptor;
            bool customExpressions = descriptor != null && descriptor.customExpressions;

            int rewritten = 0;
            int motionless = 0;

            foreach (var animator in context.AvatarRootObject.GetComponentsInChildren<Animator>(true))
            {
                if (!(animator.runtimeAnimatorController is AnimatorController controller))
                {
                    Debug.LogWarning("[MMDBlinkFixer] Pass C: animator uses a non-Asset controller; skipping.");
                    continue;
                }

                var controllerClone = Object.Instantiate(controller);
                int layerCount = controllerClone.layers.Length;

                for (int layerIdx = 0; layerIdx < layerCount; layerIdx++)
                {
                    // FX layer is the avatar's own only when Custom Expressions is off.
                    bool isFx = layerIdx >= 4;
                    if (isFx && customExpressions) continue;

                    var sm = controllerClone.layers[layerIdx].stateMachine;
                    if (sm == null) continue;
                    WalkStateMachine(sm, sculptByPath, ref rewritten, ref motionless);
                }

                animator.runtimeAnimatorController = controllerClone;
            }

            // Custom playable layers (VRCAvatarDescriptor.customizeAnimationLayers) replace the
            // default layers at runtime and are not attached to any Animator component — the
            // avatar's own baked base face may live there too. Default layers are skipped: they
            // are the Animator component's controller, already processed above.
            if (descriptor != null && descriptor.customizeAnimationLayers)
            {
                var layers = descriptor.baseAnimationLayers;
                for (int layerIdx = 0; layerIdx < layers.Length; layerIdx++)
                {
                    var layer = layers[layerIdx];
                    if (layer.isDefault) continue;
                    if (layerIdx >= 4 && customExpressions) continue;   // product FX

                    if (!(layer.animatorController is AnimatorController controller)) continue;

                    var controllerClone = Object.Instantiate(controller);
                    int layerCount = controllerClone.layers.Length;
                    for (int l2 = 0; l2 < layerCount; l2++)
                    {
                        var sm = controllerClone.layers[l2].stateMachine;
                        if (sm != null)
                            WalkStateMachine(sm, sculptByPath, ref rewritten, ref motionless);
                    }

                    layer.animatorController = controllerClone;
                    layers[layerIdx] = layer;
                }
            }

            if (motionless > 0)
                Debug.Log($"[MMDBlinkFixer] Pass C: {motionless} state(s) have no motion (baked WD defaults only); " +
                          "not fixed in v0.1 — sculpted shapes in those states may double-count.");

            return rewritten;
        }

        private static void WalkStateMachine(AnimatorStateMachine sm,
            Dictionary<string, List<string>> sculptByPath, ref int rewritten, ref int motionless)
        {
            foreach (var child in sm.states)
            {
                var state = child.state;
                if (state == null) continue;
                if (state.motion == null) { motionless++; continue; }
                rewritten += HandleMotion(state, state.motion, sculptByPath);
            }

            foreach (var sub in sm.stateMachines)
                if (sub.stateMachine != null)
                    WalkStateMachine(sub.stateMachine, sculptByPath, ref rewritten, ref motionless);
        }

        private static int HandleMotion(AnimatorState state, Motion motion,
            Dictionary<string, List<string>> sculptByPath)
        {
            if (motion is AnimationClip clip)
                return RewriteClip(state, clip, sculptByPath) ? 1 : 0;

            if (motion is BlendTree tree)
            {
                int n = 0;
                var children = tree.children;
                for (int i = 0; i < children.Length; i++)
                {
                    var childMotion = children[i].motion;
                    if (childMotion is AnimationClip childClip)
                    {
                        if (RewriteClip(null, childClip, sculptByPath, out var clone))
                        {
                            children[i].motion = clone;
                            n++;
                        }
                    }
                    else if (childMotion is BlendTree childTree)
                    {
                        n += HandleMotion(null, childTree, sculptByPath);
                    }
                }
                tree.children = children;
                return n;
            }

            return 0;
        }

        private static bool RewriteClip(AnimatorState state, AnimationClip clip,
            Dictionary<string, List<string>> sculptByPath)
        {
            return RewriteClip(state, clip, sculptByPath, out _);
        }

        /// <summary>
        /// Zeroes constant baked-pose curves of sculpted shapes. Returns true (and assigns the
        /// cloned clip to the state / out-param) when anything was rewritten.
        /// </summary>
        private static bool RewriteClip(AnimatorState state, AnimationClip clip,
            Dictionary<string, List<string>> sculptByPath, out AnimationClip clone)
        {
            clone = null;
            bool modified = false;

            foreach (var kv in sculptByPath)
            {
                string path = kv.Key;
                foreach (var shape in kv.Value)
                {
                    var binding = EditorCurveBinding.FloatCurve(
                        path, typeof(SkinnedMeshRenderer), "blendShape." + shape);

                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve == null || curve.keys.Length == 0) continue;

                    // Animated curves are left to the affine map; only baked poses are zeroed.
                    if (!IsConstant(curve)) continue;
                    if (Mathf.Approximately(curve.keys[0].value, 0f)) continue;

                    if (!modified)
                    {
                        clone = Object.Instantiate(clip);
                        modified = true;
                    }

                    AnimationUtility.SetEditorCurve(clone, binding, AnimationCurve.Constant(0f, 1f, 0f));
                }
            }

            if (modified && state != null)
                state.motion = clone;

            return modified;
        }

        private static bool IsConstant(AnimationCurve curve)
        {
            if (curve.keys.Length <= 1) return true;
            float v0 = curve.keys[0].value;
            for (int i = 1; i < curve.keys.Length; i++)
                if (!Mathf.Approximately(curve.keys[i].value, v0)) return false;
            return true;
        }
    }
}
