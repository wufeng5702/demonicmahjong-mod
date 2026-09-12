using BepInEx;
using BepInEx.Unity.IL2CPP;
using System.Collections.Generic;
using Shared;

namespace SLMenuTrigger
{
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, GitVersion.Version)]
    public class Plugin : BasePlugin
    {
        public new static BepInEx.Logging.ManualLogSource Log;

        public static bool Enabled = true;

        public override void Load()
        {
            Log = base.Log;
            AddComponent<MenuTriggerScript>();
        }
    }

    internal static class PluginInfo
    {
        public const string GUID = "wufeng.demonicmahjong.SLMenuTrigger";
        public const string Name = "SLMenuTrigger";
    }
}
