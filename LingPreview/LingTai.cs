using System;
using System.Collections.Generic;
using System.Text;
using Il2CppInterop.Runtime;
using MaJiang;
using MaJiang.DataConstruct.BaoLingPai;
using MaJiang.DataConstruct.MaJiang;
using MaJiang.GameEvent.Runtime.Controller;
using MaJiang.UI.GameEvent;
using Shared;
using UnityEngine;

namespace LingPreview
{
    /// <summary>行的样式：正文一律同色，组与组之间靠间距区分。
    /// Title=标题、Normal=正文（白）、Dim=次要说明（灰）、Highlight=当前牌（黄加粗）。</summary>
    internal enum LineKind
    {
        Title,
        Normal,
        Dim,
        Highlight
    }

    internal sealed class PanelLine
    {
        public readonly string Text;
        public readonly LineKind Kind;

        public PanelLine(string text, LineKind kind)
        {
            Text = text;
            Kind = kind;
        }
    }

    /// <summary>面板内容（每次状态变化重建一次）。</summary>
    internal sealed class LingView
    {
        public readonly List<PanelLine> Lines = new List<PanelLine>();
        public BaoLing Ling;
        public BaoLingPayload Payload;
    }

    /// <summary>
    /// 与游戏「幻灵台」的对接层。
    ///
    /// 注意：游戏里的 <c>PaiMianWorktable.GetData(payload, ling)</c> **不做牌面变换**
    /// （实测原样返回该牌），不能拿它当「权威结果」交叉校验——那只会一直报假 MISMATCH。
    /// 牌面变换的权威只有**玩家实机验证**，规则固化在 Shared/LingRules.cs。
    ///
    /// 幻灵台 = <see cref="BaoLingTai"/>（GameEvent 控制器，MonoBehaviour）+
    /// <see cref="PaiMianAdditionUI"/>（界面：上排灵按钮 + 下排牌按钮 + 确认/重抽）。
    /// 状态全在控制器上：
    ///   _CurSelectEnhance  —— 当前选中的牌灵（IPaiMianEnhanceButton.Enhance = BaoLingPayload）
    ///   _CurSelectPaiMian  —— 当前选中的牌面（ItemContainer&lt;PaiMianPayload&gt;）
    ///   curConfirmCount / maxConfirmCount —— 已确认 / 可确认次数
    /// 变换结果由 <see cref="LingRules"/> 算出（纯函数），不改游戏任何数据。
    /// </summary>
    internal static class LingTai
    {
        /// <summary>花色字的兜底名。游戏自己的文案是「索」（实机 displayName：五索/六索），
        /// 虽然枚举名写作 `HuaSe.Tiao`——按游戏文案来，别改成「条」。</summary>
        private const string SuitFallback = "万筒索";

        /// <summary>字牌的兜底名（按游戏枚举顺序：feng 1-4 / jian 1-3）。
        /// 正常情况下都用游戏自己的 displayName，这里只是取不到时的兜底。</summary>
        private static readonly string[] FengNames = { "东风", "南风", "西风", "北风" };
        private static readonly string[] JianNames = { "红中", "白板", "发财" };

        private static readonly string[] ChineseDigits =
            { "", "一", "二", "三", "四", "五", "六", "七", "八", "九" };

        /// <summary>debug 日志出口（由 LingPanel 注入）。</summary>
        internal static Action<string> Log;

