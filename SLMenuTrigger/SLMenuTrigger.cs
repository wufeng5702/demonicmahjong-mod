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
        private TMP_Text _deckTextCache;

        // ========== 状态字段 ==========
        private bool _triggered = false;
        private float _resumeCooldown = 0f;
        private bool _hasTriggeredThisRound = false;   // 新增：本局是否已触发过暂停

        // 等待玩家总分更新
        private bool _waitingForPlayerScore = false;
        private float _waitStartTime;
        private const float WAIT_TIMEOUT = 0.5f; // 等待 0.5 秒，UI 更新足够

        // ========== Unity 生命周期 ==========
        private void Update()
        {
            // 1. Mod 被禁用 → 强制恢复并重置等待状态（最高优先级）
            if (!Plugin.Enabled)
            {
                if (_triggered)
                {
                    Time.timeScale = 1f;
                    _triggered = false;
                    Plugin.Log.LogInfo("Mod disabled. Game resumed.");
                }
                _waitingForPlayerScore = false;
                return;
            }

            // 2. 如果已触发暂停，监听外部恢复（例如按ESC后继续）
            if (_triggered)
            {
                if (Time.timeScale == 1f)
                {
                    _triggered = false;                     // 清除暂停状态，提示框消失
                    _resumeCooldown = Time.unscaledTime + 2f;
                    Plugin.Log.LogInfo("Game resumed by other means. Cooldown 2s.");
                }
                // 无论是否恢复，都直接返回，避免继续执行后续检测
                return;
            }

            // 3. 本局已经处理过牌堆耗尽（无论是否触发暂停）→ 跳过所有检测
            if (_hasTriggeredThisRound)
                return;

            // 4. 如果游戏处于暂停状态（非我们引起的），不检测
            if (Time.timeScale == 0f) return;

            // 5. 冷却时间
            if (Time.unscaledTime < _resumeCooldown) return;

            // 6. 如果正在等待玩家总分更新
            if (_waitingForPlayerScore)
            {
                CheckScoresDuringWait();
                return;
            }

            // 7. 正常检测：检测牌堆是否耗尽
            int deckCount = GetDeckCount();
            if (deckCount == 0)
            {
                _waitingForPlayerScore = true;
                _waitStartTime = Time.unscaledTime;
                Plugin.Log.LogInfo("牌堆耗尽，等待总分更新...");
                return;
            }
        }

        // ========== 等待过程中检查总分 ==========
        private void CheckScoresDuringWait()
        {
            // 每次强制重新查找，不依赖缓存
            int playerScore = GetScoreDirect(_playerPath);
            int aiScore = GetScoreDirect(_aiPath);

            // 如果两者都不是占位符（1234567），说明 UI 已更新
            bool playerValid = (playerScore != 1234567);
            bool aiValid = (aiScore != 1234567);

            // 超时则强制使用当前值（即使为 0）
            bool timeout = (Time.unscaledTime - _waitStartTime > WAIT_TIMEOUT);

            if ((playerValid && aiValid) || timeout)
            {
                _waitingForPlayerScore = false;

                // 如果超时且仍然为占位符，则将其视为 0（但实际上不会，因为占位符一般很快消失）
                if (playerScore == 1234567) playerScore = 0;
                if (aiScore == 1234567) aiScore = 0;

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

                // 无论是否触发暂停，都标记本局已处理过牌堆耗尽，不再重复检测
                _hasTriggeredThisRound = true;
            }
            // 若未满足条件（有效且未超时），则继续等待，不做任何操作
        }

        // ========== 直接读取分数（强制刷新） ==========
        private int GetScoreDirect(string path)
        {
            var texts = FindObjectsOfType<TMP_Text>(true);
            foreach (var t in texts)
            {
                if (t != null && GetPath(t.transform) == path)
                {
                    string clean = CleanNumber(t.m_text);
                    if (int.TryParse(clean, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out int val))
                    {
                        return val;
                    }
                }
            }
            return -1;
        }

        // ========== 读取牌堆剩余数量（带缓存） ==========
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

        // ========== 根据路径查找 TMP_Text ==========
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

        // ========== 获取 UI 路径 ==========
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

        // ========== 清理数字字符串 ==========
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

        // ========== 暂停提示界面 ==========
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