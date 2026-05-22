using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MmdBlendShapeScaler
{
    /// <summary>
    /// 使用临时 Camera + AnimationMode 将单个 BlendShape 渲染为 Texture2D。
    /// AnimationMode 在批次内保持一次 Start→...→Stop，避免跨会话的懒初始化影响首个缩略图。
    /// </summary>
    public static class BlendShapePreviewRenderer
    {
        public static float ZoomMultiplier { get; set; } = 1.0f;

        // ── Batch 级别缓存 ──
        private static int _cachedRendererId;
        private static GameObject _cachedCameraGo;
        private static Camera _cachedCamera;
        private static bool _batchActive;

        /// <summary>
        /// 清空批次缓存。每次 Scan 前由外部调用一次。
        /// </summary>
        public static void ClearCache()
        {
            EndBatch();
            _cachedRendererId = 0;
            if (_cachedCameraGo != null)
            {
                Object.DestroyImmediate(_cachedCameraGo);
                _cachedCameraGo = null;
                _cachedCamera = null;
            }
        }

        /// <summary>
        /// 结束当前 AnimationMode 批次（若仍在激活状态）。
        /// 在 Scan() 的循环结束后调用。
        /// </summary>
        public static void EndBatch()
        {
            if (_batchActive && AnimationMode.InAnimationMode())
            {
                AnimationMode.StopAnimationMode();
            }
            _batchActive = false;
        }

        public static Texture2D Render(
            SkinnedMeshRenderer renderer,
            int blendshapeIndex,
            float weight,
            int size)
        {
            if (renderer == null || renderer.sharedMesh == null) return null;
            if (blendshapeIndex < 0 || blendshapeIndex >= renderer.sharedMesh.blendShapeCount) return null;

            var blendShapeName = renderer.sharedMesh.GetBlendShapeName(blendshapeIndex);

            EnsureBatchResources(renderer);
            var cam = _cachedCamera;

            // ── 批次级 AnimationMode：首次 Render 启动并预热，后续复用 ──
            EnsureAnimationModeBatch(renderer.gameObject);

            var clip = new AnimationClip();
            clip.hideFlags = HideFlags.HideAndDontSave;
            AnimationUtility.SetEditorCurve(
                clip,
                new EditorCurveBinding
                {
                    path = "",
                    type = typeof(SkinnedMeshRenderer),
                    propertyName = $"blendShape.{blendShapeName}"
                },
                AnimationCurve.Constant(0, 1f / 60f, weight)
            );

            Texture2D result = null;

            try
            {
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(renderer.gameObject, clip, 1f / 60f);
                AnimationMode.EndSampling();

                var rt = RenderTexture.GetTemporary(size, size, 24);
                rt.wrapMode = TextureWrapMode.Clamp;

                var originalTarget = cam.targetTexture;
                var originalAspect = cam.aspect;
                cam.targetTexture = rt;
                cam.aspect = 1f;
                cam.Render();
                cam.targetTexture = originalTarget;
                cam.aspect = originalAspect;

                result = new Texture2D(size, size, TextureFormat.RGB24, false);
                result.wrapMode = TextureWrapMode.Clamp;
                RenderTexture.active = rt;
                result.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                result.Apply();
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }

            return result;
        }

        /// <summary>
        /// 确保 AnimationMode 已启动且已预热。
        /// 整个批次共享一次 StartAnimationMode，
        /// 并在启动后立即用一次 dummy 采样吸收懒初始化。
        /// 这样批次内首个缩略图的采样是本次会话的第 2 次采样，正确应用权重。
        /// </summary>
        private static void EnsureAnimationModeBatch(GameObject target)
        {
            if (_batchActive) return;

            AnimationMode.StartAnimationMode();
            _batchActive = true;

            // 用空权重采样预热 — 首次采样不生效但会吸收懒初始化
            var dummyClip = new AnimationClip();
            dummyClip.hideFlags = HideFlags.HideAndDontSave;
            // 使用一个真实存在的属性绑定来触发完整管道
            var smr = target.GetComponent<SkinnedMeshRenderer>();
            if (smr != null && smr.sharedMesh != null && smr.sharedMesh.blendShapeCount > 0)
            {
                var name = smr.sharedMesh.GetBlendShapeName(0);
                AnimationUtility.SetEditorCurve(
                    dummyClip,
                    new EditorCurveBinding
                    {
                        path = "",
                        type = typeof(SkinnedMeshRenderer),
                        propertyName = $"blendShape.{name}"
                    },
                    AnimationCurve.Constant(0, 1f / 60f, 0f)
                );
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(target, dummyClip, 1f / 60f);
                AnimationMode.EndSampling();
            }
            Object.DestroyImmediate(dummyClip);
        }

        /// <summary>
        /// 初始化批次资源：创建 Camera + 计算面部包围盒 + 定位相机。
        /// </summary>
        private static void EnsureBatchResources(SkinnedMeshRenderer renderer)
        {
            int id = renderer.GetInstanceID();
            if (id == _cachedRendererId) return;

            if (_cachedCameraGo != null)
            {
                Object.DestroyImmediate(_cachedCameraGo);
                _cachedCameraGo = null;
                _cachedCamera = null;
            }
            _cachedRendererId = id;

            _cachedCameraGo = new GameObject("__MMD_BS_Preview_Cam__");
            _cachedCameraGo.hideFlags = HideFlags.HideAndDontSave;
            _cachedCamera = _cachedCameraGo.AddComponent<Camera>();

            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null)
            {
                _cachedCamera.clearFlags = sceneView.camera.clearFlags;
                _cachedCamera.backgroundColor = sceneView.camera.backgroundColor;
            }
            else
            {
                _cachedCamera.clearFlags = CameraClearFlags.SolidColor;
                _cachedCamera.backgroundColor = new Color(0.25f, 0.25f, 0.25f, 1f);
            }
            _cachedCamera.nearClipPlane = 0.01f;
            _cachedCamera.farClipPlane = 100f;

            Transform headBone = FindHeadBone(renderer);
            Vector3 faceForward = GetFaceForward(renderer);
            Bounds bounds = GetFaceBounds(renderer);

            PositionCamera(_cachedCamera, bounds, faceForward, headBone);
        }

        internal static void FrameRendererInCamera(Camera cam, SkinnedMeshRenderer renderer)
        {
            PositionCamera(cam, GetFaceBounds(renderer), GetFaceForward(renderer), FindHeadBone(renderer));
        }

        private static void PositionCamera(Camera cam, Bounds bounds, Vector3 faceForward, Transform headBone)
        {
            float extentMag = bounds.extents.magnitude;
            if (extentMag < 0.001f || float.IsNaN(extentMag))
            {
                Vector3 fallbackTarget = headBone != null ? headBone.position : bounds.center;
                cam.transform.position = fallbackTarget + faceForward * 1f;
                cam.transform.LookAt(fallbackTarget);
                cam.fieldOfView = 30f;
                return;
            }

            Vector3 target = bounds.center;

            float fov = 25f;
            cam.fieldOfView = fov;
            cam.orthographic = false;

            float objectRadius = extentMag * 1.2f;
            float distance = objectRadius / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            distance = Mathf.Max(distance, 0.3f);

            float zoom = Mathf.Max(ZoomMultiplier, 0.1f);
            distance /= zoom;

            cam.transform.position = target + faceForward * distance;
            cam.transform.LookAt(target);

            cam.transform.Rotate(Vector3.right, -2.5f, Space.Self);
        }

        // ════════════════════════════════════════════════════════
        //  面部包围盒计算
        // ════════════════════════════════════════════════════════

        private static Bounds GetFaceBounds(SkinnedMeshRenderer renderer)
        {
            Transform headBone = FindHeadBone(renderer);

            Bounds meshBounds = GetFaceBoneWeightBounds(renderer);
            if (meshBounds.extents.magnitude > 0.001f)
            {
                Vector3 faceCenter = GetFaceVisualCenter(renderer, meshBounds);
                float faceWidth  = Mathf.Clamp(meshBounds.size.x, 0.10f, 0.40f);
                float faceHeight = Mathf.Clamp(meshBounds.size.y, 0.12f, 0.45f);
                float faceDepth  = Mathf.Clamp(meshBounds.size.z, 0.08f, 0.35f);
                return new Bounds(faceCenter, new Vector3(faceWidth, faceHeight, faceDepth));
            }

            if (headBone != null)
            {
                Vector3 faceForward = GetFaceForward(renderer);
                float estimatedFaceHeight = 0.30f;
                Vector3 faceCenter = headBone.position
                                     + headBone.up * (estimatedFaceHeight * 0.40f)
                                     + faceForward * 0.08f;
                return new Bounds(faceCenter, Vector3.one * estimatedFaceHeight);
            }

            return renderer.bounds;
        }

        private static Vector3 GetFaceVisualCenter(SkinnedMeshRenderer renderer, Bounds faceBounds)
        {
            Transform headBone = FindHeadBone(renderer);
            if (headBone == null) return faceBounds.center;

            FindEyeBones(renderer, out Transform leftEye, out Transform rightEye);
            if (leftEye != null && rightEye != null)
            {
                Vector3 eyeMidpoint = (leftEye.position + rightEye.position) * 0.5f;
                float downOffset = faceBounds.size.y * 0.10f;
                return eyeMidpoint - headBone.up * downOffset;
            }

            Vector3 faceForward = GetFaceForward(renderer);
            return headBone.position + headBone.up * (faceBounds.size.y * 0.40f) + faceForward * 0.08f;
        }

        private static Bounds GetFaceBoneWeightBounds(SkinnedMeshRenderer renderer)
        {
            Mesh sharedMesh = renderer.sharedMesh;
            if (sharedMesh == null || !sharedMesh.isReadable) return default;

            BoneWeight[] boneWeights = sharedMesh.boneWeights;
            Vector3[] meshVertices = sharedMesh.vertices;
            if (boneWeights == null || boneWeights.Length == 0) return default;
            if (meshVertices == null || boneWeights.Length != meshVertices.Length) return default;

            Transform[] bones = renderer.bones;
            if (bones == null) return default;

            HashSet<int> faceBoneIndices = new HashSet<int>();
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null && IsFaceBone(bones[i].name))
                    faceBoneIndices.Add(i);
            }
            if (faceBoneIndices.Count == 0) return default;

            Matrix4x4 localToWorld = renderer.transform.localToWorldMatrix;
            List<Vector3> worldVerts = new List<Vector3>();

            for (int i = 0; i < boneWeights.Length; i++)
            {
                BoneWeight bw = boneWeights[i];

                int bestBone = -1;
                float bestWeight = 0f;
                CheckBoneWeight(bw.boneIndex0, bw.weight0, ref bestBone, ref bestWeight);
                CheckBoneWeight(bw.boneIndex1, bw.weight1, ref bestBone, ref bestWeight);
                CheckBoneWeight(bw.boneIndex2, bw.weight2, ref bestBone, ref bestWeight);
                CheckBoneWeight(bw.boneIndex3, bw.weight3, ref bestBone, ref bestWeight);

                if (bestBone >= 0 && faceBoneIndices.Contains(bestBone) && bestWeight > 0.3f)
                    worldVerts.Add(localToWorld.MultiplyPoint3x4(meshVertices[i]));
            }

            if (worldVerts.Count < 3) return default;

            Bounds bounds = new Bounds(worldVerts[0], Vector3.zero);
            foreach (var v in worldVerts)
                bounds.Encapsulate(v);

            const float maxFaceSize = 0.60f;
            if (bounds.size.x > maxFaceSize || bounds.size.y > maxFaceSize || bounds.size.z > maxFaceSize)
                return default;

            return bounds;
        }

        private static void CheckBoneWeight(int boneIndex, float weight, ref int bestBone, ref float bestWeight)
        {
            if (weight > bestWeight) { bestBone = boneIndex; bestWeight = weight; }
        }

        private static bool IsFaceBone(string boneName)
        {
            if (string.IsNullOrEmpty(boneName)) return false;
            string lower = boneName.ToLowerInvariant();

            if (lower.Contains("hair") || lower.Contains("kami") ||
                lower.Contains("ear") || lower.Contains("mimi") ||
                lower.Contains("hat") || lower.Contains("ribbon") ||
                lower.Contains("accessory") || lower.Contains("ahoge") ||
                lower.Contains("tail") || lower.Contains("ponytail"))
                return false;

            return lower.Contains("head") ||
                   lower.Contains("eye") ||
                   lower.Contains("jaw") || lower.Contains("chin") ||
                   lower.Contains("nose") ||
                   lower.Contains("lip") || lower.Contains("mouth") ||
                   lower.Contains("brow") || lower.Contains("eyelid") ||
                   lower.Contains("face") || lower.Contains("cheek") ||
                   lower.Contains("tongue");
        }

        // ════════════════════════════════════════════════════════
        //  骨骼查找
        // ════════════════════════════════════════════════════════

        private static void FindEyeBones(SkinnedMeshRenderer renderer, out Transform leftEye, out Transform rightEye)
        {
            leftEye = null;
            rightEye = null;
            if (renderer.bones == null) return;

            foreach (var bone in renderer.bones)
            {
                if (bone == null) continue;
                string lower = bone.name.ToLowerInvariant();
                if (!lower.Contains("eye")) continue;

                if (lower.Contains("left") || lower.EndsWith("_l") || lower.EndsWith(".l") || lower.Contains("_l_"))
                    leftEye = bone;
                else if (lower.Contains("right") || lower.EndsWith("_r") || lower.EndsWith(".r") || lower.Contains("_r_"))
                    rightEye = bone;
                else if (leftEye == null)
                    leftEye = bone;
                else if (rightEye == null)
                    rightEye = bone;
            }
        }

        private static Transform FindHeadBone(SkinnedMeshRenderer renderer)
        {
            if (renderer.bones == null) return null;
            foreach (var bone in renderer.bones)
            {
                if (bone != null && bone.name.IndexOf("head", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return bone;
            }
            return null;
        }

        private static Vector3 GetFaceForward(SkinnedMeshRenderer renderer)
        {
            Transform headBone = FindHeadBone(renderer);
            if (headBone != null)
            {
                float dot = Vector3.Dot(headBone.forward, renderer.transform.forward);
                return dot >= 0f ? headBone.forward : -headBone.forward;
            }
            return renderer.transform.forward;
        }
    }
}
