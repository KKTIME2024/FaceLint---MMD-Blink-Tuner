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
            window.minSize = new Vector2(500, 380);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("MMD BlendShape 过闭合检测器", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("检测面部 MMD blendshape 的过度闭合/穿透/法线翻转 (含超限外推)", EditorStyles.miniLabel);
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
            int 严重数 = _report.所有结果.Count(r => r.严重程度 == 检测严重程度.严重);
            int 警告数 = _report.所有结果.Count(r => r.严重程度 == 检测严重程度.警告);
            int 注意数 = _report.所有结果.Count(r => r.严重程度 == 检测严重程度.注意);
            int 正常数 = _report.所有结果.Count(r => r.严重程度 == 检测严重程度.正常);
            int 缺失数 = _report.缺失形状列表.Count;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                $"网格: {_report.网格名称}  |  总形状: {_report.总形状数}  |  MMD形状: {_report.MMD形状数}",
                EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            var oldColor = GUI.color;

            GUI.color = new Color(1f, 0.3f, 0.3f);
            EditorGUILayout.LabelField($"严重: {严重数}", GUILayout.Width(70));
            GUI.color = new Color(1f, 0.7f, 0.2f);
            EditorGUILayout.LabelField($"警告: {警告数}", GUILayout.Width(70));
            GUI.color = new Color(0.5f, 0.8f, 1f);
            EditorGUILayout.LabelField($"注意: {注意数}", GUILayout.Width(70));
            GUI.color = new Color(0.3f, 0.8f, 0.3f);
            EditorGUILayout.LabelField($"正常: {正常数}", GUILayout.Width(70));
            GUI.color = Color.gray;
            EditorGUILayout.LabelField($"缺失: {缺失数}", GUILayout.Width(70));

            GUI.color = oldColor;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void 显示分类结果(string title, MmdShapeCategory category, ref bool foldout)
        {
            var items = _report.所有结果.Where(r => (r.分类 & category) != 0).ToList();
            if (items.Count == 0) return;

            int severe = items.Count(r => r.严重程度 == 检测严重程度.严重);
            int warn = items.Count(r => r.严重程度 == 检测严重程度.警告);

            string label = $"{title} ({items.Count})";
            if (severe > 0) label += $"  [{severe}严重]";
            if (warn > 0) label += $"  [{warn}警告]";

            foldout = EditorGUILayout.Foldout(foldout, label, true);
            if (!foldout) return;

            EditorGUI.indentLevel++;
            foreach (var item in items.OrderBy(r => (int)r.严重程度))
            {
                显示结果条目(item);
            }
            EditorGUI.indentLevel--;
        }

        private void 显示缺失列表(ref bool foldout)
        {
            if (_report.缺失形状列表.Count == 0) return;

            foldout = EditorGUILayout.Foldout(foldout,
                $"缺失标准MMD形状 ({_report.缺失形状列表.Count})", true);
            if (!foldout) return;

            EditorGUI.indentLevel++;
            var gray = new Color(0.6f, 0.6f, 0.6f);
            var oldColor = GUI.color;
            GUI.color = gray;
            foreach (var name in _report.缺失形状列表)
            {
                EditorGUILayout.LabelField($"- {name}");
            }
            GUI.color = oldColor;
            EditorGUI.indentLevel--;
        }

        private void 显示结果条目(检测结果 item)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();

            var oldColor = GUI.color;
            string icon;
            Color iconColor;
            switch (item.严重程度)
            {
                case 检测严重程度.严重:
                    icon = "■";
                    iconColor = new Color(1f, 0.2f, 0.2f);
                    break;
                case 检测严重程度.警告:
                    icon = "▲";
                    iconColor = new Color(1f, 0.6f, 0.1f);
                    break;
                case 检测严重程度.注意:
                    icon = "●";
                    iconColor = new Color(0.3f, 0.6f, 1f);
                    break;
                default:
                    icon = "✓";
                    iconColor = new Color(0.2f, 0.7f, 0.2f);
                    break;
            }

            GUI.color = iconColor;
            EditorGUILayout.LabelField($"{icon} {item.形状名称}", EditorStyles.boldLabel, GUILayout.Width(150));
            GUI.color = oldColor;

            EditorGUILayout.LabelField(item.中文说明, GUILayout.Width(120));
            GUILayout.FlexibleSpace();

            if (item.主数值 > 0.0001f)
            {
                var numStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
                EditorGUILayout.LabelField($"值: {item.主数值:F2}", numStyle, GUILayout.Width(80));
            }

            EditorGUILayout.EndHorizontal();

            var descStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, richText = true };
            foreach (var detail in item.问题详情)
            {
                EditorGUILayout.LabelField($"  {detail}", descStyle);
            }

            EditorGUILayout.EndVertical();
        }
    }
}
