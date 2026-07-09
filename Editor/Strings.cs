using UnityEditor;
using UnityEngine;

namespace MmdBlendShapeScaler
{
    public enum UILang { En, ZhCN, Ja }

    /// <summary>
    /// Trilingual UI string database. ~30 translatable labels for window, inspector, and dialogs.
    /// Usage: Strings.Current.FaceRenderer, etc. Language persisted in EditorPrefs.
    /// </summary>
    public class Strings
    {
        // ── Singleton ──

        private static Strings _current;
        private static UILang _cachedLang;

        static Strings()
        {
            _cachedLang = (UILang)EditorPrefs.GetInt("MmdBlendShapeScaler.Lang", 0);
            _current = For(_cachedLang);
        }

        public static UILang Language
        {
            get => _cachedLang;
            set
            {
                if (_cachedLang == value) return;
                _cachedLang = value;
                _current = For(value);
                EditorPrefs.SetInt("MmdBlendShapeScaler.Lang", (int)value);
            }
        }

        public static Strings Current => _current;

        public static Strings For(UILang lang)
        {
            switch (lang)
            {
                case UILang.ZhCN: return ZhCN;
                case UILang.Ja:   return Ja;
                default:          return En;
            }
        }

        // ══════════════════════════════════════════════
        //  String fields (grouped by UI area)
        // ══════════════════════════════════════════════

        // ── Window & Menu ──
        public string WindowTitle;
        public string ProgressTitle;
        public string LangLabel;

        // ── Header (renderer selection) ──
        public string FaceRenderer;
        public string ScanMmdShapes;
        public string HelpDragRenderer;
        public string HelpClickScan;
        public string ScalesStoredFmt;       // "Scales stored: {0}"
        public string ComponentStatusFmt;     // "Scales stored: {0} | Component: {1}"
        public string StatusValid;
        public string StatusInvalid;

        // ── Grid View ──
        public string SummaryFmt;            // "{0} MMD shapes"
        public string SummaryModifiedFmt;    // "{0} MMD shapes | Modified: {1}"
        public string SummaryDirtyFmt;       // " | Unconfirmed: {0}"
        public string ThumbnailSize;
        public string ZoomLevel;
        public string Eyes;
        public string Mouth;
        public string Eyebrows;
        public string Other;
        public string ModifiedCountFmt;      // "[modified: {0}]"
        public string ResetAll;
        public string ReScan;
        public string CurrentTooltipFmt;     // "Current: {0}%"

        // ── Detail View ──
        public string BackToGrid;
        public string Prev;
        public string Next;
        public string NoPreview;
        public string PreviewDragHint;   // "Drag to orbit"
        public string ScaleFactor;
        public string Quick;
        public string SceneViewHint;
        public string ScaledToFmt;           // "Scaled to {0}% (default 100%)"
        public string Confirm;
        public string Cancel;
        public string PctValue;              // "100%"
        public string SyncToBlinkFmt;       // "Sync to {0} other blink shape(s)"
        public string SyncToBlinkDone;       // "Synced {0}% to {1} blink shape(s)"

        // ── Inspector ──
        public string TargetRenderer;
        public string OpenCalibrator;
        public string ClearAllScales;
        public string NoScaleHint;
        public string ConfiguredFmt;         // "Configured: {0} blendshape(s)"
        public string EntryFmt;              // "  {0}  →  {1}%"

        // ── Dialogs ──
        public string DlgResetTitle;
        public string DlgResetMsg;
        public string DlgResetBtn;
        public string DlgCancelBtn;
        public string DlgRescanTitle;
        public string DlgRescanMsgFmt;       // "{0} shapes have unconfirmed changes..."
        public string DlgRescanBtn;

        // ── Progress ──
        public string ProgressFmt;           // "Rendering {0} ({1}/{2})"

        // ══════════════════════════════════════════════
        //  Language instances
        // ══════════════════════════════════════════════

