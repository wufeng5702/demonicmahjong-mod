using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace ScorePreview
{
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BasePlugin
    {
        public override void Load()
        {
            Log.LogInfo($"[{PluginInfo.Name}] v{PluginInfo.Version} loaded");
            ScoreHud.Log = Log;

            var harmony = new Harmony(PluginInfo.GUID);
            harmony.PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo("harmony patches applied");

            AddComponent<ScoreHud>();
        }
    }
}