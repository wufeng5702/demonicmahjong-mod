using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using Il2CppInterop.Runtime;
using MaJiang;
using MaJiang.DataConstruct.Relic;
using MaJiang.GameMap;
using UnityEngine.Events;

namespace DlcBugFix
{
    [HarmonyPatch(typeof(GameMapMgr), nameof(GameMapMgr.NeedTryRevive))]
    internal static class SwordRevivePatch
    {
        [ThreadStatic] private static bool _creatingOriginal;
        private static Cascade _active;

        internal static bool OwnsHpChanges(GameMapMgr manager)
        {
            return _active != null && _active.Manager.Pointer == manager.Pointer;
        }

        private static bool Prefix(GameMapMgr __instance, Il2CppSystem.Action<bool> __0,
            ref Il2CppSystem.Collections.IEnumerator __result)
        {
            if (_creatingOriginal) return true;
            if (!OwnsHpChanges(__instance))
            {
                var data = GlobalDataCenter.Instance;
                if (data == null || data.GetData<int>(GlobalDataType.Hp) >= 0 || !HasSword(data)) return true;
            }
            __result = Run(__instance, __0).WrapToIl2Cpp();
            return false;
        }

        private static bool HasSword(GlobalDataCenter data)
        {
            var relics = data.GetCurHadRelic();
            for (int i = 0; i < relics.Length; i++)
                if (relics[i] == RelicId.XunDaoZhiJian) return true;
            return false;
        }

        private static IEnumerator Run(GameMapMgr manager, Il2CppSystem.Action<bool> callback)
        {
            // Creation is not execution: acquire ownership only on MoveNext.
            // A second real caller waits for this settlement instead of starting
            // another animation or consuming the same revival item again.
            if (OwnsHpChanges(manager))
            {
                var existing = _active;
                while (!existing.Done) yield return null;
                callback?.Invoke(existing.Survived);
                yield break;
            }

            var state = new Cascade(manager);
            _active = state;
            var loss = new SequentialLifeLoss();
            var data = GlobalDataCenter.Instance;
            Plugin.Logger.LogInfo($"[life] sequential defeat begins: hp={data.GetData<int>(GlobalDataType.Hp)}");
            try
            {
                var resolve = loss.Resolve(
                    () => data.GetData<int>(GlobalDataType.Hp),
                    hp => data.SetData(GlobalDataType.Hp, hp, Record("首次归零", "=0")),
                    () => data.ModifyData(GlobalDataType.Hp, -1, Record("殉道之剑剩余伤害", "-1")),
                    done => Attempt(manager, done),
                    survived => state.Survived = survived);
                try
                {
                    while (resolve.MoveNext()) yield return resolve.Current;
                }
                finally { (resolve as IDisposable)?.Dispose(); }
            }
            finally
            {
                state.Done = true;
                if (ReferenceEquals(_active, state)) _active = null;
            }
            Plugin.Logger.LogInfo($"[life] sequential defeat completed: survived={state.Survived} hp={data.GetData<int>(GlobalDataType.Hp)} remaining={loss.RemainingDamage}");
            // Only the FINAL outcome reaches the game's caller. A revival can
            // succeed mid-cascade and still be followed by a fatal sword hit.
            callback?.Invoke(state.Survived);
        }

        private static IRecord Record(string description, string value)
        {
            return new SimpleRecord("DlcBugFix", description, value).Cast<IRecord>();
        }

        private static IEnumerator Attempt(GameMapMgr manager, Action<bool> done)
        {
            bool called = false;
            bool revived = false;
            Il2CppSystem.Collections.IEnumerator original;
            bool previous = _creatingOriginal;
            _creatingOriginal = true;
            try
            {
                original = manager.NeedTryRevive((Action<bool>)(result =>
                {
                    called = true;
                    revived = result;
                }));
            }
            finally { _creatingOriginal = previous; }

            yield return original;

            // Egg destruction/hatching can enqueue more popups from OnRevive.
            // Use the game's queue completion API, not a guessed delay.
            var pop = manager.popUIManager;
            if (pop != null)
            {
                bool settled = false;
                pop.AddCallBack((UnityAction)(() => settled = true));
                while (!settled) yield return null;
            }
            if (Plugin.DebugEnabled)
                Plugin.Logger.LogInfo($"[life] revival settled: callback={called} revived={revived} {ReviveTrace.Life()}");
            done(called && revived);
        }

        private sealed class Cascade
        {
            internal readonly GameMapMgr Manager;
            internal bool Done;
            internal bool Survived;
            internal Cascade(GameMapMgr manager) { Manager = manager; }
        }
    }

    [HarmonyPatch(typeof(GameMapMgr), nameof(GameMapMgr.OnHpChange))]
    internal static class CascadeHpChangePatch
    {
        private static bool Prefix(GameMapMgr __instance)
        {
            // Our coroutine already schedules every zero-HP revival. Letting this
            // observer also start NeedTryRevive would create overlapping sequences.
            return !SwordRevivePatch.OwnsHpChanges(__instance);
        }
    }
}
