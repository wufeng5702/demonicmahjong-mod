using System;
using System.Collections.Generic;
using System.Globalization;
using Shared;
using UnityEngine;

namespace LingPreview
{
    /// <summary>
    /// 幻灵台（附牌灵）提示面板：进入幻灵台后，用 IMGUI 显示当前牌灵的变换规则，
    /// 并把「当前选中的牌」变成哪张牌直接写出来。
    /// 只在幻灵台界面存在时显示，其它场景（含对局中）完全不出现。
    /// </summary>
    public class LingPanel : MonoBehaviour
    {
        internal static BepInEx.Logging.ManualLogSource Log;

        private const string CfgName = "LingPreview.yml";
        private const float PollInterval = 0.15f;

        private bool _enabled = true;
        private int _fontSize = 18;
        private int _lineSpacing = 10;   // 行高 = 字号 + 行距（与配置默认值一致）
        private float _xOffset = 0.5f;   // 水平锚点 = 屏幕宽度 × 比例（0.5 = 顶部居中）
        private float _yOffset = 0.0f;   // 垂直锚点 = 屏幕高度 × 比例（0 = 贴顶）
        private bool _fullDetail = true; // true=完整映射表；false=只一行摘要
        private bool _debug;

        private float _nextPoll;
        private float _nextHeartbeat;
        private LingView _view;
        private string _lastSignature;
        private string _lastDebugState = "";
        private MaJiang.DataConstruct.BaoLingPai.BaoLing _lastProbedLing = MaJiang.DataConstruct.BaoLingPai.BaoLing.None;

        private GUIStyle _styleTitle;
        private GUIStyle _styleNormal;
        private GUIStyle _styleDim;
        private GUIStyle _styleHighlight;
        private GUIStyle _boxStyle;

        private LingView _measuredView;   // 已经量过宽度的面板内容
        private float _viewWidth;
        private bool _errorLogged;

        private void Awake()
        {
            LoadConfig();
            EnsureStyles();
            LingTai.Log = msg => Log?.LogInfo(msg);
            Log?.LogInfo("LingPanel active xoffset=" + _xOffset.ToString(CultureInfo.InvariantCulture)
                + " yoffset=" + _yOffset.ToString(CultureInfo.InvariantCulture)
                + " fontsize=" + _fontSize + " linespacing=" + _lineSpacing
                + " detail=" + (_fullDetail ? "full" : "brief"));
        }

        private void Update()
        {
            if (!_enabled) return;
            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + PollInterval;

            // debug 心跳：确认 Update 真的在跑（面板只在幻灵台出现，没进幻灵台时靠它看活）
            if (_debug && Time.unscaledTime >= _nextHeartbeat)
            {
                _nextHeartbeat = Time.unscaledTime + 5f;
                Log?.LogInfo("[probe] panel tick");
            }

            LingView view = null;
            MaJiang.GameEvent.Runtime.Controller.BaoLingTai tai = null;
            MaJiang.UI.GameEvent.PaiMianAdditionUI ui = null;
            try
            {
                if (LingTai.TryGet(out tai, out ui))
                    view = LingTai.Build(tai, ui, _fullDetail, _styleNormal);
            }
            catch (Exception e)
            {
                // 第一次异常必须留线索（哪怕没开 debug），否则面板不出现且日志毫无痕迹
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log?.LogWarning("LingPanel build failed: " + e);
                }
                if (_debug) Log?.LogInfo("LingPanel build failed: " + e.Message);
            }

            // debug：把幻灵台当前状态打出来，方便定位「面板为什么没出来」。
            // 用 FindController() 而不是 tai —— tai 只在判据全过时才有值，
            // 而「面板不出现」恰恰是唯一需要排障的场景。
            if (_debug)
            {
                var probe = tai != null ? tai : LingTai.FindController();
                if (probe != null)
                {
                    string st = LingTai.DebugState(probe);
                    if (st != _lastDebugState)
                    {
                        _lastDebugState = st;
                        Log?.LogInfo("[probe] state " + st);
                    }
                }
            }

            _view = view;
            string sig = Signature(view);
            if (sig != _lastSignature)
            {
                _lastSignature = sig;
                if (sig.Length > 0) Log?.LogInfo("ling -> " + sig);
            }

            // debug：每个灵只探一次，把灵配置与幻灵台里的牌面表打出来
            if (_debug && view != null && view.Payload != null
                && _lastProbedLing != view.Ling)
            {
                _lastProbedLing = view.Ling;
                try
                {
                    int diff = 0;
                    var rule = LingTai.RuleKindFor(view.Ling, view.Payload, out diff);
                    LingTai.DumpPayload(view.Payload, rule, diff);
                    var payloads = ui != null && ui.paiMianRoot != null ? ui.paiMianRoot.Payloads : null;
                    LingTai.DumpTiles(payloads);
                }
                catch (Exception e)
                {
                    Log?.LogInfo("[probe] failed: " + e.Message);
                }
            }
        }

        private static string Signature(LingView view)
        {
            if (view == null || view.Lines.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < view.Lines.Count; i++)
            {
                if (i > 0) sb.Append(" | ");
                sb.Append(view.Lines[i].Text);
            }
            return sb.ToString();
        }

