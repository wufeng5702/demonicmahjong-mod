namespace Shared
{
    public static class StringTruncator
    {
        public static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            int i = s.IndexOf('\n');
            return i < 0 ? s : s.Substring(0, i);
        }
    }
}
