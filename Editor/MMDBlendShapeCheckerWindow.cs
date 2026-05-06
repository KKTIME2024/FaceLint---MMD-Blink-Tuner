using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MMDBlendShapeChecker
{
    public class MMDBlendShapeCheckerWindow : EditorWindow
    {
        private SkinnedMeshRenderer _faceRenderer;
        private 校准报告 _report;
        private Vector2 _scrollPos;
        private bool _foldout眼部 = true;
        private bool _foldout嘴部 = true;
        private bool _foldout眉毛 = true;
        private bool _foldout缺失 = true;
        private bool _showCopyButton;

        [MenuItem("Tools/MMD BlendShape 校准器")]
        public static void 打开窗口()
        {
            var window = GetWindow<MMDBlendShapeCheckerWindow>("MMD 校准器");
            window.minSize = new Vector2(560, 420);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("MMD BlendShape 校准器", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("基于中性基准的自动校准 — 检测用户基值造成的形变偏移，输出 make-it-mmd 推荐 scale 值", EditorStyles.miniLabel);
            EditorGUILayout.Space(8);

            _faceRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(
                "面部网格 (Body 的 SkinnedMeshRenderer)", _faceRenderer, typeof(SkinnedMeshRenderer), true);

            EditorGUILayout.Space(4);

            EditorGUI.BeginDisabledGroup(_faceRenderer == null);
            if (GUILayout.Button("执行校准", GUILayout.Height(32)))
            {
                _report = BlendShapeCalibrator.执行校准(_faceRenderer);
                _showCopyButton = true;
                Repaint();
            }
            EditorGUI.EndDisabledGroup();

            if (_report == null) return;

            EditorGUILayout.Space(6);

            if (!string.IsNullOrEmpty(_report.错误信息))
            {
                EditorGUILayout.HelpBox(_report.错误信息, MessageType.Warning);
                return;
            }

            显示摘要();

            if (_showCopyButton && _report.所有结果.Count > 0)
            {
                if (GUILayout.Button("复制全部建议操作到剪贴板"))
                {
                    string text = string.Join("\n", _report.所有结果
                        .Where(r => r.推荐值 < 99.5f || r.推荐值 > 100.5f)
                        .Select(r => r.建议操作));
                    if (!string.IsNullOrEmpty(text))
                        EditorGUIUtility.systemCopyBuffer = text;
                }
            }

            EditorGUILayout.Space(4);
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));
            显示分类结果("眼部", MmdShapeCategory.眼部, ref _foldout眼部);
            显示分类结果("嘴部", MmdShapeCategory.嘴部, ref _foldout嘴部);
            显示分类结果("眉毛", MmdShapeCategory.眉毛, ref _foldout眉毛);
            显示缺失列表(ref _foldout缺失);
            EditorGUILayout.EndScrollView();
        }

        private void 显示摘要()
        {
            int needAdjust = _report.所有结果.Count(r => r.推荐值 < 99.5f || r.推荐值 > 100.5f);
            int ok = _report.所有结果.Count - needAdjust;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                $"网格: {_report.网格名称}  |  总形状: {_report.总形状数}  |  MMD形状: {_report.MMD形状数}  |  非零基值: {_report.非零基值数}",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField(
                $"需调整: {needAdjust}  无需调整: {ok}  缺失: {_report.缺失形状列表.Count}",
                EditorStyles.boldLabel);
            EditorGUILayout.EndVertical();
        }

        private void 显示分类结果(string title, MmdShapeCategory category, ref bool foldout)
        {
            var items = _report.所有结果.Where(r => (r.分类 & category) != 0).ToList();
            if (items.Count == 0) return;

            int needAdjust = items.Count(r => r.推荐值 < 99.5f || r.推荐值 > 100.5f);
            string label = $"{title} ({items.Count})";
            if (needAdjust > 0) label += $"  [需调整: {needAdjust}]";

            foldout = EditorGUILayout.Foldout(foldout, label, true);
            if (!foldout) return;

            EditorGUI.indentLevel++;
            foreach (var item in items.OrderBy(r => r.推荐值))
                显示校准条目(item);
            EditorGUI.indentLevel--;
        }

        private void 显示缺失列表(ref bool foldout)
        {
            if (_report.缺失形状列表.Count == 0) return;
            foldout = EditorGUILayout.Foldout(foldout, $"缺失标准MMD形状 ({_report.缺失形状列表.Count})", true);
            if (!foldout) return;
            EditorGUI.indentLevel++;
            var c = GUI.color;
            GUI.color = new Color(0.6f, 0.6f, 0.6f);
            foreach (var name in _report.缺失形状列表)
                EditorGUILayout.LabelField($"- {name}");
            GUI.color = c;
            EditorGUI.indentLevel--;
        }

        private void 显示校准条目(校准结果 item)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();

            // 状态指示
            var c = GUI.color;
            if (item.推荐值 < 95f)
            {
                GUI.color = new Color(1f, 0.7f, 0.2f);
                EditorGUILayout.LabelField("▲", EditorStyles.boldLabel, GUILayout.Width(16));
            }
            else if (item.推荐值 > 105f)
            {
                GUI.color = new Color(1f, 0.7f, 0.2f);
                EditorGUILayout.LabelField("▲", EditorStyles.boldLabel, GUILayout.Width(16));
            }
            else if (item.推荐值 < 99.5f || item.推荐值 > 100.5f)
            {
                GUI.color = new Color(0.5f, 0.8f, 1f);
                EditorGUILayout.LabelField("●", EditorStyles.boldLabel, GUILayout.Width(16));
            }
            else
            {
                GUI.color = new Color(0.3f, 0.8f, 0.3f);
                EditorGUILayout.LabelField("✓", EditorStyles.boldLabel, GUILayout.Width(16));
            }
            GUI.color = c;

            EditorGUILayout.LabelField(item.形状名称, EditorStyles.boldLabel, GUILayout.Width(120));
            EditorGUILayout.LabelField(item.中文说明, GUILayout.Width(100));

            // 推荐值
            if (item.推荐值 > 99.5f && item.推荐值 < 100.5f)
            {
                GUI.color = new Color(0.3f, 0.8f, 0.3f);
                EditorGUILayout.LabelField("推荐 100% (无需调整)", GUILayout.Width(160));
            }
            else
            {
                GUI.color = new Color(1f, 0.8f, 0.3f);
                EditorGUILayout.LabelField($"推荐 {item.推荐值:F0}%  scale={item.推荐值 / 100f:F2}", EditorStyles.boldLabel, GUILayout.Width(180));
            }
            GUI.color = c;

            // 匹配形状
            if (!string.IsNullOrEmpty(item.匹配形状))
            {
                GUI.color = new Color(0.5f, 0.9f, 0.5f);
                EditorGUILayout.LabelField($"← {item.匹配形状}", EditorStyles.miniLabel, GUILayout.Width(140));
                GUI.color = c;
            }

            EditorGUILayout.EndHorizontal();

            // 影响因素
            if (item.主要影响因素.Count > 0)
            {
                var factors = string.Join(", ", item.主要影响因素
                    .Select(f => $"{f.名称}({f.贡献 * 100f:F0}%)"));
                EditorGUILayout.LabelField($"  影响: {factors}", EditorStyles.miniLabel);
            }

            // 建议操作
            if (item.推荐值 < 99.5f || item.推荐值 > 100.5f)
            {
                EditorGUILayout.LabelField($"  操作: {item.建议操作}", new GUIStyle(EditorStyles.miniLabel) { richText = true });
            }

            EditorGUILayout.EndVertical();
        }
    }
}
