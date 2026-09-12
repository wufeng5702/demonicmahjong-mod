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
    }
}