        private void OnGUI()
        {
            if (!_enabled || _view == null || _view.Lines.Count == 0) return;
            EnsureStyles();

            const float pad = 10f;
            // 行高下限：字号 18 的实际行高约 21px，行高等于字号会把字的下缘切掉
            float lineHeight = Mathf.Max(_fontSize + _lineSpacing, _fontSize * 1.25f);
            float gapHeight = _fontSize * 0.45f;      // 空行（组与组之间的间隔）

            // 宽度：内容变了才重量。**必须在 OnGUI 里测**——GUIStyle.CalcSize 依赖
            // IMGUI 的字体状态，在 Update 里调可能返回 0（面板只剩 20px 宽、文字全被裁）。
            if (!ReferenceEquals(_measuredView, _view))
            {
                _measuredView = _view;
                _viewWidth = MeasureWidth(_view, pad);
                if (_viewWidth > Screen.width - 20f)
                {
                    Log?.LogInfo("[probe] 面板内容超宽（" + _viewWidth.ToString("0")
                        + " > " + (Screen.width - 20).ToString("0") + "），窄屏下会被裁剪；"
                        + "可把 fontsize 调小或 detail 改成 brief");
                }
            }
            float width = Mathf.Min(_viewWidth, Screen.width - 20f);

            float height = pad;
            for (int i = 0; i < _view.Lines.Count; i++)
                height += _view.Lines[i].Text.Length == 0 ? gapHeight : lineHeight;

            float x = Screen.width * _xOffset - width * 0.5f;
            float y = Screen.height * _yOffset;
            if (x < 10f) x = 10f;
            if (x + width > Screen.width - 10f) x = Screen.width - 10f - width;
            y = Mathf.Clamp(y, 0f, Mathf.Max(0f, Screen.height - height));

            GUI.Box(new Rect(x, y, width, height), GUIContent.none, _boxStyle);
            float cursor = y + pad * 0.5f;
            for (int i = 0; i < _view.Lines.Count; i++)
            {
                var line = _view.Lines[i];
                if (line.Text.Length == 0)
                {
                    cursor += gapHeight;
                    continue;
                }
                GUI.Label(new Rect(x + pad, cursor, width - pad * 2f, lineHeight), line.Text, StyleOf(line.Kind));
                cursor += lineHeight;
            }
        }

        /// <summary>量出面板需要的宽度（各行自身样式的最大宽 + 内边距）。</summary>
        private float MeasureWidth(LingView view, float pad)
        {
            if (view == null) return 0f;
            float width = 0f;
            for (int i = 0; i < view.Lines.Count; i++)
            {
                var line = view.Lines[i];
                if (line.Text.Length == 0) continue;
                float w = StyleOf(line.Kind).CalcSize(new GUIContent(line.Text)).x;
                if (w > width) width = w;
            }
            return width + pad * 2f;
        }

        private GUIStyle StyleOf(LineKind kind)
        {
            switch (kind)
            {
                case LineKind.Title: return _styleTitle;
                case LineKind.Dim: return _styleDim;
                case LineKind.Highlight: return _styleHighlight;
                default: return _styleNormal;
            }
        }

        private void EnsureStyles()
        {
            if (_styleNormal != null && _styleNormal.fontSize == _fontSize) return;

            _styleTitle = MakeStyle(new Color(1f, 0.85f, 0.3f), true);
            _styleNormal = MakeStyle(Color.white, false);
            _styleDim = MakeStyle(new Color(0.68f, 0.68f, 0.68f), false);
            _styleHighlight = MakeStyle(new Color(1f, 0.9f, 0.35f), true);

            _boxStyle = new GUIStyle();
            var bg = new Texture2D(1, 1);
            bg.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.6f));
            bg.Apply();
            _boxStyle.normal.background = bg;
        }

        private GUIStyle MakeStyle(Color color, bool bold)
        {
            var style = new GUIStyle();
            style.fontSize = _fontSize;
            style.normal.textColor = color;
            if (bold) style.fontStyle = FontStyle.Bold;
            return style;
        }

        /// <summary>读配置：dll 同目录 LingPreview.yml（改后重启生效）。</summary>
        private void LoadConfig()
        {
            try
            {
                var defaults = new Dictionary<string, string>
                {
                    ["enabled"] = "true",
                    ["fontsize"] = "18",
                    ["linespacing"] = "10",
                    ["xoffset"] = "0.5",
                    ["yoffset"] = "0.0",
                    ["detail"] = "full",
                    ["debug"] = "false"
                };
                var cfg = YamlConfig.Load(CfgName, defaults);
                if (cfg.TryGetValue("enabled", out string ev))
                    _enabled = ParseBool(ev, _enabled);
                if (cfg.TryGetValue("fontsize", out string fv) && int.TryParse(fv, out int fs))
                    _fontSize = fs < 10 ? 10 : (fs > 60 ? 60 : fs);
                if (cfg.TryGetValue("linespacing", out string sv) && int.TryParse(sv, out int sp))
                    _lineSpacing = sp < 0 ? 0 : (sp > 40 ? 40 : sp);
                if (cfg.TryGetValue("xoffset", out string xv)
                    && float.TryParse(xv, NumberStyles.Float, CultureInfo.InvariantCulture, out float xr))
                    _xOffset = xr < 0f ? 0f : (xr > 1f ? 1f : xr);
                if (cfg.TryGetValue("yoffset", out string yv)
                    && float.TryParse(yv, NumberStyles.Float, CultureInfo.InvariantCulture, out float yr))
                    _yOffset = yr < 0f ? 0f : (yr > 1f ? 1f : yr);
                if (cfg.TryGetValue("detail", out string dv))
                    _fullDetail = !string.Equals(dv, "brief", StringComparison.OrdinalIgnoreCase);
                if (cfg.TryGetValue("debug", out string dv2))
                    _debug = ParseBool(dv2, _debug);
            }
            catch (Exception e)
            {
                Log?.LogWarning("LingPreview config load failed: " + e.Message);
            }
        }

        private static bool ParseBool(string v, bool d)
        {
            if (string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(v, "false", StringComparison.OrdinalIgnoreCase)) return false;
            if (v == "1") return true;
            if (v == "0") return false;
            return d;
        }
    }
}
