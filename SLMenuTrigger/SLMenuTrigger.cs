using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Shared;

namespace SLMenuTrigger
{
    public class MenuTriggerScript : MonoBehaviour
    {
        // ========== UI 路径常量 ==========
        private const string PlayerPath = "Canvas/ScoreBar/PlayerScore/Number";
        private const string AiPath = "Canvas/ScoreBar/AIScore/Number";
        private const string DeckPath = "Canvas/RoundStatistics/PaiLeftCountPanel/PlayerSwapPaiLeftCountText";
        private const string BossDeckPath = "Canvas/RoundStatistics/PaiLeftCountPanel/BossSwapPaiLeftCountText";
        private const long PlaceholderScore = 1234567;
        private const float PauseScale = 0f;  // 必须用 0，否则游戏检测到非零会立即恢复

        // ========== 缓存字段 ==========
        private TMP_Text _deckTextCache;
        private TMP_Text _bossDeckTextCache;

        // ========== 状态字段 ==========
        private int _fontSize = 24;
        private bool _triggered = false;
        private float _resumeCooldown = 0f;
        private bool _hasTriggeredThisRound = false;   // 本局是否已触发暂停
        private bool _deckWasZero = false;             // 上一帧牌堆是否为 0，用于检测新对局

        // 等待玩家总分更新
        private bool _waitingForPlayerScore = false;
        private float _waitStartTime;
        private const float WAIT_TIMEOUT = 0.5f; // 等待 0.5 秒，UI 更新足够

        // TimeScale 管理
        private float _savedTimeScale = 1f;      // 暂停前保存的 TimeScale

        // 监控日志去重
        private long _lastLogPlayerScore;
        private long _lastLogAiScore;
        private int _lastLogBossDeck;

        // ========== Unity 生命周期 ==========
        private void Awake()
        {
            LoadConfig();
        }

        private void Update()
        {
            // ---- 新对局检测：牌堆从 0 变为正数时重置“本局已触发”标志 ----
            int deckCount = GetDeckCount();
            if (deckCount > 0 && _deckWasZero)
            {
                _hasTriggeredThisRound = false;
                _waitingForPlayerScore = false;
                _deckWasZero = false;
                Plugin.Log.LogInfo("检测到新对局开始，重置触发标志。");
            }
            else if (deckCount == 0)
            {
                _deckWasZero = true;
            }
            // deckCount == -1 表示未找到 UI，忽略

            // 1. Mod 被禁用 → 强制恢复并重置等待状态（最高优先级）
            if (!Plugin.Enabled)
            {
                if (_triggered)
                {
                    Time.timeScale = _savedTimeScale;
                    _triggered = false;
                    Plugin.Log.LogInfo("Mod disabled. Game resumed. TimeScale=" + Time.timeScale);
                }
                _waitingForPlayerScore = false;
                return;
            }

            // 2. 如果已触发暂停，监听外部恢复（例如按ESC后继续）
            if (_triggered)
            {
                if (Time.timeScale != PauseScale)
                {
                    // 游戏自己解除了暂停（如玩家关闭菜单），抢回 TimeScale
                    Time.timeScale = _savedTimeScale;
                    _triggered = false;
                    _resumeCooldown = Time.unscaledTime + 2f;
                    Plugin.Log.LogInfo("Game resumed by other means. Restored TimeScale=" + _savedTimeScale + ". Cooldown 2s.");
                }
                // 无论是否恢复，都直接返回，避免继续执行后续检测
                return;
            }

            // 3. 本局已经处理过牌堆耗尽（无论是否触发暂停）→ 跳过所有检测
            if (_hasTriggeredThisRound)
                return;

            // 4. 如果游戏处于暂停状态（非我们引起的），不检测
            if (Time.timeScale == PauseScale) return;

            // 5. 冷却时间
            if (Time.unscaledTime < _resumeCooldown) return;

            // 6. 如果正在等待玩家总分更新
            if (_waitingForPlayerScore)
            {
                CheckScoresDuringWait();
                return;
            }

            // 7. 正常检测：检测牌堆是否耗尽
            if (deckCount == 0)   // 复用上面获取的 deckCount
            {
                _waitingForPlayerScore = true;
                _waitStartTime = Time.unscaledTime;
                _lastLogPlayerScore = long.MinValue; // 强制首次输出日志
                Plugin.Log.LogInfo("牌堆耗尽，等待总分更新...");
                return;
            }
        }

