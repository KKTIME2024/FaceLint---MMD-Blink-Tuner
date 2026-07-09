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

        // ── Sync feedback ──
        private string _syncFeedbackMessage;
        private double _syncFeedbackEndTime;

        // ── Search / filter ──
        private string _searchFilter = "";
        private bool _showModifiedOnly;

        // ── First-time guide ──
        private static readonly string GuideShownKey = "FaceLint.GuideShown";

        // ── A/B compare ──
        private bool _isComparing;

        // ── Embedded 3D preview ──
        private GameObject _previewCameraGo;
        private Camera _previewCamera;
        private RenderTexture _previewRT;
        private float _orbitYaw;
        private float _orbitPitch;
        private Vector3 _orbitTarget;
        private bool _isDraggingPreview;
        private Vector2 _dragLastMouse;
        private const int PreviewSize = 200;

        // ── Window ──
        public static void ShowWindow(MmdBlendShapeScaler scaler)
        {
            var window = GetWindow<MmdCalibratorWindow>(Strings.Current.WindowTitle);
            window.minSize = new Vector2(520, 400);
            window._scaler = scaler;
            window._faceRenderer = scaler.targetRenderer;
            window.Show();
        }

        [MenuItem("Tools/FaceLint - MMD & Blink Tuner")]
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
            DestroyPreviewResources();
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

            // ── Search / filter ──
            GUILayout.BeginHorizontal();
            _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField);
            _showModifiedOnly = GUILayout.Toggle(_showModifiedOnly, S.ShowModifiedOnly, GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();
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
            if (GUILayout.Button(S.ExportPreset, GUILayout.Height(26)))
                ExportPreset();
            if (GUILayout.Button(S.ImportPreset, GUILayout.Height(26)))
                ImportPreset();
            if (GUILayout.Button(S.ReScan, GUILayout.Height(26)))
                Scan();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawCategoryGrid(string title, MmdShapeCategory category, ref bool foldout)
        {
            var S = Strings.Current;

            // Apply search + modified-only filter before category filter
            IEnumerable<ShapeEntry> filtered = _entries;
            if (!string.IsNullOrEmpty(_searchFilter))
                filtered = filtered.Where(e => e.name.IndexOf(_searchFilter, System.StringComparison.OrdinalIgnoreCase) >= 0);
            if (_showModifiedOnly)
                filtered = filtered.Where(e => e.IsModified);

            var items = filtered.Where(e => (e.category & category) != 0 ||
                (category == MmdShapeCategory.未知 && e.category == MmdShapeCategory.未知)).ToList();
            if (items.Count == 0) return;

            int modified = items.Count(e => e.IsModified);
            string label = $"{title} ({items.Count})";
            if (modified > 0) label += $" {string.Format(S.ModifiedCountFmt, modified)}";

            EditorGUILayout.BeginHorizontal();
            foldout = EditorGUILayout.Foldout(foldout, label, true);
            if (modified > 0)
            {
                if (GUILayout.Button("↺", EditorStyles.miniButton, GUILayout.Width(20)))
                {
                    if (EditorUtility.DisplayDialog(S.DlgResetTitle,
                        string.Format(S.DlgResetCategoryFmt, title, items.Count),
                        S.DlgResetBtn, S.DlgCancelBtn))
                    {
                        foreach (var e in items)
                        {
                            e.sliderValue = 100;
                            if (_scaler != null)
                            {
                                Undo.RegisterCompleteObjectUndo(_scaler, $"Reset {title} to 100%");
                                _scaler.RemoveScale(e.name);
                            }
                        }
                        EditorUtility.SetDirty(_scaler);
                        RestoreAllWeights();
                        Repaint();
                    }
                }
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
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

            // Lazy thumbnail render on first visible draw
            EnsureThumbnail(entry);

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
                // Restore current shape weight without destroying preview
                if (_selectedEntry != null)
                    _faceRenderer.SetBlendShapeWeight(_selectedEntry.meshIndex, 0f);
                SelectEntry(_entries[curIdx - 1]);
                exitDetail = true;
            }
            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginDisabledGroup(curIdx >= _entries.Count - 1);
            if (GUILayout.Button(S.Next, GUILayout.Width(80)))
            {
                ConfirmCurrent();
                // Restore current shape weight without destroying preview
                if (_selectedEntry != null)
                    _faceRenderer.SetBlendShapeWeight(_selectedEntry.meshIndex, 0f);
                SelectEntry(_entries[curIdx + 1]);
                exitDetail = true;
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
            if (exitDetail) return;

            EditorGUILayout.Space(8);

            // ── Embedded 3D preview + Controls ──
            EditorGUILayout.BeginHorizontal();

            // Live 3D preview (replaces static thumbnail)
            EditorGUILayout.BeginVertical();
            var previewRect = GUILayoutUtility.GetRect(PreviewSize, PreviewSize, GUILayout.Width(PreviewSize));
            if (_previewRT != null)
            {
                GUI.DrawTexture(previewRect, _previewRT);
                if (Event.current.type == EventType.MouseDown && previewRect.Contains(Event.current.mousePosition))
                {
                    _isDraggingPreview = true;
                    _dragLastMouse = Event.current.mousePosition;
                    Event.current.Use();
                }
                if (_isDraggingPreview && Event.current.type == EventType.MouseDrag)
                {
                    Vector2 delta = Event.current.mousePosition - _dragLastMouse;
                    _orbitYaw += delta.x * 0.5f;
                    _orbitPitch = Mathf.Clamp(_orbitPitch + delta.y * 0.5f, -80f, 80f);
                    _dragLastMouse = Event.current.mousePosition;
                    PositionPreviewCamera();
                    RenderPreview();
                    Event.current.Use();
                }
                if (_isDraggingPreview && Event.current.type == EventType.MouseUp)
                {
                    _isDraggingPreview = false;
                    Event.current.Use();
                }

                // Scroll to zoom in the embedded preview
                if (Event.current.type == EventType.ScrollWheel && previewRect.Contains(Event.current.mousePosition))
                {
                    _orbitTargetDistance *= Event.current.delta.y < 0f ? 0.9f : 1.1f;
                    _orbitTargetDistance = Mathf.Clamp(_orbitTargetDistance, 0.1f, 5f);
                    PositionPreviewCamera();
                    RenderPreview();
                    Event.current.Use();
                }
            }
            else
            {
                GUI.Box(previewRect, S.NoPreview);
            }
            EditorGUILayout.LabelField(S.PreviewDragHint, EditorStyles.centeredGreyMiniLabel, GUILayout.Width(PreviewSize));
            EditorGUILayout.EndVertical();

            GUILayout.Space(12);

            // Slider and presets
            EditorGUILayout.BeginVertical();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(S.ScaleFactor, EditorStyles.boldLabel);
            if (GUILayout.Button(_isComparing ? "100%" : "A/B", EditorStyles.miniButton, GUILayout.Width(40)))
            {
                _isComparing = !_isComparing;
                UpdatePreviewAfterCompare();
            }
            EditorGUILayout.EndHorizontal();

            if (_isComparing)
            {
                var c = GUI.color;
                GUI.color = new Color(0.4f, 0.8f, 1f);
                EditorGUILayout.LabelField(S.CompareHint, EditorStyles.miniLabel);
                GUI.color = c;
            }

            // Slider — preview only, save on navigation / confirm
            float newVal = EditorGUILayout.Slider(entry.sliderValue, 0f, 200f);
            if (Mathf.Abs(newVal - entry.sliderValue) > 0.1f)
            {
                entry.sliderValue = Mathf.RoundToInt(newVal);
                PreviewOnMesh(entry);
                RenderPreview();
                Repaint();
            }

            // Zone labels
            if (Event.current.type == EventType.Repaint)
            {
                var r = GUILayoutUtility.GetLastRect();
                var half = r.width * 0.5f;
                EditorGUI.DrawRect(new Rect(r.x, r.y + r.height - 3, half, 3), new Color(0f, 0.5f, 0f, 0.15f));
                EditorGUI.DrawRect(new Rect(r.x + half, r.y + r.height - 3, half, 3), new Color(1f, 0.5f, 0f, 0.15f));
                // Tick at 100
                float t = r.x + half;
                EditorGUI.DrawRect(new Rect(t - 1, r.y + r.height - 6, 2, 6), new Color(0.5f, 0.5f, 0.5f, 0.5f));
            }

            EditorGUILayout.Space(4);

            // Quick-apply: up to 5 recent values (including 100%)
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(S.Quick, EditorStyles.miniLabel, GUILayout.Width(36));
            int shown = 0;
            int maxShow = 5;
            if (GUILayout.Button(S.PctValue, GUILayout.Width(45))) ApplyQuickPct(100);
            shown++;
            foreach (float val in _recentValues)
            {
                if (shown >= maxShow) break;
                int pct = Mathf.RoundToInt(val);
                if (pct != entry.sliderValue && pct != 100)
                {
                    if (GUILayout.Button($"{pct}%", GUILayout.Width(45)))
                        ApplyQuickPct(pct);
                    shown++;
                }
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            // ── Sync to sibling blink shapes ──
            if (IsCurrentEyeClosing && _selectedEntry != null)
            {
                var siblings = _entries.Where(e => e != _selectedEntry && IsEyeClosing(e)).ToList();
                if (siblings.Count > 0)
                {
                    bool allMatch = siblings.All(e => e.sliderValue == entry.sliderValue);
                    EditorGUI.BeginDisabledGroup(allMatch);
                    if (GUILayout.Button(string.Format(S.SyncToBlinkFmt, siblings.Count), GUILayout.Height(24)))
                    {
                        SyncToSiblingBlink(entry, siblings);
                    }
                    EditorGUI.EndDisabledGroup();
                }
            }

            // Sync feedback
            if (!string.IsNullOrEmpty(_syncFeedbackMessage) && EditorApplication.timeSinceStartup < _syncFeedbackEndTime)
            {
                var c = GUI.color;
                GUI.color = Color.green;
                EditorGUILayout.LabelField(_syncFeedbackMessage, EditorStyles.miniLabel);
                GUI.color = c;
                Repaint();
            }
            else
            {
                _syncFeedbackMessage = null;
            }

            // First-time guide (once per Unity session)
            if (!SessionState.GetBool(GuideShownKey, false))
            {
                EditorGUILayout.HelpBox(S.FirstTimeGuide, MessageType.Info);
                if (Event.current.type == EventType.Repaint)
                    SessionState.SetBool(GuideShownKey, true);
            }

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

        private bool IsEyeClosing(ShapeEntry e)
        {
            return MmdShapeDatabase.名称到信息映射.TryGetValue(e.name, out var info)
                && info.是闭合类 && (info.分类 & MmdShapeCategory.眼部) != 0;
        }

        private bool IsCurrentEyeClosing => _selectedEntry != null && IsEyeClosing(_selectedEntry);

        private void SelectEntry(ShapeEntry entry)
        {
            if (_selectedEntry == entry) return;

            // Create embedded preview on first detail entry
            if (_selectedEntry == null)
                CreatePreviewResources();

            // Restore previous
            if (_selectedEntry != null)
                _faceRenderer.SetBlendShapeWeight(_selectedEntry.meshIndex, 0f);

            _selectedEntry = entry;
            PreviewOnMesh(entry);
            RenderPreview();

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
            _isComparing = false;
            DestroyPreviewResources();
        }

        private void PreviewOnMesh(ShapeEntry entry)
        {
            if (_faceRenderer == null) return;
            float weight = _isComparing ? 100f : entry.sliderValue;
            _faceRenderer.SetBlendShapeWeight(entry.meshIndex, weight);
            SceneView.RepaintAll();
        }

        private void UpdatePreviewAfterCompare()
        {
            if (_selectedEntry == null) return;
            PreviewOnMesh(_selectedEntry);
            RenderPreview();
            Repaint();
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
            }

            if (_scaler != null)
            {
                Undo.RegisterCompleteObjectUndo(_scaler, $"Set MMD Scale {entry.name}={entry.sliderValue}%");
                _scaler.SetScale(entry.name, entry.Scale);
            }

            EditorUtility.SetDirty(_scaler);
        }

        private void ApplyQuickPct(int pct)
        {
            if (_selectedEntry == null) return;
            _selectedEntry.sliderValue = pct;
            PreviewOnMesh(_selectedEntry);
            ConfirmCurrent();
        }

        private void SyncToSiblingBlink(ShapeEntry source, List<ShapeEntry> siblings)
        {
            if (_scaler == null) return;
            var S = Strings.Current;

            Undo.RegisterCompleteObjectUndo(_scaler,
                $"Sync {source.sliderValue}% to {siblings.Count} blink shape(s)");

            foreach (var sib in siblings)
            {
                sib.sliderValue = source.sliderValue;
                _scaler.SetScale(sib.name, source.Scale);
            }

            EditorUtility.SetDirty(_scaler);
            Repaint();

            _syncFeedbackMessage = string.Format(S.SyncToBlinkDone, source.sliderValue, siblings.Count);
            _syncFeedbackEndTime = EditorApplication.timeSinceStartup + 2.0;
        }

        // ══════════════════════════════════════════════
        //  Embedded 3D Preview
        // ══════════════════════════════════════════════

        private void CreatePreviewResources()
        {
            if (_faceRenderer == null) return;
            DestroyPreviewResources();

            _previewRT = RenderTexture.GetTemporary(PreviewSize, PreviewSize, 24);
            _previewRT.wrapMode = TextureWrapMode.Clamp;

            _previewCameraGo = new GameObject("__FaceLint_Preview_Cam__");
            _previewCameraGo.hideFlags = HideFlags.HideAndDontSave;
            _previewCamera = _previewCameraGo.AddComponent<Camera>();

            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null)
            {
                _previewCamera.clearFlags = sceneView.camera.clearFlags;
                _previewCamera.backgroundColor = sceneView.camera.backgroundColor;
            }
            else
            {
                _previewCamera.clearFlags = CameraClearFlags.SolidColor;
                _previewCamera.backgroundColor = new Color(0.25f, 0.25f, 0.25f, 1f);
            }
            _previewCamera.nearClipPlane = 0.01f;
            _previewCamera.farClipPlane = 100f;
            _previewCamera.targetTexture = _previewRT;
            _previewCamera.aspect = 1f;

            // Compute face bounds and initial camera position
            var bounds = BlendShapePreviewRenderer.GetFaceBounds(_faceRenderer);
            var headBone = BlendShapePreviewRenderer.GetHeadBone(_faceRenderer);
            var faceForward = _faceRenderer.transform.root.forward;
            _orbitTarget = bounds.center;
            _orbitYaw = 0f;
            _orbitPitch = -2.5f;

            float fov = 25f;
            _previewCamera.fieldOfView = fov;
            float objectRadius = bounds.extents.magnitude * 1.2f;
            if (objectRadius < 0.001f)
            {
                objectRadius = 0.15f;
                _orbitTarget = headBone != null ? headBone.position : _faceRenderer.transform.position;
            }
            float distance = objectRadius / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            _orbitTargetDistance = Mathf.Max(distance, 0.3f) / Mathf.Max(_zoomLevel, 0.1f);

            PositionPreviewCamera();
        }

        private void DestroyPreviewResources()
        {
            _isDraggingPreview = false;
            if (_previewCameraGo != null)
            {
                Object.DestroyImmediate(_previewCameraGo);
                _previewCameraGo = null;
                _previewCamera = null;
            }
            if (_previewRT != null)
            {
                RenderTexture.ReleaseTemporary(_previewRT);
                _previewRT = null;
            }
        }

        // ReSharper disable once NotAccessedField.Local — serialized for persistence across frames
        private float _orbitTargetDistance;

        private void PositionPreviewCamera()
        {
            if (_previewCamera == null) return;

            var faceForward = _faceRenderer != null
                ? _faceRenderer.transform.root.forward
                : Vector3.forward;

            var dir = Quaternion.Euler(_orbitPitch, _orbitYaw, 0f) * faceForward;
            _previewCamera.transform.position = _orbitTarget - dir * _orbitTargetDistance;
            _previewCamera.transform.LookAt(_orbitTarget);
        }

        private void RenderPreview()
        {
            if (_previewCamera == null || _previewRT == null) return;
            _previewCamera.Render();
            Repaint();
        }

        private void RestoreAllWeights()
        {
            if (_faceRenderer == null || _faceRenderer.sharedMesh == null) return;
            int count = _faceRenderer.sharedMesh.blendShapeCount;
            foreach (var entry in _entries)
            {
                if (entry.meshIndex < count)
                    _faceRenderer.SetBlendShapeWeight(entry.meshIndex, 0f);
            }
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

            // Prepare for lazy thumbnail rendering
            BlendShapePreviewRenderer.ZoomMultiplier = _zoomLevel;
            BlendShapePreviewRenderer.ClearCache();

            Repaint();
        }

        // ══════════════════════════════════════════════
        //  Preset import / export
        // ══════════════════════════════════════════════

        private void ExportPreset()
        {
            if (_entries.Count == 0) return;

            var entries = _entries
                .Where(e => e.IsModified)
                .Select(e => new MmdScaleEntry { name = e.name, scale = e.Scale })
                .ToList();

            var data = new MmdScaleList { entries = entries };
            string json = JsonUtility.ToJson(data, prettyPrint: true);

            string path = EditorUtility.SaveFilePanel(
                "Export MMD Scale Preset", "", "mmd-preset.json", "json");
            if (string.IsNullOrEmpty(path)) return;

            System.IO.File.WriteAllText(path, json);
            AssetDatabase.Refresh();
        }

        private void ImportPreset()
        {
            string path = EditorUtility.OpenFilePanel(
                "Import MMD Scale Preset", "", "json");
            if (string.IsNullOrEmpty(path)) return;

            string json = System.IO.File.ReadAllText(path);
            var data = JsonUtility.FromJson<MmdScaleList>(json);
            if (data?.entries == null) return;

            // Build a lookup from the loaded data
            var lookup = new Dictionary<string, float>();
            foreach (var e in data.entries)
                if (!string.IsNullOrEmpty(e.name))
                    lookup[e.name] = Mathf.Clamp(e.scale, 0f, 2f);

            if (lookup.Count == 0) return;

            // Apply to current entries where names match
            Undo.RegisterCompleteObjectUndo(_scaler, "Import MMD Scale Preset");
            foreach (var entry in _entries)
            {
                if (lookup.TryGetValue(entry.name, out float scale))
                {
                    entry.sliderValue = Mathf.RoundToInt(scale * 100f);
                    _scaler?.SetScale(entry.name, scale);
                }
            }

            if (_scaler != null) EditorUtility.SetDirty(_scaler);
            Repaint();
        }

        [System.Serializable]
        private class MmdScaleList
        {
            public List<MmdScaleEntry> entries = new List<MmdScaleEntry>();
        }

        private void EnsureThumbnail(ShapeEntry entry)
        {
            if (entry.thumbnail != null) return;
            if (_faceRenderer == null || _faceRenderer.sharedMesh == null) return;
            if (entry.meshIndex < 0 || entry.meshIndex >= _faceRenderer.sharedMesh.blendShapeCount) return;

            entry.thumbnail = BlendShapePreviewRenderer.Render(
                _faceRenderer, entry.meshIndex, entry.sliderValue, _thumbnailSize);
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
