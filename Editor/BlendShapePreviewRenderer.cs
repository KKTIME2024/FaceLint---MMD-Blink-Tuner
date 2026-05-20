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

            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null || sceneView.camera == null) return null;

            var sceneCam = sceneView.camera;
            var blendShapeName = renderer.sharedMesh.GetBlendShapeName(blendshapeIndex);

            // ── 创建临时相机 ──
            var camGo = new GameObject("__MMD_BS_Preview_Cam__");
            camGo.hideFlags = HideFlags.HideAndDontSave;
            var cam = camGo.AddComponent<Camera>();
            cam.transform.position = sceneCam.transform.position;
            cam.transform.rotation = sceneCam.transform.rotation;
            cam.fieldOfView = sceneCam.fieldOfView;
            cam.orthographic = sceneCam.orthographic;
            cam.nearClipPlane = sceneCam.nearClipPlane;
            cam.farClipPlane = sceneCam.farClipPlane;
            cam.orthographicSize = sceneCam.orthographicSize;
            cam.clearFlags = sceneCam.clearFlags;
            cam.backgroundColor = sceneCam.backgroundColor;

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
    }
}
