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
        private string _text = "计分: --\n和牌: --";
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
                decimal jfMul = TingSnap.Has && TingSnap.Cur.Mul > 0 ? TingSnap.Cur.Mul : LiveMul();
                if (jfMul > 0)
                    settleLine = "计分: " + MakeEst(LiveBase(), (decimal)(long)jfFan, jfMul);
                else
                    settleLine = "计分: " + jfFan + " 番?" + (LiveBase().Length > 0 ? " x " + LiveBase() : "");
            }

            // 和牌行：听牌钩子预测（底分实时 x 最小番 x 倍率）。
            // 番数优先用游戏听牌面板直接显示的 FanNum（多等待取最小），这是权威值，不推算。
            string huLine = "和牌: --";
            bool hasMul = TingSnap.Has && TingSnap.Cur.Mul > 0;
            decimal mul = hasMul ? TingSnap.Cur.Mul : 0;
            if (hasMul && TryFanNumMin(out int uiFan))
                huLine = "和牌: " + MakeEst(LiveBase(), (decimal)(long)uiFan, mul);
            else if (TingSnap.Has && TingSnap.Cur.MinFan > 0)
                huLine = "和牌: " + MakeEst(LiveBase(), TingSnap.Cur.MinFan, TingSnap.Cur.Mul);
            else
            {
                var prs = PlayerRoundStatistics.Instance;
                if (prs != null && TryGetHand() != null && _hand.CanHuPaiMianPayloads != null)
                {
                    string fail = "";
                    var q = Comp.Try(_hand.CanHuPaiMianPayloads, ref fail);
                    if (q.HasValue)
                        huLine = "和牌: " + MakeEst(LiveBase(), q.Value.MinFan, q.Value.Mul);
                }
            }

            if (settleLine.EndsWith("--") && LastSettleFactors == null && huLine.EndsWith("--"))
                _text = "计分: --\n和牌: --";
            else
                _text = settleLine + "\n" + huLine;

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
                        && ReadyNum(m0) && ReadyNum(m1) && ReadyNum(m2) && total.Contains("sprite"))
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
                if (m0 != "" && m1 != "" && m2 != "" && total != "")
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
        /// 两个标签（计分:/和牌:）由调用方拼，这里不再带 Est: 前缀。</summary>
        private static string MakeEst(string baseS, decimal fan, decimal mul)
        {
            if (fan <= 0) return "--";
            decimal b;
            string bs = baseS == null ? "" : baseS.Trim();
            if (decimal.TryParse(bs, NumberStyles.Number, CultureInfo.InvariantCulture, out b))
            {
                string total = Fmt(b * fan * mul);
                return bs + " x " + Fmt(fan) + " x " + Fmt(mul) + " = " + total;
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
                var prs = PlayerRoundStatistics.Instance;
                if (prs != null && prs._baseScore != null)
                {
                    var t = prs._baseScore.GetComponentInChildren<TMPro.TMP_Text>();
                    if (t != null && t.m_text != null) return t.m_text.Trim();
                }
                var all = UnityEngine.Object.FindObjectsOfType<TMPro.TMP_Text>();
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] != null && all[i].m_text != null && all[i].gameObject.name == "BaseScoreText")
                    {
                        string s = all[i].m_text.Trim();
                        if (s.Length > 0) return s;
                    }
                }
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] == null || all[i].m_text == null) continue;
                    string s = all[i].m_text.Trim();
                    if (s.Length > 0 && s.Length < 10 && IsAllNums(s))
                    {
                        var p = all[i].transform.parent;
                        if (p != null && (p.name == "BaseScoreText"))
                            return s;
                    }
                }
            }
            catch (Exception) { }
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
                    string s = t.m_text.Trim();
                    if (decimal.TryParse(s, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out decimal m) && m > 0)
                        return m;
                }
            }
            catch (Exception) { }
            return 0;
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
            return text.m_text.Trim();
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
            GUI.Label(new Rect(12, 12 + _yOffset, 620, 40), _text, _style);
        }
    }
}