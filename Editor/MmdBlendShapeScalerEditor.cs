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

            // ── UI Language (global, persisted to EditorPrefs) ──
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(S.LangLabel, GUILayout.Width(80));
            var newLang = (UILang)EditorGUILayout.EnumPopup(Strings.Language, GUILayout.Width(100));
            if (newLang != Strings.Language)
            {
                Strings.Language = newLang;
                S = Strings.Current;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // ── Renderer reference ──
            var newRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(
                S.TargetRenderer, scaler.targetRenderer, typeof(SkinnedMeshRenderer), true);
            if (newRenderer != scaler.targetRenderer)
            {
                Undo.RecordObject(scaler, "Set Target Renderer");
                scaler.targetRenderer = newRenderer;
                EditorUtility.SetDirty(scaler);
            }

            // ── Pass C toggle ──
            var newNeutralize = EditorGUILayout.Toggle(S.NeutralizeBakedFace, scaler.neutralizeBakedFace);
            if (newNeutralize != scaler.neutralizeBakedFace)
            {
                Undo.RecordObject(scaler, "Toggle Neutralize Baked Face");
                scaler.neutralizeBakedFace = newNeutralize;
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
