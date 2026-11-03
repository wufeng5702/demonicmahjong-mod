using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MaJiang;
using MaJiang.DataConstruct.MaJiang;
using MaJiang.DataConstruct.Offering;
using MaJiang.DataConstruct.Relic;
using MaJiang.DataConstruct.XiaoChouPai;
using MaJiang.GM;
using MaJiang.PlayMaJiang;
using MaJiang.PlayMaJiang.Buff;
using MaJiang.PlayMaJiang.Buff.Multiply3;
using MaJiang.PlayMaJiang.InOut;
using MaJiang.PlayMaJiang.Player;
using MaJiang.PlayMaJiang.Player.Offering;
using MaJiang.PlayMaJiang.Player.PlayerXiaoChouPai;
using MaJiang.PlayMaJiang.Player.Relic;
using MaJiang.PlayMaJiang.RoundStatistics;
using MaJiang.PlayMaJiang.UI;
using MaJiang.PlayMaJiang.XiaoChouPaiCommon;
using UnityEngine;
using UnityEngine.UI;
using Obj = UnityEngine.Object;
using NList = Il2CppSystem.Collections.Generic.List<MaJiang.PlayMaJiang.XiaoChouPaiCommon.XiaoChouPaiDisplay>;

namespace DlcBugProbe;

[BepInPlugin("wufeng.demonicmahjong.dlcbugprobe", "DlcBugProbe", "0.1.0")]
[BepInDependency("wufeng.demonicmahjong.dlcbugfix")]
public class ProbePlugin : BasePlugin
{
    public override void Load()
    {
        Application.runInBackground = true;
        Probe.Log = Log;
        AddComponent<Probe>();
        new Harmony("wufeng.demonicmahjong.dlcbugprobe").PatchAll(typeof(ProbePlugin).Assembly);
        Log.LogInfo("Write 'run' to BepInEx/DlcBugProbe.command at the main menu to run isolated component regressions.");
    }
}

public class Probe : MonoBehaviour
{
    internal static ManualLogSource Log;
    internal static readonly HashSet<IntPtr> Presentation = new();
    private static readonly Harmony FixHarmony = new("wufeng.demonicmahjong.dlcbugfix");
    private static readonly Assembly FixAssembly = typeof(DlcBugFix.Plugin).Assembly;
    private static bool running;
    private float nextCheck;
    public Probe(IntPtr p) : base(p) { }

