namespace Shared
{
    /// <summary>解析游戏 UI 上的「番数」文本。纯字符串处理，不依赖 Unity/TMP，可单测。</summary>
    public static class FanTextParser
    {
        /// <summary>从可能带富文本的文本里解析一个番数：挑「数字后面紧跟(可隔空白)番」的第一处。
        /// 例：「16番」→16，「&lt;color=#75D962&gt;16番&lt;/color&gt;」→16，「6 番」→6。</summary>
        public static bool TryParseFan(string s, out int v)
        {
            v = 0;
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] < '0' || s[i] > '9') continue;
                int j = i, n = 0;
                while (j < s.Length && s[j] >= '0' && s[j] <= '9') { n = n * 10 + (s[j] - '0'); j++; }
                while (j < s.Length && s[j] == ' ') j++;
                if (j < s.Length && (s[j] == '番' || s[j] == 'ン'))
                {
                    v = n;
                    return true;
                }
                i = j - 1; // for 循环会再 i++；退一格避免漏掉紧邻的下一个候选
            }
            return false;
        }
    }
}
