using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MmdBlendShapeScaler
{
    public class MmdCalibratorWindow : EditorWindow
    {
        // ── External references ──
        private MmdBlendShapeScaler _scaler;
        private SkinnedMeshRenderer _faceRenderer;

        // ── State ──
        private class ShapeEntry
        {
            public string name;            // まばたき
            public string description;     // 眨眼/Blink
            public MmdShapeCategory category;
            public int meshIndex;
            public int sliderValue = 100;  // 0-200 (displayed as %)
            public Texture2D thumbnail;

            public float Scale => sliderValue / 100f;
            public bool IsModified => sliderValue != 100;
        }

        private List<ShapeEntry> _entries = new List<ShapeEntry>();
        private ShapeEntry _selectedEntry;  // null = grid mode
        private Vector2 _scrollPos;

        // ── View options ──
        private int _thumbnailSize = 150;
        private float _zoomLevel = 2.0f;
        private static List<float> _recentValues = new List<float>();  // sorted ascending, deduped
        private const string ZoomLevelPrefKey = "MmdBlendShapeScaler.ZoomLevel";
        private const int MaxRecentValues = 8;

        // ── Foldouts ──
        private bool _foldoutEye = true;
        private bool _foldoutMouth = true;
        private bool _foldoutBrow = true;
        private bool _foldoutOther = true;

        // ── Window ──
        public static void ShowWindow(MmdBlendShapeScaler scaler)
        {
            var window = GetWindow<MmdCalibratorWindow>(Strings.Current.WindowTitle);
            window.minSize = new Vector2(520, 400);
            window._scaler = scaler;
            window._faceRenderer = scaler.targetRenderer;
            window.Show();
        }

        [MenuItem("Tools/VRC Avatar MMD & Blink Fixer")]
        public static void OpenStandalone()
        {
            var window = GetWindow<MmdCalibratorWindow>(Strings.Current.WindowTitle);
            window.minSize = new Vector2(520, 400);
            window.Show();
        }

        private void OnEnable()
        {
            _zoomLevel = EditorPrefs.GetFloat(ZoomLevelPrefKey, 2.0f);
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        private void OnDisable()
        {
            EditorPrefs.SetFloat(ZoomLevelPrefKey, _zoomLevel);
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            RestoreAllWeights();
            ClearThumbnails();
        }

        private void OnDestroy()
        {
            RestoreAllWeights();
            ClearThumbnails();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode ||
                state == PlayModeStateChange.EnteredEditMode)
            {
                RestoreAllWeights();
            }
        }

        private void OnBeforeAssemblyReload()
        {
            RestoreAllWeights();
        }

        // ══════════════════════════════════════════════
        //  OnGUI
        // ══════════════════════════════════════════════

        private void OnGUI()
        {
            var S = Strings.Current;

            // ── Header ──
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(S.WindowTitle, EditorStyles.boldLabel);

            // ── Renderer selection ──
            EditorGUILayout.BeginHorizontal();
            var prevRenderer = _faceRenderer;
            _faceRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(
                S.FaceRenderer, _faceRenderer, typeof(SkinnedMeshRenderer), true);

            // Track the scaler component and sync targetRenderer on selection change
            if (prevRenderer != _faceRenderer && _faceRenderer != null)
            {
                _scaler = _faceRenderer.GetComponentInParent<MmdBlendShapeScaler>();
                if (_scaler != null && _scaler.targetRenderer != _faceRenderer)
                {
                    Undo.RecordObject(_scaler, "Set Target Renderer");
                    _scaler.targetRenderer = _faceRenderer;
                    EditorUtility.SetDirty(_scaler);
                }
            }

            EditorGUI.BeginDisabledGroup(_faceRenderer == null);
            if (GUILayout.Button(S.ScanMmdShapes, GUILayout.Width(140)))
            {
                Scan();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            // ── Component status ──
            if (_scaler != null)
            {
                var status = _scaler.IsValid ? S.StatusValid : S.StatusInvalid;
                EditorGUILayout.LabelField(
                    string.Format(S.ComponentStatusFmt, _scaler.Count, status),
                    EditorStyles.miniLabel);
            }

            // ── Help boxes ──
            if (_faceRenderer == null)
            {
                EditorGUILayout.HelpBox(S.HelpDragRenderer, MessageType.Info);
                return;
            }
            if (_entries.Count == 0)
            {
                EditorGUILayout.HelpBox(S.HelpClickScan, MessageType.Info);
                return;
            }

            EditorGUILayout.Space(4);

            // ── View mode routing ──
            if (_selectedEntry != null)
                DrawDetailView();
            else
                DrawGridView();
        }

        // ══════════════════════════════════════════════
        //  Grid View
        // ══════════════════════════════════════════════

        private void DrawGridView()
        {
            var S = Strings.Current;
            int modifiedCount = _entries.Count(e => e.IsModified);
            int dirtyCount = _entries.Count(e => Mathf.Abs(e.sliderValue - GetSavedScale(e.name) * 100f) > 0.5f);

            // ── Summary bar ──
            string summary;
            if (modifiedCount > 0)
            {
                summary = string.Format(S.SummaryModifiedFmt, _entries.Count, modifiedCount);
                if (dirtyCount > 0)
                    summary += string.Format(S.SummaryDirtyFmt, dirtyCount);

                var c = GUI.color;
                GUI.color = new Color(1f, 0.65f, 0.2f);
                EditorGUILayout.LabelField(summary, EditorStyles.miniLabel);
                GUI.color = c;
            }
            else
            {
                EditorGUILayout.LabelField(string.Format(S.SummaryFmt, _entries.Count), EditorStyles.miniLabel);
            }

            // ── View options ──
            EditorGUILayout.BeginHorizontal();
            _thumbnailSize = EditorGUILayout.IntSlider(S.ThumbnailSize, _thumbnailSize, 100, 150);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            _zoomLevel = EditorGUILayout.Slider(S.ZoomLevel, _zoomLevel, 0.3f, 3.0f);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            DrawCategoryGrid(S.Eyes, MmdShapeCategory.眼部, ref _foldoutEye);
            DrawCategoryGrid(S.Mouth, MmdShapeCategory.嘴部, ref _foldoutMouth);
            DrawCategoryGrid(S.Eyebrows, MmdShapeCategory.眉毛, ref _foldoutBrow);
            DrawCategoryGrid(S.Other, MmdShapeCategory.未知, ref _foldoutOther);

            EditorGUILayout.EndScrollView();

            // ── Bottom bar ──
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(S.ResetAll, GUILayout.Height(26)))
                ResetAll();
            if (GUILayout.Button(S.ReScan, GUILayout.Height(26)))
                Scan();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawCategoryGrid(string title, MmdShapeCategory category, ref bool foldout)
        {
            var S = Strings.Current;
            var items = _entries.Where(e => (e.category & category) != 0 ||
                (category == MmdShapeCategory.未知 && e.category == MmdShapeCategory.未知)).ToList();
            if (items.Count == 0) return;

            int modified = items.Count(e => e.IsModified);
            string label = $"{title} ({items.Count})";
            if (modified > 0) label += $" {string.Format(S.ModifiedCountFmt, modified)}";

            foldout = EditorGUILayout.Foldout(foldout, label, true);
            if (!foldout) return;

            float availableWidth = position.width - 40;
            int perRow = Mathf.Max(1, Mathf.FloorToInt(availableWidth / (_thumbnailSize + 16)));

            for (int i = 0; i < items.Count; i++)
            {
                if (i % perRow == 0)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                }

                DrawGridCell(items[i]);

                if ((i + 1) % perRow == 0 || i == items.Count - 1)
                {
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.Space(4);
        }

        private void DrawGridCell(ShapeEntry entry)
        {
            var S = Strings.Current;
            EditorGUILayout.BeginVertical(GUILayout.Width(_thumbnailSize));

            var oldBg = GUI.backgroundColor;
            if (entry.IsModified)
                GUI.backgroundColor = new Color(1f, 0.7f, 0.3f, 0.3f);

            var thumbContent = new GUIContent
            {
                image = entry.thumbnail,
                tooltip = $"{entry.name}\n{entry.description}\n{string.Format(S.CurrentTooltipFmt, entry.sliderValue)}"
            };

            if (GUILayout.Button(thumbContent, GUILayout.Width(_thumbnailSize), GUILayout.Height(_thumbnailSize)))
            {
                SelectEntry(entry);
            }

            GUI.backgroundColor = oldBg;

            // Name
            var nameStyle = entry.IsModified ? EditorStyles.boldLabel : EditorStyles.miniLabel;
            EditorGUILayout.LabelField(entry.name, nameStyle, GUILayout.Width(_thumbnailSize));
            EditorGUILayout.LabelField(entry.description, EditorStyles.miniLabel, GUILayout.Width(_thumbnailSize));

            // Scale value
            if (entry.IsModified)
            {
                var c = GUI.color;
                GUI.color = new Color(1f, 0.6f, 0.2f);
                EditorGUILayout.LabelField($"{entry.sliderValue}%", EditorStyles.boldLabel, GUILayout.Width(_thumbnailSize));
                GUI.color = c;
            }
            else
            {
                EditorGUILayout.LabelField(S.PctValue, EditorStyles.miniLabel, GUILayout.Width(_thumbnailSize));
            }

            EditorGUILayout.EndVertical();
        }

        // ══════════════════════════════════════════════
        //  Detail View
        // ══════════════════════════════════════════════

        private void DrawDetailView()
        {
            var S = Strings.Current;
            var entry = _selectedEntry;

            // ── Navigation ──
            bool exitDetail = false;
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(S.BackToGrid, GUILayout.Width(120)))
            {
                ConfirmCurrent();
                DeselectCurrent();
                exitDetail = true;
            }

            EditorGUILayout.LabelField($"{entry.name}", EditorStyles.boldLabel, GUILayout.Width(100));
            EditorGUILayout.LabelField(entry.description, GUILayout.Width(100));
            GUILayout.FlexibleSpace();

            int curIdx = _entries.IndexOf(entry);
            EditorGUI.BeginDisabledGroup(curIdx <= 0);
            if (GUILayout.Button(S.Prev, GUILayout.Width(80)))
            {
                ConfirmCurrent();
                DeselectCurrent();
                SelectEntry(_entries[curIdx - 1]);
                exitDetail = true;
            }
            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginDisabledGroup(curIdx >= _entries.Count - 1);
            if (GUILayout.Button(S.Next, GUILayout.Width(80)))
            {
                ConfirmCurrent();
                DeselectCurrent();
                SelectEntry(_entries[curIdx + 1]);
                exitDetail = true;
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
            if (exitDetail) return;

            EditorGUILayout.Space(8);

            // ── Thumbnail + Controls ──
            EditorGUILayout.BeginHorizontal();

            // Large reference thumbnail
            if (entry.thumbnail != null)
                GUILayout.Box(entry.thumbnail, GUILayout.Width(200), GUILayout.Height(200));
            else
                GUILayout.Box(S.NoPreview, GUILayout.Width(200), GUILayout.Height(200));

            GUILayout.Space(12);

            // Slider and presets
            EditorGUILayout.BeginVertical();

            EditorGUILayout.LabelField(S.ScaleFactor, EditorStyles.boldLabel);

            // Slider — preview only, save on navigation / confirm
            float newVal = EditorGUILayout.Slider(entry.sliderValue, 0f, 200f);
            if (Mathf.Abs(newVal - entry.sliderValue) > 0.1f)
            {
                entry.sliderValue = Mathf.RoundToInt(newVal);
                PreviewOnMesh(entry);
                Repaint();
            }

            EditorGUILayout.Space(8);

            // Quick-apply: up to 5 recent values (including 100%)
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(S.Quick, EditorStyles.miniLabel, GUILayout.Width(36));
            int shown = 0;
            int maxShow = 5;
            if (GUILayout.Button(S.PctValue, GUILayout.Width(45))) { entry.sliderValue = 100; PreviewOnMesh(entry); ConfirmCurrent(); }
            shown++;
            foreach (float val in _recentValues)
            {
                if (shown >= maxShow) break;
                int pct = Mathf.RoundToInt(val);
                if (pct != entry.sliderValue && pct != 100)
                {
                    if (GUILayout.Button($"{pct}%", GUILayout.Width(45)))
                        { entry.sliderValue = pct; PreviewOnMesh(entry); ConfirmCurrent(); }
                    shown++;
                }
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            // Status
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(S.SceneViewHint, EditorStyles.miniLabel);
            if (entry.IsModified)
            {
                var c = GUI.color;
                GUI.color = new Color(1f, 0.6f, 0.2f);
                EditorGUILayout.LabelField(string.Format(S.ScaledToFmt, entry.sliderValue), EditorStyles.miniLabel);
                GUI.color = c;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            // ── Bottom actions ──
            EditorGUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(S.Confirm, GUILayout.Height(30)))
            {
                ConfirmCurrent();
                DeselectCurrent();
            }
            if (GUILayout.Button(S.Cancel, GUILayout.Height(30)))
            {
                // Restore from component value
                float saved = GetSavedScale(entry.name) * 100f;
                entry.sliderValue = (int)saved;
                DeselectCurrent();
            }
            EditorGUILayout.EndHorizontal();
        }

        // ══════════════════════════════════════════════
        //  Selection / Preview / Confirm
        // ══════════════════════════════════════════════

        private void SelectEntry(ShapeEntry entry)
        {
            if (_selectedEntry == entry) return;

            // Restore previous
            if (_selectedEntry != null)
                _faceRenderer.SetBlendShapeWeight(_selectedEntry.meshIndex, 0f);

            _selectedEntry = entry;
            PreviewOnMesh(entry);

            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.Focus();

            FrameSceneViewCamera();
        }

        private void FrameSceneViewCamera()
        {
            if (_faceRenderer == null) return;
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null) return;

            var sceneCam = sceneView.camera;
            BlendShapePreviewRenderer.FrameRendererInCamera(sceneCam, _faceRenderer);
            sceneView.Repaint();
        }

        private void DeselectCurrent()
        {
            if (_selectedEntry == null) return;
            _faceRenderer.SetBlendShapeWeight(_selectedEntry.meshIndex, 0f);
            _selectedEntry = null;
        }

        private void PreviewOnMesh(ShapeEntry entry)
        {
            if (_faceRenderer == null) return;
            _faceRenderer.SetBlendShapeWeight(entry.meshIndex, entry.sliderValue);
            SceneView.RepaintAll();
        }

        private void ConfirmCurrent()
        {
            if (_selectedEntry == null || _scaler == null) return;

            var entry = _selectedEntry;
            float pct = entry.sliderValue;

            if (_recentValues == null)
                _recentValues = new List<float>();

            // Insert into sorted history (deduped, skip 100)
            if (Mathf.Abs(pct - 100f) > 0.5f && !_recentValues.Contains(pct))
            {
                _recentValues.Add(pct);
                _recentValues.Sort();
                if (_recentValues.Count > MaxRecentValues)
                    _recentValues.RemoveAt(_recentValues.Count - 1); // drop largest
                Debug.Log($"[MMDBlinkFixer] Recent values: [{string.Join(", ", _recentValues)}]");
            }

            Undo.RegisterCompleteObjectUndo(_scaler, $"Set MMD Scale {entry.name}={entry.sliderValue}%");
            _scaler.SetScale(entry.name, entry.Scale);
            EditorUtility.SetDirty(_scaler);
        }

        private void RestoreAllWeights()
        {
            if (_faceRenderer == null) return;
            foreach (var entry in _entries)
                _faceRenderer.SetBlendShapeWeight(entry.meshIndex, 0f);
        }

        private float GetSavedScale(string mmdName)
        {
            return _scaler != null ? _scaler.GetScale(mmdName) : 1.0f;
        }

        // ══════════════════════════════════════════════
        //  Reset
        // ══════════════════════════════════════════════

        private void ResetAll()
        {
            var S = Strings.Current;
            if (_entries.Any(e => e.IsModified))
            {
                if (!EditorUtility.DisplayDialog(S.DlgResetTitle,
                    S.DlgResetMsg,
                    S.DlgResetBtn, S.DlgCancelBtn))
                    return;
            }

            foreach (var entry in _entries)
                entry.sliderValue = 100;

            if (_scaler != null)
            {
                Undo.RegisterCompleteObjectUndo(_scaler, "Reset All MMD Scales");
                _scaler.RemoveAll();
                EditorUtility.SetDirty(_scaler);
            }

            RestoreAllWeights();
            Repaint();
        }

        // ══════════════════════════════════════════════
        //  Scan + Thumbnail Generation
        // ══════════════════════════════════════════════

        private void Scan()
        {
            if (_faceRenderer == null) return;

            var S = Strings.Current;
            int unsaved = _entries.Count(e =>
                Mathf.Abs(e.sliderValue - GetSavedScale(e.name) * 100f) > 0.5f);

            if (unsaved > 0)
            {
                if (!EditorUtility.DisplayDialog(S.DlgRescanTitle,
                    string.Format(S.DlgRescanMsgFmt, unsaved),
                    S.DlgRescanBtn, S.DlgCancelBtn))
                    return;
            }

            DeselectCurrent();
            ClearThumbnails();
            _entries.Clear();

            var mesh = _faceRenderer.sharedMesh;
            if (mesh == null || mesh.blendShapeCount == 0) return;

            // Find MMD blendshapes on this mesh
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string name = mesh.GetBlendShapeName(i);
                if (!MmdShapeDatabase.名称到信息映射.TryGetValue(name, out var info)) continue;

                // Load saved scale from component
                float savedScale = GetSavedScale(name);
                int sliderValue = Mathf.RoundToInt(savedScale * 100f);

                _entries.Add(new ShapeEntry
                {
                    name = name,
                    description = info.中文说明,
                    category = info.分类,
                    meshIndex = i,
                    sliderValue = sliderValue
                });
            }

            // Sort by category + name
            _entries = _entries
                .OrderBy(e => (e.category & MmdShapeCategory.眼部) != 0 ? 0 :
                              (e.category & MmdShapeCategory.嘴部) != 0 ? 1 :
                              (e.category & MmdShapeCategory.眉毛) != 0 ? 2 : 3)
                .ThenBy(e => e.name)
                .ToList();

            // Ensure all weights are zero before rendering thumbnails
            RestoreAllWeights();

            // Start a fresh render batch — face bounds / camera are cached inside Render()
            BlendShapePreviewRenderer.ZoomMultiplier = _zoomLevel;
            BlendShapePreviewRenderer.ClearCache();

            // Generate thumbnails
            try
            {
                for (int i = 0; i < _entries.Count; i++)
                {
                    var entry = _entries[i];
                    EditorUtility.DisplayProgressBar(
                        S.ProgressTitle,
                        string.Format(S.ProgressFmt, entry.name, i + 1, _entries.Count),
                        (float)i / _entries.Count);

                    entry.thumbnail = BlendShapePreviewRenderer.Render(
                        _faceRenderer,
                        entry.meshIndex,
                        entry.sliderValue,  // Render at saved scale to reflect adjustments
                        _thumbnailSize);
                }
            }
            finally
            {
                BlendShapePreviewRenderer.EndBatch();
                EditorUtility.ClearProgressBar();
            }

            Repaint();
        }

        private void ClearThumbnails()
        {
            foreach (var entry in _entries)
            {
                if (entry.thumbnail != null)
                    Object.DestroyImmediate(entry.thumbnail);
            }
        }
    }
}