        private static readonly Strings En = new Strings
        {
            WindowTitle     = "FaceLint - MMD & Blink Tuner",
            ProgressTitle   = "FaceLint - MMD & Blink Tuner",
            LangLabel       = "Language",

            FaceRenderer    = "Face Renderer",
            ScanMmdShapes   = "Scan MMD Shapes",
            HelpDragRenderer = "Drag in the face SkinnedMeshRenderer (usually the Body mesh).",
            HelpClickScan   = "Click 'Scan MMD Shapes' to generate thumbnails.",
            ScalesStoredFmt = "Scales stored: {0}",
            ComponentStatusFmt = "Scales stored: {0} | Component: {1}",
            StatusValid     = "Valid",
            StatusInvalid   = "Invalid (no mesh)",

            SummaryFmt         = "{0} MMD shapes",
            SummaryModifiedFmt = "{0} MMD shapes | Modified: {1}",
            SummaryDirtyFmt    = " | Unconfirmed: {0}",
            ThumbnailSize      = "Thumbnail Size",
            ZoomLevel          = "Zoom Level",
            Eyes               = "Eyes",
            Mouth              = "Mouth",
            Eyebrows           = "Eyebrows",
            Other              = "Other",
            ModifiedCountFmt   = "[modified: {0}]",
            ResetAll           = "Reset All to 100%",
            ReScan             = "Re-scan",
            CurrentTooltipFmt  = "Current: {0}%",

            BackToGrid     = "← Back to Grid",
            Prev           = "◀ Prev",
            Next           = "Next ▶",
            NoPreview      = "No Preview",
            PreviewDragHint = "Drag to orbit | Scroll to zoom",
            ScaleFactor    = "Scale Factor",
            Quick          = "Quick:",
            SceneViewHint  = "Scene View shows live preview. Rotate to inspect.",
            ScaledToFmt    = "Scaled to {0}% (default 100%)",
            Confirm        = "✓ Confirm",
            Cancel         = "✗ Cancel",
            PctValue       = "100%",
            SyncToBlinkFmt = "Apply to {0} other blink shape(s)",
            SyncToBlinkDone = "✓ Synced {0}% to {1} blink shape(s)",

            TargetRenderer   = "Target Renderer",
            OpenCalibrator   = "Open Calibrator",
            ClearAllScales   = "Clear All Scales",
            NoScaleHint      = "No scale configured. All MMD blendshapes remain at 100%.",
            ConfiguredFmt    = "Configured: {0} blendshape(s)",
            EntryFmt         = "  {0}  →  {1}%",

            DlgResetTitle  = "Reset All",
            DlgResetMsg    = "Reset ALL MMD shapes to 100%? This cannot be undone.",
            DlgResetBtn    = "Reset All",
            DlgCancelBtn   = "Cancel",
            DlgRescanTitle = "Re-scan",
            DlgRescanMsgFmt = "{0} shapes have unconfirmed changes. Re-scanning will lose them.\n\n" +
                              "Confirm changes first or enable Auto-Confirm mode.",
            DlgRescanBtn   = "Re-scan (discard changes)",

            ProgressFmt    = "Rendering {0} ({1}/{2})",
        };

        private static readonly Strings ZhCN = new Strings
        {
            WindowTitle     = "FaceLint - MMD & Blink Tuner",
            ProgressTitle   = "FaceLint - MMD & Blink Tuner",
            LangLabel       = "语言",

            FaceRenderer    = "面部渲染器",
            ScanMmdShapes   = "扫描 MMD 形态键",
            HelpDragRenderer = "拖入面部 SkinnedMeshRenderer（通常是 Body 网格）。",
            HelpClickScan   = "点击「扫描 MMD 形态键」生成缩略图。",
            ScalesStoredFmt = "已存储缩放: {0}",
            ComponentStatusFmt = "已存储缩放: {0} | 组件: {1}",
            StatusValid     = "有效",
            StatusInvalid   = "无效（无网格）",

            SummaryFmt         = "{0} 个 MMD 形态键",
            SummaryModifiedFmt = "{0} 个 MMD 形态键 | 已修改: {1}",
            SummaryDirtyFmt    = " | 未确认: {0}",
            ThumbnailSize      = "缩略图大小",
            ZoomLevel          = "缩放倍率",
            Eyes               = "眼部",
            Mouth              = "嘴部",
            Eyebrows           = "眉毛",
            Other              = "其他",
            ModifiedCountFmt   = "[已修改: {0}]",
            ResetAll           = "全部重置为 100%",
            ReScan             = "重新扫描",
            CurrentTooltipFmt  = "当前: {0}%",

            BackToGrid     = "← 返回网格",
            Prev           = "◀ 上一个",
            Next           = "下一个 ▶",
            NoPreview      = "无预览",
            PreviewDragHint = "拖拽旋转 | 滚轮缩放",
            ScaleFactor    = "缩放系数",
            Quick          = "快速：",
            SceneViewHint  = "Scene View 显示实时预览，可旋转查看。",
            ScaledToFmt    = "已缩放至 {0}%（默认 100%）",
            Confirm        = "✓ 确认",
            Cancel         = "✗ 取消",
            PctValue       = "100%",
            SyncToBlinkFmt = "同步到其他 {0} 个闭眼形状",
            SyncToBlinkDone = "✓ 已将 {0}% 同步到 {1} 个闭眼形状",

            TargetRenderer   = "目标渲染器",
            OpenCalibrator   = "打开校准器",
            ClearAllScales   = "清除所有缩放",
            NoScaleHint      = "未配置缩放。所有 MMD 形态键保持 100%。",
            ConfiguredFmt    = "已配置: {0} 个形态键",
            EntryFmt         = "  {0}  →  {1}%",

            DlgResetTitle  = "全部重置",
            DlgResetMsg    = "重置所有 MMD 形态键至 100%？此操作不可撤销。",
            DlgResetBtn    = "全部重置",
            DlgCancelBtn   = "取消",
            DlgRescanTitle = "重新扫描",
            DlgRescanMsgFmt = "{0} 个形态键有未确认的修改。重新扫描将丢失这些修改。\n\n" +
                              "请先确认修改，或启用自动确认模式。",
            DlgRescanBtn   = "重新扫描（放弃修改）",

            ProgressFmt    = "正在渲染 {0}（{1}/{2}）",
        };

