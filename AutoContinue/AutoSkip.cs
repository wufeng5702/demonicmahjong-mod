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
    /// 自动跳过三个「等玩家点一下」的环节，以及预战面板快捷键：
    ///   1) 启动后的公告界面 → 自动点【继续】进大厅；
    ///   2) 与 Boss 对决加载完成后底部【点击继续】→ 自动点击进对局；
    ///   3) 对局结算后的结算/查看详情界面 → 自动点【继续】；
    ///   4) 随机模式开始时的角色轮播动画 → 加速到 random_scroll_time 秒（默认 0.05，近乎瞬时落点；功能默认关闭）；
    ///   5) 预战面板快捷键：random_jump_key 直跳随机角色卡（默认 ↑/↓），start_game_key 开始游戏（默认 空格）。
    ///      键位可配（逗号分隔 KeyCode 名，支持鼠标侧键 Mouse3=第一个侧键 Mouse4=第二个侧键）。
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
        private bool _randomScrollEnabled = false;
        private float _randomScrollTime = 0.05f;
        private float _lastRandomLog;
        private bool _randomJumpEnabled = true;
        private List<KeyCode> _randomJumpKeys = new List<KeyCode> { KeyCode.UpArrow, KeyCode.DownArrow };
        private bool _startGameEnabled = true;
        private List<KeyCode> _startGameKeys = new List<KeyCode> { KeyCode.Space };
        private bool _preWarKeysBroken;

        private float _nextScan;

        private enum Kind { None, Announce, Battle, Result, Start }
        private Kind _cur;
        private float _curFirstSeen;
        private Kind _lastTriggered;
        private float _lastTriggerTime;

        private void Awake()
        {
            LoadConfig();
        }

        private void Update()
        {
            // 预战面板快捷键：GetKeyDown 必须逐帧抓取，不能走下面的 0.25s 扫描节奏
            if (!_preWarKeysBroken && (_randomJumpEnabled || _startGameEnabled))
                HandlePreWarKeys();

            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + ScanInterval;
            if (!_announceEnabled && !_battleEnabled && !_resultEnabled && !_randomScrollEnabled) return;

            if (_randomScrollEnabled)
                TryAccelerateRandomScroll();

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
                    if (UiText.Normalize(item.text) == "继续")
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
                    if (UiText.Normalize(item.text) == "继续")
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
        [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
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

        /// <summary>
        /// 随机模式开始时，把预战面板里角色选择器的轮播总时长压到 random_scroll_time。
        /// 游戏自身仍负责选中随机角色并触发后续流程，只是动画近乎瞬时完成。
        /// </summary>
        [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
        private void TryAccelerateRandomScroll()
        {
            try
            {
                var selectors = UnityEngine.Object.FindObjectsOfType<CharacterSelectorCtrl>();
                foreach (var selector in selectors)
                {
                    if (selector == null) continue;
                    float cur = selector.randomScrollTime;
                    if (cur <= _randomScrollTime) continue;
                    selector.randomScrollTime = _randomScrollTime;
                    if (Time.unscaledTime - _lastRandomLog > 1f)
                    {
                        _lastRandomLog = Time.unscaledTime;
                        Log?.LogInfo("AutoContinue: random scroll accelerated randomScrollTime " + cur + " -> " + _randomScrollTime);
                    }
                }
            }
            catch (Exception e)
            {
                if (Time.unscaledTime - _lastRandomLog > 5f)
                {
                    _lastRandomLog = Time.unscaledTime;
                    Log?.LogWarning("AutoContinue: random scroll skip failed: " + FirstLine(e.ToString()));
                }
            }
        }

        /// <summary>
        /// 预战面板快捷键。legacy Input 后端若被游戏关闭会抛异常；捕获后一次性禁用并告警，
        /// 不至于每帧刷 InvalidOperationException。
        /// </summary>
        [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
        private void HandlePreWarKeys()
        {
            try
            {
                if (_randomJumpEnabled && AnyKeyDown(_randomJumpKeys))
                    TryJumpToRandomCharacter();
                if (_startGameEnabled && AnyKeyDown(_startGameKeys))
                    TryClickStartGame();
            }
            catch (Exception e)
            {
                _preWarKeysBroken = true;
                Log?.LogWarning("AutoContinue: prewar hotkeys disabled (input backend error): " + FirstLine(e.ToString()));
            }
        }

        [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
        private static bool AnyKeyDown(List<KeyCode> keys)
        {
            for (int i = 0; i < keys.Count; i++)
                if (UnityEngine.Input.GetKeyDown(keys[i]))
                    return true;
            return false;
        }

        /// <summary>
        /// 直跳预战面板里的随机角色卡（CharacterRandomCard），落点与正常点击卡牌一致。
        /// </summary>
        [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
        private void TryJumpToRandomCharacter()
        {
            try
            {
                var selectors = UnityEngine.Object.FindObjectsOfType<CharacterSelectorCtrl>();
                foreach (var selector in selectors)
                {
                    if (selector == null || !selector.gameObject.activeInHierarchy) continue;
                    var cards = selector.cardInstances;
                    if (cards == null) continue;
                    int count = cards.Count;
                    int found = -1;
                    for (int i = 0; i < count; i++)
                    {
                        var card = cards[i];
                        if (card == null) continue;
                        bool isRandom = card.TryCast<MaJiang.UICtrl.PreWarPanel.CharacterRandomCard>() != null;
                        if (!isRandom && card.gameObject != null)
                        {
                            string n = card.gameObject.name ?? "";
                            isRandom = n.IndexOf("Random", StringComparison.OrdinalIgnoreCase) >= 0 || n.Contains("随机");
                        }
                        if (isRandom) { found = i; break; }
                    }
                    if (found < 0)
                    {
                        Log?.LogWarning("AutoContinue: prewar jump — carousel has no random card (count=" + count + ")");
                        continue;
                    }
                    selector.OnCardClick(found);
                    Log?.LogInfo("AutoContinue: prewar jump -> random card index=" + found + "/" + count);
                }
            }
            catch (Exception e)
            {
                Log?.LogWarning("AutoContinue: prewar jump failed: " + FirstLine(e.ToString()));
            }
        }

        /// <summary>
        /// 预战面板内按钮文本含“开始”的，等同于点击开始按钮。限定路径含 PreWar，避免误触其他面板。
        /// </summary>
        [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
        private void TryClickStartGame()
        {
            try
            {
                var btns = UnityEngine.Object.FindObjectsOfType<Button>();
                foreach (var b in btns)
                {
                    if (b == null || !b.gameObject.activeInHierarchy) continue;
                    string path = TransformPath.GoPath(b.transform);
                    if (path.IndexOf("PreWar", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    string text = ButtonText(b);
                    if (UiText.Normalize(text).Contains("开始"))
                        Click(b, Kind.Start, text);
                }
            }
            catch (Exception e)
            {
                Log?.LogWarning("AutoContinue: prewar start-game click failed: " + FirstLine(e.ToString()));
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
                    ["result_delay"] = "5.0",
                    ["random_scroll_enabled"] = "false",
                    ["random_scroll_time"] = "0.05",
                    ["random_jump_enabled"] = "true",
                    ["random_jump_key"] = "UpArrow,DownArrow",
                    ["start_game_enabled"] = "true",
                    ["start_game_key"] = "Space"
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
                if (cfg.TryGetValue("random_scroll_enabled", out string rae))
                    _randomScrollEnabled = ParseBool(rae, _randomScrollEnabled);
                if (cfg.TryGetValue("random_scroll_time", out string rst))
                {
                    _randomScrollTime = ParseFloat(rst, _randomScrollTime);
                    if (_randomScrollTime < 0.01f)
                        _randomScrollTime = 0.01f;
                }
                if (cfg.TryGetValue("random_jump_enabled", out string rje))
                    _randomJumpEnabled = ParseBool(rje, _randomJumpEnabled);
                if (cfg.TryGetValue("random_jump_key", out string rjk))
                    _randomJumpKeys = ParseKeyList(rjk, _randomJumpKeys);
                if (cfg.TryGetValue("start_game_enabled", out string sge))
                    _startGameEnabled = ParseBool(sge, _startGameEnabled);
                if (cfg.TryGetValue("start_game_key", out string sgk))
                    _startGameKeys = ParseKeyList(sgk, _startGameKeys);
            }
            catch (Exception e)
            {
                Log?.LogWarning("AutoContinue: cfg read failed: " + FirstLine(e.ToString()));
            }
            Log?.LogInfo("AutoContinue cfg: announce=" + _announceEnabled + "/d=" + _announceDelay
                + " battle=" + _battleEnabled + "/d=" + _battleDelay
                + " result=" + _resultEnabled + "/d=" + _resultDelay
                + " random_scroll=" + _randomScrollEnabled + "/t=" + _randomScrollTime
                + " random_jump=" + _randomJumpEnabled + "/" + KeyListText(_randomJumpKeys)
                + " start_game=" + _startGameEnabled + "/" + KeyListText(_startGameKeys));
        }

        /// <summary>解析逗号分隔的 KeyCode 列表；全是无效名时保留 fallback。</summary>
        private static List<KeyCode> ParseKeyList(string value, List<KeyCode> fallback)
        {
            var list = new List<KeyCode>();
            if (!string.IsNullOrWhiteSpace(value))
            {
                foreach (var raw in value.Split(','))
                {
                    string s = raw.Trim();
                    if (s.Length == 0) continue;
                    if (Enum.TryParse<KeyCode>(s, true, out var kc))
                    {
                        list.Add(kc);
                    }
                    else
                    {
                        Log?.LogWarning("AutoContinue: unknown key name skipped: " + s);
                    }
                }
            }
            return list.Count > 0 ? list : fallback;
        }

        private static string KeyListText(List<KeyCode> keys)
        {
            var parts = new string[keys.Count];
            for (int i = 0; i < keys.Count; i++)
                parts[i] = keys[i].ToString();
            return string.Join("+", parts);
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
