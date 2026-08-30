using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace AutoContinue
{
    /// <summary>
    /// 自动跳过两个「等玩家点一下」的环节：
    ///   1) 启动后的公告界面 → 自动点【继续】进大厅；
    ///   2) 与 Boss 对决加载完成后底部【点击继续】→ 自动点击进对局。
    /// 由 dll 同目录 AutoContinue.yml 控制开关与延迟（改后重启生效）。
    /// </summary>
    public class AutoSkip : MonoBehaviour
    {
        internal static BepInEx.Logging.ManualLogSource Log;

        private const string CfgName = "AutoContinue.yml";
        private const float ScanInterval = 0.25f;

        private bool _announceEnabled = true;
        private float _announceDelay;
        private bool _battleEnabled = true;
        private float _battleDelay;

        private float _nextScan;

        private enum Kind { None, Announce, Battle }
        private Kind _cur;
        private float _curFirstSeen;
        private Kind _lastTriggered;
        private float _lastTriggerTime;

        private void Awake()
        {
            LoadConfig();
            Log?.LogInfo("AutoContinue cfg: announce=" + _announceEnabled + "/d=" + _announceDelay
                + " battle=" + _battleEnabled + "/d=" + _battleDelay);
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + ScanInterval;
            if (!_announceEnabled && !_battleEnabled) return;

            Button target = null;
            Kind kind = Kind.None;
            string info = "";
            try
            {
                var btns = UnityEngine.Object.FindObjectsOfType<Button>();
                for (int i = 0; i < btns.Length && target == null; i++)
                {
                    var b = btns[i];
                    if (b == null || !b.isActiveAndEnabled) continue;
                    string text = ButtonText(b);
                    if (text.Length == 0) continue;
                    if (_battleEnabled && text.Contains("点击继续"))
                    {
                        target = b; kind = Kind.Battle; info = text;
                    }
                    else if (_announceEnabled && Normalize(text) == "继续")
                    {
                        target = b; kind = Kind.Announce; info = text;
                    }
                }
            }
            catch (Exception) { }

            if (target == null)
            {
                _cur = Kind.None;
                return;
            }

            if (_cur != kind)
            {
                _cur = kind;
                _curFirstSeen = Time.unscaledTime;
            }

            // 同一屏触发过一次后冷却 3s；按钮消失→重arm，避免回到同屏时连点。
            if (_cur != _lastTriggered || Time.unscaledTime - _lastTriggerTime > 3f)
            {
                float delay = kind == Kind.Battle ? _battleDelay : _announceDelay;
                if (Time.unscaledTime - _curFirstSeen >= delay)
                {
                    Click(target, kind, info);
                    _lastTriggered = _cur;
                    _lastTriggerTime = Time.unscaledTime;
                }
            }
        }

        private static string ButtonText(Button b)
        {
            var sb = new System.Text.StringBuilder();
            var texts = b.GetComponentsInChildren<TMPro.TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null && texts[i].m_text != null)
                    sb.Append(texts[i].m_text);
            }
            return sb.ToString().Trim();
        }

        /// <summary>去掉空白与【】『』等装饰，便于精确匹配「继续」。</summary>
        private static string Normalize(string s)
        {
            if (s == null) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == ' ' || c == '\n' || c == '\t' || c == '\r'
                    || c == '【' || c == '】' || c == '『' || c == '』' || c == '<')
                    continue;
                sb.Append(c);
            }
            return sb.ToString();
        }

        private void Click(Button b, Kind kind, string info)
        {
            try
            {
                b.onClick.Invoke();
                Log?.LogInfo("AutoContinue: clicked " + kind + " btn=[" + b.gameObject.name + "] text=[" + info + "]");
            }
            catch (Exception e)
            {
                Log?.LogInfo("AutoContinue: click failed " + kind + ": " + FirstLine(e.ToString()));
            }
        }

        private static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            int nl = s.IndexOf('\n');
            return nl >= 0 ? s.Substring(0, nl) : s;
        }

        private void LoadConfig()
        {
            try
            {
                var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var f = Path.Combine(dir, CfgName);
                if (!File.Exists(f))
                {
                    File.WriteAllText(f, DefaultConfig(), System.Text.Encoding.UTF8);
                    Log?.LogInfo("AutoContinue: created config " + f);
                }
                string[] lines = File.ReadAllLines(f);
                for (int i = 0; i < lines.Length; i++)
                {
                    string t = lines[i].Trim();
                    if (t.Length == 0 || t.StartsWith("#")) continue;
                    int ci = t.IndexOf(':');
                    if (ci < 0) continue;
                    string k = t.Substring(0, ci).Trim();
                    string v = t.Substring(ci + 1).Trim();
                    if (string.Equals(k, "announce_enabled", StringComparison.OrdinalIgnoreCase))
                        _announceEnabled = ParseBool(v, _announceEnabled);
                    else if (string.Equals(k, "announce_delay", StringComparison.OrdinalIgnoreCase))
                        _announceDelay = ParseFloat(v, _announceDelay);
                    else if (string.Equals(k, "battle_enabled", StringComparison.OrdinalIgnoreCase))
                        _battleEnabled = ParseBool(v, _battleEnabled);
                    else if (string.Equals(k, "battle_delay", StringComparison.OrdinalIgnoreCase))
                        _battleDelay = ParseFloat(v, _battleDelay);
                }
            }
            catch (Exception e)
            {
                Log?.LogInfo("AutoContinue: cfg read failed: " + FirstLine(e.ToString()));
            }
        }

        private static string DefaultConfig() =>
            "# AutoContinue — 自动跳过「等玩家点一下」的环节（改后重启游戏生效）\n" +
            "\n" +
            "# 1. 启动后的公告界面：自动点【继续】进入大厅\n" +
            "announce_enabled: true\n" +
            "announce_delay: 0.0\n" +
            "\n" +
            "# 2. 与 Boss 对决加载完成后底部【点击继续】：自动点击进入对局\n" +
            "battle_enabled: true\n" +
            "battle_delay: 0.0\n";

        private static bool ParseBool(string v, bool d)
        {
            if (string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(v, "false", StringComparison.OrdinalIgnoreCase)) return false;
            if (v == "1") return true;
            if (v == "0") return false;
            return d;
        }

        private static float ParseFloat(string v, float d)
        {
            if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float r))
                return r < 0 ? 0 : r;
            return d;
        }
    }
}