using System.Text;

namespace Shared
{
    /// <summary>按钮/UI 文本归一化：剥富文本标签、去空白与装饰括号，得到可比对的纯文本。可单测。</summary>
    public static class UiText
    {
        /// <summary>把「【继续】」「&lt;color=#fff&gt;继续&lt;/color&gt;」「  继续  」统一成「继续」。
        /// 标签按成对 &lt;…&gt; 整体剥离（原实现只删 '&lt;' 会残留 '&gt;' 导致精确匹配失败）。</summary>
        public static string Normalize(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length);
            bool inTag = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '<') { inTag = true; continue; }
                if (c == '>') { inTag = false; continue; }
                if (inTag) continue;
                if (c == ' ' || c == '\n' || c == '\t' || c == '\r'
                    || c == '【' || c == '】' || c == '『' || c == '』')
                    continue;
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
