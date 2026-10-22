using System;
using System.Collections.Generic;
using System.Globalization;
using Shared;

namespace AutoContinue
{
    /// <summary>
    /// AutoContinue.yml 的唯一配置入口：默认值 + 读取 + 解析。
    /// 所有键都列在 Defaults 中，配置文件缺失时自动生成默认模板；
    /// 配置文件已存在但缺某个键时，该键回落到这里的默认值。
    /// </summary>
    internal static class AutoConfig
    {
        internal static BepInEx.Logging.ManualLogSource Log;

        public const string FileName = "AutoContinue.yml";

        // 公告界面
        public static bool AnnounceEnabled = true;
        public static float AnnounceDelay = 2f;
        // Boss 战加载
        public static bool BattleEnabled = true;
        public static float BattleDelay = 1f;
        // 对局结算
        public static bool ResultEnabled = false;
        public static float ResultDelay = 5f;
        // 局外灵俑演出动画
        public static bool LingYongAnimEnabled = true;
        public static string LingYongAnimMode = "skip";   // skip=跳过 / hide=隐藏
        public static string LingYongAnimScope = "lobby"; // lobby=只局外 / all=含对局内
        public static bool LingYongAnimDebug = false;

        private static bool _loaded;

        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>
        {
            ["announce_enabled"] = "true",
            ["announce_delay"] = "2.0",
            ["battle_enabled"] = "true",
            ["battle_delay"] = "1.0",
            ["result_enabled"] = "false",
            ["result_delay"] = "5.0",
            ["lingyong_anim_enabled"] = "true",
            ["lingyong_anim_mode"] = "skip",
            ["lingyong_anim_scope"] = "lobby",
            ["lingyong_anim_debug"] = "false"
        };

        public static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                var cfg = YamlConfig.Load(FileName, Defaults);
                AnnounceEnabled = ParseBool(cfg["announce_enabled"], AnnounceEnabled);
                AnnounceDelay = ParseFloat(cfg["announce_delay"], AnnounceDelay);
                BattleEnabled = ParseBool(cfg["battle_enabled"], BattleEnabled);
                BattleDelay = ParseFloat(cfg["battle_delay"], BattleDelay);
                ResultEnabled = ParseBool(cfg["result_enabled"], ResultEnabled);
                ResultDelay = ParseFloat(cfg["result_delay"], ResultDelay);
                LingYongAnimEnabled = ParseBool(cfg["lingyong_anim_enabled"], LingYongAnimEnabled);
                LingYongAnimMode = (cfg["lingyong_anim_mode"] ?? "skip").Trim().ToLowerInvariant();
                LingYongAnimScope = (cfg["lingyong_anim_scope"] ?? "lobby").Trim().ToLowerInvariant();
                LingYongAnimDebug = ParseBool(cfg["lingyong_anim_debug"], LingYongAnimDebug);
            }
            catch (Exception e)
            {
                Log?.LogWarning("AutoContinue: cfg read failed: " + StringTruncator.FirstLine(e.ToString()));
            }
        }

        public static void LogSummary()
        {
            Log?.LogInfo("AutoContinue cfg: announce=" + AnnounceEnabled + "/d=" + AnnounceDelay
                + " battle=" + BattleEnabled + "/d=" + BattleDelay
                + " result=" + ResultEnabled + "/d=" + ResultDelay
                + " lingyong_anim=" + LingYongAnimEnabled + "/mode=" + LingYongAnimMode
                + "/scope=" + LingYongAnimScope
                + " debug=" + LingYongAnimDebug);
        }

        internal static bool ParseBool(string v, bool d)
        {
            if (string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(v, "false", StringComparison.OrdinalIgnoreCase)) return false;
            if (v == "1") return true;
            if (v == "0") return false;
            return d;
        }

        internal static float ParseFloat(string v, float d)
        {
            if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float r))
                return r < 0 ? 0 : r;
            return d;
        }
    }
}
