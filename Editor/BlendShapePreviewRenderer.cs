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
        /// 沿 renderer.transform.forward 方向取景，用 25° FOV 做面部特写。
        /// </summary>
        private static void FrameRendererInCamera(Camera cam, SkinnedMeshRenderer renderer)
        {
            Bounds bounds = renderer.bounds;

            // 若包围盒异常（零或 NaN），保守回退
            float extentMag = bounds.extents.magnitude;
            if (extentMag < 0.001f || float.IsNaN(extentMag))
            {
                cam.transform.position = renderer.transform.position - renderer.transform.forward * 1f;
                cam.transform.LookAt(renderer.transform.position);
                cam.fieldOfView = 30f;
                return;
            }

            // 瞄准点：包围盒中心略偏上（人脸通常在网格上半部）
            Vector3 target = bounds.center + Vector3.up * bounds.extents.y * 0.25f;

            // 25° FOV 面部特写，透视投影
            float fov = 25f;
            cam.fieldOfView = fov;
            cam.orthographic = false;

            // 计算距离：让包围球体在 1.5x 留白下刚好装入画面
            float objectRadius = extentMag * 1.5f;
            float distance = objectRadius / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            distance = Mathf.Max(distance, 0.3f);

            // 相机置于角色正面，看向瞄准点
            cam.transform.position = target - renderer.transform.forward * distance;
            cam.transform.LookAt(target);
        }
    }
}
