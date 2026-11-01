using System.Collections.Generic;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Shared;

namespace BanLingYongLimit
{
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, GitVersion.Version)]
    public class Plugin : BasePlugin
    {
        public override void Load()
        {
            Log.LogInfo($"[{PluginInfo.Name}] v{GitVersion.Version} loaded");
            var cfg = YamlConfig.Load("BanLingYongLimit.yml", new Dictionary<string, string>
            {
                ["enabled"] = "true",
                ["count"] = "10"
            });
            bool enabled = true;
            if (bool.TryParse(cfg["enabled"], out bool parsedEnabled)) enabled = parsedEnabled;
            if (!int.TryParse(cfg["count"], out int extraCount))
            {
                Log.LogWarning("BanLingYongLimit: invalid extra count, using 10");
                extraCount = 10;
            }
            extraCount = System.Math.Max(0, extraCount);

            QuotaPatch.ExtraCount = extraCount;
            QuotaPatch.Log = Log;
            Log.LogInfo($"BanLingYongLimit cfg: enabled={enabled} count={extraCount} (extra quota; unlocked talents only)");
            if (!enabled) return;

            new Harmony(PluginInfo.GUID).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo("harmony patches applied");
        }
    }

    internal static class PluginInfo
    {
        public const string GUID = "wufeng.demonicmahjong.banlingyonglimit";
        public const string Name = "BanLingYongLimit";
    }
}
