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
        private bool _showDifferences = false; // diff highlighting (P2 feature, stub for now)
        private bool _autoConfirm = false;
        
        // ── Foldouts ──
        private bool _foldoutEye = true;
        private bool _foldoutMouth = true;
        private bool _foldoutBrow = true;
        private bool _foldoutOther = true;

        // ── Window ──
        public static void ShowWindow(MmdBlendShapeScaler scaler)
        {
            var window = GetWindow<MmdCalibratorWindow>("MMD Calibrator");
            window.minSize = new Vector2(520, 400);
            window._scaler = scaler;
            window._faceRenderer = scaler.targetRenderer;
            window.Show();
        }

        [MenuItem("Tools/MMD BlendShape Calibrator")]
        public static void OpenStandalone()
        {
            var window = GetWindow<MmdCalibratorWindow>("MMD Calibrator");
            window.minSize = new Vector2(520, 400);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        private void OnDisable()
        {
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
            // ── Header ──
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("MMD BlendShape Calibrator", EditorStyles.boldLabel);
            
            // ── Renderer selection ──
            EditorGUILayout.BeginHorizontal();
            var prevRenderer = _faceRenderer;
            _faceRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(
                "Face Renderer", _faceRenderer, typeof(SkinnedMeshRenderer), true);
            
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
            if (GUILayout.Button("Scan MMD Shapes", GUILayout.Width(120)))
            {
                Scan();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            // ── Component status ──
            if (_scaler != null)
            {
                EditorGUILayout.LabelField(
                    $"Scales stored: {_scaler.Count} | " +
                    $"Component: {(_scaler.IsValid ? "\u2713 Valid" : "\u2717 Invalid (no mesh)")}",
                    EditorStyles.miniLabel);
            }

            // ── Help boxes ──
            if (_faceRenderer == null)
            {
                EditorGUILayout.HelpBox("Drag in the face SkinnedMeshRenderer (usually the Body mesh).", MessageType.Info);
                return;
            }
            if (_entries.Count == 0)
            {
                EditorGUILayout.HelpBox("Click 'Scan MMD Shapes' to generate thumbnails.", MessageType.Info);
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
            int modifiedCount = _entries.Count(e => e.IsModified);
            int dirtyCount = _entries.Count(e => Mathf.Abs(e.sliderValue - GetSavedScale(e.name) * 100f) > 0.5f);

            // ── Summary bar ──
            string summary = $"{_entries.Count} MMD shapes";
            if (modifiedCount > 0)
            {
                var c = GUI.color;
                GUI.color = new Color(1f, 0.65f, 0.2f);
                EditorGUILayout.LabelField($"{summary} | Modified: {modifiedCount}" +
                    (dirtyCount > 0 && !_autoConfirm ? $" | Unconfirmed: {dirtyCount}" : ""),
                    EditorStyles.miniLabel);
                GUI.color = c;
            }
            else
            {
                EditorGUILayout.LabelField(summary, EditorStyles.miniLabel);
            }

            // ── View options ──
            EditorGUILayout.BeginHorizontal();
            _thumbnailSize = EditorGUILayout.IntSlider("Thumbnail Size", _thumbnailSize, 100, 300);
            // ★ Diff toggle (stub for P2, currently does nothing)
            // _showDifferences = EditorGUILayout.ToggleLeft("Show Diff", _showDifferences);
            _autoConfirm = EditorGUILayout.ToggleLeft("Auto-Confirm", _autoConfirm);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            DrawCategoryGrid("Eyes", MmdShapeCategory.眼部, ref _foldoutEye);
            DrawCategoryGrid("Mouth", MmdShapeCategory.嘴部, ref _foldoutMouth);
            DrawCategoryGrid("Eyebrows", MmdShapeCategory.眉毛, ref _foldoutBrow);
            DrawCategoryGrid("Other", MmdShapeCategory.未知, ref _foldoutOther);

            EditorGUILayout.EndScrollView();

            // ── Bottom bar ──
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset All to 100%", GUILayout.Height(26)))
                ResetAll();
            if (GUILayout.Button("Re-scan", GUILayout.Height(26)))
                Scan();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawCategoryGrid(string title, MmdShapeCategory category, ref bool foldout)
        {
            var items = _entries.Where(e => (e.category & category) != 0 || 
                (category == MmdShapeCategory.未知 && e.category == MmdShapeCategory.未知)).ToList();
            if (items.Count == 0) return;

            int modified = items.Count(e => e.IsModified);
            string label = $"{title} ({items.Count})";
            if (modified > 0) label += $" [modified: {modified}]";

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
            EditorGUILayout.BeginVertical(GUILayout.Width(_thumbnailSize));

            var oldBg = GUI.backgroundColor;
            if (entry.IsModified)
                GUI.backgroundColor = new Color(1f, 0.7f, 0.3f, 0.3f);

            var thumbContent = new GUIContent
            {
                image = entry.thumbnail,
                tooltip = $"{entry.name}\n{entry.description}\nCurrent: {entry.sliderValue}%"
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
                EditorGUILayout.LabelField("100%", EditorStyles.miniLabel, GUILayout.Width(_thumbnailSize));
            }

            EditorGUILayout.EndVertical();
        }

        // ══════════════════════════════════════════════
        //  Detail View
        // ══════════════════════════════════════════════

        private void DrawDetailView()
        {
            var entry = _selectedEntry;

            // ── Navigation ──
            bool exitDetail = false;
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("\u2190 Back to Grid", GUILayout.Width(100)))
            {
                DeselectCurrent();
                exitDetail = true;
            }
            
            EditorGUILayout.LabelField($"{entry.name}", EditorStyles.boldLabel, GUILayout.Width(100));
            EditorGUILayout.LabelField(entry.description, GUILayout.Width(100));
            GUILayout.FlexibleSpace();

            int curIdx = _entries.IndexOf(entry);
            EditorGUI.BeginDisabledGroup(curIdx <= 0);
            if (GUILayout.Button("\u25C0 Prev", GUILayout.Width(60)))
            {
                DeselectCurrent();
                SelectEntry(_entries[curIdx - 1]);
                exitDetail = true;
            }
            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginDisabledGroup(curIdx >= _entries.Count - 1);
            if (GUILayout.Button("Next \u25B6", GUILayout.Width(60)))
            {
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
                GUILayout.Box("No Preview", GUILayout.Width(200), GUILayout.Height(200));

            GUILayout.Space(12);

            // Slider and presets
            EditorGUILayout.BeginVertical();
            
            EditorGUILayout.LabelField("Scale Factor", EditorStyles.boldLabel);
            
            // Slider
            float newVal = EditorGUILayout.Slider(entry.sliderValue, 0f, 200f);
            if (Mathf.Abs(newVal - entry.sliderValue) > 0.1f)
            {
                // ★ Only write on mouse up, not every frame
                if (Event.current.type == EventType.MouseUp || Event.current.type == EventType.Used)
                {
                    entry.sliderValue = Mathf.RoundToInt(newVal);
                    PreviewOnMesh(entry);
                    if (_autoConfirm) ConfirmCurrent();
                }
                else
                {
                    entry.sliderValue = Mathf.RoundToInt(newVal);
                    PreviewOnMesh(entry);
                }
                Repaint();
            }

            // Numeric input
            EditorGUILayout.BeginHorizontal();
            var valStr = EditorGUILayout.TextField(entry.sliderValue.ToString(), GUILayout.Width(40));
            if (float.TryParse(valStr, out float parsed) && Mathf.Abs(parsed - entry.sliderValue) > 0.1f)
            {
                entry.sliderValue = Mathf.Clamp((int)parsed, 0, 200);
                PreviewOnMesh(entry);
                if (_autoConfirm) ConfirmCurrent();
            }
            EditorGUILayout.LabelField("%", GUILayout.Width(15));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            // Preset buttons
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset 100%", GUILayout.Width(80)))
                SetSliderValue(entry, 100);
            if (GUILayout.Button("80%", GUILayout.Width(50)))
                SetSliderValue(entry, 80);
            if (GUILayout.Button("60%", GUILayout.Width(50)))
                SetSliderValue(entry, 60);
            if (GUILayout.Button("120%", GUILayout.Width(50)))
                SetSliderValue(entry, 120);
            if (GUILayout.Button("150%", GUILayout.Width(50)))
                SetSliderValue(entry, 150);
            EditorGUILayout.EndHorizontal();

            // Status
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Scene View shows live preview. Rotate to inspect.", EditorStyles.miniLabel);
            if (entry.IsModified)
            {
                var c = GUI.color;
                GUI.color = new Color(1f, 0.6f, 0.2f);
                EditorGUILayout.LabelField($"Scaled to {entry.sliderValue}% (default 100%)", EditorStyles.miniLabel);
                GUI.color = c;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            // ── Bottom actions ──
            EditorGUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("\u2713 Confirm", GUILayout.Height(30)))
            {
                ConfirmCurrent();
                DeselectCurrent();
            }
            if (GUILayout.Button("\u2717 Cancel", GUILayout.Height(30)))
            {
                // Restore from component value
                float saved = GetSavedScale(entry.name) * 100f;
                entry.sliderValue = (int)saved;
                DeselectCurrent();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void SetSliderValue(ShapeEntry entry, int value)
        {
            entry.sliderValue = value;
            PreviewOnMesh(entry);
            if (_autoConfirm) ConfirmCurrent();
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
            if (_entries.Any(e => e.IsModified))
            {
                if (!EditorUtility.DisplayDialog("Reset All",
                    "Reset ALL MMD shapes to 100%? This cannot be undone.",
                    "Reset All", "Cancel"))
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

            int unsaved = _entries.Count(e => 
                Mathf.Abs(e.sliderValue - GetSavedScale(e.name) * 100f) > 0.5f);
            
            if (unsaved > 0 && !_autoConfirm)
            {
                if (!EditorUtility.DisplayDialog("Re-scan",
                    $"{unsaved} shapes have unconfirmed changes. Re-scanning will lose them.\n\n" +
                    "Confirm changes first or enable Auto-Confirm mode.",
                    "Re-scan (discard changes)", "Cancel"))
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

            // Generate thumbnails
            try
            {
                for (int i = 0; i < _entries.Count; i++)
                {
                    var entry = _entries[i];
                    EditorUtility.DisplayProgressBar(
                        "MMD Calibrator",
                        $"Rendering {entry.name} ({i + 1}/{_entries.Count})",
                        (float)i / _entries.Count);

                    entry.thumbnail = BlendShapePreviewRenderer.Render(
                        _faceRenderer,
                        entry.meshIndex,
                        100f,       // Always render at 100% for reference
                        _thumbnailSize);
                }
            }
            finally
            {
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
