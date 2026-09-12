using BepInEx;
using BepInEx.Unity.IL2CPP;
using System.Collections.Generic;
using Shared;

namespace SLMenuTrigger
{
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BasePlugin
    {
        public new static BepInEx.Logging.ManualLogSource Log;

        public static bool Enabled = true;

        public override void Load()
        {
            Log = base.Log;
            LoadConfig();
            Log.LogInfo($"{PluginInfo.Name} v{PluginInfo.Version} loaded. enabled={Enabled}");
            AddComponent<MenuTriggerScript>();
        }

        private void LoadConfig()
        {
            var defaults = new Dictionary<string, string> { ["enabled"] = "true" };
            var cfg = YamlConfig.Load("SLMenuTrigger.yml", defaults);
            if (cfg.TryGetValue("enabled", out string val))
            {
                if (bool.TryParse(val, out bool b))
                    Enabled = b;
                else
                    Log.LogWarning("Invalid enabled value, using default 'true'");
            }
        }
    }
}
