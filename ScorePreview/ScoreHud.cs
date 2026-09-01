using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using MaJiang.PlayMaJiang.Player;
using MaJiang.PlayMaJiang.RoundStatistics;
using UnityEngine;

namespace ScorePreview
{
    /// <summary>
    /// IMGUI 悬浮面板。显示预计得分（底分 x 番数 x 倍率）。
    /// 优先读取游戏自己的计分预览面板 PlayerHuPanel 的文本（权威，尚未解析格式前先原样显示），
    /// 其次用 Harmony 钩子（PlayerPipeline.OnProcessTingResult）算好的听牌预测，
    /// 兜底尝试读手牌 CanHu。
    /// </summary>
    public class ScoreHud : MonoBehaviour
    {
        internal static BepInEx.Logging.ManualLogSource Log;

        private GUIStyle _style;
        private string _text = "计分: --\n和牌1: --\n和牌2: --\n和牌3: --";
        private string _lastLogged;
        private string _lastDiagLogged;
        private string _lastError;
        private int _pollCount;

        private PlayerHandPaiMianContainer _hand;
        private PlayerHuPanel _panel;
        private float _nextPoll;
        private float _nextPanelSearch;
        private string _liveBase = "";
        private int _yOffset;

        private void Awake()
        {
            _style = new GUIStyle();
            _style.fontSize = 24;
            _style.normal.textColor = Color.white;
            _style.padding = new RectOffset(10, 10, 6, 6);

            var bg = new Texture2D(1, 1);
            bg.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.6f));
            bg.Apply();
            _style.normal.background = bg;

