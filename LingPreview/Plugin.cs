using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace LingPreview
{
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, GitVersion.Version)]
    public class Plugin : BasePlugin
    {
        public override void Load()
        {
            Log.LogInfo($"[{PluginInfo.Name}] v{GitVersion.Version} loaded");
            LingPanel.Log = Log;

            AddComponent<LingPanel>();
        }
    }

    internal static class PluginInfo
    {
        public const string GUID = "wufeng.demonicmahjong.lingpreview";
        public const string Name = "LingPreview";
    }
}
