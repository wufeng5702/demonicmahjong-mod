using System;
using HarmonyLib;
using Il2CppInterop.Runtime;
using MaJiang;
using MaJiang.GameMap;
using MaJiang.PlayMaJiang.XiaoChouPaiCommon.Interfaces;

namespace DlcBugFix
{
    internal static class ReviveTrace
    {
        internal static string Life()
        {
            try
            {
                var data = GlobalDataCenter.Instance;
                return data == null ? "data=null"
                    : $"hp={data.GetData<int>(GlobalDataType.Hp)} max={data.GetData<int>(GlobalDataType.HpMax)} soul={data.GetData<int>(GlobalDataType.Soul)}";
            }
            catch (Exception ex) { return $"snapshot unavailable: {ex.Message}"; }
        }
    }

    [HarmonyPatch(typeof(GameMapMgr), nameof(GameMapMgr.NeedTryRevive))]
    internal static class ReviveCompletionTrace
    {
        private static void Prefix(ref Il2CppSystem.Action<bool> __0)
        {
            Plugin.Logger.LogInfo($"[revive] requested {ReviveTrace.Life()}");
            var callback = __0;
            // Observe the actual completion callback, not iterator creation.
            __0 = (System.Action<bool>)(success =>
            {
                Plugin.Logger.LogInfo($"[revive] completed success={success} {ReviveTrace.Life()}");
                callback?.Invoke(success);
            });
        }
    }

    [HarmonyPatch(typeof(GameMapMgr), nameof(GameMapMgr.DoRevive))]
    internal static class ReviveItemTrace
    {
        private static void Prefix(IReviveItem __0, ref Il2CppSystem.Action __1)
        {
            Plugin.Logger.LogInfo($"[revive] selected {__0?.Cast<Il2CppSystem.Object>().GetIl2CppType().FullName} {ReviveTrace.Life()}");
            var callback = __1;
            __1 = (System.Action)(() =>
            {
                Plugin.Logger.LogInfo($"[revive] item animation completed {ReviveTrace.Life()}");
                callback?.Invoke();
            });
        }
    }
}
