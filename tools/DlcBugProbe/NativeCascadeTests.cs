using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using DlcBugFix;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MaJiang;
using MaJiang.DataConstruct.Relic;
using MaJiang.GameMap;
using MaJiang.GM;
using MaJiang.ValueClasses;

namespace DlcBugProbe;

// Exercises the real Harmony entry points, managed/native coroutine bridge,
// queue-completion barrier and re-entry suppression. Item effects are fixtures;
// the player's Saver and inventory are never replaced or written.
internal sealed class NativeCascadeTests : IDisposable
{
    internal static NativeCascadeTests Active;
    internal readonly GlobalDataCenter Data = GlobalDataCenter.Instance;
    internal readonly GameMapMgr Manager = GameMapMgr.Instance;
    internal readonly GvHp Hp = new GvHp();
    private readonly IGobalValue previousHp;
    private readonly bool hadHp;
    private readonly Saver save = GameManager.Instance.Saver;
    private readonly int savedHp;
    private readonly Queue<char> items;
    internal int Attempts;
    internal int UnexpectedObservers;
    internal string Consumed = "";
    internal int MaxHp = 1;
    internal int ChuFeng;
    internal bool Animating;
    internal readonly List<int> DamageValues = new List<int>();

    private NativeCascadeTests(int afterLoss, string items)
    {
        savedHp = save.Hp;
        this.items = new Queue<char>(items);
        hadHp = Data._globalValues.TryGetValue(GlobalDataType.Hp, out previousHp);
        Hp.Value = afterLoss;
        Data._globalValues[GlobalDataType.Hp] = Hp.Cast<IGobalValue>();
        Active = this;
    }

    internal static IEnumerator Run()
    {
        Probe.Log.LogInfo("BEGIN sequential life integration: fixture revivers, real coroutine hooks and popup-queue barrier");
        foreach (var row in new[] { (-1, "E", false, 0, "E"), (-1, "EP", true, 1, "EP"),
            (-2, "EP", false, 0, "EP"), (-3, "EP", false, 0, "EP"), (-4, "EEEEE", true, 1, "EEEEE"),
            (-1, "", false, 0, "") })
        {
            using var fixture = new NativeCascadeTests(row.Item1, row.Item2);
            int callbacks = 0;
            bool survived = false;
            var routine = fixture.Manager.NeedTryRevive((Action<bool>)(alive =>
            {
                Probe.Require(!fixture.Animating, "terminal callback waits for item consumption");
                callbacks++;
                survived = alive;
            }));
            Probe.Require(!SwordRevivePatch.OwnsHpChanges(fixture.Manager), "iterator creation does not acquire cascade ownership");
            yield return routine;
            Probe.Require(callbacks == 1 && survived == row.Item3, "one FINAL outcome: " + row);
            Probe.Require(fixture.Hp.Value == row.Item4 && fixture.Consumed == row.Item5, "HP and item consumption: " + row);
            Probe.Require(fixture.UnexpectedObservers == 0, "automatic HP observer must not start overlapping revival");
            Probe.Require(!SwordRevivePatch.OwnsHpChanges(fixture.Manager), "cascade ownership released");
            Probe.Log.LogInfo($"PASS cascade initial={row.Item1} items={row.Item2} survived={survived} hp={fixture.Hp.Value} consumed={fixture.Consumed} attempts={fixture.Attempts}");
        }
        Probe.Log.LogInfo("PASS ALL sequential life integration cases");
    }

    internal void SetHp(int value, bool damage)
    {
        if (damage) Probe.Require(!Animating, "remaining damage must wait for revival animation");
        int old = Hp.Value;
        Hp.Value = value;
        if (damage) DamageValues.Add(value);
        Manager.OnHpChange(old, value);
    }

    internal IEnumerator Revive(Il2CppSystem.Action<bool> callback)
    {
        Attempts++;
        Probe.Require(Hp.Value == 0, "a revival starts at exactly zero");
        if (items.Count == 0) { callback?.Invoke(false); yield break; }
        Probe.Require(!Animating, "revival attempts are serial");
        Animating = true;
        char item = items.Dequeue();
        if (item == 'E') MaxHp++;
        else MaxHp = 1;
        SetHp(1, false);
        // Intentionally early: the resolver must also await the iterator and
        // popup queue, rather than spending the next point inside this callback.
        callback?.Invoke(true);
        yield return null;
        yield return null;
        Consumed += item;
        if (item == 'E') ChuFeng++;
        Animating = false;
    }

    public void Dispose()
    {
        if (hadHp) Data._globalValues[GlobalDataType.Hp] = previousHp;
        else Data._globalValues.Remove(GlobalDataType.Hp);
        Active = null;
        Probe.Require(GameManager.Instance.Saver.Pointer == save.Pointer && save.Hp == savedHp,
            "sequential integration leaves real saved HP untouched");
    }
}

[HarmonyPatch(typeof(GlobalDataCenter), nameof(GlobalDataCenter.GetCurHadRelic))]
internal static class FixtureSwordInventory
{
    private static bool Prefix(ref Il2CppStructArray<RelicId> __result)
    {
        if (NativeCascadeTests.Active == null) return true;
        __result = new Il2CppStructArray<RelicId>(new[] { RelicId.XunDaoZhiJian });
        return false;
    }
}

[HarmonyPatch(typeof(GlobalDataCenter), nameof(GlobalDataCenter.SetData))]
internal static class FixtureSetHp
{
    private static bool Prefix(GlobalDataType __0, Il2CppSystem.Object __1)
    {
        var fixture = NativeCascadeTests.Active;
        if (fixture == null || __0 != GlobalDataType.Hp) return true;
        fixture.SetHp(Il2CppSystem.Convert.ToInt32(__1), false);
        return false;
    }
}

[HarmonyPatch(typeof(GlobalDataCenter), nameof(GlobalDataCenter.ModifyData))]
internal static class FixtureLoseHp
{
    private static bool Prefix(GlobalDataType __0, Il2CppSystem.Object __1)
    {
        var fixture = NativeCascadeTests.Active;
        if (fixture == null || __0 != GlobalDataType.Hp) return true;
        int delta = Il2CppSystem.Convert.ToInt32(__1);
        Probe.Require(delta == -1, "deferred damage is applied one point at a time");
        fixture.SetHp(fixture.Hp.Value + delta, true);
        return false;
    }
}

[HarmonyPatch(typeof(GameMapMgr), nameof(GameMapMgr.NeedTryRevive))]
internal static class FixtureReviveAttempt
{
    [HarmonyPriority(Priority.Last)]
    private static bool Prefix(Il2CppSystem.Action<bool> __0, bool __runOriginal,
        ref Il2CppSystem.Collections.IEnumerator __result)
    {
        var fixture = NativeCascadeTests.Active;
        // HarmonyX runs later prefixes even after a previous prefix returned
        // false. Preserve the production wrapper's result on the outer call.
        if (fixture == null || !__runOriginal) return true;
        __result = fixture.Revive(__0).WrapToIl2Cpp();
        return false;
    }
}

[HarmonyPatch(typeof(GameMapMgr), nameof(GameMapMgr.OnHpChange))]
internal static class FixtureHpObserver
{
    [HarmonyPriority(Priority.Last)]
    private static bool Prefix(bool __runOriginal)
    {
        var fixture = NativeCascadeTests.Active;
        if (fixture == null || !__runOriginal) return true;
        fixture.UnexpectedObservers++;
        return false;
    }
}
