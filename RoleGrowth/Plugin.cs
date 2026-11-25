using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace RoleGrowth
{
    /// <summary>
    /// 修复角色主动技能「局内永久成长」重启丢失（首个目标：水晶兰·贩酒）。
    /// 把成长值存到 BepInEx/plugins/RoleGrowthState.json，key = 存档槽:技能id。
    /// </summary>
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, GitVersion.Version)]
    public class Plugin : BasePlugin
    {
        public override void Load()
        {
            Log.LogInfo("[" + PluginInfo.Name + "] v" + GitVersion.Version + " loaded");
            GrowthStore.Log = Log;
            GrowthStore.Init(Paths.PluginPath);
            Log.LogInfo("[RoleGrowth] current slot=" + GrowthStore.CurrentSlot()
                + " statePath=" + System.IO.Path.Combine(Paths.PluginPath, "RoleGrowthState.json"));
            new Harmony(PluginInfo.GUID).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo("RoleGrowth: patches applied");
        }
    }

    internal static class PluginInfo
    {
        public const string GUID = "wufeng.demonicmahjong.rolegrowth";
        public const string Name = "RoleGrowth";
    }
}
