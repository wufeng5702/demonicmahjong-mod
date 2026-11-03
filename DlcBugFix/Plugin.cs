using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Shared;

namespace DlcBugFix
{
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, GitVersion.Version)]
    public class Plugin : BasePlugin
    {
        internal static ManualLogSource Logger;
        internal static bool DebugEnabled;

        public override void Load()
        {
            Logger = Log;
            var config = YamlConfig.Load("DlcBugFix.yml", new Dictionary<string, string>
            {
                ["qilin_growth"] = "true",
                ["sequential_sword_damage"] = "true",
                ["debug"] = "false",
            });
            bool qilin = ReadBool(config, "qilin_growth", true);
            bool sword = ReadBool(config, "sequential_sword_damage", true);
            DebugEnabled = ReadBool(config, "debug", false);

            var harmony = new Harmony(PluginInfo.GUID);
            if (qilin)
            {
                harmony.CreateClassProcessor(typeof(QiLinGrowthPatch)).Patch();
                harmony.CreateClassProcessor(typeof(QiLinPlayingPatch)).Patch();
            }
            if (sword)
            {
                harmony.CreateClassProcessor(typeof(SwordRevivePatch)).Patch();
                harmony.CreateClassProcessor(typeof(CascadeHpChangePatch)).Patch();
            }
            if (DebugEnabled)
            {
                harmony.CreateClassProcessor(typeof(ReviveCompletionTrace)).Patch();
                harmony.CreateClassProcessor(typeof(ReviveItemTrace)).Patch();
            }
            Log.LogInfo($"[{PluginInfo.Name}] v{GitVersion.Version} loaded; qilin={qilin} sword={sword} debug={DebugEnabled}");
        }

        private static bool ReadBool(Dictionary<string, string> config, string key, bool fallback)
        {
            return config.TryGetValue(key, out string text) && bool.TryParse(text, out bool value)
                ? value : fallback;
        }
    }

    internal static class PluginInfo
    {
        public const string GUID = "wufeng.demonicmahjong.dlcbugfix";
        public const string Name = "DlcBugFix";
    }
}