        /// <summary>找幻灵台控制器（不判断界面是否可见）——给 debug 排查用：
        /// 面板不出现时也要能打出「是哪一条判据不满足」。</summary>
        public static BaoLingTai FindController()
        {
            try
            {
                var tais = UnityEngine.Object.FindObjectsOfType<BaoLingTai>();
                if (tais == null) return null;
                for (int i = 0; i < tais.Length; i++)
                {
                    var t = tais[i];
                    if (t == null || !t.gameObject.activeInHierarchy) continue;
                    if (!t.IsRunning) continue;
                    return t;
                }
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>找当前打开的幻灵台；没开返回 false。
        /// 判据只有一条：**界面真的开着**（UI 在场景里 + 没隐藏 + 节点在跑）。
        /// 不用「选中过灵」当判据——离开幻灵台时游戏只是把界面藏起来，并不会清空
        /// CurSelectEnhance，用它当判据会让面板残留在游戏画面上。</summary>
        public static bool TryGet(out BaoLingTai tai, out PaiMianAdditionUI ui)
        {
            tai = null;
            ui = null;
            try
            {
                var tais = UnityEngine.Object.FindObjectsOfType<BaoLingTai>();
                if (tais == null) return false;

                for (int i = 0; i < tais.Length; i++)
                {
                    var t = tais[i];
                    if (t == null || !t.gameObject.activeInHierarchy) continue;
                    if (!t.IsRunning) continue;

                    var u = t.UI;
                    if (u == null) continue;
                    if (!u.gameObject.activeInHierarchy) continue;
                    if (u.IsHide || u.Finished) continue;

                    tai = t;
                    ui = u;
                    return true;
                }
            }
            catch (Exception) { }
            return false;
        }

        /// <summary>读幻灵台状态并生成面板内容；没选灵返回 null。</summary>
        public static LingView Build(BaoLingTai tai, PaiMianAdditionUI ui, bool full, GUIStyle measure)
        {
            if (tai == null) return null;

            BaoLingPayload payload = SelectedPayload(tai, ui);
            if (payload == null) return null;          // 还没选灵
            if (payload.IsBao) return null;            // 宝牌不改牌面，不提示

            var names = CollectNames(ui);

            var view = new LingView();
            view.Ling = payload.id;
            view.Payload = payload;
            view.Lines.Add(new PanelLine(Header(tai, payload), LineKind.Title));

            LingRuleKind rule = RuleKindFor(payload.id, payload, out int diff);
            if (rule != LingRuleKind.None)
                view.Lines.Add(new PanelLine(LingRules.Describe(rule, diff), LineKind.Normal));

            string desc = Description(payload);
            if (desc.Length > 0)
                view.Lines.Add(new PanelLine(desc, LineKind.Dim));

            if (full && rule != LingRuleKind.None)
            {
                // 三组的映射项起始 x 对齐：组名按实测宽度补空格（不靠字数猜，比例字体下更准）
                float labelWidth = MeasureLabel(measure, "数牌", "风牌", "三元牌");

                // 组与组之间用空行分隔（正文同色，靠间距分组）
                view.Lines.Add(new PanelLine(NumberLine("数牌", NumberTokens(rule, diff), labelWidth, measure), LineKind.Normal));
                view.Lines.Add(new PanelLine("", LineKind.Normal));
                view.Lines.Add(new PanelLine(NumberLine("风牌", HonorTokens(LingTileKind.Feng, rule, diff, names), labelWidth, measure), LineKind.Normal));
                view.Lines.Add(new PanelLine("", LineKind.Normal));
                view.Lines.Add(new PanelLine(NumberLine("三元牌", HonorTokens(LingTileKind.Jian, rule, diff, names), labelWidth, measure), LineKind.Normal));
            }

            AddTileLine(tai, rule, diff, names, view);
            return view;
        }

        /// <summary>组名 + 映射项。映射项之间用「|」分隔：单空格/双空格都容易把相邻两项
        /// 连读（如 1→8 2→9 被看成 8 2），竖线是明确的项边界。
        /// 组名用全角空格补齐，保证各组映射项的起始 x 一致。</summary>
        private static string NumberLine(string label, List<string> tokens, float labelWidth, GUIStyle measure)
        {
            if (tokens == null || tokens.Count == 0) return PadLabel(label, labelWidth, measure) + FullSpace + "无效";
            var sb = new StringBuilder(PadLabel(label, labelWidth, measure));
            sb.Append(FullSpace);
            for (int i = 0; i < tokens.Count; i++)
            {
                if (i > 0) sb.Append(" | ");
                sb.Append(tokens[i]);
            }
            return sb.ToString();
        }

        /// <summary>全角空格（U+3000）：与汉字同宽，用它对齐最准。</summary>
        private const string FullSpace = "　";

        /// <summary>取几个组名里最宽的一个（像素），用来对齐映射项的起始 x。</summary>
        private static float MeasureLabel(GUIStyle measure, params string[] labels)
        {
            float max = 0f;
            for (int i = 0; i < labels.Length; i++)
            {
                float w = measure.CalcSize(new GUIContent(labels[i])).x;
                if (w > max) max = w;
            }
            return max;
        }

        /// <summary>组名右侧补全角空格，补到 targetWidth 像素宽。
        /// 按「一个汉字的宽度」为单位换算（汉字与全角空格同宽），所以列对齐是精确的。
        /// 用 Round 再补足（不用 Ceil）：CalcSize 是浮点累加，Ceil 会因为 1.0000001
        /// 多补一个字宽，正好破坏对齐。</summary>
        private static string PadLabel(string label, float targetWidth, GUIStyle measure)
        {
            if (measure == null) return label;
            float w = measure.CalcSize(new GUIContent(label)).x;
            if (w >= targetWidth) return label;
            float cjkW = measure.CalcSize(new GUIContent("中")).x;
            if (cjkW <= 0f) return label;
            int n = (int)Math.Round((targetWidth - w) / cjkW);
            while (n > 0 && w + (n - 1) * cjkW >= targetWidth) n--;   // 别多补
            while (w + n * cjkW < targetWidth) n++;                    // 别少补
            if (n <= 0) return label;
            return label + new string('　', n);
        }

        /// <summary>当前选中的牌灵配置（没有则 null）。
        ///
        /// 两个来源都可能是「上一轮的残留」（关闭幻灵台时游戏不会清空 `CurSelected` /
        /// `CurSelectEnhance`），所以两个都取出来一起判：
        ///   · 两个来源指向同一个灵 → 界面与控制器都认它，认定是当前选择（被动直送、
        ///     不在按钮列表里的灵也走这里，不会被误杀）；
        ///   · 不一致 → 由「本轮展示的灵按钮」裁定谁是真的；
        ///   · 都不在按钮里 → 认为是残留，按未选灵处理（面板不出现）。</summary>
        private static BaoLingPayload SelectedPayload(BaoLingTai tai, PaiMianAdditionUI ui)
        {
            BaoLingPayload fromUi = null;
            BaoLingPayload fromController = null;
            try
            {
                if (ui != null && ui.CurSelected != null)
                    fromUi = ui.CurSelected.TryCast<BaoLingPayload>();
            }
            catch (Exception) { }
            try
            {
                var enhance = tai.CurSelectEnhance;
                if (enhance != null && enhance.Enhance != null)
                    fromController = enhance.Enhance.TryCast<BaoLingPayload>();
            }
            catch (Exception) { }

            if (fromUi != null && fromController != null && fromUi.id == fromController.id)
                return fromUi;
            if (fromUi != null && IsInCurrentButtons(ui, fromUi)) return fromUi;
            if (fromController != null && IsInCurrentButtons(ui, fromController)) return fromController;
            return null;
        }

        /// <summary>该灵是否在「本轮展示的灵按钮」里（不在 → 是上一轮的残留，按未选处理）。</summary>
        private static bool IsInCurrentButtons(PaiMianAdditionUI ui, BaoLingPayload payload)
        {
            if (ui == null) return true;          // 拿不到按钮列表就不拦（别把功能整个关掉）
            try
            {
                var buttons = ui.CurButtons;
                if (buttons == null || buttons.Count == 0) return true;
                for (int i = 0; i < buttons.Count; i++)
                {
                    var b = buttons[i];
                    if (b == null) continue;
                    var info = b.Enhance;
                    if (info == null) continue;
                    var p = info.TryCast<BaoLingPayload>();
                    if (p != null && p.id == payload.id) return true;
                }
                return false;
            }
            catch (Exception) { return true; }
        }

        /// <summary>标题行：灵名 + 已确认/可确认次数。</summary>
        private static string Header(BaoLingTai tai, BaoLingPayload payload)
        {
            string name = payload.Name;
            if (string.IsNullOrEmpty(name))
            {
                try { name = BaoLingExtend.GetName(payload.id); } catch (Exception) { }
            }
            if (string.IsNullOrEmpty(name)) name = payload.id.ToString();

            int cur = 0, max = 0;
            try { cur = tai.curConfirmCount; max = tai.maxConfirmCount; } catch (Exception) { }
            return max > 0 ? name + "   确认 " + cur + "/" + max : name;
        }

        /// <summary>游戏自带的灵说明（去富文本标签 + 截断）。</summary>
        private static string Description(BaoLingPayload payload)
        {
            if (payload == null) return "";
            string desc = "";
            try { desc = payload.GetDescription(); } catch (Exception) { }
            if (string.IsNullOrEmpty(desc)) return "";
            desc = StripRichText(desc).Replace("\n", " ").Replace("\r", " ").Trim();
            return desc.Length > 46 ? desc.Substring(0, 46) + "…" : desc;
        }

        /// <summary>去掉 &lt;color=#xxx&gt; 这类富文本标签（IMGUI 不认）。</summary>
        private static string StripRichText(string s)
        {
            if (s.IndexOf('<') < 0) return s;
            var sb = new StringBuilder(s.Length);
            bool inTag = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '<') { inTag = true; continue; }
                if (c == '>') { inTag = false; continue; }
                if (!inTag) sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>牌灵 → 规则类型；象/鼠的偏移量取自 payload.numberDiff（方向由牌灵种类决定）。</summary>
        public static LingRuleKind RuleKindFor(BaoLing ling, BaoLingPayload payload, out int diff)
        {
            diff = 0;
            int raw = 0;
            if (payload != null)
            {
                try { raw = payload.numberDiff; } catch (Exception) { }
            }

            switch (ling)
            {
                case BaoLing.XiangLing:
                    diff = LingRules.DiffFor(LingKind.Xiang, raw);
                    return LingRuleKind.Shift;
                case BaoLing.ShuLing:
                    diff = LingRules.DiffFor(LingKind.Shu, raw);
                    return LingRuleKind.Shift;
                case BaoLing.MaLing:
                    return LingRuleKind.PairSwap;
                default:
                    return LingRuleKind.None;
            }
        }

        /// <summary>数牌映射项：象/鼠逐个列出，马灵按对子列。</summary>
        private static List<string> NumberTokens(LingRuleKind rule, int diff)
        {
            var tokens = new List<string>();
            if (rule == LingRuleKind.PairSwap)
            {
                for (int i = 1; i <= 8; i += 2) tokens.Add(i + "↔" + (i + 1));
                tokens.Add("9→8");
                return tokens;
            }
            for (int i = 1; i <= LingRules.NumberSize; i++)
                tokens.Add(i + "→" + LingRules.Shift(i, diff, LingRules.NumberSize));
            return tokens;
        }

        /// <summary>字牌（风牌/三元牌）映射项；规则不适用时返回空表。</summary>
        private static List<string> HonorTokens(LingTileKind kind, LingRuleKind rule, int diff,
            Dictionary<string, string> names)
        {
            var tokens = new List<string>();
            if (rule == LingRuleKind.None) return tokens;
            int size = LingRules.Size(kind);
            for (int i = 1; i <= size; i++)
            {
                int res;
                if (!LingRules.TryMap(kind, i, rule, diff, out res)) return tokens;
                tokens.Add(Name(names, kind, 0, i) + "→" + Name(names, kind, 0, res));
            }
            return tokens;
        }

        /// <summary>当前选中牌的变换结果（"东风 → 西风"）。</summary>
        private static void AddTileLine(BaoLingTai tai, LingRuleKind rule, int diff,
            Dictionary<string, string> names, LingView view)
        {
            if (rule == LingRuleKind.None) return;
            try
            {
                var sel = tai.CurSelectPaiMian;
                if (sel == null) return;
                var payload = sel.Item;
                if (payload == null) return;

                var p = payload.paiMian;
                LingTileKind kind = KindOf(p);
                if (kind == LingTileKind.None) return;
                int index = IndexOf(p, kind);
                int res;
                if (!LingRules.TryMap(kind, index, rule, diff, out res)) return;

                string from = Name(names, kind, (int)p.huaSe, index);
                string to = Name(names, kind, (int)p.huaSe, res);
                view.Lines.Add(new PanelLine("▶ " + from + "  →  " + to, LineKind.Highlight));
            }
            catch (Exception) { }
        }

        private static LingTileKind KindOf(PaiMian p)
        {
            // Any 是通配值，不是具体牌面，要排除
            if (p.huaSe != HuaSe.None && p.huaSe != HuaSe.Any) return LingTileKind.Number;
            if (p.feng != Feng.None && p.feng != Feng.Any) return LingTileKind.Feng;
            if (p.jian != Jian.None && p.jian != Jian.Any) return LingTileKind.Jian;
            return LingTileKind.None;
        }

        private static int IndexOf(PaiMian p, LingTileKind kind)
        {
            switch (kind)
            {
                case LingTileKind.Number: return p.number;
                case LingTileKind.Feng: return (int)p.feng;
                default: return (int)p.jian;
            }
        }

        /// <summary>收集牌面名（displayName），键：N花色-点数 / F风 / J三元。
        /// 先取游戏全部 34 张牌（GlobalStaticDataManager.PaiMianTotalList），
        /// 再用幻灵台里展示的那批补齐——这样「结果牌」即使不在幻灵台列表里也有名字。</summary>
        private static Dictionary<string, string> CollectNames(PaiMianAdditionUI ui)
        {
            var names = new Dictionary<string, string>();
            AddNames(names, GlobalTiles());
            AddNames(names, UiTiles(ui));
            return names;
        }

        private static void AddNames(Dictionary<string, string> names, List<PaiMianPayload> payloads)
        {
            if (payloads == null) return;
            for (int i = 0; i < payloads.Count; i++)
            {
                var payload = payloads[i];
                if (payload == null) continue;
                string name = payload.displayName;
                if (string.IsNullOrEmpty(name)) name = payload.Name;
                if (string.IsNullOrEmpty(name)) continue;

                var p = payload.paiMian;
                LingTileKind kind = KindOf(p);
                if (kind == LingTileKind.None) continue;
                names[Key(kind, (int)p.huaSe, IndexOf(p, kind))] = name;
            }
        }

        /// <summary>游戏全部牌面（34 张）。带 TTL 缓存：原来每 0.15s 就要
        /// FindObjectsOfType 扫一遍全场景并重建 34 项列表，太浪费（同 ScorePreview 的
        /// CachedTMPs 做法：1 秒 TTL）。</summary>
        private static List<PaiMianPayload> GlobalTiles()
        {
            if (_globalTiles != null && Time.unscaledTime - _globalTilesTime < 1f) return _globalTiles;

            var list = new List<PaiMianPayload>();
            try
            {
                var mgrs = UnityEngine.Object.FindObjectsOfType<GlobalStaticDataManager>();
                for (int i = 0; i < mgrs.Length; i++)
                {
                    var total = mgrs[i] != null ? mgrs[i].PaiMianTotalList : null;
                    if (total == null) continue;
                    var arr = total.value;
                    if (arr == null) continue;
                    for (int j = 0; j < arr.Length; j++) list.Add(arr[j]);
                    break;
                }
            }
            catch (Exception) { }

            _globalTiles = list;
            _globalTilesTime = Time.unscaledTime;
            return list;
        }

        private static List<PaiMianPayload> _globalTiles;
        private static float _globalTilesTime = -10f;

        /// <summary>幻灵台下排展示的牌面。</summary>
        private static List<PaiMianPayload> UiTiles(PaiMianAdditionUI ui)
        {
            var list = new List<PaiMianPayload>();
            if (ui == null) return list;
            try
            {
                var root = ui.paiMianRoot;
                if (root == null) return list;
                var payloads = root.Payloads;
                if (payloads == null) return list;
                for (int i = 0; i < payloads.Length; i++) list.Add(payloads[i]);
            }
            catch (Exception) { }
            return list;
        }

        private static string Key(LingTileKind kind, int huaSe, int index)
        {
            if (kind == LingTileKind.Number) return "N" + huaSe + "-" + index;
            if (kind == LingTileKind.Feng) return "F" + index;
            return "J" + index;
        }

        /// <summary>取牌面名：优先用游戏自己的显示名（四万/六万/东风/红中…），
        /// 取不到才用固定名兜底。固定名与枚举一一对应：
        /// Feng(东=1/南=2/西=3/北=4)、Jian(中=1/白=2/发=3)。</summary>
        private static string Name(Dictionary<string, string> names, LingTileKind kind, int huaSe, int index)
        {
            string key = Key(kind, huaSe, index);
            string n;
            if (names != null && names.TryGetValue(key, out n) && !string.IsNullOrEmpty(n)) return n;

            switch (kind)
            {
                case LingTileKind.Number:
                    return NumberFallback(index) + SuitChar(names, huaSe);
                case LingTileKind.Feng:
                    return index >= 1 && index <= 4 ? FengNames[index - 1] : "";
                default:
                    return index >= 1 && index <= 3 ? JianNames[index - 1] : "";
            }
        }

        /// <summary>数牌兜底名：汉字数字（跟游戏一致），如「六万」。</summary>
        private static string NumberFallback(int index)
        {
            return index >= 1 && index <= 9 ? ChineseDigits[index] : index.ToString();
        }

        /// <summary>花色字：取游戏自己的叫法（万/筒/索/条…），取不到用兜底。</summary>
        private static string SuitChar(Dictionary<string, string> names, int huaSe)
        {
            for (int i = 1; i <= 9; i++)
            {
                string key = Key(LingTileKind.Number, huaSe, i);
                string n;
                if (names != null && names.TryGetValue(key, out n) && !string.IsNullOrEmpty(n))
                    return n.Substring(n.Length - 1);
            }
            return huaSe >= 1 && huaSe <= 3 ? SuitFallback[huaSe - 1].ToString() : "";
        }

        /// <summary>debug：把幻灵台当前状态拼成一行，方便定位「为什么面板没出来」。
        /// 把四个判据旗标一起打出来（IsRunning / 界面在场景里 / IsHide / Finished），
        /// 面板不出现时能一眼看出是哪一条不满足。</summary>
        public static string DebugState(BaoLingTai tai)
        {
            try
            {
                var sb = new StringBuilder();
                var u = tai.UI;
                sb.Append("running=").Append(tai.IsRunning)
                  .Append(" ctrlActive=").Append(tai.gameObject.activeInHierarchy);
                sb.Append(" ui=");
                if (u == null)
                {
                    sb.Append("null");
                }
                else
                {
                    sb.Append("active=").Append(u.gameObject.activeInHierarchy)
                      .Append(" hide=").Append(u.IsHide)
                      .Append(" finished=").Append(u.Finished);
                }

                sb.Append(" enhance=");
                var enhance = tai.CurSelectEnhance;
                if (enhance == null)
                {
                    sb.Append("null");
                }
                else
                {
                    var info = enhance.Enhance;
                    if (info == null)
                    {
                        sb.Append("null");
                    }
                    else
                    {
                        sb.Append(info.ToString());
                        try
                        {
                            var p = info.TryCast<BaoLingPayload>();
                            if (p != null)
                                sb.Append("(id=").Append(p.id).Append(",nd=").Append(p.numberDiff)
                                  .Append(",bao=").Append(p.IsBao).Append(",name=").Append(p.Name).Append(")");
                            else
                                sb.Append("(not-baoLingPayload)");
                        }
                        catch (Exception e) { sb.Append("(cast-fail ").Append(e.GetType().Name).Append(")"); }
                    }
                }

                sb.Append(" curSelected=");
                try
                {
                    var cs = u != null ? u.CurSelected : null;
                    if (cs == null) sb.Append("null");
                    else
                    {
                        var cp = cs.TryCast<BaoLingPayload>();
                        sb.Append(cp == null ? "not-baoLing" : cp.id.ToString());
                    }
                }
                catch (Exception e) { sb.Append("cast-fail ").Append(e.GetType().Name); }

                sb.Append(" buttons=[");
                try
                {
                    var btns = u != null ? u.CurButtons : null;
                    if (btns != null)
                    {
                        for (int i = 0; i < btns.Count; i++)
                        {
                            if (i > 0) sb.Append(',');
                            var b = btns[i];
                            if (b == null) { sb.Append('?'); continue; }
                            var bp = b.Enhance != null ? b.Enhance.TryCast<BaoLingPayload>() : null;
                            sb.Append(bp == null ? "?" : bp.id.ToString());
                        }
                    }
                }
                catch (Exception e) { sb.Append("err ").Append(e.GetType().Name); }
                sb.Append(']');

                sb.Append(" tile=");
                var sel = tai.CurSelectPaiMian;
                if (sel == null)
                {
                    sb.Append("null");
                }
                else
                {
                    var it = sel.Item;
                    sb.Append(it == null ? "null" : it.displayName);
                }
                return sb.ToString();
            }
            catch (Exception e) { return "err " + e.Message; }
        }

        /// <summary>debug：把幻灵台里的牌面表（枚举值 + 游戏显示名）打一遍，核对命名与顺序。</summary>
        public static void DumpTiles(IEnumerable<PaiMianPayload> payloads)
        {
            if (payloads == null) return;
            try
            {
                var sb = new StringBuilder("[probe] tiles: ");
                int n = 0;
                foreach (var payload in payloads)
                {
                    if (payload == null) continue;
                    var p = payload.paiMian;
                    sb.Append("{hua=").Append((int)p.huaSe).Append(",num=").Append(p.number)
                      .Append(",feng=").Append((int)p.feng).Append(",jian=").Append((int)p.jian)
                      .Append("}=").Append(payload.displayName).Append("  ");
                    n++;
                }
                Log?.Invoke(sb.ToString());
            }
            catch (Exception e) { Log?.Invoke("[probe] dump tiles failed: " + e.Message); }
        }

        /// <summary>debug：打印灵配置字段（numberDiff / menLei / 游戏说明）。</summary>
        public static void DumpPayload(BaoLingPayload payload, LingRuleKind rule, int diff)
        {
            if (payload == null) return;
            try
            {
                int raw = 0;
                string menLei = "";
                string desc = "";
                try { raw = payload.numberDiff; } catch (Exception) { }
                try { menLei = payload.menLei.ToString(); } catch (Exception) { }
                try { desc = payload.GetDescription(); } catch (Exception) { }
                Log?.Invoke("[probe] ling=" + payload.id + " name=" + payload.Name + " numberDiff=" + raw
                    + " menLei=" + menLei + " rule=" + rule + " diff=" + diff + " desc=[" + desc + "]");
            }
            catch (Exception e) { Log?.Invoke("[probe] dump failed: " + e.Message); }
        }

    }
}
