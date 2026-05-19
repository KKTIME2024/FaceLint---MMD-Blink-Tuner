using System;
using System.Collections.Generic;

namespace MmdBlendShapeScaler
{
    [Flags]
    public enum MmdShapeCategory
    {
        未知 = 0,
        嘴部 = 1,
        眼部 = 2,
        眉毛 = 4
    }

    public class MmdShapeInfo
    {
        public string 日文名;
        public string 中文说明;
        public MmdShapeCategory 分类;
        public bool 是闭合类;

        public MmdShapeInfo(string 日文名, string 中文说明, MmdShapeCategory 分类, bool 是闭合类 = false)
        {
            this.日文名 = 日文名;
            this.中文说明 = 中文说明;
            this.分类 = 分类;
            this.是闭合类 = 是闭合类;
        }
    }

    public static class MmdShapeDatabase
    {
        public static readonly List<MmdShapeInfo> 标准形状列表 = new List<MmdShapeInfo>
        {
            // ── 嘴部 ──
            new MmdShapeInfo("あ", "张嘴/Ah", MmdShapeCategory.嘴部),
            new MmdShapeInfo("い", "咧嘴/Ch", MmdShapeCategory.嘴部),
            new MmdShapeInfo("う", "嘟嘴/U", MmdShapeCategory.嘴部),
            new MmdShapeInfo("え", "露齿/E", MmdShapeCategory.嘴部),
            new MmdShapeInfo("お", "圆嘴/Oh", MmdShapeCategory.嘴部),
            new MmdShapeInfo("にやり", "抿嘴笑/Grin", MmdShapeCategory.嘴部, true),
            new MmdShapeInfo("∧", "猫嘴∧", MmdShapeCategory.嘴部),
            new MmdShapeInfo("ワ", "大张嘴/Wa", MmdShapeCategory.嘴部),
            new MmdShapeInfo("ω", "ω嘴型", MmdShapeCategory.嘴部),
            new MmdShapeInfo("▲", "三角嘴", MmdShapeCategory.嘴部),
            new MmdShapeInfo("口角上げ", "嘴角上扬", MmdShapeCategory.嘴部),
            new MmdShapeInfo("口角下げ", "嘴角下撇", MmdShapeCategory.嘴部),
            new MmdShapeInfo("口横広げ", "横向扩嘴", MmdShapeCategory.嘴部),
            new MmdShapeInfo("にやり２", "抿嘴笑2", MmdShapeCategory.嘴部, true),
            new MmdShapeInfo("ん", "抿嘴/n", MmdShapeCategory.嘴部, true),
            new MmdShapeInfo("あ２", "张嘴2", MmdShapeCategory.嘴部),
            new MmdShapeInfo("□", "方嘴□", MmdShapeCategory.嘴部),
            new MmdShapeInfo("ω□", "ω□嘴型", MmdShapeCategory.嘴部),
            new MmdShapeInfo("にっこり", "微笑/Smile", MmdShapeCategory.嘴部),
            new MmdShapeInfo("ぺろっ", "吐舌/Pero", MmdShapeCategory.嘴部),
            new MmdShapeInfo("てへぺろ", "吐舌笑", MmdShapeCategory.嘴部),
            new MmdShapeInfo("てへぺろ２", "吐舌笑2", MmdShapeCategory.嘴部),
            new MmdShapeInfo("はんっ！", "半张嘴", MmdShapeCategory.嘴部),
            new MmdShapeInfo("歯無し下", "下齿隐去", MmdShapeCategory.嘴部),
            new MmdShapeInfo("歯無し上", "上齿隐去", MmdShapeCategory.嘴部),
            new MmdShapeInfo("頬染め", "脸颊绯红", MmdShapeCategory.嘴部),
            new MmdShapeInfo("照れ", "害羞", MmdShapeCategory.嘴部),

            // ── 眼部（闭合类）──
            // 这些形状在 weight=100 时应自然闭合，但很多模型会过度闭合
            new MmdShapeInfo("まばたき", "眨眼/Blink", MmdShapeCategory.眼部, true),
            new MmdShapeInfo("笑い", "笑眼/Blink Happy", MmdShapeCategory.眼部, true),
            new MmdShapeInfo("はぅ", "闭眼><", MmdShapeCategory.眼部, true),
            new MmdShapeInfo("ウィンク", "眨眼/Wink", MmdShapeCategory.眼部, true),
            new MmdShapeInfo("ウィンク右", "右眨眼/Wink Right", MmdShapeCategory.眼部, true),
            new MmdShapeInfo("ウィンク２", "眨眼2/Wink 2", MmdShapeCategory.眼部, true),
            new MmdShapeInfo("ｳｨﾝｸ２右", "右眨眼2/Wink 2 Right", MmdShapeCategory.眼部, true),
            new MmdShapeInfo("なごみ", "温和眼/Calm", MmdShapeCategory.眼部, true),
            new MmdShapeInfo("なごみω", "温和ω眼", MmdShapeCategory.眼部, true),
            new MmdShapeInfo("はちゅ目", "豆豆眼/O O", MmdShapeCategory.眼部),
            new MmdShapeInfo("恐ろしい子！", "恐怖眼/Funky", MmdShapeCategory.眼部),
            new MmdShapeInfo("じと目", "死鱼眼/Stare", MmdShapeCategory.眼部),
            new MmdShapeInfo("びっくり", "惊讶眼/Surprised", MmdShapeCategory.眼部),
            new MmdShapeInfo("ｷﾘｯ", "锐利眼/Slant", MmdShapeCategory.眼部),
            new MmdShapeInfo("瞳小", "小瞳孔", MmdShapeCategory.眼部),
            new MmdShapeInfo("瞳大", "大瞳孔", MmdShapeCategory.眼部),
            new MmdShapeInfo("瞳縦潰れ", "瞳孔压扁", MmdShapeCategory.眼部),
            new MmdShapeInfo("はぁと", "爱心眼/Heart", MmdShapeCategory.眼部),
            new MmdShapeInfo("星目", "星星眼/Star", MmdShapeCategory.眼部),
            new MmdShapeInfo("光下", "眼神光下移", MmdShapeCategory.眼部),
            new MmdShapeInfo("ハイライト消", "高光消失", MmdShapeCategory.眼部),
            new MmdShapeInfo("映り込み消", "反光消失", MmdShapeCategory.眼部),
            new MmdShapeInfo("喜び", "喜悦(眼+眉)", MmdShapeCategory.眼部 | MmdShapeCategory.眉毛),
            new MmdShapeInfo("悲しむ", "悲伤(眼+眉)", MmdShapeCategory.眼部 | MmdShapeCategory.眉毛, true),
            new MmdShapeInfo("敵意", "敌意(眼+眉)", MmdShapeCategory.眼部 | MmdShapeCategory.眉毛),
            new MmdShapeInfo("わぉ!?", "哇哦(眼+眉)", MmdShapeCategory.眼部 | MmdShapeCategory.眉毛),

            // ── 眉毛 ──
            new MmdShapeInfo("にこり", "愉悦眉/Cheerful", MmdShapeCategory.眉毛),
            new MmdShapeInfo("上", "抬眉/Up", MmdShapeCategory.眉毛),
            new MmdShapeInfo("下", "降眉/Down", MmdShapeCategory.眉毛),
            new MmdShapeInfo("真面目", "认真眉/Serious", MmdShapeCategory.眉毛),
            new MmdShapeInfo("困る", "困惑眉/Sadness", MmdShapeCategory.眉毛),
            new MmdShapeInfo("怒り", "愤怒眉/Anger", MmdShapeCategory.眉毛),
            new MmdShapeInfo("前", "前移眉/Front", MmdShapeCategory.眉毛),
            new MmdShapeInfo("がーん", "震惊", MmdShapeCategory.眉毛),
            new MmdShapeInfo("青ざめ", "脸色发青", MmdShapeCategory.眉毛),

            // ── 其他 ──
            new MmdShapeInfo("涙", "流泪", MmdShapeCategory.未知),
            new MmdShapeInfo("しいたけ", "香菇", MmdShapeCategory.未知),

            // ── Avatar 原生形状 (MMD 世界无直接驱动，但与 MMD 形状叠加) ──
            new MmdShapeInfo("vrc.blink (3.0)", "眨眼3.0/Native Blink", MmdShapeCategory.眼部),
            new MmdShapeInfo("vrc.blink(3.0)", "眨眼3.0/Native Blink", MmdShapeCategory.眼部)
        };

        public static readonly HashSet<string> 标准名称集合 = new HashSet<string>();
        public static readonly Dictionary<string, MmdShapeInfo> 名称到信息映射 = new Dictionary<string, MmdShapeInfo>();

        static MmdShapeDatabase()
        {
            foreach (var info in 标准形状列表)
            {
                标准名称集合.Add(info.日文名);
                名称到信息映射[info.日文名] = info;
            }
        }
    }
}