        // ========== 等待过程中检查总分 ==========
        private void CheckScoresDuringWait()
        {
            // 每次强制重新查找，不依赖缓存
            long playerScore = GetScoreDirect(PlayerPath);
            long aiScore = GetScoreDirect(AiPath);

            // 如果两者都不是占位符，说明 UI 已更新
            bool playerValid = (playerScore != PlaceholderScore);
            bool aiValid = (aiScore != PlaceholderScore);

            // 超时则强制使用当前值（即使为 0）
            bool timeout = (Time.unscaledTime - _waitStartTime > WAIT_TIMEOUT);

            if ((playerValid && aiValid) || timeout)
            {
                // 如果超时且仍然为占位符，则将其视为 0（但实际上不会，因为占位符一般很快消失）
                if (playerScore == PlaceholderScore) playerScore = 0;
                if (aiScore == PlaceholderScore) aiScore = 0;

                // 如果任一分数为 -1（UI 未找到），视为无效，不触发暂停
                if (playerScore < 0 || aiScore < 0)
                {
                    Plugin.Log.LogInfo($"牌堆耗尽，但分数读取异常 Player {playerScore} / Boss {aiScore}，跳过本次检测。");
                    _waitingForPlayerScore = false;
                    _hasTriggeredThisRound = true;
                }
                else if (playerScore < aiScore)
                {
                    Plugin.Log.LogInfo($"牌堆耗尽! Player {playerScore} < Boss {aiScore}. Pausing. TimeScale=" + Time.timeScale);
                    _savedTimeScale = (Time.timeScale > 0f) ? Time.timeScale : 1f;
                    _triggered = true;
                    Time.timeScale = PauseScale;
                    _waitingForPlayerScore = false;
                    _hasTriggeredThisRound = true;
                }
                else
                {
                    // 玩家分数 >= Boss，但 Boss 可能还有摸牌机会，继续监控
                    int bossDeck = GetBossDeckCount();
                    if (bossDeck == 0 || bossDeck < 0)
                    {
                        // Boss 也无牌可摸，安全
                        Plugin.Log.LogInfo($"牌堆耗尽，但玩家 {playerScore} >= Boss {aiScore}，不触发暂停。");
                        _waitingForPlayerScore = false;
                        _hasTriggeredThisRound = true;
                    }
                    else
                    {
                        // Boss 仍有摸牌机会，可能和牌逆转，继续监控
                        // 仅在分数或牌堆数变化时输出日志，避免刷屏
                        if (playerScore != _lastLogPlayerScore || aiScore != _lastLogAiScore || bossDeck != _lastLogBossDeck)
                        {
                            Plugin.Log.LogInfo($"玩家 {playerScore} >= Boss {aiScore}，但 Boss 仍有 {bossDeck} 次摸牌，继续监控...");
                            _lastLogPlayerScore = playerScore;
                            _lastLogAiScore = aiScore;
                            _lastLogBossDeck = bossDeck;
                        }
                        // 保持 _waitingForPlayerScore = true，下一帧继续检查
                    }
                }
            }
            // 若未满足条件（有效且未超时），则继续等待，不做任何操作
        }

        // ========== 直接读取分数（强制刷新） ==========
        private long GetScoreDirect(string path)
        {
            var texts = FindObjectsOfType<TMP_Text>(true);
            foreach (var t in texts)
            {
                if (t == null) continue;
                string tPath = GetPath(t.transform);
                if (tPath == path)
                {
                    string clean = CleanNumber(t.m_text).Replace(",", "");
                    if (long.TryParse(clean, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out long val))
                    {
                        return val;
                    }
                    return -1;
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
                _deckTextCache = FindTextByPath(DeckPath);
            }

            if (_deckTextCache != null && !string.IsNullOrEmpty(_deckTextCache.m_text))
            {
                string clean = CleanNumber(_deckTextCache.m_text).Replace(",", "");
                if (int.TryParse(clean, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int val))
                {
                    return val;
                }
            }
            return -1;
        }

        // ========== 读取 Boss 牌堆剩余数量（带缓存） ==========
        private int GetBossDeckCount()
        {
            if (_bossDeckTextCache == null || !_bossDeckTextCache.gameObject.activeInHierarchy)
            {
                _bossDeckTextCache = FindTextByPath(BossDeckPath);
            }

            if (_bossDeckTextCache != null && !string.IsNullOrEmpty(_bossDeckTextCache.m_text))
            {
                string clean = CleanNumber(_bossDeckTextCache.m_text).Replace(",", "");
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
        private string GetPath(Transform t) => TransformPath.GoPath(t);

        // ========== 清理数字字符串 ==========
        private string CleanNumber(string input) => NumberParser.CleanNumber(input);

        // ========== 暂停提示界面 ==========
        private void OnGUI()
        {
            if (!_triggered) return;

            // 备份原样式
            int oldFontSize = GUI.skin.label.fontSize;
            TextAnchor oldAlignment = GUI.skin.label.alignment;
            bool oldWordWrap = GUI.skin.label.wordWrap;
            try
            {
                // 设置样式：大字号、居中对齐、自动换行
                GUI.skin.label.fontSize = _fontSize;
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
            }
            finally
            {
                // 恢复原样式
                GUI.skin.label.fontSize = oldFontSize;
                GUI.skin.label.alignment = oldAlignment;
                GUI.skin.label.wordWrap = oldWordWrap;
            }
        }

        private void LoadConfig()
        {
            var defaults = new Dictionary<string, string>
            {
                ["enabled"] = "true",
                ["fontsize"] = "24"
            };
            var cfg = YamlConfig.Load("SLMenuTrigger.yml", defaults);
            if (cfg.TryGetValue("enabled", out string val))
            {
                if (bool.TryParse(val, out bool b))
                    Plugin.Enabled = b;
                else
                    Plugin.Log.LogWarning("Invalid enabled value, using default 'true'");
            }
            if (cfg.TryGetValue("fontsize", out string fs)
                && int.TryParse(fs, out int size))
            {
                _fontSize = size;
            }
            Plugin.Log.LogInfo("SLMenuTrigger cfg: enabled=" + Plugin.Enabled + " fontsize=" + _fontSize);
        }
    }
}