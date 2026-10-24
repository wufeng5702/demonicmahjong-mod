using Shared;
using Xunit;

namespace Shared.Tests
{
    public class LingRulesTests
    {
        [Theory]
        // 象灵 +2：8万→1万、9万→2万
        [InlineData(8, 2, 9, 1)]
        [InlineData(9, 2, 9, 2)]
        [InlineData(1, 2, 9, 3)]
        // 鼠灵 -2：1→8、2→9
        [InlineData(1, -2, 9, 8)]
        [InlineData(2, -2, 9, 9)]
        // 风牌 +2：东→西、北→南（风序 东南西北）
        [InlineData(1, 2, 4, 3)]
        [InlineData(4, 2, 4, 2)]
        [InlineData(2, -2, 4, 4)]
        // 三元牌 +2：中→发、白→中、发→白（三元序 中白发）
        [InlineData(1, 2, 3, 3)]
        [InlineData(2, 2, 3, 1)]
        [InlineData(3, 2, 3, 2)]
        public void Shift_wraps_around(int index, int diff, int size, int expected)
        {
            Assert.Equal(expected, LingRules.Shift(index, diff, size));
        }

        [Theory]
        [InlineData(1, 2)]
        [InlineData(2, 1)]
        [InlineData(3, 4)]
        [InlineData(4, 3)]
        [InlineData(5, 6)]
        [InlineData(6, 5)]
        [InlineData(7, 8)]
        [InlineData(8, 7)]
        [InlineData(9, 8)]
        public void PairSwap_swaps_adjacent_numbers(int index, int expected)
        {
            Assert.Equal(expected, LingRules.PairSwap(index));
        }

        [Fact]
        public void TryMap_shift_applies_to_all_categories()
        {
            Assert.True(LingRules.TryMap(LingTileKind.Number, 9, LingRuleKind.Shift, 2, out int num));
            Assert.Equal(2, num);

            Assert.True(LingRules.TryMap(LingTileKind.Feng, 1, LingRuleKind.Shift, 2, out int feng));
            Assert.Equal(3, feng);

            Assert.True(LingRules.TryMap(LingTileKind.Jian, 1, LingRuleKind.Shift, 2, out int jian));
            Assert.Equal(3, jian);
        }

        [Fact]
        public void TryMap_pairswap_rejects_honors()
        {
            // 马灵对风牌/三元牌无效
            Assert.False(LingRules.TryMap(LingTileKind.Feng, 1, LingRuleKind.PairSwap, 0, out int feng));
            Assert.Equal(1, feng);

            Assert.False(LingRules.TryMap(LingTileKind.Jian, 1, LingRuleKind.PairSwap, 0, out int jian));
            Assert.Equal(1, jian);

            Assert.True(LingRules.TryMap(LingTileKind.Number, 5, LingRuleKind.PairSwap, 0, out int num));
            Assert.Equal(6, num);
        }

        [Fact]
        public void TryMap_none_keeps_tile()
        {
            Assert.False(LingRules.TryMap(LingTileKind.Number, 5, LingRuleKind.None, 0, out int num));
            Assert.Equal(5, num);
        }

        [Fact]
        public void Size_matches_tile_counts()
        {
            Assert.Equal(9, LingRules.Size(LingTileKind.Number));
            Assert.Equal(4, LingRules.Size(LingTileKind.Feng));
            Assert.Equal(3, LingRules.Size(LingTileKind.Jian));
        }

        [Fact]
        public void Describe_states_effect_and_scope()
        {
            // 摘要行必须自洽：brief 模式下只看这一行就要知道「怎么变 + 对哪类牌生效」
            Assert.Equal("数字 +2 循环（数牌/风牌/三元牌；数牌 9→2）",
                LingRules.Describe(LingRuleKind.Shift, 2));
            Assert.Equal("数字 -2 循环（数牌/风牌/三元牌；数牌 9→7）",
                LingRules.Describe(LingRuleKind.Shift, -2));
            Assert.Equal("相邻两数互换（仅数牌，风牌/三元牌无效）",
                LingRules.Describe(LingRuleKind.PairSwap, 0));
            Assert.Equal("", LingRules.Describe(LingRuleKind.None, 0));
        }

        [Theory]
        // 游戏里存的是幅度（象/鼠都是 2），方向由牌灵种类决定
        [InlineData(LingKind.Xiang, 2, 2)]
        [InlineData(LingKind.Xiang, -2, 2)]   // 存储约定不同也取绝对值
        [InlineData(LingKind.Shu, 2, -2)]
        [InlineData(LingKind.Shu, -2, -2)]
        [InlineData(LingKind.Xiang, 0, 2)]    // 未配置 → 兜底 +2
        [InlineData(LingKind.Shu, 0, -2)]     // 未配置 → 兜底 -2
        [InlineData(LingKind.Other, 2, 0)]
        public void DiffFor_uses_magnitude_and_direction_of_spirit(LingKind kind, int raw, int expected)
        {
            Assert.Equal(expected, LingRules.DiffFor(kind, raw));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(10)]
        public void TryMap_rejects_out_of_range_index(int index)
        {
            // 畸形牌面（number=0 / Feng.Any=5）不能漏出负数或错牌
            Assert.False(LingRules.TryMap(LingTileKind.Number, index, LingRuleKind.Shift, 2, out _));
            Assert.False(LingRules.TryMap(LingTileKind.Number, index, LingRuleKind.PairSwap, 0, out _));
            Assert.False(LingRules.TryMap(LingTileKind.Feng, index, LingRuleKind.Shift, 2, out _));
            Assert.False(LingRules.TryMap(LingTileKind.Jian, index, LingRuleKind.Shift, 2, out _));
        }

        [Fact]
        public void Shift_and_PairSwap_are_safe_on_bad_input()
        {
            Assert.Equal(3, LingRules.Shift(3, 2, 0));      // size=0 不再抛除零
            Assert.Equal(0, LingRules.PairSwap(0));         // 越界原样返回
            Assert.Equal(10, LingRules.PairSwap(10));
        }

        [Fact]
        public void Shift_wraps_on_honor_sizes()
        {
            // 三元牌长度 3：鼠灵 发财(3)→红中(1)、红中(1)→白板(2)、白板(2)→发财(3)
            Assert.Equal(1, LingRules.Shift(3, -2, 3));
            Assert.Equal(2, LingRules.Shift(1, -2, 3));
            Assert.Equal(3, LingRules.Shift(2, -2, 3));
            // 风牌长度 4：鼠灵 东(1)→西(3)、南(2)→北(4)、西(3)→东(1)、北(4)→南(2)
            Assert.Equal(3, LingRules.Shift(1, -2, 4));
            Assert.Equal(4, LingRules.Shift(2, -2, 4));
            Assert.Equal(1, LingRules.Shift(3, -2, 4));
            Assert.Equal(2, LingRules.Shift(4, -2, 4));
        }
    }
}
