using System;
using System.Globalization;
using System.Runtime.InteropServices;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppSystem.Collections.Generic;
using MaJiang.DataConstruct.MaJiang;
using MaJiang.PlayMaJiang;
using MaJiang.PlayMaJiang.Buff.BuffPayloads;
using MaJiang.PlayMaJiang.Player;
using MaJiang.PlayMaJiang.RoundStatistics;
using UnityEngine;
using II = Il2CppSystem.Collections.Generic;

namespace ScorePreview
{
    public struct Prediction
    {
        public decimal BaseScore;
        public decimal MinFan;
        public decimal Mul;
        public Prediction[] TopScores;
    }

    /// <summary>Harmony 钩子写入的最新听牌预测快照。</summary>
    public static class TingSnap
    {
        public static bool Has;
        public static Prediction Cur;
        public static int Calls;
        public static int Hits;
        public static string LastErr;
    }

    /// <summary>
    /// 对「玩家听牌可胡结果」字典做预测计算：
    /// 每张可胡牌的各拆解复用游戏结算 GetTotalScore(...) 得到 (底分,番数,倍率,总分)，
    /// 全手取番数最小者，显示 底分 × 番数 × 倍率 = 乘积。
    /// </summary>
    public static class Comp
    {
        private static readonly Il2CppSystem.Collections.Generic.List<FloatBuffPayload> Empty =
            new Il2CppSystem.Collections.Generic.List<FloatBuffPayload>();

