using HarmonyLib;
using MaJiang.PlayMaJiang.Player.PlayerXiaoChouPai;

namespace DlcBugFix
{
    internal static class QiLinProgress
    {
        internal static void RepairMissingKeys(QiLinDisplay qilin, string stage)
        {
            // OnXiaoChouLevelUp clears the dictionary; CheckKeAndShunAndDui and
            // CheckDui subsequently index it. Natural growth rebuilds all three
            // keys with false values (build 26325171, RVA 0x21D854B).
            var data = qilin._data;
            if (data == null) return;

            int added = 0;
            if (!data.ContainsKey(QiLinDisplay.Type.Shun)) { data.Add(QiLinDisplay.Type.Shun, false); added++; }
            if (!data.ContainsKey(QiLinDisplay.Type.Ke)) { data.Add(QiLinDisplay.Type.Ke, false); added++; }
            if (!data.ContainsKey(QiLinDisplay.Type.Dui)) { data.Add(QiLinDisplay.Type.Dui, false); added++; }
            if (added != 0)
                Plugin.Logger.LogInfo($"[qilin] {stage}: restored {added} progress keys; instance=0x{qilin.Pointer.ToInt64():X}");
        }

        internal static void Trace(QiLinDisplay qilin, string stage)
        {
            if (!Plugin.DebugEnabled) return;
            var data = qilin._data;
            var buff = qilin._independentBuffPayload;
            string Flag(QiLinDisplay.Type key) => data != null && data.TryGetValue(key, out bool value)
                ? value.ToString() : "missing";
            Plugin.Logger.LogInfo($"[qilin] {stage}: keys={data?.Count} shun={Flag(QiLinDisplay.Type.Shun)} ke={Flag(QiLinDisplay.Type.Ke)} dui={Flag(QiLinDisplay.Type.Dui)} level={qilin._level} buff={buff?.value}");
        }
    }

    [HarmonyPatch(typeof(QiLinDisplay), nameof(QiLinDisplay.OnXiaoChouLevelUp))]
    internal static class QiLinGrowthPatch
    {
        private static void Prefix(QiLinDisplay __instance) => QiLinProgress.Trace(__instance, "growth.begin");

        private static void Postfix(QiLinDisplay __instance)
        {
            QiLinProgress.RepairMissingKeys(__instance, "growth.end");
            QiLinProgress.Trace(__instance, "growth.end");
        }
    }

    [HarmonyPatch(typeof(QiLinDisplay), nameof(QiLinDisplay.CheckForBuff))]
    internal static class QiLinPlayingPatch
    {
        private static void Prefix(QiLinDisplay __instance)
        {
            // Also repairs an already-cleared dictionary before the next play.
            // Existing true values remain intact, including partial progress.
            QiLinProgress.RepairMissingKeys(__instance, "playing.begin");
            QiLinProgress.Trace(__instance, "playing.begin");
        }

        private static void Postfix(QiLinDisplay __instance) => QiLinProgress.Trace(__instance, "playing.end");
    }
}
