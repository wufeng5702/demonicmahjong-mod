using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace AutoContinue
{
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BasePlugin
    {
        public override void Load()
        {
            AutoSkip.Log = Log;
            AddComponent<AutoSkip>();
            Log.LogInfo($"[{PluginInfo.Name}] v{PluginInfo.Version} loaded");
        }
    }

    internal static class PluginInfo
    {
        public const string GUID = "com.local.autocontinue";
        public const string Name = "AutoContinue";
        public const string Version = "0.1.0";
    }
}