        /// <summary>从 PRS.BuffList 的三类 buff 里取当前实际 payload 列表（类别 0=底分 1=番 2=倍率）。失败返回空表。</summary>
        private static Il2CppSystem.Collections.Generic.List<FloatBuffPayload> RealBuffList(PlayerRoundStatistics prs, int which)
        {
            var outList = new Il2CppSystem.Collections.Generic.List<FloatBuffPayload>();
            try
            {
                var bl = prs.BuffList;
                if (bl == null) return outList;
                var stacked = which == 0 ? bl._baseScoreBuff
                           : which == 1 ? bl._fanBuff
                           : (object)bl._independentBuff;
                if (stacked == null) return outList;
                var stackedBase = stacked as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
                if (stackedBase == null) return outList;
                var stackedIf = stackedBase.Cast<MaJiang.PlayMaJiang.Buff.IStackedBuff>();
                if (stackedIf == null) return outList;
                var payloads = stackedIf.Payloads;
                if (payloads == null) return outList;
                var list = payloads.Cast<Il2CppSystem.Collections.Generic.List<BuffPayload>>();
                int n = list.Count;
                for (int i = 0; i < n; i++)
                {
                    var o = list[i];
                    if (o == null) continue;
                    try { outList.Add(o.Cast<FloatBuffPayload>()); }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
            return outList;
        }

        public static bool TryGetBuffStats(PlayerRoundStatistics prs,
            out decimal baseScore, out decimal fan, out decimal multiplier)
        {
            baseScore = 0m;
            fan = 0m;
            multiplier = 1m;
            try
            {
                var bl = prs != null ? prs.BuffList : null;
                if (bl == null) return false;
                var b1 = RealBuffList(prs, 0);
                var b2 = RealBuffList(prs, 1);
                var b3 = RealBuffList(prs, 2);
                var stats = bl.Get3Statistics(
                    b1.Cast<II.IEnumerable<FloatBuffPayload>>(),
                    b2.Cast<II.IEnumerable<FloatBuffPayload>>(),
                    b3.Cast<II.IEnumerable<FloatBuffPayload>>());
                baseScore = D(stats.Item1);
                fan = D(stats.Item2);
                multiplier = D(stats.Item3);
                return multiplier > 0m;
            }
            catch (Exception e)
            {
                Diag("buff stats failed: " + First(e.ToString()));
                return false;
            }
        }

        public static Prediction? Try(IReadOnlyDictionary<PaiMianPayload, IReadOnlyList<HuResult>> raw, ref string failMsg)
        {
            failMsg = "";
            if (raw == null) { failMsg = "raw=null"; return null; }
            var prs = PlayerRoundStatistics.Instance;
            if (prs == null) { failMsg = "prs=null"; return null; }

            string tn = raw.ToString();
            failMsg = "raw=" + tn + " type=" + raw.GetType().FullName;

            // 运行时实例可能是 Dictionary<K,List<V>> 或 Dictionary<K,IReadOnlyList<V>>，
            // 两种都试；读字段走的都是同一类型定义的偏移（value 槽均为引用）。
            Dictionary<PaiMianPayload, IReadOnlyList<HuResult>> d1;
            Dictionary<PaiMianPayload, List<HuResult>> d2;
            try { d1 = raw.Cast<Dictionary<PaiMianPayload, IReadOnlyList<HuResult>>>(); d2 = null; }
            catch (InvalidCastException e1)
            {
                d1 = null;
                try { d2 = raw.Cast<Dictionary<PaiMianPayload, List<HuResult>>>(); }
                catch (InvalidCastException) { failMsg = "cast fail: " + First(e1.Message); return null; }
            }

            bool found = false;
            decimal baseScore = 0m, minFan = 0m, mul = 0m;
            var candidates = new System.Collections.Generic.List<Prediction>();

            if (d1 != null)
            {
                int n = d1.Count;
                failMsg = "d1 n=" + n + " raw=" + tn;
                for (int i = 0; i < n; i++)
                    EvalHand((object)d1._entries[i].value, prs, candidates,
                        ref found, ref baseScore, ref minFan, ref mul, ref failMsg);
            }
            else if (d2 != null)
            {
                int n = d2.Count;
                failMsg = "d2 n=" + n + " raw=" + tn;
                for (int i = 0; i < n; i++)
                    EvalHand((object)d2._entries[i].value, prs, candidates,
                        ref found, ref baseScore, ref minFan, ref mul, ref failMsg);
            }
            else return null;

            if (!found) { failMsg = "no hand fan | " + failMsg; return null; }

            candidates.Sort((x, y) => (x.MinFan * x.Mul).CompareTo(y.MinFan * y.Mul));
            int topCount = Math.Min(3, candidates.Count);
            var top = new Prediction[topCount];
            for (int i = 0; i < topCount; i++) top[i] = candidates[i];

            return new Prediction
            {
                BaseScore = baseScore,
                MinFan = minFan,
                Mul = mul,
                TopScores = top
            };
        }

        private static void EvalHand(object valueObj, PlayerRoundStatistics prs,
            System.Collections.Generic.List<Prediction> candidates,
            ref bool found, ref decimal baseScore, ref decimal minFan, ref decimal mul,
            ref string failMsg)
        {
            if (valueObj == null) { failMsg = "val=null"; return; }
            var raw = valueObj as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
            if (raw == null) { failMsg = "val-not-interop"; return; }

            Il2CppSystem.Collections.Generic.List<HuResult> results;
            try { results = raw.Cast<Il2CppSystem.Collections.Generic.List<HuResult>>(); }
            catch (InvalidCastException e) { failMsg = "val-cast-fail " + First(e.Message); return; }

            for (int j = 0; j < results.Count; j++)
            {
                var hu = results[j];

                var fansRaw = hu.FanZhongs as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
                if (fansRaw == null) { failMsg = "fans=null j=" + j; continue; }

                var inner = new Il2CppSystem.Collections.Generic.List<FanZhong>();

                Il2CppSystem.Collections.Generic.List<FanZhong> fans = null;
                try { fans = fansRaw.Cast<Il2CppSystem.Collections.Generic.List<FanZhong>>(); }
                catch (InvalidCastException) { }
                if (fans != null)
                {
                    for (int k = 0; k < fans.Count; k++)
                        inner.Add(fans[k]);
                    Diag("fans=list count=" + fans.Count);
                }
                else
                {
                    HashSet<FanZhong> set;
                    try { set = fansRaw.Cast<HashSet<FanZhong>>(); }
                    catch (InvalidCastException e) { failMsg = "fans-cast-fail " + First(e.Message); continue; }
                    if (set == null) { failMsg = "fans=null-set"; continue; }
                    int setCount = set._count;
                    int bucketsLen = set._buckets != null ? set._buckets.Length : -1;
                    int slotsLen = set._slots != null ? set._slots.Length : -1;
                    FillFromSet(set, inner);
                    Diag("fans=set count=" + setCount + " buckets=" + bucketsLen + " slots=" + slotsLen
                        + " filled=" + inner.Count);
                    DumpSetOnce(set);
                }

                long fanSum = FanSum(prs, inner);
                Diag("fanSum(" + j + ")=" + fanSum + " inner=" + inner.Count + FanIds(inner));

                // 外层容器元素类型用接口 IEnumerable<FanZhong>，使其原生实现的
                // IEnumerable<T> 与 GetTotalScore 参数 IEnumerable<IEnumerable<FanZhong>>
                // 的 T 完全一致；所有接口转换走原生 Cast（托管 cast 对 interop 门面无效）。
                var outer = new Il2CppSystem.Collections.Generic.List<II.IEnumerable<FanZhong>>();
                outer.Add(inner.Cast<II.IEnumerable<FanZhong>>());

                var b1 = RealBuffList(prs, 0);
                var b2 = RealBuffList(prs, 1);
                var b3 = RealBuffList(prs, 2);
                Diag("buffs: base=" + b1.Count + " fan=" + b2.Count + " indep=" + b3.Count);

                var t = prs.GetTotalScore(
                    outer.Cast<II.IEnumerable<II.IEnumerable<FanZhong>>>(),
                    b1.Cast<II.IReadOnlyList<FloatBuffPayload>>(),
                    b2.Cast<II.IReadOnlyList<FloatBuffPayload>>(),
                    b3.Cast<II.IReadOnlyList<FloatBuffPayload>>());

                decimal b, f, m;
                var wins = ReadTupleWins(t);
                if (wins != null)
                {
                    // 原生内存窗口已实证：+16D=番数(fanSum 精确匹配)、+32D=倍率。
                    b = wins[0];
                    f = wins[1];
                    m = wins[2];
                }
                else
                {
                    b = D(t.Item1);
                    f = D(t.Item2);
                    m = D(t.Item3);
                }

                if (!found || f < minFan)
                {
                    baseScore = b;
                    minFan = f;
                    mul = m;
                    found = true;
                }
                if (f > 0 && m > 0)
                {
                    bool duplicate = false;
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        if (candidates[i].MinFan == f && candidates[i].Mul == m)
                        {
                            duplicate = true;
                            break;
                        }
                    }
                    if (!duplicate)
                        candidates.Add(new Prediction { BaseScore = b, MinFan = f, Mul = m });
                }

                Diag("score: fans=" + inner.Count + " f=" + F(f) + " m=" + F(m)
                    + " raw=" + t.Item1.ToString() + "/" + t.Item2.ToString() + "/"
                    + t.Item3.ToString() + "/" + t.Item4.ToString());
                DumpTuple(t, inner.Count, f, m);
            }
        }

        internal static void Diag(string msg)
        {
            ScoreHud.Log?.LogInfo("Diag: " + msg);
        }

        private static long FanSum(PlayerRoundStatistics prs, Il2CppSystem.Collections.Generic.List<FanZhong> inner)
        {
            long sum = 0;  // 结算小番 = payload.number 之和（FanZhongCtr 列表文案已验证：箭刻2+风刻2+全带幺4+...=FanNum）
            long big = 0;  // payload.fan 大类（调试）
            var sb = new System.Text.StringBuilder("fanmap");
            var plist = prs._fanZhongPayloadList;
            if (plist == null) return sum;
            var arr = plist.value;
            if (arr == null) return sum;
            for (int a = 0; a < arr.Length; a++)
            {
                var payload = arr[a];
                if (payload == null) continue;
                for (int k = 0; k < inner.Count; k++)
                {
                    if ((int)payload.id == (int)inner[k])
                    {
                        sum += payload.number;
                        big += payload.fan;
                        sb.Append(" id=").Append((int)payload.id)
                          .Append("(num=").Append(payload.number)
                          .Append(",fan=").Append(payload.fan).Append(")");
                        break;
                    }
                }
            }
            if (sb.Length > 7) Diag(sb.ToString() + " small=" + sum + " big=" + big);
            return sum;
        }

        /// <summary>枚举 Fans 值（inner 从集合/列表读出后）。</summary>
        private static string FanIds(Il2CppSystem.Collections.Generic.List<FanZhong> inner)
        {
            var sb = new System.Text.StringBuilder("[");
            for (int i = 0; i < inner.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append((int)inner[i]);
            }
            sb.Append("]");
            return sb.ToString();
        }

        private static string Hex(int v) => "0x" + v.ToString("X8");

        private static void DumpTuple(object t, int fans, decimal f, decimal m)
        {
            var tobj = t as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
            if (tobj == null)
            {
                Diag("tuple is not Il2CppObjectBase: " + (t == null ? "null" : t.GetType().FullName));
                return;
            }
            long baseOff = tobj.Pointer.ToInt64() + 0x10;
            try
            {
                var sb = new System.Text.StringBuilder("tuple @0x" + baseOff.ToString("X") + " win16-any:");
                for (int i = 0; i < 9; i++)
                {
                    var p = new IntPtr(baseOff + 16L * i);
                    int w0 = Marshal.ReadInt32(p, 0);
                    int w1 = Marshal.ReadInt32(p, 4);
                    int w2 = Marshal.ReadInt32(p, 8);
                    int w3 = Marshal.ReadInt32(p, 12);
                    decimal v = TryDecode(w2, w3, w1, w0);
                    sb.Append(" +" + (i * 16) + "D=" + Hex(w0) + " " + Hex(w1) + " " + Hex(w2) + " " + Hex(w3)
                        + "=" + Kw(v));
                    if (i < 8) sb.Append(" | ");
                }
                sb.Append(" picked=(f=" + F(f) + ",m=" + F(m) + ")");
                Diag(sb.ToString());
            }
            catch (Exception e) { Diag("tuple read failed: " + FirstLine(e.ToString())); }
        }

        /// <summary>原生内存读 ValueTuple4 的 4 个 Decimal（16 字节 stride；item0=145 恒值垃圾，item1=番数、item2=倍率 可用）。</summary>
        private static decimal[] ReadTupleWins(object t)
        {
            var tobj = t as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
            if (tobj == null) return null;
            long baseOff = tobj.Pointer.ToInt64() + 0x10;
            var wins = new decimal[4];
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    var p = new IntPtr(baseOff + 16L * i);
                    int w0 = Marshal.ReadInt32(p, 0);
                    int w1 = Marshal.ReadInt32(p, 4);
                    int w2 = Marshal.ReadInt32(p, 8);
                    int w3 = Marshal.ReadInt32(p, 12);
                    wins[i] = TryDecode(w2, w3, w1, w0);
                }
                return wins;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void DumpSetOnce(HashSet<FanZhong> set)
        {
            if (_setDumped) return;
            _setDumped = true;
            try
            {
                var buckets = set._buckets;
                var slots = set._slots;
                var sb = new System.Text.StringBuilder("set dump: buckets[");
                for (int i = 0; i < buckets.Length; i++) sb.Append(buckets[i] + ",");
                sb.Append("] slots[");
                for (int i = 0; i < slots.Length; i++)
                    sb.Append("{h=" + slots[i].hashCode + " v=" + slots[i].value + " n=" + slots[i].next + "},");
                sb.Append("]");
                Diag(sb.ToString());
            }
            catch (Exception e) { Diag("set dump failed: " + FirstLine(e.ToString())); }
        }

        private static bool _setDumped;

        private static decimal TryDecode(int lo, int mid, int hi, int flags)
        {
            try
            {
                byte scale = (byte)((flags >> 16) & 0x7F);
                if (scale > 28) return -1m;
                return new decimal(lo, mid, hi, flags < 0, scale);
            }
            catch { return -1m; }
        }

        private static string Kw(decimal d) => d == -1m ? "inv" : F(d);

        /// <summary>
        /// 将 Il2CppSystem.Decimal 转成 System.Decimal。
        /// interop 的 lo/mid/hi/flags 字段偏移不可靠（读取得到脏数据），
        /// 所以优先走与布局无关的原生访问器；最后才做字段排列探测。
        /// </summary>
        private static decimal D(Il2CppSystem.Decimal d)
        {
            try
            {
                int scale = d.Scale;
                if (scale >= 0 && scale <= 28)
                {
                    bool neg = d.IsNegative;
                    int lo = unchecked((int)d.Low);
                    int mid = unchecked((int)d.Mid);
                    int hi = unchecked((int)d.High);
                    var v = new decimal(lo, mid, hi, neg, (byte)scale);
                    LogDRoute("props", d);
                    return v;
                }
            }
            catch (Exception) { }

            try
            {
                string s = d.ToString();
                if (s != null && s.Length > 0)
                {
                    var v = decimal.Parse(s, NumberStyles.Number, CultureInfo.InvariantCulture);
                    LogDRoute("tostr", d);
                    return v;
                }
            }
            catch (Exception) { }

            int fa = d.flags, b = d.hi, c = d.lo, e = d.mid;
            int[,] perms = new int[,]
            {
                { c, e, fa, b },
                { c, b, e, fa },
                { fa, e, c, b },
                { fa, b, c, e },
                { e, c, b, fa },
                { b, c, e, fa },
                { c, b, fa, e },
                { c, e, b, fa },
                { e, fa, c, b },
                { fa, c, e, b },
                { b, e, fa, c },
                { e, b, fa, c },
            };
            for (int k = 0; k < perms.GetLength(0); k++)
            {
                int l = perms[k, 0], m = perms[k, 1], h = perms[k, 2], f = perms[k, 3];
                int scale = (f >> 16) & 0x7F;
                if (scale < 0 || scale > 28) continue;
                try
                {
                    var v = new decimal(l, m, h, f < 0, (byte)scale);
                    LogDRoute("perm" + k, d);
                    return v;
                }
                catch (Exception) { }
            }

            throw new InvalidOperationException("Decimal undecodable lo=" + c + " mid=" + e + " hi=" + b + " flags=" + fa);
        }

        private static string _dRoute;
        private static void LogDRoute(string route, Il2CppSystem.Decimal d)
        {
            if (_dRoute != route)
            {
                _dRoute = route;
                ScoreHud.Log?.LogInfo("Decimal route: " + route
                    + " (raw flags=" + d.flags + " hi=" + d.hi + " lo=" + d.lo + " mid=" + d.mid
                    + ") tostr='" + d.ToString() + "'");
            }
        }

        private static string F(decimal value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string First(string s)
        {
            int i = s.IndexOf('\n');
            return i < 0 ? s : s.Substring(0, i);
        }

        private static string FirstLine(string s) => First(s);

        private static void FillFromSet(HashSet<FanZhong> set, Il2CppSystem.Collections.Generic.List<FanZhong> inner)
        {
            var buckets = set._buckets;
            var slots = set._slots;
            if (buckets == null || slots == null || buckets.Length == 0) return;
            int n = set._count;
            if (n < 0 || n > slots.Length) return;

            DumpSlotsRaw(set);

            // interop 的 Slot.value 读到脏数据 → 原生内存扫 (base,stride,valOff)：
            // 选让读出的 n 个值全部落在合法枚举区间且尽量多样的一组。
            long Pl = slots.Pointer.ToInt64();
            long[] bases = { 0x10, 0x18 };
            int[] strides = { 12, 16 };
            long bestScore = -1; int bestB = 0, bestS = 12, bestO = 8;
            for (int bi = 0; bi < 2; bi++)
                for (int si = 0; si < 2; si++)
                    for (int oi = 0; oi < 4; oi++)
                    {
                        int voff = oi * 4;
                        long score = 0; bool bad = false;
                        for (int i = 0; i < n; i++)
                        {
                            int v;
                            try { v = Marshal.ReadInt32(new IntPtr(Pl + bases[bi] + (long)i * strides[si] + voff)); }
                            catch (Exception) { bad = true; break; }
                            if (v < 0 || v > 1024) { bad = true; break; }
                            score += v;
                        }
                        if (bad) continue;
                        if (score > bestScore || (score == bestScore && strides[si] > bestS)) { bestScore = score; bestB = bi; bestS = strides[si]; bestO = voff; }
                    }
            if (bestScore >= 0 && n > 0)
            {
                var sb = new System.Text.StringBuilder("slotcfg base=");
                sb.Append(bases[bestB].ToString("X")).Append(" stride=").Append(bestS).Append(" valOff=").Append(bestO).Append(" vals=[");
                for (int i = 0; i < n; i++)
                {
                    int v;
                    try { v = Marshal.ReadInt32(new IntPtr(Pl + bases[bestB] + (long)i * bestS + bestO)); }
                    catch (Exception) { break; }
                    if (i > 0) sb.Append(",");
                    sb.Append(i).Append(":").Append(v);
                    inner.Add((FanZhong)v);
                }
                sb.Append("]");
                Diag(sb.ToString());
            }
            if (inner.Count == 0 || inner.Count < n)
            {
                int stride = bestS;
                long p = Pl + 0x10;
                for (int i = 0; i < n && inner.Count < n; i++)
                {
                    int v = -1;
                    if (stride >= 12)
                    {
                        try { v = Marshal.ReadInt32(new IntPtr(p + (long)i * stride + 8)); } catch (Exception) { }
                    }
                    if (v < 0)
                        inner.Add(slots[i].value);
                    else
                        inner.Add((FanZhong)v);
                }
            }
        }

        private static void DumpSlotsRaw(HashSet<FanZhong> set)
        {
            try
            {
                var slots = set._slots;
                if (slots == null) return;
                var sb = new System.Text.StringBuilder("slots raw: ");
                long p = slots.Pointer.ToInt64() + 0x10;
                for (int i = 0; i < Math.Min(set._count, 6); i++)
                {
                    sb.Append("[s").Append(i).Append(" h=").Append(slots[i].hashCode);
                    for (int k = 0; k < 4; k++)
                    {
                        int w = 0;
                        try { w = Marshal.ReadInt32(new IntPtr(p + (long)i * 16 + k * 4)); } catch (Exception) { }
                        sb.Append(" +").Append(k * 4).Append("=").Append(w);
                    }
                    sb.Append("] ");
                }
                Diag(sb.ToString());
            }
            catch (Exception e) { Diag("slots raw failed: " + FirstLine(e.ToString())); }
        }
    }

    /// <summary>
    /// Harmony 钩子：PlayerPipeline.OnProcessTingResult —— 每次听牌/计分重算结果都会经过这里，
    /// 参数就是当前的「可胡结果字典」，在这里同步算好预测快照。
    /// </summary>
    [HarmonyPatch(typeof(PlayerPipeline), nameof(PlayerPipeline.OnProcessTingResult))]
    internal static class TingHookPatch
    {
        private static void Prefix(IReadOnlyDictionary<PaiMianPayload, IReadOnlyList<HuResult>> huResults)
        {
            TingSnap.Calls++;
            try
            {
                string fail = "";
                var p = Comp.Try(huResults, ref fail);
                if (p.HasValue)
                {
                    TingSnap.Has = true;
                    TingSnap.Cur = p.Value;
                    TingSnap.Hits++;
                    TingSnap.LastErr = null;
                    if (ScoreHud.Log != null)
                        ScoreHud.Log.LogInfo("ting hook #" + TingSnap.Hits + " -> fan=" + p.Value.MinFan
                            + " mul=" + p.Value.Mul.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    TingSnap.LastErr = fail;
                    if (ScoreHud.Log != null)
                        ScoreHud.Log.LogInfo("ting hook -> none: " + fail);
                }
            }
            catch (Exception e)
            {
                TingSnap.LastErr = "ex: " + e;
            }
        }
    }
}