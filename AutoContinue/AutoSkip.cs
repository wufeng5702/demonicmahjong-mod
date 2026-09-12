using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using Shared;

namespace AutoContinue
{
    /// <summary>
    /// 自动跳过三个「等玩家点一下」的环节：
    ///   1) 启动后的公告界面 → 自动点【继续】进大厅；
    ///   2) 与 Boss 对决加载完成后底部【点击继续】→ 自动点击进对局；
    ///   3) 对局结算后的结算/查看详情界面 → 自动点【继续】。
    /// 由 dll 同目录 AutoContinue.yml 控制开关与延迟（改后重启生效）。
    /// </summary>
    public class AutoSkip : MonoBehaviour
    {
        internal static BepInEx.Logging.ManualLogSource Log;

        private const string CfgName = "AutoContinue.yml";
        private const float ScanInterval = 0.25f;

        private bool _announceEnabled = true;
        private float _announceDelay = 2f;
        private bool _battleEnabled = true;
        private float _battleDelay = 1f;
        private bool _resultEnabled = false;
        private float _resultDelay = 5f;

        private float _nextScan;

        private enum Kind { None, Announce, Battle, Result }
        private Kind _cur;
        private float _curFirstSeen;
        private Kind _lastTriggered;
        private float _lastTriggerTime;

        private void Awake()
        {
            LoadConfig();
            Log?.LogInfo("AutoContinue cfg: announce=" + _announceEnabled + "/d=" + _announceDelay
                + " battle=" + _battleEnabled + "/d=" + _battleDelay
                + " result=" + _resultEnabled + "/d=" + _resultDelay);
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + ScanInterval;
            if (!_announceEnabled && !_battleEnabled && !_resultEnabled) return;

            // 收集所有可见按钮及其文本
            var buttonList = new List<(Button btn, string text)>();
            var btns = UnityEngine.Object.FindObjectsOfType<Button>();
            foreach (var b in btns)
            {
                if (b == null || !b.gameObject.activeInHierarchy) continue;
                string text = ButtonText(b);
                if (text.Length == 0) continue;
                buttonList.Add((b, text));
            }

            Button target = null;
            Kind kind = Kind.None;
            string info = "";

            // 1. 战斗加载界面（文本包含“点击继续”）
            if (_battleEnabled)
            {
                foreach (var item in buttonList)
                {
                    if (item.text.Contains("点击继续"))
                    {
                        target = item.btn;
                        kind = Kind.Battle;
                        info = item.text;
                        break;
                    }
                }
            }

            // 2. 结算界面：仅在启用时检测
            if (target == null && _resultEnabled)
            {
                Button continueBtn = null;
                string continueText = "";
                foreach (var item in buttonList)
                {
                    if (Normalize(item.text) == "继续")
                    {
                        continueBtn = item.btn;
                        continueText = item.text;
                        break;
                    }
                }

                if (continueBtn != null && IsSettlementButton(continueBtn, buttonList))
                {
                    target = continueBtn;
                    kind = Kind.Result;
                    info = continueText;
                }
            }

            // 3. 公告界面：仅有“继续”，但必须排除结算界面
            if (target == null && _announceEnabled)
            {
                foreach (var item in buttonList)
                {
                    if (Normalize(item.text) == "继续")
                    {
                        // 使用统一的结算判定，如果是结算按钮则跳过（无论 result_enabled 是否开启）
                        if (IsSettlementButton(item.btn, buttonList))
                        {
                            continue;
                        }

                        target = item.btn;
                        kind = Kind.Announce;
                        info = item.text;
                        break;
                    }
                }
            }

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

            if (_cur != _lastTriggered || Time.unscaledTime - _lastTriggerTime > 3f)
            {
                float delay = kind == Kind.Battle ? _battleDelay
                    : kind == Kind.Result ? _resultDelay
                    : _announceDelay;
                if (Time.unscaledTime - _curFirstSeen >= delay)
                {
                    Click(target, kind, info);
                    _lastTriggered = _cur;
                    _lastTriggerTime = Time.unscaledTime;
                }
            }
        }

        /// <summary>
        /// 统一判断一个“继续”按钮是否属于结算界面。
        /// 规则：
        /// 1. 按钮自身或父级路径包含结算关键词（HuPaiJieSuan、FanXingJieSuan、Settlement、Result、结算、详情等）；
        /// 2. 同一父级或附近存在“详情”按钮（作为兜底）。
        /// </summary>
        private bool IsSettlementButton(Button continueBtn, List<(Button btn, string text)> allButtons)
        {
            // 检查路径关键词
            Transform t = continueBtn.transform;
            while (t != null)
            {
                string name = t.name;
                if (name.Contains("HuPaiJieSuan") || name.Contains("FanXingJieSuan")
                    || name.Contains("Settlement") || name.Contains("Result")
                    || name.Contains("结算") || name.Contains("详情"))
                {
                    return true;
                }
                t = t.parent;
            }

            // 检查同一父级下是否存在“详情”按钮
            Transform parent = continueBtn.transform.parent;
            if (parent != null)
            {
                foreach (var item in allButtons)
                {
                    if (item.btn == continueBtn) continue;
                    if (item.btn.transform.parent == parent && item.text.Contains("详情"))
                    {
                        return true;
                    }
                }
            }

            return false;
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

        private static string FirstLine(string s) => StringTruncator.FirstLine(s);

        private void LoadConfig()
        {
            try
            {
                var defaults = new Dictionary<string, string>
                {
                    ["announce_enabled"] = "true",
                    ["announce_delay"] = "2.0",
                    ["battle_enabled"] = "true",
                    ["battle_delay"] = "1.0",
                    ["result_enabled"] = "false",
                    ["result_delay"] = "5.0"
                };
                var cfg = YamlConfig.Load(CfgName, defaults);
                if (cfg.TryGetValue("announce_enabled", out string ae))
                    _announceEnabled = ParseBool(ae, _announceEnabled);
                if (cfg.TryGetValue("announce_delay", out string ad))
                    _announceDelay = ParseFloat(ad, _announceDelay);
                if (cfg.TryGetValue("battle_enabled", out string be))
                    _battleEnabled = ParseBool(be, _battleEnabled);
                if (cfg.TryGetValue("battle_delay", out string bd))
                    _battleDelay = ParseFloat(bd, _battleDelay);
                if (cfg.TryGetValue("result_enabled", out string re))
                    _resultEnabled = ParseBool(re, _resultEnabled);
                if (cfg.TryGetValue("result_delay", out string rd))
                    _resultDelay = ParseFloat(rd, _resultDelay);
            }
            catch (Exception e)
            {
                Log?.LogInfo("AutoContinue: cfg read failed: " + FirstLine(e.ToString()));
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

        private static float ParseFloat(string v, float d)
        {
            if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float r))
                return r < 0 ? 0 : r;
            return d;
        }
    }
}