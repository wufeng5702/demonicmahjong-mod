using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using Shared;

namespace InventoryStats
{
    [BepInPlugin("wufeng.demonicmahjong.inventorystats", "InventoryStats", GitVersion.Version)]
    public class Plugin : BasePlugin
    {
        public override void Load()
        {
            var cfg = YamlConfig.Load("InventoryStats.yml", new Dictionary<string, string>
            {
                ["display"] = "bag",
                ["yoffset"] = "0",
                ["fontsize"] = "24"
            });
            string display = cfg["display"];
            InventoryHud.AlwaysShow = string.Equals(display, "always", StringComparison.OrdinalIgnoreCase);
            if (!InventoryHud.AlwaysShow && !string.Equals(display, "bag", StringComparison.OrdinalIgnoreCase))
                Log.LogWarning($"InventoryStats: invalid display '{display}', using bag");

            InventoryHud.YOffset = float.TryParse(cfg["yoffset"], NumberStyles.Float, CultureInfo.InvariantCulture, out float offset)
                && float.IsFinite(offset) ? Math.Clamp(offset, 0f, 1f) : 0f;
            InventoryHud.FontSize = int.TryParse(cfg["fontsize"], out int fontSize) ? Math.Clamp(fontSize, 8, 96) : 24;

            InventoryHud.Log = Log;
            AddComponent<InventoryHud>();
            Log.LogInfo($"[InventoryStats] v{GitVersion.Version} loaded, display={(InventoryHud.AlwaysShow ? "always" : "bag")}");
        }
    }
}