        private static readonly Strings Ja = new Strings
        {
            WindowTitle     = "FaceLint - MMD & Blink Tuner",
            ProgressTitle   = "FaceLint - MMD & Blink Tuner",
            LangLabel       = "言語",

            FaceRenderer    = "顔レンダラー",
            ScanMmdShapes   = "MMDシェイプをスキャン",
            HelpDragRenderer = "顔のSkinnedMeshRenderer（通常はBodyメッシュ）をドラッグしてください。",
            HelpClickScan   = "「MMDシェイプをスキャン」をクリックしてサムネイルを生成。",
            ScalesStoredFmt = "保存された倍率: {0}",
            ComponentStatusFmt = "保存された倍率: {0} | コンポーネント: {1}",
            StatusValid     = "有効",
            StatusInvalid   = "無効（メッシュなし）",

            SummaryFmt         = "{0} 個のMMDシェイプ",
            SummaryModifiedFmt = "{0} 個のMMDシェイプ | 変更: {1}",
            SummaryDirtyFmt    = " | 未確認: {0}",
            ThumbnailSize      = "サムネイルサイズ",
            ZoomLevel          = "拡大率",
            Eyes               = "目",
            Mouth              = "口",
            Eyebrows           = "眉",
            Other              = "その他",
            ModifiedCountFmt   = "[変更: {0}]",
            ResetAll           = "全て100%にリセット",
            ReScan             = "再スキャン",
            CurrentTooltipFmt  = "現在: {0}%",

            BackToGrid     = "← グリッドに戻る",
            Prev           = "◀ 前へ",
            Next           = "次へ ▶",
            NoPreview      = "プレビューなし",
            PreviewDragHint = "ドラッグで回転 | スクロールで拡大",
            ScaleFactor    = "拡大率",
            Quick          = "クイック:",
            SceneViewHint  = "Scene Viewでリアルタイムプレビュー。回転して確認できます。",
            ScaledToFmt    = "{0}%に拡大縮小（デフォルト100%）",
            Confirm        = "✓ 確認",
            Cancel         = "✗ キャンセル",
            PctValue       = "100%",
            SyncToBlinkFmt = "他の{0}個のまばたきに適用",
            SyncToBlinkDone = "✓ {0}%を{1}個のまばたきに同期しました",

            TargetRenderer   = "ターゲットレンダラー",
            OpenCalibrator   = "キャリブレーターを開く",
            ClearAllScales   = "全ての倍率をクリア",
            NoScaleHint      = "倍率未設定。全てのMMDシェイプは100%のままです。",
            ConfiguredFmt    = "設定済み: {0} シェイプ",
            EntryFmt         = "  {0}  →  {1}%",

            DlgResetTitle  = "全てリセット",
            DlgResetMsg    = "全てのMMDシェイプを100%にリセットしますか？この操作は元に戻せません。",
            DlgResetBtn    = "全てリセット",
            DlgCancelBtn   = "キャンセル",
            DlgRescanTitle = "再スキャン",
            DlgRescanMsgFmt = "{0} 個のシェイプに未確認の変更があります。再スキャンすると失われます。\n\n" +
                              "先に変更を確認するか、自動確認モードを有効にしてください。",
            DlgRescanBtn   = "再スキャン（変更を破棄）",

            ProgressFmt    = "レンダリング中 {0}（{1}/{2}）",
        };
    }
}
