using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SLMenuTrigger
{
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, GitVersion.Version)]
    public class Plugin : BasePlugin
    {
        // 静态访问点（MenuTriggerScript 需要从非实例上下文打日志）；刻意不叫 Log，避免隐藏基类成员
        public static BepInEx.Logging.ManualLogSource Logger;

        public static bool Enabled = true;

        public override void Load()
        {
            Logger = Log;
            AddComponent<MenuTriggerScript>();
            Log.LogInfo($"[{PluginInfo.Name}] v{GitVersion.Version} loaded");
        }
    }

    internal static class PluginInfo
    {
        public const string GUID = "wufeng.demonicmahjong.SLMenuTrigger";
        public const string Name = "SLMenuTrigger";
    }
}
