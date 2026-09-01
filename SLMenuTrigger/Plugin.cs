using BepInEx;
using BepInEx.Unity.IL2CPP;
using System.IO;
using System;

namespace SLMenuTrigger
{
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BasePlugin
    {
        // 保存自身实例，供静态方法调用
        public static Plugin Instance;
        // 用 new 隐藏基类 Log，避免警告
        public new static BepInEx.Logging.ManualLogSource Log;

        private static string ConfigPath => Path.Combine(Paths.ConfigPath, "SLMenuTrigger.yml");
        public static bool Enabled = true;

        public override void Load()
        {
            Instance = this;
            Log = base.Log;
            LoadConfig();
            Log.LogInfo($"{PluginInfo.Name} v{PluginInfo.Version} loaded. enabled={Enabled}");
            AddComponent<MenuTriggerScript>();
        }

        private void LoadConfig()
        {
            if (!File.Exists(ConfigPath))
            {
                File.WriteAllText(ConfigPath, "enabled: true");
                Enabled = true;
                return;
            }

            try
            {
                string[] lines = File.ReadAllLines(ConfigPath);
                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("#") || string.IsNullOrEmpty(trimmed)) continue;
                    int idx = trimmed.IndexOf(':');
                    if (idx < 0) continue;
                    string key = trimmed.Substring(0, idx).Trim().ToLowerInvariant();
                    string val = trimmed.Substring(idx + 1).Trim().ToLowerInvariant();

                    if (key == "enabled")
                    {
                        if (bool.TryParse(val, out bool b))
                            Enabled = b;
                        else
                            Log.LogWarning("Invalid enabled value, using default 'true'");
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                Log.LogError($"Config load error: {e.Message}");
            }
        }

        public static void ReloadConfig()
        {
            if (Instance != null)
            {
                Instance.LoadConfig();
                Log.LogInfo($"Config reloaded: enabled={Enabled}");
            }
        }
    }
}