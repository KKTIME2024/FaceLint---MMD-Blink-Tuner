using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MMDBlendShapeChecker
{
    public class MMDBlendShapeCheckerWindow : EditorWindow
    {
        private SkinnedMeshRenderer _faceRenderer;
        private 全面检测报告 _report;
        private Vector2 _scrollPos;
        private bool _foldout眼部 = true;
        private bool _foldout嘴部 = true;
        private bool _foldout眉毛 = true;
        private bool _foldout缺失 = true;

        [MenuItem("Tools/MMD BlendShape 过闭合检测器")]
        public static void 打开窗口()
        {
            var window = GetWindow<MMDBlendShapeCheckerWindow>("MMD 过闭合检测");
            window.minSize = new Vector2(520, 400);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("MMD BlendShape 过闭合检测器", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("检测基值叠加 + MMD驱动下的眼/嘴/眉过度闭合 (含超限外推)", EditorStyles.miniLabel);
            EditorGUILayout.Space(8);

            _faceRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(
                "面部网格 (SkinnedMeshRenderer)", _faceRenderer, typeof(SkinnedMeshRenderer), true);

            EditorGUILayout.Space(4);

            EditorGUI.BeginDisabledGroup(_faceRenderer == null);
            if (GUILayout.Button("执行全面检测", GUILayout.Height(32)))
            {
                _report = OverCloseDetector.执行全面检测(_faceRenderer);
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
            int s = _report.所有结果.Count(r => r.严重程度 == 检测严重程度.严重);
            int w = _report.所有结果.Count(r => r.严重程度 == 检测严重程度.警告);
            int n = _report.所有结果.Count(r => r.严重程度 == 检测严重程度.注意);
            int ok = _report.所有结果.Count(r => r.严重程度 == 检测严重程度.正常);
            int miss = _report.缺失形状列表.Count;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                $"网格: {_report.网格名称}  |  总形状: {_report.总形状数}  |  MMD形状: {_report.MMD形状数}  |  有基值: {_report.存在基值的形状数}",
                EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            var c = GUI.color;
            GUI.color = new Color(1f, 0.3f, 0.3f); EditorGUILayout.LabelField($"严重: {s}", GUILayout.Width(60));
            GUI.color = new Color(1f, 0.7f, 0.2f); EditorGUILayout.LabelField($"警告: {w}", GUILayout.Width(60));
            GUI.color = new Color(0.5f, 0.8f, 1f); EditorGUILayout.LabelField($"注意: {n}", GUILayout.Width(60));
            GUI.color = new Color(0.3f, 0.8f, 0.3f); EditorGUILayout.LabelField($"正常: {ok}", GUILayout.Width(60));
            GUI.color = Color.gray;                EditorGUILayout.LabelField($"缺失: {miss}", GUILayout.Width(60));
            GUI.color = c;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void 显示分类结果(string title, MmdShapeCategory category, ref bool foldout)
        {
            var items = _report.所有结果.Where(r => (r.分类 & category) != 0).ToList();
            if (items.Count == 0) return;

            int s = items.Count(r => r.严重程度 == 检测严重程度.严重);
            int w = items.Count(r => r.严重程度 == 检测严重程度.警告);
            string label = $"{title} ({items.Count})";
            if (s > 0) label += $"  [{s}严重]";
            if (w > 0) label += $"  [{w}警告]";

            foldout = EditorGUILayout.Foldout(foldout, label, true);
            if (!foldout) return;

            EditorGUI.indentLevel++;
            foreach (var item in items.OrderBy(r => (int)r.严重程度))
                显示结果条目(item);
            EditorGUI.indentLevel--;
        }

        private void 显示缺失列表(ref bool foldout)
        {
            if (_report.缺失形状列表.Count == 0) return;
            foldout = EditorGUILayout.Foldout(foldout,
                $"缺失标准MMD形状 ({_report.缺失形状列表.Count})", true);
            if (!foldout) return;

            EditorGUI.indentLevel++;
            var c = GUI.color;
            GUI.color = new Color(0.6f, 0.6f, 0.6f);
            foreach (var name in _report.缺失形状列表)
                EditorGUILayout.LabelField($"- {name}");
            GUI.color = c;
            EditorGUI.indentLevel--;
        }

        private void 显示结果条目(检测结果 item)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // 标题行: 图标 + 名称 + 匹配 + 有效权重
            EditorGUILayout.BeginHorizontal();

            var c = GUI.color;
            switch (item.严重程度)
            {
                case 检测严重程度.严重:
                    GUI.color = new Color(1f, 0.2f, 0.2f);
                    EditorGUILayout.LabelField("■", EditorStyles.boldLabel, GUILayout.Width(16));
                    break;
                case 检测严重程度.警告:
                    GUI.color = new Color(1f, 0.6f, 0.1f);
                    EditorGUILayout.LabelField("▲", EditorStyles.boldLabel, GUILayout.Width(16));
                    break;
                case 检测严重程度.注意:
                    GUI.color = new Color(0.3f, 0.6f, 1f);
                    EditorGUILayout.LabelField("●", EditorStyles.boldLabel, GUILayout.Width(16));
                    break;
                default:
                    GUI.color = new Color(0.2f, 0.7f, 0.2f);
                    EditorGUILayout.LabelField("✓", EditorStyles.boldLabel, GUILayout.Width(16));
                    break;
            }
            GUI.color = c;

            EditorGUILayout.LabelField(item.形状名称, EditorStyles.boldLabel, GUILayout.Width(120));
            EditorGUILayout.LabelField(item.中文说明, GUILayout.Width(100));

            // 匹配信息
            if (!string.IsNullOrEmpty(item.匹配原始形状))
            {
                GUI.color = new Color(0.5f, 0.9f, 0.5f);
                EditorGUILayout.LabelField($"← {item.匹配原始形状} (基值:{item.原始基值:F0}) 有效:{item.有效权重:F0}%",
                    EditorStyles.miniLabel, GUILayout.Width(240));
                GUI.color = c;
            }

            EditorGUILayout.EndHorizontal();

            // 详情
            var ds = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            foreach (var d in item.问题详情)
                EditorGUILayout.LabelField($"  {d}", ds);

            // 影响基值
            if (item.影响基值列表.Count > 0)
            {
                EditorGUILayout.LabelField($"  影响基值: {string.Join(", ", item.影响基值列表)}", ds);
            }

            EditorGUILayout.EndVertical();
        }
    }
}