    public void Update()
    {
        if (running || Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + 1;
        var command = Path.Combine(Paths.BepInExRootPath, "DlcBugProbe.command");
        if (!File.Exists(command) || GlobalDataCenter.Instance == null || GameManager.Instance?.Saver == null
            || MaJiang.GameMap.GameMapMgr.Instance == null || !MaJiang.GameMap.GameMapMgr.Instance.mapManagerInitFinish) return;
        string request = File.ReadAllText(command).Trim();
        File.Delete(command);
        if (Obj.FindObjectOfType<PlayerPipeline>() != null)
        {
            Log.LogError("Probe requires the main menu, not an active battle.");
            return;
        }
        running = true;
        StartCoroutine(GuardedRun(request == "life" ? NativeCascadeTests.Run() : Run()).WrapToIl2Cpp());
    }

    [HideFromIl2Cpp]
    private IEnumerator GuardedRun(IEnumerator suite)
    {
        while (true)
        {
            bool moved;
            try { moved = suite.MoveNext(); }
            catch (Exception ex)
            {
                Log.LogError(ex);
                (suite as IDisposable)?.Dispose();
                running = false;
                yield break;
            }
            if (!moved) yield break;
            yield return suite.Current;
        }
    }

    [HideFromIl2Cpp]
    private IEnumerator Run()
    {
        var save = GameManager.Instance.Saver;
        int hp = save.Hp;
        var randomState = UnityEngine.Random.state;
        using var capture = new NativeErrors();
        BepInEx.Logging.Logger.Listeners.Add(capture);
        Log.LogInfo("BEGIN component suite: native growth/selection/progress; fixture-only score UI is stubbed.");
        try
        {
            RemoveQiLinPatches();
            using (var fixture = new Fixture())
            {
                Log.LogInfo($"CONTROL: base={fixture.QiLin._independentBuffPayload.value} growth={fixture.QiLin.Payload.accMultiIndependent}");
                fixture.Song(0);
                Require(fixture.QiLin._data.Count == 0, "baseline song clears progress keys");
                try { fixture.Play(2, 2); }
                catch (Exception ex)
                {
                    capture.Errors.Enqueue(ex.ToString());
                    Log.LogInfo($"CONTROL exception: {ex}");
                }
                // Harmony's native->managed trampoline logs (and catches) an
                // IL2CPP exception when entered from another native game method.
                Require(capture.Errors.Any(e => e.Contains("KeyNotFoundException")), "baseline natural play fails with KeyNotFoundException");
                Log.LogInfo("PASS baseline reproduced KeyNotFoundException in native natural-play path");
                yield return new WaitForSeconds(0.6f);
            }
            RestoreQiLinPatches();
            capture.Errors.Clear();

            for (int level = 0; level < 2; level++)
            {
                using var fixture = new Fixture();
                fixture.Play(2, 2);
                Require(fixture.QiLin._data[QiLinDisplay.Type.Dui], "partial progress before song");
                for (int cycle = 0; cycle < 2; cycle++)
                {
                    float before = fixture.QiLin._independentBuffPayload.value;
                    float songGrowth = fixture.Song(level);
                    Require(fixture.QiLin._data.Count == 3, $"song level {level}: keys restored");
                    fixture.AssertReset();
                    Require(Math.Abs(fixture.QiLin._independentBuffPayload.value - before - songGrowth) < 0.0001f,
                        "song grants exactly the configured growth");
                    float afterSong = fixture.QiLin._independentBuffPayload.value;
                    Log.LogInfo($"SONG level={level} cycle={cycle} buff={before}->{afterSong}");
                    yield return new WaitForSeconds(0.6f);
                    fixture.AssertEffectsReset();
                    fixture.Play(2, 2);
                    Require(fixture.QiLin._data[QiLinDisplay.Type.Dui], "pair progress");
                    fixture.Play(3, 4, 5);
                    Require(fixture.QiLin._data[QiLinDisplay.Type.Shun], "sequence progress");
                    fixture.Play(7, 7, 7);
                    fixture.AssertReset();
                    Require(Math.Abs(fixture.QiLin._independentBuffPayload.value - afterSong - fixture.QiLin.Payload.accMultiIndependent) < 0.0001f, "exactly one natural growth");
                    yield return new WaitForSeconds(0.6f);
                    fixture.AssertEffectsReset();
                }
                Log.LogInfo($"PASS song level={level}, repeat song -> pair -> sequence -> triplet");
            }

            using (var fixture = new Fixture())
            {
                float before = fixture.QiLin._independentBuffPayload.value;
                fixture.Play(2, 2);
                fixture.Play(3, 4, 5);
                fixture.Play(7, 7, 7);
                fixture.AssertReset();
                Require(fixture.QiLin._independentBuffPayload.value == before + fixture.QiLin.Payload.accMultiIndependent, "natural-only control");
                yield return new WaitForSeconds(0.6f);
                fixture.QiLin._data.Clear();
                fixture.QiLin._data.Add(QiLinDisplay.Type.Dui, true);
                fixture.Play(3, 4, 5);
                Require(fixture.QiLin._data[QiLinDisplay.Type.Dui] && fixture.QiLin._data[QiLinDisplay.Type.Shun], "repair preserves existing partial progress");
                Log.LogInfo("PASS natural-only and partial-key recovery controls");
            }
            yield return NativeCascadeTests.Run();
            Require(capture.Errors.IsEmpty, "no native trampoline errors after repair: " + string.Join("\n", capture.Errors));
            Require(GameManager.Instance.Saver.Pointer == save.Pointer && save.Hp == hp, "real save object and HP untouched");
            Log.LogInfo("PASS ALL component regressions. Full map revival/consumption and battle UI still require in-game cases.");
        }
        finally
        {
            UnityEngine.Random.state = randomState;
            BepInEx.Logging.Logger.Listeners.Remove(capture);
            RestoreQiLinPatches();
            running = false;
            Log.LogInfo("END component suite");
        }
    }

    [HideFromIl2Cpp]
    private static void RemoveQiLinPatches()
    {
        FixHarmony.Unpatch(AccessTools.Method(typeof(QiLinDisplay), nameof(QiLinDisplay.OnXiaoChouLevelUp)), HarmonyPatchType.All, FixHarmony.Id);
        FixHarmony.Unpatch(AccessTools.Method(typeof(QiLinDisplay), nameof(QiLinDisplay.CheckForBuff)), HarmonyPatchType.All, FixHarmony.Id);
    }

    [HideFromIl2Cpp]
    private static void RestoreQiLinPatches()
    {
        RemoveQiLinPatches();
        FixHarmony.CreateClassProcessor(FixAssembly.GetType("DlcBugFix.QiLinGrowthPatch", true)).Patch();
        FixHarmony.CreateClassProcessor(FixAssembly.GetType("DlcBugFix.QiLinPlayingPatch", true)).Patch();
    }

    internal static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("FAIL: " + message);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly GameObject root = new("DlcProbe.Fixture");
        private readonly List<Obj> assets = new();
        private readonly List<IntPtr> presentationPointers = new();
        internal readonly QiLinDisplay QiLin;
        internal readonly PlayerPipeline Pipeline;
        internal readonly BuffList Buffs;

        internal Fixture()
        {
            try
            {
                Buffs = Add<BuffList>(false);
                Buffs._baseScoreBuff = Add<UseAddBuff>(false);
                Buffs._fanBuff = Add<UseAddBuff>(false);
                Buffs._independentBuff = Add<UseMultiBuff>(false);
                foreach (var b in new AbsBuff[] { Buffs._baseScoreBuff, Buffs._fanBuff, Buffs._independentBuff })
                {
                    TrackPresentation(b);
                    b.BuffPayloads = new Il2CppSystem.Collections.Generic.List<MaJiang.PlayMaJiang.Buff.BuffPayloads.BuffPayload>();
                }
                Pipeline = Add<PlayerPipeline>(false);
                Pipeline._playerRoundStatistics = Add<PlayerRoundStatistics>(false);
                Pipeline._playerRoundStatistics._BuffList_k__BackingField = Buffs;
                Pipeline._tempAddXiaoChou = new NList();
                Pipeline._playerXiaoChouPaiContainer = Add<PlayerXiaoChouPaiContainer>(false);
                Pipeline._playerXiaoChouPaiContainer._displays = new NList();
                QiLin = Add<QiLinDisplay>(true);
                QiLin.Payload = GlobalDataCenter.Instance.GetXiaoChouPaiPayload(XiaoChou.QiLin);
                QiLin.XiaoChouSaveData = new XiaoChouSaveData();
                QiLin.IsEnabled = true;
                QiLin.IsPlayerXiaoChouPai = true;
                QiLin._data = new Il2CppSystem.Collections.Generic.Dictionary<QiLinDisplay.Type, bool>();
                foreach (QiLinDisplay.Type key in Enum.GetValues(typeof(QiLinDisplay.Type))) QiLin._data.Add(key, false);
                QiLin._ongoingEffect = Add<ParticleSystem>(false);
                QiLin._levelEffect = new Il2CppReferenceArray<ParticleSystem>(new[] { Add<ParticleSystem>(false), Add<ParticleSystem>(false), Add<ParticleSystem>(false) });
                QiLin._upLevelEffect = Add<GetStatisticEffect>(false);
                TrackPresentation(QiLin._upLevelEffect);
                QiLin.EnsureInitPayload(Buffs, QiLin.Payload.baseMultiIndependent);
                Pipeline._playerXiaoChouPaiContainer._displays.Add(QiLin);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void TrackPresentation(Component component)
        {
            presentationPointers.Add(component.Pointer);
            Presentation.Add(component.Pointer);
        }

        internal T Add<T>(bool active) where T : Component
        {
            var go = new GameObject("DlcProbe." + typeof(T).Name);
            go.SetActive(false);
            go.transform.SetParent(root.transform, false);
            var component = go.AddComponent<T>();
            if (active) go.SetActive(true);
            return component;
        }

        internal float Song(int level)
        {
            var original = GlobalDataCenter.Instance.GetOffering(MaJiang.DataConstruct.Offering.Offering.GeSheng);
            var song = Add<GeShengDisplay>(false);
            song.Payload = original.Payloads[level];
            float expectedGrowth = float.NaN;
            foreach (var item in song.Payload.geShengAdd)
                if (item.xiaoChouId == XiaoChou.QiLin)
                {
                    expectedGrowth = item.accMultiIndependent;
                    Log.LogInfo($"SONG table level={level} input={expectedGrowth}");
                }
            Require(!float.IsNaN(expectedGrowth), "song has Qilin in its configured targets");
            song.CompleteUse(new CompleteOfferingInput { Pipeline = Pipeline.Cast<IPipeline>() });
            return expectedGrowth;
        }

        internal void Play(params int[] numbers)
        {
            var tiles = new Il2CppSystem.Collections.Generic.List<PaiMianDisplay>();
            foreach (int number in numbers)
            {
                var payload = ScriptableObject.CreateInstance<PaiMianPayload>();
                assets.Add(payload);
                payload.paiMian = new PaiMian { huaSe = HuaSe.Wan, number = number };
                var tile = Add<PaiMianDisplay>(false);
                tile._PaiMianPayload_k__BackingField = payload;
                tiles.Add(tile);
            }
            QiLin.OnPlaying(new OnPlayingOrSlidingInput
            {
                BuffList = Buffs,
                PlayingDisplays = tiles.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<PaiMianDisplay>>()
            }, new OnPlayingOrSlidingOutput());
        }

        internal void AssertReset()
        {
            foreach (QiLinDisplay.Type key in Enum.GetValues(typeof(QiLinDisplay.Type))) Require(!QiLin._data[key], "all three progress flags reset");
        }

        internal void AssertEffectsReset()
        {
            foreach (var effect in QiLin._levelEffect) Require(!effect.gameObject.activeSelf, "level effects disabled after coroutine");
        }

        public void Dispose()
        {
            foreach (var pointer in presentationPointers) Presentation.Remove(pointer);
            QiLin?.StopAllCoroutines();
            root.SetActive(false);
            Obj.Destroy(root);
            foreach (var asset in assets) Obj.Destroy(asset);
        }
    }
}

internal sealed class NativeErrors : ILogListener
{
    internal readonly System.Collections.Concurrent.ConcurrentQueue<string> Errors = new();
    public LogLevel LogLevelFilter => LogLevel.Error | LogLevel.Fatal;
    public void LogEvent(object sender, LogEventArgs args)
    {
        if ((args.Level & (LogLevel.Error | LogLevel.Fatal)) != 0 && args.Source.SourceName == "Il2CppInterop")
            Errors.Enqueue(args.Data?.ToString() ?? "");
    }
    public void Dispose() { }
}

[HarmonyPatch]
internal static class PresentationOnly
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var type in new[] { typeof(UseAddBuff), typeof(UseMultiBuff) })
            foreach (var m in AccessTools.GetDeclaredMethods(type).Where(m => m.Name == "Refresh")) yield return m;
        yield return AccessTools.Method(typeof(GetStatisticEffect), nameof(GetStatisticEffect.PlayIndependentEffect));
    }
    private static bool Prefix(Component __instance) => !Probe.Presentation.Contains(__instance.Pointer);
}
