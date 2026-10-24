using System;

namespace Shared
{
    /// <summary>牌面类别：数牌（万/筒/索）、风牌、三元牌。</summary>
    public enum LingTileKind
    {
        None,
        Number,
        Feng,
        Jian
    }

    /// <summary>牌灵种类（决定偏移方向）。</summary>
    public enum LingKind
    {
        /// <summary>其它牌灵（无位移规则）。</summary>
        Other,
        /// <summary>象灵：数字 +2。</summary>
        Xiang,
        /// <summary>鼠灵：数字 -2。</summary>
        Shu
    }

    /// <summary>牌灵的变换规则类型。</summary>
    public enum LingRuleKind
    {
        /// <summary>无已知规则（只展示游戏自带说明）。</summary>
        None,
        /// <summary>带偏移的循环（象灵 +2 / 鼠灵 -2）。</summary>
        Shift,
        /// <summary>相邻两数互换（马灵）。</summary>
        PairSwap
    }

    /// <summary>
    /// 牌灵变换规则（纯函数，与游戏解耦，可单测）：
    ///   Shift    —— 1 起算的循环偏移，超出范围回绕：8万 +2 → 1万，9万 +2 → 2万
    ///   PairSwap —— 相邻两数互换：1↔2 3↔4 5↔6 7↔8，9→8（只对数牌）
    /// </summary>
    public static class LingRules
    {
        public const int NumberSize = 9;
        public const int FengSize = 4;
        public const int JianSize = 3;

        /// <summary>该类别的牌数量（数牌 9 / 风牌 4 / 三元牌 3）。</summary>
        public static int Size(LingTileKind kind)
        {
            switch (kind)
            {
                case LingTileKind.Number: return NumberSize;
                case LingTileKind.Feng: return FengSize;
                case LingTileKind.Jian: return JianSize;
                default: return 0;
            }
        }

        /// <summary>
        /// 牌灵 → 有符号偏移。游戏里存的是「幅度」（象/鼠都是 2），方向由牌灵种类决定：
        /// 象灵 +2、鼠灵 -2。raw=0 视为未配置，兜底 ±2。
        /// </summary>
        public static int DiffFor(LingKind kind, int raw)
        {
            int magnitude = raw != 0 ? Math.Abs(raw) : 2;
            switch (kind)
            {
                case LingKind.Xiang: return magnitude;
                case LingKind.Shu: return -magnitude;
                default: return 0;
            }
        }

        /// <summary>循环偏移：index ∈ [1, size]，diff 可正可负，超出范围回绕。</summary>
        public static int Shift(int index, int diff, int size)
        {
            if (size <= 0) return index;
            int v = (index - 1 + diff) % size;
            if (v < 0) v += size;
            return v + 1;
        }

        /// <summary>相邻两数互换：1↔2 3↔4 5↔6 7↔8，9→8。</summary>
        public static int PairSwap(int index)
        {
            if (index < 1 || index > NumberSize) return index;
            if (index == NumberSize) return NumberSize - 1; // 9 → 8
            return (index % 2 == 1) ? index + 1 : index - 1;
        }

        /// <summary>按规则映射牌面下标；规则不适用该类别、或下标越界时返回 false。</summary>
        public static bool TryMap(LingTileKind kind, int index, LingRuleKind rule, int diff, out int result)
        {
            result = index;
            int size = Size(kind);
            if (size <= 0) return false;
            if (index < 1 || index > size) return false;
            switch (rule)
            {
                case LingRuleKind.Shift:
                    result = Shift(index, diff, size);
                    return true;
                case LingRuleKind.PairSwap:
                    if (kind != LingTileKind.Number) return false;
                    result = PairSwap(index);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>规则的一句话说明（面板摘要行用）：说明变换方式 + 作用范围。</summary>
        public static string Describe(LingRuleKind rule, int diff)
        {
            switch (rule)
            {
                case LingRuleKind.Shift:
                    return "数字 " + (diff >= 0 ? "+" + diff : diff.ToString())
                        + " 循环（数牌/风牌/三元牌；数牌 " + NumberSize + "→" + Shift(NumberSize, diff, NumberSize) + "）";
                case LingRuleKind.PairSwap:
                    return "相邻两数互换（仅数牌，风牌/三元牌无效）";
                default:
                    return "";
            }
        }
    }
}
