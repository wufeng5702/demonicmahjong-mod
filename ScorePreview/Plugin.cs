using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace ScorePreview
{
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, GitVersion.Version)]
    public class Plugin : BasePlugin
    {
        public override void Load()
        {
            Log.LogInfo($"[{PluginInfo.Name}] v{GitVersion.Version} loaded");
            ScoreHud.Log = Log;

            var harmony = new Harmony(PluginInfo.GUID);
            harmony.PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo("harmony patches applied");

            AddComponent<ScoreHud>();
        }
    }

    internal static class PluginInfo
    {
        public const string GUID = "wufeng.demonicmahjong.scorepreview";
        public const string Name = "ScorePreview";
    }
}