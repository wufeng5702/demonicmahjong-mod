using System;
using System.Collections.Generic;
using System.IO;
using Shared;
using Xunit;

namespace Shared.Tests
{
    public class NumberParserTests
    {
        [Theory]
        [InlineData("1,234", "1,234")]
        [InlineData("<color=#75D962>16番</color>", "16")]
        [InlineData("  500M  ", "500")]
        [InlineData("abc", "")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void CleanNumber_keeps_digits_commas_dot_minus(string input, string expected)
        {
            Assert.Equal(expected, NumberParser.CleanNumber(input));
        }

        [Theory]
        [InlineData(49612, "49612")]
        [InlineData(2.25, "2.25")]
        [InlineData(1.50, "1.5")]
        [InlineData(-3, "-3")]
        public void Fmt_trims_trailing_zeros(decimal value, string expected)
        {
            Assert.Equal(expected, NumberParser.Fmt(value));
        }

        [Theory]
        [InlineData("1,234", 1234)]
        [InlineData("500M", 500000000)]
        [InlineData("1.5K", 1500)]
        [InlineData("<color=#fff>25</color>", 25)]
        [InlineData("0", 0)]
        public void ParseDisplayNumber_handles_separators_and_suffixes(string input, decimal expected)
        {
            Assert.True(NumberParser.ParseDisplayNumber(input, out decimal v));
            Assert.Equal(expected, v);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("abc")]
        [InlineData("-5")]
        [InlineData("<color=#fff>oops</color>")]
        public void ParseDisplayNumber_rejects_unparsable(string input)
        {
            Assert.False(NumberParser.ParseDisplayNumber(input, out decimal v));
            Assert.Equal(0m, v);
        }
    }

    public class FanTextParserTests
    {
        [Theory]
        [InlineData("16番", 16)]
        [InlineData("<color=#75D962>16番</color> [Count=×4]", 16)]
        [InlineData("6 番", 6)]
        [InlineData("番数 12番", 12)]
        public void TryParseFan_reads_number_before_fan(string input, int expected)
        {
            Assert.True(FanTextParser.TryParseFan(input, out int v));
            Assert.Equal(expected, v);
        }

        [Fact]
        public void TryParseFan_survives_number_followed_by_non_fan_text()
        {
            // 回归：旧实现匹配失败后 i=j 会漏掉一个字符，"1 2番" 直接解析失败
            Assert.True(FanTextParser.TryParseFan("1 2番", out int v));
            Assert.Equal(2, v);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("没有番数")]
        [InlineData("12")]
        public void TryParseFan_returns_false_when_absent(string input)
        {
            Assert.False(FanTextParser.TryParseFan(input, out _));
        }
    }

    public class ScoreFormulaTests
    {
        [Fact]
        public void MakeEst_multiplies_base_fan_mul()
        {
            // 150 x 147 x 2.25 = 49,612.5（AGENT.md 里的实测样本）
            Assert.Equal("150 x 147 x 2.25 = 49612.5", ScoreFormula.MakeEst("150", 147, 2.25m));
        }

        [Fact]
        public void MakeEst_expands_display_suffix_before_multiplying()
        {
            // 游戏底分可能显示成 "500M"；乘之前先展开，等式右边才能和结算的完整数字对齐
            Assert.Equal("500000000 x 4 x 2 = 4000000000", ScoreFormula.MakeEst("500M", 4, 2));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void MakeEst_shows_placeholder_when_no_fan(decimal fan)
        {
            Assert.Equal("--", ScoreFormula.MakeEst("150", fan, 2.25m));
        }

        [Fact]
        public void MakeEst_flags_unreadable_base()
        {
            Assert.Equal("base? x 16 x 2.25", ScoreFormula.MakeEst("", 16, 2.25m));
        }
    }

    public class UiTextTests
    {
        [Theory]
        [InlineData("【继续】", "继续")]
        [InlineData("  继续  ", "继续")]
        [InlineData("【点击继续】", "点击继续")]
        public void Normalize_strips_decorations(string input, string expected)
        {
            Assert.Equal(expected, UiText.Normalize(input));
        }

        [Fact]
        public void Normalize_drops_complete_rich_text_tags()
        {
            // 回归：旧实现只删 '<'，"color=#fff>继续" 残留的 '>' 导致精确匹配失败
            Assert.Equal("继续", UiText.Normalize("<color=#fff>继续</color>"));
            Assert.Equal("继续", UiText.Normalize("<b>继</b><i>续</i>"));
        }

        [Fact]
        public void Normalize_handles_null()
        {
            Assert.Equal("", UiText.Normalize(null));
        }
    }

    public class StringTruncatorTests
    {
        [Theory]
        [InlineData(null, "")]
        [InlineData("", "")]
        [InlineData("one", "one")]
        [InlineData("first\nsecond", "first")]
        public void FirstLine_cuts_at_newline(string input, string expected)
        {
            Assert.Equal(expected, StringTruncator.FirstLine(input));
        }
    }

    public class YamlConfigTests : IDisposable
    {
        private readonly string _dir;

        public YamlConfigTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "shared-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private static Dictionary<string, string> Defaults()
        {
            return new Dictionary<string, string>
            {
                ["enabled"] = "true",
                ["fontsize"] = "24"
            };
        }

        [Fact]
        public void Load_creates_default_file_when_missing()
        {
            var cfg = YamlConfig.Load("Mod.yml", Defaults(), _dir);

            Assert.True(File.Exists(Path.Combine(_dir, "Mod.yml")));
            Assert.Equal("true", cfg["enabled"]);
            Assert.Equal("24", cfg["fontsize"]);
        }

        [Fact]
        public void Load_reads_values_and_keeps_defaults_for_missing_keys()
        {
            File.WriteAllText(Path.Combine(_dir, "Mod.yml"),
                "# 注释\nenabled: false\n\n  fontsize :   48  \n");

            var cfg = YamlConfig.Load("Mod.yml", Defaults(), _dir);

            Assert.Equal("false", cfg["enabled"]);
            Assert.Equal("48", cfg["fontsize"]);
        }

        [Fact]
        public void Load_is_case_insensitive_on_keys()
        {
            File.WriteAllText(Path.Combine(_dir, "Mod.yml"), "Enabled: false\n");

            var cfg = YamlConfig.Load("Mod.yml", Defaults(), _dir);

            Assert.Equal("false", cfg["enabled"]);
        }

        [Fact]
        public void Load_ignores_malformed_lines()
        {
            File.WriteAllText(Path.Combine(_dir, "Mod.yml"),
                "no-colon-here\n\n# only a comment\nfontsize: 12\n");

            var cfg = YamlConfig.Load("Mod.yml", Defaults(), _dir);

            Assert.Equal("12", cfg["fontsize"]);
            Assert.Equal("true", cfg["enabled"]);
        }
    }
}
