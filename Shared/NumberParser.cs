using System.Globalization;
using System.Text;

namespace Shared
{
    public static class NumberParser
    {
        /// <summary>清理 TMP 文本为纯数字字符串（保留数字、逗号、点、负号）。</summary>
        public static string CleanNumber(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder();
            bool inTag = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '<') { inTag = true; continue; }
                if (c == '>') { inTag = false; continue; }
                if (inTag) continue;
                if ((c >= '0' && c <= '9') || c == ',' || c == '.' || c == '-')
                    sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>decimal 格式化：最多 2 位小数，去掉尾零。</summary>
        public static string Fmt(decimal value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        /// <summary>解析游戏显示用数字：先剥富文本标签，去逗号/空格，支持 K/M 缩写（1.5M = 1500000）。
        /// 解析失败或为负返回 false。纯字符串处理，可单测。</summary>
        public static bool ParseDisplayNumber(string text, out decimal value)
        {
            value = 0m;
            if (string.IsNullOrEmpty(text)) return false;
            string raw = text.Trim();
            var plain = new StringBuilder(raw.Length);
            bool inTag = false;
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] == '<') { inTag = true; continue; }
                if (raw[i] == '>') { inTag = false; continue; }
                if (!inTag) plain.Append(raw[i]);
            }
            raw = plain.ToString().Trim();
            raw = raw.Replace(",", "").Replace(" ", "");
            decimal scale = 1m;
            if (raw.EndsWith("M", System.StringComparison.OrdinalIgnoreCase))
            {
                scale = 1000000m;
                raw = raw.Substring(0, raw.Length - 1);
            }
            else if (raw.EndsWith("K", System.StringComparison.OrdinalIgnoreCase))
            {
                scale = 1000m;
                raw = raw.Substring(0, raw.Length - 1);
            }
            if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                value = 0m;
                return false;
            }
            value *= scale;
            if (value < 0m) { value = 0m; return false; }
            return true;
        }
    }
}
