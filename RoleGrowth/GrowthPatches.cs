using System;
using HarmonyLib;
using MaJiang.GM;
using MaJiang.PlayMaJiang.Player.Skills.Active;

namespace RoleGrowth
{
    /// <summary>
    /// 只 Hook 稳定的补丁点：
    /// - FanJiuSkillDisplay.Init：启动时把 JSON 值写进 Saver.CharaterSkillData（游戏权威值）
    /// - GameSaverUtil.SaveAsync：存盘时把 Saver 里的成长值写回 JSON
    /// - Saver.NewCreateSaver：开始新游戏时重置该存档ID
    /// 绝不 Hook CompleteUse/ExecuteDirectly/GetDynamicDescription/OnMaJiangRoundEnd（含 struct 参数会崩）。
    /// </summary>
    internal static class GrowthPatches
    {
        private const int FANJIU_SKILL_ID = 20036;

        [HarmonyPatch(typeof(FanJiuSkillDisplay), "Init")]
        private static class FanJiuInitPatch
        {
            private static void Postfix(FanJiuSkillDisplay __instance)
            {
                try
                {
                    int slot = GrowthStore.CurrentSlot();
                    float saved = GrowthStore.Get(slot, FANJIU_SKILL_ID);
                    if (saved <= 0f) return;
                    GrowthStore.PendingValue = saved;
                    GrowthStore.PendingSkillId = FANJIU_SKILL_ID;
                    // 写进 Saver 的权威 CharaterSkillData，而不是 Display 实例的副本
                    var saverObj = GrowthStore.GetSaverObject();
                    if (saverObj != null)
                    {
                        var d = saverObj.GetCharaterSkillData();
                        d.Count = saved;
                        saverObj.SetCharaterSkillData(d);
                        GrowthStore.Log?.LogInfo("[RoleGrowth] Init restored slot=" + slot
                            + " id=" + FANJIU_SKILL_ID + " Count=" + saved.ToString("0.####") + " (Saver)");
                    }
                    else
                    {
                        GrowthStore.Log?.LogWarning("[RoleGrowth] Init: no Saver, cannot restore");
                    }
                }
                catch (Exception e)
                {
                    GrowthStore.Log?.LogWarning("[RoleGrowth] FanJiu.Init restore failed: " + e.GetType().Name);
                }
            }
        }

        [HarmonyPatch(typeof(GameSaverUtil), "SaveAsync")]
        private static class SaveAsyncPatch
        {
            private static void Prefix()
            {
                try
                {
                    int slot = GrowthStore.CurrentSlot();
                    var saverObj = GrowthStore.GetSaverObject();
                    if (saverObj == null) return;
                    var d = saverObj.GetCharaterSkillData();
                    if (d.Count > 0f)
                    {
                        GrowthStore.Set(slot, FANJIU_SKILL_ID, d.Count);
                        GrowthStore.Log?.LogInfo("[RoleGrowth] SaveAsync wrote slot=" + slot
                            + " id=" + FANJIU_SKILL_ID + " Count=" + d.Count.ToString("0.####"));
                    }
                    GrowthStore.Flush();
                }
                catch (Exception e)
                {
                    GrowthStore.Log?.LogWarning("[RoleGrowth] SaveAsync hook failed: " + e.GetType().Name);
                }
            }
        }

        [HarmonyPatch(typeof(MaJiang.OutsideGameAction), "OnNewGameStart")]
        private static class NewGamePatch
        {
            private static void Postfix()
            {
                try
                {
                    int slot = GrowthStore.CurrentSlot();
                    GrowthStore.ResetSlot(slot);
                    GrowthStore.Flush();
                    GrowthStore.PendingValue = 0f;
                }
                catch (Exception e)
                {
                    GrowthStore.Log?.LogWarning("[RoleGrowth] NewGame hook failed: " + e.GetType().Name);
                }
            }
        }
    }
}
