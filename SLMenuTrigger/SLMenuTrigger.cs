using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace SLMenuTrigger
{
    public class MenuTriggerScript : MonoBehaviour
    {
        // ========== UI 路径常量 ==========
        private readonly string _playerPath = "Canvas/ScoreBar/PlayerScore/Number";
        private readonly string _aiPath = "Canvas/ScoreBar/AIScore/Number";
        private readonly string _deckPath = "Canvas/RoundStatistics/PaiLeftCountPanel/PlayerSwapPaiLeftCountText";

        // ========== 缓存字段 ==========
        private TMP_Text _playerTextCache;
        private TMP_Text _aiTextCache;
        private TMP_Text _deckTextCache;

        // ========== 状态字段 ==========
        private bool _triggered = false;
        private float _resumeCooldown = 0f;

        // ========== Unity 生命周期 ==========
        private void Update()
        {
            // 1. Mod 被禁用 → 强制恢复
            if (!Plugin.Enabled)
            {
                if (_triggered)
                {
                    Time.timeScale = 1f;
                    _triggered = false;
                    Plugin.Log.LogInfo("Mod disabled. Game resumed.");
                }
                return;
            }

            // 2. 如果已触发暂停，监听恢复条件
            if (_triggered)
            {
                if (Time.timeScale == 1f)
                {
                    _triggered = false;
                    _resumeCooldown = Time.unscaledTime + 2f;
                    Plugin.Log.LogInfo("Game resumed by other means. Cooldown 2s.");
                }
                return;
            }

            // 3. 如果游戏处于暂停状态（非我们引起的），不检测
            if (Time.timeScale == 0f) return;

            // 4. 冷却时间
            if (Time.unscaledTime < _resumeCooldown) return;

            // 5. 正常检测
            CheckDeckAndTrigger();
        }

        private void CheckDeckAndTrigger()
        {
            int deckCount = GetDeckCount();
            if (deckCount != 0) return;

            // 牌堆耗尽，读取双方分数
            int playerScore = GetCachedScore(ref _playerTextCache, _playerPath);
            int aiScore = GetCachedScore(ref _aiTextCache, _aiPath);

            if (playerScore <= 0 || aiScore <= 0) return;
            if (playerScore == 1234567 || aiScore == 1234567) return;

            if (playerScore < aiScore)
            {
                Plugin.Log.LogInfo($"牌堆耗尽! Player {playerScore} < Boss {aiScore}. Pausing.");
                _triggered = true;
                Time.timeScale = 0f;
            }
            else
            {
                Plugin.Log.LogInfo($"牌堆耗尽，但玩家 {playerScore} >= Boss {aiScore}，不触发暂停。");
            }
        }

        private int GetDeckCount()
        {
            // 检查缓存是否有效，如果无效则重新查找
            if (_deckTextCache == null || !_deckTextCache.gameObject.activeInHierarchy)
            {
                _deckTextCache = FindTextByPath(_deckPath);
            }

            if (_deckTextCache != null && !string.IsNullOrEmpty(_deckTextCache.m_text))
            {
                string clean = CleanNumber(_deckTextCache.m_text);
                if (int.TryParse(clean, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int val))
                {
                    return val;
                }
            }
            return -1;
        }

        private int GetCachedScore(ref TMP_Text cache, string targetPath)
        {
            // 检查缓存是否有效，如果无效则重新查找
            if (cache == null || !cache.gameObject.activeInHierarchy)
            {
                cache = FindTextByPath(targetPath);
            }

            if (cache != null && !string.IsNullOrEmpty(cache.m_text))
            {
                string clean = CleanNumber(cache.m_text);
                if (int.TryParse(clean, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int val))
                {
                    return val;
                }
            }
            return -1;
        }

        // 通用方法：根据路径查找 TMP_Text（不缓存）
        private TMP_Text FindTextByPath(string path)
        {
            var texts = FindObjectsOfType<TMP_Text>(true);
            foreach (var t in texts)
            {
                if (t != null && GetPath(t.transform) == path)
                {
                    return t;
                }
            }
            return null;
        }

        private string GetPath(Transform t)
        {
            var names = new List<string>();
            while (t != null)
            {
                names.Add(t.name);
                t = t.parent;
            }
            names.Reverse();
            return string.Join("/", names);
        }

        private string CleanNumber(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            bool inTag = false;
            var sb = new System.Text.StringBuilder();
            foreach (char c in input)
            {
                if (c == '<') { inTag = true; continue; }
                if (c == '>') { inTag = false; continue; }
                if (inTag) continue;
                if (char.IsDigit(c) || c == '.') sb.Append(c);
            }
            return sb.ToString().Replace(",", "");
        }

        // ========== 大号提示框 ==========
        private void OnGUI()
        {
            if (!_triggered) return;

            // 备份原样式
            int oldFontSize = GUI.skin.label.fontSize;
            TextAnchor oldAlignment = GUI.skin.label.alignment;
            bool oldWordWrap = GUI.skin.label.wordWrap;

            // 设置样式：大字号、居中对齐、自动换行
            GUI.skin.label.fontSize = 24;
            GUI.skin.label.alignment = TextAnchor.MiddleCenter;
            GUI.skin.label.wordWrap = true;

            // 使用屏幕比例计算框大小：宽 60%，高 15%（确保足够显示两行文字）
            int width = (int)(Screen.width * 0.60f);
            int height = (int)(Screen.height * 0.15f);
            int x = (Screen.width - width) / 2;
            int y = (Screen.height - height) / 2;

            GUI.Box(new Rect(x, y, width, height), "");

            string message = "⚠ 当前分数落后，游戏已暂停。\n按 ESC 打开菜单，选择 SL 或 解除菜单让游戏继续。";

            // 文字区域留边距 20px
            GUI.Label(new Rect(x + 20, y + 10, width - 40, height - 20), message);

            // 恢复原样式
            GUI.skin.label.fontSize = oldFontSize;
            GUI.skin.label.alignment = oldAlignment;
            GUI.skin.label.wordWrap = oldWordWrap;
        }
    }
}