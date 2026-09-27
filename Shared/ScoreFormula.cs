namespace Shared
{
    /// <summary>预计分展示：底分 × 番数 × 倍率。纯字符串/数值处理，可单测。</summary>
    public static class ScoreFormula
    {
        /// <summary>底分 × 番数 × 倍率 = 预计分。番数 &lt;= 0 显示 "--"，底分读不到显示 "base?"。
        /// 标签由调用方拼，这里不带前缀。</summary>
        public static string MakeEst(string baseS, decimal fan, decimal mul)
        {
            if (fan <= 0) return "--";
            if (NumberParser.ParseDisplayNumber(baseS, out decimal b))
            {
                string total = NumberParser.Fmt(b * fan * mul);
                return NumberParser.Fmt(b) + " x " + NumberParser.Fmt(fan) + " x " + NumberParser.Fmt(mul) + " = " + total;
            }
            return "base? x " + NumberParser.Fmt(fan) + " x " + NumberParser.Fmt(mul);
        }
    }
}
