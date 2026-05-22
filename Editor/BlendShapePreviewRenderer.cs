using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MmdBlendShapeScaler
{
    /// <summary>
    /// 使用临时 Camera + AnimationMode 将单个 BlendShape 渲染为 Texture2D。
    /// 基于 blendshape-viewer (Hai~) 的方案简化。
    /// </summary>
    public static class BlendShapePreviewRenderer
    {
        private static bool _warmedUp;

        /// <summary>
        /// 缩放倍率：>1 放大（相机更近），<1 缩小（相机更远）。
        /// 绘制缩略图前设置，例如 2.0 表示面在画面中占 2 倍大。
        /// </summary>
        public static float ZoomMultiplier { get; set; } = 1.0f;

        /// <summary>
        /// 预热 AnimationMode 完整管线。
        /// Unity 的 AnimationMode 在首次调用时存在延迟初始化问题：
        /// BeginSampling → SampleAnimationClip → EndSampling 路径的第一个周期
        /// 不会把动画状态同步到网格，导致第一个缩略图捕获到权重为 0 的画面。
        /// 这里用一个包含真实 Clip + 完整采样周期的空操作来预热。
        /// </summary>
        public static void WarmupAnimationMode(SkinnedMeshRenderer renderer)
        {
            if (_warmedUp || renderer == null || renderer.sharedMesh == null) return;
            _warmedUp = true;

            var mesh = renderer.sharedMesh;
            if (mesh.blendShapeCount == 0) return;

            var clip = new AnimationClip();
            clip.hideFlags = HideFlags.HideAndDontSave;

            var firstBsName = mesh.GetBlendShapeName(0);
            AnimationUtility.SetEditorCurve(
                clip,
                new EditorCurveBinding
                {
                    path = "",
                    type = typeof(SkinnedMeshRenderer),
                    propertyName = $"blendShape.{firstBsName}"
                },
                AnimationCurve.Constant(0, 1f / 60f, 0f)  // weight=0, no visual side effect
            );

            try
            {
                AnimationMode.StartAnimationMode();
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(renderer.gameObject, clip, 1f / 60f);
                AnimationMode.EndSampling();
            }
            finally
            {
                AnimationMode.StopAnimationMode();
                Object.DestroyImmediate(clip);
            }
        }

        /// <summary>
        /// 渲染单个 BlendShape 在指定权重下的缩略图。
        /// 自动将相机对准 renderer 包围盒正面中心，确保缩略图一致。
        /// 调用者负责用 DestroyImmediate 释放返回的 Texture2D。
        /// </summary>
        public static Texture2D Render(
            SkinnedMeshRenderer renderer,
            int blendshapeIndex,
            float weight,
            int size)
        {
            if (renderer == null || renderer.sharedMesh == null) return null;
            if (blendshapeIndex < 0 || blendshapeIndex >= renderer.sharedMesh.blendShapeCount) return null;

            var blendShapeName = renderer.sharedMesh.GetBlendShapeName(blendshapeIndex);

            // ── 创建临时相机 ──
            var camGo = new GameObject("__MMD_BS_Preview_Cam__");
            camGo.hideFlags = HideFlags.HideAndDontSave;
            var cam = camGo.AddComponent<Camera>();

            // 用 Scene View 的背景色，但独立定位
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null)
            {
                cam.clearFlags = sceneView.camera.clearFlags;
                cam.backgroundColor = sceneView.camera.backgroundColor;
            }
            else
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.25f, 0.25f, 0.25f, 1f);
            }
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;

            // 根据 renderer 包围盒定位相机，保证人脸居中
            FrameRendererInCamera(cam, renderer);

            // ── 创建 AnimationClip ──
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
                // ── 采样并渲染 ──
                AnimationMode.StartAnimationMode();
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
                AnimationMode.StopAnimationMode();
                Object.DestroyImmediate(clip);
                Object.DestroyImmediate(camGo);
            }

            return result;
        }

        /// <summary>
        /// 将相机对准 renderer 包围盒正面中心，确保缩略图角度一致。
        /// 优先使用 Head bone 的朝向（面部实际朝向），回退到 renderer.transform.forward。
        /// </summary>
        private static void FrameRendererInCamera(Camera cam, SkinnedMeshRenderer renderer)
        {
            // Use face-level bounds instead of full-body renderer bounds
            Bounds bounds = GetFaceBounds(renderer);
            Vector3 faceForward = GetFaceForward(renderer);

            // 若包围盒异常（零或 NaN），保守回退
            float extentMag = bounds.extents.magnitude;
            if (extentMag < 0.001f || float.IsNaN(extentMag))
            {
                cam.transform.position = bounds.center + faceForward * 1f;
                cam.transform.LookAt(bounds.center);
                cam.fieldOfView = 30f;
                return;
            }

            // 瞄准点：包围盒中心略偏上（人脸通常在网格上半部）
            // 使用 head bone 的本地 up 方向，避免角色倾斜时偏移方向错误
            Transform headBone = FindHeadBone(renderer);
            Vector3 upDir = headBone != null ? headBone.up : renderer.transform.up;
            Vector3 target = bounds.center + upDir * bounds.extents.y * 0.25f;

            // 25° FOV 面部特写，透视投影
            float fov = 25f;
            cam.fieldOfView = fov;
            cam.orthographic = false;

            // 计算距离：让包围球体在 1.2x 留白下刚好装入画面
            float objectRadius = extentMag * 1.2f;
            float distance = objectRadius / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            distance = Mathf.Max(distance, 0.3f);

            // 应用缩放倍率（>1 放大=相机更近）
            float zoom = Mathf.Max(ZoomMultiplier, 0.1f);
            distance /= zoom;

            // 相机置于角色正面（faceForward 指向面部外侧，故 += 将相机放在前方）
            cam.transform.position = target + faceForward * distance;
            cam.transform.LookAt(target);
        }

        /// <summary>
        /// 返回面部级别的包围盒。
        /// 优先从蒙皮网格的顶点数据中提取 Head bone 权重 > 0.3 的顶点包围盒；
        /// 回退到头骨位置偏移估算；
        /// 最终回退到整个 renderer 的包围盒。
        /// </summary>
        private static Bounds GetFaceBounds(SkinnedMeshRenderer renderer)
        {
            // 1) 从蒙皮权重提取头骨关联顶点的包围盒
            Bounds meshBounds = GetHeadBoneVertexBounds(renderer);
            if (meshBounds.extents.magnitude > 0.001f)
                return meshBounds;

            // 2) 头骨位置偏移估算
            Transform headBone = FindHeadBone(renderer);
            if (headBone != null)
            {
                Vector3 faceForward = GetFaceForward(renderer);
                Vector3 faceCenter = headBone.position
                                     + headBone.up * 0.12f
                                     + faceForward * 0.08f;
                return new Bounds(faceCenter, Vector3.one * 0.30f);
            }

            // 3) 全身包围盒
            return renderer.bounds;
        }

        /// <summary>
        /// 找到 Head bone 在骨骼数组中的索引，收集所有权重 > 0.3 的顶点，
        /// 返回其世界空间包围盒。
        /// </summary>
        private static Bounds GetHeadBoneVertexBounds(SkinnedMeshRenderer renderer)
        {
            Mesh sharedMesh = renderer.sharedMesh;
            if (sharedMesh == null) return default;

            if (!sharedMesh.isReadable) return default;

            BoneWeight[] boneWeights = sharedMesh.boneWeights;
            Vector3[] meshVertices = sharedMesh.vertices;
            if (boneWeights == null || boneWeights.Length == 0) return default;
            if (meshVertices == null || meshVertices.Length == 0) return default;
            if (boneWeights.Length != meshVertices.Length) return default;

            Transform headBone = FindHeadBone(renderer);
            if (headBone == null) return default;

            Transform[] bones = renderer.bones;
            int headBoneIndex = -1;
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == headBone)
                {
                    headBoneIndex = i;
                    break;
                }
            }
            if (headBoneIndex < 0) return default;

            Matrix4x4 localToWorld = renderer.transform.localToWorldMatrix;

            List<Vector3> worldVerts = new List<Vector3>();
            for (int i = 0; i < boneWeights.Length; i++)
            {
                BoneWeight bw = boneWeights[i];
                float headWeight = 0f;
                if (bw.boneIndex0 == headBoneIndex) headWeight = bw.weight0;
                else if (bw.boneIndex1 == headBoneIndex) headWeight = bw.weight1;
                else if (bw.boneIndex2 == headBoneIndex) headWeight = bw.weight2;
                else if (bw.boneIndex3 == headBoneIndex) headWeight = bw.weight3;

                if (headWeight > 0.3f)
                    worldVerts.Add(localToWorld.MultiplyPoint3x4(meshVertices[i]));
            }

            if (worldVerts.Count == 0) return default;

            Bounds bounds = new Bounds(worldVerts[0], Vector3.zero);
            foreach (var v in worldVerts)
                bounds.Encapsulate(v);

            return bounds;
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

        /// <summary>
        /// 返回面部朝向。
        /// 优先使用 Head bone 的 forward（面的实际朝向），
        /// 回退到 renderer.transform.forward。
        /// </summary>
        private static Vector3 GetFaceForward(SkinnedMeshRenderer renderer)
        {
            Transform headBone = FindHeadBone(renderer);
            if (headBone != null)
            {
                // headBone.forward 在部分 MMD 模型上指向头部内部(Z轴朝后)而非面部前方
                // 此时应与角色大致朝向 (renderer.transform.forward) 做点积判断
                float dot = Vector3.Dot(headBone.forward, renderer.transform.forward);
                return dot >= 0f ? headBone.forward : -headBone.forward;
            }
            return renderer.transform.forward;
        }
    }
}
