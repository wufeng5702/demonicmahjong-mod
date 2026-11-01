using BepInEx.Logging;
using HarmonyLib;
using MaJiang.Talent;

namespace BanLingYongLimit
{
    [HarmonyPatch(typeof(TalentManager), nameof(TalentManager.TalentInt))]
    internal static class QuotaPatch
    {
        internal static int ExtraCount = 10;
        internal static ManualLogSource Log;
        private static readonly bool[] Reported = new bool[4];

        [HarmonyPostfix]
        private static void Postfix(TalentType __0, ref int __result)
        {
            int index = (int)__0 - (int)TalentType.BanLingYong1;
            if (index < 0 || index >= Reported.Length || __result <= 0) return;

            int original = __result;
            __result = (int)System.Math.Min(int.MaxValue, (long)original + ExtraCount);
            if (Reported[index]) return;
            Reported[index] = true;
            Log.LogInfo($"BanLingYongLimit: {__0} quota {original} -> {__result}");
        }
    }
}