            _yOffset = LoadYOffset();
            Log?.LogInfo("ScoreHud active (mirror PlayerHuPanel + ting hook), yoffset=" + _yOffset);
        }

        /// <summary>读 HUD 下移值：dll 同目录 ScorePreview.yml（改后重启生效），如
        ///   yoffset: 0.1    # 屏幕高度的比例，1.0=100%
        /// 无文件/无字段默认 0.1（10%）。返回最终像素偏移。</summary>
        private static int LoadYOffset()
        {
            float p = 0.10f;
            try
            {
                var dir = System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                var f = System.IO.Path.Combine(dir, "ScorePreview.yml");
                if (System.IO.File.Exists(f))
                {
                    string[] lines = System.IO.File.ReadAllLines(f);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string t = lines[i].Trim();
                        if (t.Length == 0 || t.StartsWith("#")) continue;
                        int ci = t.IndexOf(':');
                        if (ci < 0) continue;
                        string k = t.Substring(0, ci).Trim();
                        string v = t.Substring(ci + 1).Trim();
                        if (string.Equals(k, "yoffset", StringComparison.OrdinalIgnoreCase)
                            && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float q))
                        {
                            p = q;
                            break;
                        }
                    }
                }
            }
            catch (Exception) { }
            if (p < 0f) p = 0f;
            return (int)(Screen.height * p);
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + 0.5f;
            _pollCount++;
            try
            {
                Refresh();
            }
            catch (Exception e)
            {
                _text = "Est: err";
                if (_lastError != e.ToString())
                {
                    _lastError = e.ToString();
                    Log?.LogError("ScoreHud poll failed: " + e);
                }
            }

            string diag = "calls=" + TingSnap.Calls + " hits=" + TingSnap.Hits
                        + (TingSnap.LastErr != null ? " last=" + FirstLine(TingSnap.LastErr) : "");
            if (diag != _lastDiagLogged || _pollCount % 240 == 0)
            {
                _lastDiagLogged = diag;
                Log?.LogInfo("D: " + diag);
            }
        }

        private static string _lastUiFanRaw = "";
        /// <summary>直接读游戏听牌面板上每个候选的 FanNum 文本（如「16番」），取最小值。
        /// 权威：不做任何推算。找不到返回 false。</summary>
        private static bool TryFanNumMin(out int min)
        {
            min = 0;
            try
            {
                var texts = UnityEngine.Object.FindObjectsOfType<TMPro.TMP_Text>();
                bool any = false;
                var raw = new System.Text.StringBuilder();
                for (int i = 0; i < texts.Length; i++)
                {
                    var t = texts[i];
                    if (t == null || t.m_text == null) continue;
                    if (t.gameObject.name != "FanNum") continue;
                    string s = t.m_text;
                    if (!TryParseFan(s, out int v)) continue;
                    any = true;
                    if (min == 0 || v < min) min = v;
                    if (raw.Length > 0) raw.Append(",");
                    raw.Append(v);
                }
                string rr = raw.ToString();
                if (any && rr != _lastUiFanRaw)
                {
                    _lastUiFanRaw = rr;
                    Log?.LogInfo("Diag: uiFanMin=" + min + " from [" + rr + "]");
                }
                return any;
            }
            catch (Exception) { return false; }
        }

        /// <summary>从可能是富文本的文本里解析一个番数：挑「数字后面紧跟(可隔空白)番」的第一处。</summary>
        private static bool TryParseFan(string s, out int v)
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
                i = j;
            }
            return false;
        }

        private void Refresh()
        {
            // 诊断：结算签名出现后的 14s 内高频重dump，等数字/动画稳定。
            ScanTexts();
            if (Time.unscaledTime < _settleWatchUntil && Time.unscaledTime >= _nextSettleDump)
            {
                _nextSettleDump = Time.unscaledTime + 0.3f;
                DumpSettlement();
            }

            // 计分行：结算面板打开且数字稳定后镜像（权威）；否则计分按钮可用时预览
            // （番数=计分按钮上的 Total，与和牌 FanNum 不是同一处）。
            string settleLine = "计分: --";
            if (_settleVisible && LastSettleFactors != null)
                settleLine = "计分: " + LastSettleFactors;
            else if (TryJiFenFan(out int jfFan) && jfFan > 0)
            {
                decimal jfMul = LiveMul();
                if (jfMul <= 0 && TingSnap.Has) jfMul = TingSnap.Cur.Mul;
                decimal totalFan = jfFan + PlayerFanBuff();

                // 底分来源：优先听牌预测，否则实时 UI
                string baseStr = PredictedBase();
                if (string.IsNullOrEmpty(baseStr))
                    baseStr = LiveBase();

                if (jfMul > 0)
                    settleLine = "计分: " + MakeEst(baseStr, totalFan, jfMul);
                else
                    settleLine = "计分: " + totalFan + " 番?" + (baseStr.Length > 0 ? " x " + baseStr : "");
            }

            // 和牌行：听牌钩子预测（底分实时 x 番数 x 倍率），按得分取前三。
            // 没有预测快照时再用游戏听牌面板的 FanNum 作为单项兜底。
            var huLines = new System.Collections.Generic.List<string> { "和牌1: --", "和牌2: --", "和牌3: --" };
            decimal liveMul = LiveMul();
            bool hasMul = liveMul > 0 || (TingSnap.Has && TingSnap.Cur.Mul > 0);
            decimal mul = liveMul > 0 ? liveMul : TingSnap.Cur.Mul;
            if (hasMul && TingSnap.Cur.TopScores != null && TingSnap.Cur.TopScores.Length > 0)
            {
                for (int i = 0; i < TingSnap.Cur.TopScores.Length && i < huLines.Count; i++)
                {
                    var item = TingSnap.Cur.TopScores[i];
                    huLines[i] = "和牌" + (i + 1) + ": " + MakeEst(
                        item.BaseScore > 0 ? Fmt(item.BaseScore) : LiveBase(), item.MinFan, mul);
                }
            }
            else if (hasMul && TryFanNumMin(out int uiFan))
                huLines[0] = "和牌1: " + MakeEst(PredictedBase(), (decimal)(long)uiFan, mul);
            else if (TingSnap.Has && TingSnap.Cur.MinFan > 0)
                huLines[0] = "和牌1: " + MakeEst(PredictedBase(), TingSnap.Cur.MinFan, TingSnap.Cur.Mul);
            else
            {
                var prs = PlayerRoundStatistics.Instance;
                if (prs != null && TryGetHand() != null && _hand.CanHuPaiMianPayloads != null)
                {
                    string fail = "";
                    var q = Comp.Try(_hand.CanHuPaiMianPayloads, ref fail);
                    if (q.HasValue)
                    {
                        var items = q.Value.TopScores;
                        if (items != null && items.Length > 0)
                            for (int i = 0; i < items.Length && i < huLines.Count; i++)
                                huLines[i] = "和牌" + (i + 1) + ": "
                                    + MakeEst(items[i].BaseScore > 0 ? Fmt(items[i].BaseScore) : LiveBase(),
                                        items[i].MinFan, items[i].Mul);
                        else
                            huLines[0] = "和牌1: " + MakeEst(PredictedBase(), q.Value.MinFan, q.Value.Mul);
                    }
                }
            }

            bool noHu = true;
            for (int i = 0; i < huLines.Count; i++)
                if (!huLines[i].EndsWith("--")) { noHu = false; break; }
            if (settleLine.EndsWith("--") && LastSettleFactors == null && noHu)
                _text = "计分: --\n" + string.Join("\n", huLines.ToArray());
            else
                _text = settleLine + "\n" + string.Join("\n", huLines.ToArray());

            if (_text != _lastLogged)
            {
                _lastLogged = _text;
                Log?.LogInfo("hud -> " + _text.Replace("\n", " | ").Replace("  ", " "));
            }
            return;
        }

        /// <summary>
        /// 读游戏计分预览面板 PlayerHuPanel 的四个数字文本；任一为空则视为未显示。
        /// 返回格式化 HUD 文本；未知格式时原样拼接，方便对照。
        /// </summary>
        private string PanelText()
        {
            if (!TryGetPanel()) return null;
            string b = Sc(_panel._baseScoreText);
            string f = Sc(_panel._fanText);
            string m = Sc(_panel._independentText);
            string t = Sc(_panel._totalScoreText);
            if (b == "" && f == "" && m == "" && t == "") return null;
            return "Panel: 底=" + b + " 番=" + f + " 倍=" + m + " 总=" + t;
        }

        private bool TryGetPanel()
        {
            if (_panel != null) return true;
            if (Time.unscaledTime < _nextPanelSearch) return false;
            _nextPanelSearch = Time.unscaledTime + 2f;
            var arr = UnityEngine.Object.FindObjectsOfType<PlayerHuPanel>();
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i] != null)
                {
                    _panel = arr[i];
                    Log?.LogInfo("found PlayerHuPanel #" + arr.Length);
                    return true;
                }
            }
            return false;
        }

        private float _settleWatchUntil;
        private float _nextSettleDump;
        private string _lastSettleLine;
        private bool _settleVisible;
        internal static string LastSettleFactors; // "mul0 x mul1 x mul2 = total"（供 HUD 计分时直接镜像）

        /// <summary>结算期深挖：读 PlayerHuPanel + 玩家 HuPaiJieSuan 的乘法拆解数字。
        /// 数字未落地（动画中/占位 1234567）时标 (anim...)，持续重dump到落地或超时。</summary>
        private void DumpSettlement()
        {
            try
            {
                var sb = new System.Text.StringBuilder("[settle] ");
                bool anim = false;
                string m0 = "", m1 = "", m2 = "", total = "";
                var prs = PlayerRoundStatistics.Instance;
                if (prs != null && prs.HuPaiJieSuan != null)
                {
                    var h = prs.HuPaiJieSuan;
                    sb.Append("win=huizhi");
                    if (h.MultiplyNumbers != null)
                    {
                        string[] mn = { "mul0", "mul1", "mul2" };
                        string[] val = { "", "", "" };
                        for (int i = 0; i < h.MultiplyNumbers.Length && i < 3; i++)
                        {
                            var m = h.MultiplyNumbers[i];
                            if (m == null) continue;
                            string s = Sc(m._numberText);
                            val[i] = s;
                            sb.Append(" ").Append(mn[i]).Append("[").Append(s).Append("]");
                            if (!ReadyNum(s)) anim = true;
                        }
                        m0 = val[0]; m1 = val[1]; m2 = val[2];
                    }
                    var tot = h.TotalNumber;
                    if (tot != null && tot._tmpText != null)
                    {
                        string s = Sc(tot._tmpText);
                        total = s;
                        sb.Append(" total[").Append(s).Append("]");
                        if (!ReadyNum(s)) anim = true;
                    }
                    // 稳定值用文本（数字动画/文本都最终收敛到目标；1234567 占位不算）。
                    if (m0 != "" && m1 != "" && m2 != "" && total != ""
                        && ReadyNum(m0) && ReadyNum(m1) && ReadyNum(m2) && ReadyNum(total))
                        LastSettleFactors = m0 + " x " + m1 + " x " + m2 + " = " + total;
                }
                var panel = GetPanelNow();
                if (panel != null)
                    sb.Append(" panel[base=").Append(Sc(panel._baseScoreText))
                      .Append(" fan=").Append(Sc(panel._fanText))
                      .Append(" indep=").Append(Sc(panel._independentText))
                      .Append(" total=").Append(Sc(panel._totalScoreText)).Append("]");
                string lb = LiveBase();
                if (lb != "") sb.Append(" liveBase=").Append(lb);
                if (TingSnap.Has)
                    sb.Append(" lastHook=fan").Append(TingSnap.Cur.MinFan)
                      .Append(" mul").Append(TingSnap.Cur.Mul);
                string line = sb.ToString();
                if (m0 != "" && m1 != "" && m2 != "" && total != ""
                    && ReadyNum(m0) && ReadyNum(m1) && ReadyNum(m2) && ReadyNum(total))
                    LastSettleFactors = m0 + " x " + m1 + " x " + m2 + " = " + total;
                if (line != _lastSettleLine)
                {
                    _lastSettleLine = line;
                    Log?.LogInfo(line + (anim ? " (anim...)" : ""));
                }
            }
            catch (Exception e)
            {
                Log?.LogInfo("[settle] failed: " + FirstLine(e.ToString()));
            }
        }

        /// <summary>数值文本是否已经“落地”（非空、非 1234567 占位、含数字）。</summary>
        private static bool ReadyNum(string s)
        {
            if (s == null || s.Trim().Length == 0) return false;
            if (s.Contains("1234567")) return false;
            foreach (char c in s)
                if (c >= '0' && c <= '9') return true;
            return false;
        }

        private static PlayerHuPanel GetPanelNow()
        {
            try
            {
                var arr = UnityEngine.Object.FindObjectsOfType<PlayerHuPanel>();
                return arr != null && arr.Length > 0 ? arr[0] : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>底分 × 番数 × 倍率 = 预计分。底分读不到时显示 base?。
        /// 标签由调用方拼，这里不再带 Est: 前缀。</summary>
        private static string MakeEst(string baseS, decimal fan, decimal mul)
        {
            if (fan <= 0) return "--";
            decimal b;
            string bs = CleanNumber(baseS);
            if (TryParseDisplayNumber(baseS, out b))
            {
                string total = Fmt(b * fan * mul);
                return Fmt(b) + " x " + Fmt(fan) + " x " + Fmt(mul) + " = " + total;
            }
            return "base? x " + Fmt(fan) + " x " + Fmt(mul);
        }

        /// <summary>原生读 List&lt;Il2CppSystem.Decimal&gt; 的内联元素（List 布局：+0x10=_items 引用,+0x18=_size；数组元素基址=+0x18,步长16）。</summary>
        private static List<decimal> RawDecimals(Il2CppSystem.Collections.Generic.List<Il2CppSystem.Decimal> list)
        {
            var res = new List<decimal>(8);
            var lbase = list as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
            if (lbase == null) return res;
            long lp = lbase.Pointer.ToInt64();
            long arrP = Marshal.ReadInt64(new IntPtr(lp + 0x10));
            if (arrP == 0) return res;
            int size = Marshal.ReadInt32(new IntPtr(lp + 0x18));
            long data = arrP + 0x18;
            for (int i = 0; i < size; i++)
            {
                long p = data + 16L * i;
                int w0 = Marshal.ReadInt32(new IntPtr(p + 0));
                int w1 = Marshal.ReadInt32(new IntPtr(p + 4));
                int w2 = Marshal.ReadInt32(new IntPtr(p + 8));
                int w3 = Marshal.ReadInt32(new IntPtr(p + 12));
                byte scale = (byte)((w0 >> 16) & 0x7F);
                if (scale > 28) res.Add(-1m);
                else res.Add(new decimal(w2, w3, w1, w0 < 0, scale));
            }
            return res;
        }

        /// <summary>读玩家当前局实时底分（ScoreBar 的 底分 TMP）。找不到返回空。</summary>
        private string LiveBase()
        {
            try
            {
                // 1. 优先从 Buff 系统精确读取底分（不受 UI 缩略影响）
                var prs = PlayerRoundStatistics.Instance;
                if (prs != null)
                {
                    if (Comp.TryGetBuffStats(prs, out decimal baseScore, out _, out _))
                    {
                        if (baseScore > 0)
                            return Fmt(baseScore);
                    }
                }

                // 2. 后备：从 UI 文本解析（原有逻辑，保留兼容性）
                var all = UnityEngine.Object.FindObjectsOfType<TMPro.TMP_Text>();
                foreach (var t in all)
                {
                    if (t == null || string.IsNullOrEmpty(t.m_text)) continue;
                    if (t.gameObject.name == "BaseScoreText")
                    {
                        if (TryParseDisplayNumber(t.m_text, out decimal value))
                            return Fmt(value);
                    }
                }

                foreach (var t in all)
                {
                    if (t == null || string.IsNullOrEmpty(t.m_text)) continue;
                    var p = t.transform.parent;
                    if (p != null && p.name == "BaseScoreText")
                    {
                        if (TryParseDisplayNumber(t.m_text, out decimal value))
                            return Fmt(value);
                    }
                }

                foreach (var t in all)
                {
                    if (t == null || string.IsNullOrEmpty(t.m_text)) continue;
                    string s = t.m_text;
                    if (s.Contains("底分"))
                    {
                        string cleaned = CleanNumber(s);
                        if (TryParseDisplayNumber(cleaned, out decimal value))
                            return Fmt(value);
                    }
                }
            }
            catch (Exception) { }
            return "";
        }
        private static string PredictedBase()
        {
            if (TingSnap.Has && TingSnap.Cur.BaseScore > 0m)
                return Fmt(TingSnap.Cur.BaseScore);
            return "";
        }

        private static bool IsAllNums(string s)
        {
            foreach (char c in s)
                if (c < '0' || c > '9') return false;
            return true;
        }

        /// <summary>画面上是否存在带「JiFen」祖先的节点（计分按钮可用）。</summary>
        private static bool HasAncestor(TMPro.TMP_Text t, string name)
        {
            var p = t.transform.parent;
            while (p != null)
            {
                if (p.name == name) return true;
                p = p.parent;
            }
            return false;
        }

        private static string _lastJfRaw = "";
        /// <summary>读计分按钮上的番数：GO 名 Total 且祖先含 JiFen，文本如「6 番」。
        /// 游戏在计分按钮上直接预览计分要用的番数（与和牌预览 FanNum 不是同一处）。</summary>
        private static bool TryJiFenFan(out int fan)
        {
            fan = 0;
            try
            {
                var texts = UnityEngine.Object.FindObjectsOfType<TMPro.TMP_Text>();
                bool any = false;
                var raw = new System.Text.StringBuilder();
                for (int i = 0; i < texts.Length; i++)
                {
                    var t = texts[i];
                    if (t == null || t.m_text == null) continue;
                    if (t.gameObject.name != "Total") continue;
                    if (!HasAncestor(t, "JiFen")) continue;
                    if (!TryParseFan(t.m_text, out int v)) continue;
                    any = true;
                    if (fan == 0 || v < fan) fan = v;
                    if (raw.Length > 0) raw.Append(",");
                    raw.Append(t.m_text.Trim());
                }
                string rr = raw.ToString();
                if (any && rr != _lastJfRaw)
                {
                    _lastJfRaw = rr;
                    Log?.LogInfo("Diag: jfFan=" + fan + " from [" + rr + "]");
                }
                return any;
            }
            catch (Exception) { return false; }
        }

        /// <summary>玩家实时倍率：RoundStatistics/PlayerStates/Independent/IndependentText（如「2.3」）。</summary>
        private static decimal LiveMul()
        {
            try
            {
                var texts = UnityEngine.Object.FindObjectsOfType<TMPro.TMP_Text>();
                for (int i = 0; i < texts.Length; i++)
                {
                    var t = texts[i];
                    if (t == null || t.m_text == null) continue;
                    if (t.gameObject.name != "IndependentText") continue;
                    if (!HasAncestor(t, "PlayerStates")) continue;
                    if (TryParseDisplayNumber(t.m_text, out decimal m) && m > 0)
                        return m;
                }
            }
            catch (Exception) { }
            return 0;
        }

        private static decimal PlayerFanBuff()
        {
            try
            {
                var texts = UnityEngine.Object.FindObjectsOfType<TMPro.TMP_Text>();
                decimal best = 0m;
                for (int i = 0; i < texts.Length; i++)
                {
                    var t = texts[i];
                    if (t == null || t.m_text == null || t.gameObject.name != "FanText") continue;
                    if (!HasAncestor(t, "PlayerStates")) continue;
                    if (TryParseDisplayNumber(t.m_text, out decimal value) && value > best)
                        best = value;
                }
                return best;
            }
            catch (Exception) { return 0m; }
        }

        private static decimal DecimalValue(Il2CppSystem.Decimal value)
        {
            try
            {
                int scale = value.Scale;
                if (scale >= 0 && scale <= 28)
                    return new decimal(unchecked((int)value.Low), unchecked((int)value.Mid),
                        unchecked((int)value.High), value.IsNegative, (byte)scale);
            }
            catch (Exception) { }
            return -1m;
        }

        private float _nextScan;
        private string _lastScanHash;

        /// <summary>扫描场景内所有 TMP 文本，收集疑似计分面板的数字/分/倍/底/番/总文本。仅日志诊断。</summary>
        private void ScanTexts()
        {
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 4f;
            try
            {
                var texts = UnityEngine.Object.FindObjectsOfType<TMPro.TMP_Text>();
                bool settleSig = false;
                for (int i = 0; i < texts.Length && !settleSig; i++)
                {
                    string s = texts[i] != null && texts[i].m_text != null ? texts[i].m_text : "";
                    settleSig = s.Contains("sprite name") || s.Contains("底分") || s.Contains("倍率")
                        || s.Contains("Title") || s.Contains("计分视为打出") || s.Contains("（计分视为打出）");
                }
                if (settleSig)
                {
                    ScanDeep(texts);
                    DumpSettlement();
                    _lastScanHash = null;
                    _nextScan = Time.unscaledTime + 2f;
                    _settleWatchUntil = Time.unscaledTime + 14f;
                    _settleVisible = true;
                    return;
                }

                var sb = new System.Text.StringBuilder("[scene] ");
                int n = 0;
                for (int i = 0; i < texts.Length && n < 20; i++)
                {
                    var t = texts[i];
                    if (t == null || t.m_text == null) continue;
                    string s = t.m_text.Trim();
                    if (s.Length == 0) continue;
                    bool interesting = s.IndexOfAny("0123456789xX.分倍底番总".ToCharArray()) >= 0;
                    if (!interesting) continue;
                    sb.Append("[");
                    sb.Append(t.gameObject.name);
                    sb.Append("=");
                    sb.Append(s);
                    sb.Append("]");
                    n++;
                }
                string hash = sb.ToString();
                if (hash != _lastScanHash)
                {
                    _lastScanHash = hash;
                    Log?.LogInfo(hash + " (total TMP=" + texts.Length + ")");
                }
                _settleVisible = false;
                // 无条件 dump 结算拆解；数字变化/落地时才输出（配合 dedup）。
                DumpSettlement();
            }
            catch (Exception e)
            {
                Log?.LogInfo("[scene] scan failed: " + FirstLine(e.ToString()));
            }
        }

        /// <summary>结算签名出现时：带 GoPath 深扫，最多 80 条，确认 底分/倍率/总 数值归属。</summary>
        private void ScanDeep(TMPro.TMP_Text[] texts)
        {
            try
            {
                var sb = new System.Text.StringBuilder("[deep]");
                int n = 0;
                for (int i = 0; i < texts.Length && n < 80; i++)
                {
                    var t = texts[i];
                    if (t == null || t.m_text == null) continue;
                    string s = t.m_text.Trim();
                    if (s.Length == 0) continue;
                    if (s.IndexOfAny("0123456789xX.".ToCharArray()) < 0 && !s.Contains("底分") && !s.Contains("倍率") && !s.Contains("番")) continue;
                    sb.Append("\n  ").Append(GoPath(t.transform)).Append(" | ").Append(t.gameObject.name).Append("=[").Append(s.Length > 60 ? s.Substring(0, 60) : s).Append("]");
                    n++;
                }
                Log?.LogInfo(sb.ToString());
            }
            catch (Exception e)
            {
                Log?.LogInfo("[deep] failed: " + FirstLine(e.ToString()));
            }
        }

        private static string GoPath(Transform tr)
        {
            var names = new System.Collections.Generic.List<string>();
            while (tr != null && names.Count < 7)
            {
                names.Add(tr.name);
                tr = tr.parent;
            }
            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        private static string Sc(TMPro.TMP_Text text)
        {
            if (text == null || text.m_text == null) return "";
            return CleanNumber(text.m_text);
        }

        private static string CleanNumber(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var result = new System.Text.StringBuilder();
            bool inTag = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '<') { inTag = true; continue; }
                if (c == '>') { inTag = false; continue; }
                if (inTag) continue;
                if ((c >= '0' && c <= '9') || c == ',' || c == '.' || c == '-')
                    result.Append(c);
            }
            return result.ToString();
        }

        private static bool TryParseDisplayNumber(string text, out decimal value)
        {
            value = 0m;
            if (string.IsNullOrEmpty(text)) return false;
            string raw = text.Trim();
            var plain = new System.Text.StringBuilder(raw.Length);
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
            if (raw.EndsWith("M", StringComparison.OrdinalIgnoreCase))
            {
                scale = 1000000m;
                raw = raw.Substring(0, raw.Length - 1);
            }
            else if (raw.EndsWith("K", StringComparison.OrdinalIgnoreCase))
            {
                scale = 1000m;
                raw = raw.Substring(0, raw.Length - 1);
            }
            return decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && (value *= scale) >= 0m;
        }

        private PlayerHandPaiMianContainer TryGetHand()
        {
            if (_hand != null) return _hand;
            var arr = UnityEngine.Object.FindObjectsOfType<PlayerHandPaiMianContainer>();
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i] != null)
                {
                    _hand = arr[i];
                    break;
                }
            }
            return _hand;
        }

        private static string Fmt(decimal value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string FirstLine(string s)
        {
            int i = s.IndexOf('\n');
            return i < 0 ? s : s.Substring(0, i);
        }

        private void OnGUI()
        {
            GUI.Label(new Rect(12, 12 + _yOffset, 620, 140), _text, _style);
        }
    }
}