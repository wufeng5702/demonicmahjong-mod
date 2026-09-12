using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace AutoContinue
{
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, GitVersion.Version)]
    public class Plugin : BasePlugin
    {
        public override void Load()
        {
            AutoSkip.Log = Log;
            AddComponent<AutoSkip>();
            Log.LogInfo($"[{PluginInfo.Name}] v{GitVersion.Version} loaded");
        }
    }

    internal static class PluginInfo
    {
        public const string GUID = "wufeng.demonicmahjong.AutoContinue";
        public const string Name = "AutoContinue";
    }
}