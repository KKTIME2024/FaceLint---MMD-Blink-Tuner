using UnityEditor;
using UnityEngine;

namespace MmdBlendShapeScaler
{
    [CustomEditor(typeof(MmdBlendShapeScaler))]
    public class MmdBlendShapeScalerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var scaler = (MmdBlendShapeScaler)target;

            // ── Renderer reference ──
            var newRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("Target Renderer", scaler.targetRenderer, typeof(SkinnedMeshRenderer), true);
            if (newRenderer != scaler.targetRenderer)
            {
                Undo.RecordObject(scaler, "Set Target Renderer");
                scaler.targetRenderer = newRenderer;
                EditorUtility.SetDirty(scaler);
            }

            // ── Summary ──
            EditorGUILayout.Space(4);
            if (scaler.Count == 0)
            {
                EditorGUILayout.HelpBox("No scale configured. All MMD blendshapes remain at 100%.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.LabelField($"Configured: {scaler.Count} blendshape(s)", EditorStyles.boldLabel);
                foreach (var entry in scaler.GetModifiedEntries())
                {
                    EditorGUILayout.LabelField($"  {entry.name}  →  {entry.scale * 100f:F0}%");
                }
            }

            // ── Buttons ──
            EditorGUILayout.Space(8);
            if (GUILayout.Button("Open Calibrator", GUILayout.Height(30)))
            {
                MmdCalibratorWindow.ShowWindow(scaler);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear All Scales"))
            {
                Undo.RecordObject(scaler, "Clear MMD Scale Mappings");
                scaler.RemoveAll();
                EditorUtility.SetDirty(scaler);
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
