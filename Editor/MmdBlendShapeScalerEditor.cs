using UnityEditor;
using UnityEngine;

namespace MmdBlendShapeScaler
{
    [CustomEditor(typeof(MmdBlendShapeScaler))]
    public class MmdBlendShapeScalerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var S = Strings.Current;
            var scaler = (MmdBlendShapeScaler)target;

            // ── Renderer reference ──
            var newRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(
                S.TargetRenderer, scaler.targetRenderer, typeof(SkinnedMeshRenderer), true);
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
                EditorGUILayout.HelpBox(S.NoScaleHint, MessageType.Info);
            }
            else
            {
                EditorGUILayout.LabelField(
                    string.Format(S.ConfiguredFmt, scaler.Count), EditorStyles.boldLabel);
                foreach (var entry in scaler.GetModifiedEntries())
                {
                    EditorGUILayout.LabelField(
                        string.Format(S.EntryFmt, entry.name, entry.scale * 100f));
                }
            }

            // ── Buttons ──
            EditorGUILayout.Space(8);
            if (GUILayout.Button(S.OpenCalibrator, GUILayout.Height(30)))
            {
                MmdCalibratorWindow.ShowWindow(scaler);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(S.ClearAllScales))
            {
                Undo.RecordObject(scaler, "Clear MMD Scale Mappings");
                scaler.RemoveAll();
                EditorUtility.SetDirty(scaler);
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
