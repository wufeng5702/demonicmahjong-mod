using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using MaJiang;
using MaJiang.Bag;
using MaJiang.PlayMaJiang.XiaoChouPaiCommon;
using Shared;

namespace AutoContinue
{
    /// <summary>
    /// 跳过 / 隐藏局外灵俑的演出动画。
    ///
    /// 灵俑（游戏代码里叫 XiaoChouPai / 小丑牌）的效果在局外触发时，大厅会播一段演出：
    /// 消耗金币 → 毕方（金币每减少 2，倍率 +1%），销毁灵俑 → 鹿蜀（每销毁一个灵俑，独立倍率 +15%）。
    /// 演出的载体有两类，本组件都盯着：
    ///
    ///   1) 灵俑模型上的 Animator 演出（XiaoChouPaiDisplay.modelObj 的动画状态）
    ///   2) 灵俑时间轴演出（LingYongTimeLineController 系列的 PlayableDirector）
    ///
    /// 两种模式：
    ///   skip = 跳过：模型动画瞬间放完（speed ×N），时间轴播放头推到片尾
    ///   hide = 隐藏：模型 / 时间轴画面藏起来，时间照常走
    ///
    /// 默认只处理局外（大厅 PlayerInfoCtr 挂着的那批灵俑），对局内的灵俑动画不动。
    ///
    /// 注意：这里不用 Harmony 挂 IL2CPP 方法——Il2CppInterop 的 native→managed 跳板
    /// 需要先把 this 对应的托管对象注册进对象池，否则每次调用都会抛
    /// "Handle is not initialized"（游戏原生调用直接失败）。纯托管侧轮询没有这个问题。
    /// </summary>
    public class LingYongAnimSkip : MonoBehaviour
    {
        internal static BepInEx.Logging.ManualLogSource Log;

        private const float ScanInterval = 0.05f;       // 时间轴扫描间隔（秒）
        private const float DisplayScanInterval = 0.2f; // 灵俑模型扫描间隔（秒）
        private const float SpeedUpFactor = 60f;        // 模型动画加速倍率
        private const float MaxBoostDuration = 3f;      // 加速最多维持多久（秒）
        private const float HideDuration = 0.6f;        // hide 模式隐藏时长（秒）

        private readonly List<PlayableDirector> _directors = new List<PlayableDirector>(4);
        private readonly Dictionary<int, HiddenState> _hidden = new Dictionary<int, HiddenState>();
        private readonly Dictionary<int, AnimWatch> _anims = new Dictionary<int, AnimWatch>();
        private readonly List<HiddenModel> _hiddenModels = new List<HiddenModel>();
        private readonly HashSet<int> _reportedPlaying = new HashSet<int>();

        private float _nextScan;
        private float _nextDisplayScan;
        private float _nextSearch;
        private float _nextWatch;
        private float _nextScopeLog;
        private float _nextNewScan;
        private HashSet<int> _activeIds = new HashSet<int>();
        private bool _loggedReady;

        /// <summary>被隐藏的渲染状态，动画结束后还原。</summary>
        private sealed class HiddenState
        {
            public readonly List<Renderer> Renderers = new List<Renderer>();
            public readonly List<bool> RendererEnabled = new List<bool>();
            public readonly List<CanvasGroup> Groups = new List<CanvasGroup>();
            public readonly List<float> Alpha = new List<float>();
        }

        /// <summary>灵俑模型上的动画监视记录。</summary>
        private sealed class AnimWatch
        {
            public Animator Animator;
            public int LastHash;
            public float OriginalSpeed = 1f;
            public float AcceleratedAt;
            public string Name;
        }

        /// <summary>被临时隐藏的灵俑模型。</summary>
        private sealed class HiddenModel
        {
            public GameObject Go;
            public readonly List<Renderer> Renderers = new List<Renderer>();
            public readonly List<bool> RendererEnabled = new List<bool>();
            public float Until;
        }

        private void Awake()
        {
            AutoConfig.Load();
        }

        private void Update()
        {
            if (!AutoConfig.LingYongAnimEnabled) return;
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + ScanInterval;
            try
            {
                ScanTimeLines();
                if (Time.unscaledTime >= _nextDisplayScan)
                {
                    _nextDisplayScan = Time.unscaledTime + DisplayScanInterval;
                    ScanDisplays();
                }
                RestoreModels();
                RestoreSpeeds();
            }
            catch (Exception e)
            {
                Log?.LogWarning("LingYongAnimSkip: scan failed: " + StringTruncator.FirstLine(e.ToString()));
            }
        }

        // ============ 灵俑模型上的 Animator 演出 ============

        /// <summary>
        /// 盯住场景里所有灵俑显示对象的模型动画：一旦状态切换（说明开始播新动画），
        /// skip 模式把 speed 拉满让它瞬间放完，hide 模式把模型藏起来。
        /// </summary>
        private void ScanDisplays()
        {
            var displays = UnityEngine.Object.FindObjectsOfType<XiaoChouPaiDisplay>(true);
            int inScope = 0;
            int withModel = 0;
            int withAnim = 0;
            for (int i = 0; i < displays.Length; i++)
            {
                XiaoChouPaiDisplay display = displays[i];
                if (display == null) continue;
                if (!InScope(display)) continue;
                inScope++;

                GameObject model = null;
                try { model = display.modelObj; }
                catch (Exception) { continue; }
                if (model == null) continue;
                withModel++;

                Animator animator = null;
                try { animator = model.GetComponent<Animator>(); }
                catch (Exception) { continue; }
                if (animator == null) continue;
                withAnim++;

                int id;
                try { id = animator.GetInstanceID(); }
                catch (Exception) { continue; }

                int hash;
                try { hash = animator.GetCurrentAnimatorStateInfo(0).shortNameHash; }
                catch (Exception) { continue; }

                if (!_anims.TryGetValue(id, out AnimWatch watch))
                {
                    // 第一次见到：记下当前状态作为基线（通常是待机）
                    _anims[id] = new AnimWatch
                    {
                        Animator = animator,
                        LastHash = hash,
                        OriginalSpeed = SafeSpeed(animator, 1f),
                        Name = model.name
                    };
                    continue;
                }

                if (watch.Animator == null) { watch.Animator = animator; }

                // 之前的加速最多维持 MaxBoostDuration 秒，防止速度卡在高速
                if (watch.AcceleratedAt > 0f && Time.unscaledTime - watch.AcceleratedAt > MaxBoostDuration)
                {
                    RestoreSpeed(watch);
                }

                if (hash == watch.LastHash) continue;
                watch.LastHash = hash;

                // 状态切换 = 上一段动画结束，先还原速度再处理新动画
                RestoreSpeed(watch);
                OnAnimationStarted(watch, animator, model, hash);
            }

            if (AutoConfig.LingYongAnimDebug && Time.unscaledTime >= _nextScopeLog)
            {
                _nextScopeLog = Time.unscaledTime + 1f;
                Log?.LogInfo("LingYongAnimSkip: 灵俑 display=" + displays.Length + " 范围内=" + inScope
                    + " 有模型=" + withModel + " 有Animator=" + withAnim
                    + " 大厅面板=" + (HasLobbyPanel() ? "有" : "无"));
                ProbeScene();
            }
        }

        /// <summary>调试用：列出场景里正在播的粒子 / 动画，以及名字带灵俑关键字的对象。</summary>
        private void ProbeScene()
        {
            try
            {
                var ps = UnityEngine.Object.FindObjectsOfType<ParticleSystem>(true);
                int shown = 0;
                for (int i = 0; i < ps.Length && shown < 30; i++)
                {
                    var p = ps[i];
                    if (p == null) continue;
                    bool playing = false;
                    try { playing = p.isPlaying; }
                    catch (Exception) { }
                    if (!playing) continue;
                    shown++;
                    Log?.LogInfo("  ps[" + shown + "] " + Path(p.gameObject) + " playing");
                }
                Log?.LogInfo("LingYongAnimSkip: 场景播放中的粒子 " + shown + " 个");

                var anims = UnityEngine.Object.FindObjectsOfType<Animator>(true);
                shown = 0;
                for (int i = 0; i < anims.Length && shown < 15; i++)
                {
                    var a = anims[i];
                    if (a == null) continue;
                    shown++;
                    Log?.LogInfo("  anim[" + shown + "] " + Path(a.gameObject)
                        + " state=" + a.GetCurrentAnimatorStateInfo(0).shortNameHash);
                }
                Log?.LogInfo("LingYongAnimSkip: 场景 Animator " + shown + " 个");

                var spines = UnityEngine.Object.FindObjectsOfType<Spine.Unity.SkeletonAnimation>(true);
                shown = 0;
                for (int i = 0; i < spines.Length && shown < 15; i++)
                {
                    var s = spines[i];
                    if (s == null) continue;
                    shown++;
                    Log?.LogInfo("  spine[" + shown + "] " + Path(s.gameObject)
                        + " anim=" + s.AnimationName);
                }
                Log?.LogInfo("LingYongAnimSkip: 场景 Spine " + shown + " 个");

                var gos = UnityEngine.Object.FindObjectsOfType<GameObject>(true);
                shown = 0;
                for (int i = 0; i < gos.Length && shown < 25; i++)
                {
                    var go = gos[i];
                    if (go == null) continue;
                    string n = go.name;
                    if (n.IndexOf("Ling", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("XiaoChou", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("Yong", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    shown++;
                    Log?.LogInfo("  go " + Path(go));
                }
                Log?.LogInfo("LingYongAnimSkip: 名字带灵俑关键字的对象 " + shown + " 个");

                DumpGameMapMgr();
                WatchNewlyActive();
            }
            catch (Exception e)
            {
                Log?.LogWarning("LingYongAnimSkip: probe failed: " + StringTruncator.FirstLine(e.ToString()));
            }
        }

        /// <summary>
        /// 调试用：找出「刚被激活」的对象——局外灵俑演出就是这种弹出来的卡/特效，
        /// 激活的瞬间会出现在这里（带粒子或动画的优先）。
        /// </summary>
        private void WatchNewlyActive()
        {
            if (Time.unscaledTime < _nextNewScan) return;
            _nextNewScan = Time.unscaledTime + 1f;
            try
            {
                var gos = UnityEngine.Object.FindObjectsOfType<GameObject>(true);
                var now = new HashSet<int>();
                var fresh = new List<GameObject>();
                for (int i = 0; i < gos.Length; i++)
                {
                    GameObject go = gos[i];
                    if (go == null || !go.activeInHierarchy) continue;
                    int id = go.GetInstanceID();
                    now.Add(id);
                    if (!_activeIds.Contains(id)) fresh.Add(go);
                }
                _activeIds = now;

                if (fresh.Count == 0) return;
                int shown = 0;
                for (int i = 0; i < fresh.Count && shown < 12; i++)
                {
                    GameObject go = fresh[i];
                    string comps = Components(go);
                    // 只报带粒子/动画/画组的新对象，避免刷屏
                    if (comps.IndexOf("Particle", StringComparison.Ordinal) < 0
                        && comps.IndexOf("Animator", StringComparison.Ordinal) < 0
                        && comps.IndexOf("CanvasGroup", StringComparison.Ordinal) < 0
                        && comps.IndexOf("Skeleton", StringComparison.Ordinal) < 0) continue;
                    shown++;
                    Log?.LogInfo("  new[" + shown + "] " + Path(go) + " comps=" + comps);
                }
                if (shown > 0) Log?.LogInfo("LingYongAnimSkip: 新激活的演出对象 " + shown + " 个");
            }
            catch (Exception e)
            {
                Log?.LogWarning("LingYongAnimSkip: watch new failed: "
                    + StringTruncator.FirstLine(e.ToString()));
            }
        }

        /// <summary>调试用：列出 GameMapMgr 下的全部子对象（灵俑演出都挂在这下面）。</summary>
        private void DumpGameMapMgr()
        {
            try
            {
                var gos = UnityEngine.Object.FindObjectsOfType<GameObject>(true);
                GameObject root = null;
                for (int i = 0; i < gos.Length; i++)
                {
                    if (gos[i] != null && gos[i].name == "GameMapMgr") { root = gos[i]; break; }
                }
                if (root == null)
                {
                    Log?.LogInfo("LingYongAnimSkip: 未找到 GameMapMgr");
                    return;
                }

                int shown = 0;
                var stack = new Stack<Transform>();
                Transform rootT = root.transform;
                for (int i = rootT.childCount - 1; i >= 0; i--) stack.Push(rootT.GetChild(i));
                while (stack.Count > 0 && shown < 60)
                {
                    Transform t = stack.Pop();
                    if (t == null) continue;
                    shown++;
                    Log?.LogInfo("  gmm[" + shown + "] " + Path(t.gameObject) + " comps=" + Components(t.gameObject));
                    for (int i = t.childCount - 1; i >= 0; i--) stack.Push(t.GetChild(i));
                }
                Log?.LogInfo("LingYongAnimSkip: GameMapMgr 子对象 " + shown + " 个");
            }
            catch (Exception e)
            {
                Log?.LogWarning("LingYongAnimSkip: dump GameMapMgr failed: "
                    + StringTruncator.FirstLine(e.ToString()));
            }
        }

        private static string Components(GameObject go)
        {
            try
            {
                var comps = go.GetComponents<Component>();
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < comps.Length; i++)
                {
                    if (comps[i] == null) continue;
                    if (sb.Length > 0) sb.Append(',');
                    sb.Append(comps[i].GetType().Name);
                }
                return sb.ToString();
            }
            catch (Exception)
            {
                return "?";
            }
        }

        private static bool HasLobbyPanel()
        {
            try { return UnityEngine.Object.FindObjectOfType<PlayerInfoCtr>() != null; }
            catch (Exception) { return false; }
        }

        private void OnAnimationStarted(AnimWatch watch, Animator animator, GameObject model, int hash)
        {
            try
            {
                if (AutoConfig.LingYongAnimMode == "hide")
                {
                    HideModel(model);
                }
                else
                {
                    if (watch.OriginalSpeed <= 0f) watch.OriginalSpeed = SafeSpeed(animator, 1f);
                    animator.speed = SpeedUpFactor;
                    watch.AcceleratedAt = Time.unscaledTime;
                }

                if (AutoConfig.LingYongAnimDebug)
                {
                    Log?.LogInfo("LingYongAnimSkip: 模型动画 [" + watch.Name + "] state=" + hash
                        + " → " + (AutoConfig.LingYongAnimMode == "hide" ? "隐藏" : "加速"));
                }
            }
            catch (Exception e)
            {
                Log?.LogWarning("LingYongAnimSkip: anim handle failed: "
                    + StringTruncator.FirstLine(e.ToString()));
            }
        }

        /// <summary>把加速过的动画速度还原。</summary>
        private static void RestoreSpeed(AnimWatch watch)
        {
            if (watch == null || watch.AcceleratedAt <= 0f) return;
            watch.AcceleratedAt = 0f;
            try
            {
                if (watch.Animator != null) watch.Animator.speed = watch.OriginalSpeed;
            }
            catch (Exception) { }
        }

        private static float SafeSpeed(Animator animator, float fallback)
        {
            try { return animator.speed; }
            catch (Exception) { return fallback; }
        }

        /// <summary>把之前加速过的动画速度还原（状态切换或超时）。</summary>
        private void RestoreSpeeds()
        {
            if (_anims.Count == 0) return;
            foreach (KeyValuePair<int, AnimWatch> kv in _anims)
            {
                AnimWatch watch = kv.Value;
                if (watch == null || watch.AcceleratedAt <= 0f) continue;
                if (Time.unscaledTime - watch.AcceleratedAt <= MaxBoostDuration) continue;
                RestoreSpeed(watch);
            }
        }

        // ============ 范围判定 ============

        /// <summary>
        /// 这块灵俑是不是我们要处理的（默认只管局外）。
        /// scope=lobby：必须是大厅 PlayerInfoCtr 挂着的那批；
        /// scope=all  ：场景里的灵俑都算。
        /// </summary>
        private static bool InScope(XiaoChouPaiDisplay display)
        {
            if (AutoConfig.LingYongAnimScope == "all") return true;
            if (display == null) return false;
            try
            {
                PlayerInfoCtr info = UnityEngine.Object.FindObjectOfType<PlayerInfoCtr>();
                if (info == null) return false;
                var list = info._displays;
                if (list == null) return false;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] == display) return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
            return false;
        }

        // ============ 隐藏模型 ============

        private void HideModel(GameObject model)
        {
            var hidden = new HiddenModel { Go = model, Until = Time.unscaledTime + HideDuration };
            try
            {
                Renderer[] rs = model.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rs.Length; i++)
                {
                    if (rs[i] == null) continue;
                    hidden.Renderers.Add(rs[i]);
                    hidden.RendererEnabled.Add(rs[i].enabled);
                    rs[i].enabled = false;
                }
            }
            catch (Exception e)
            {
                Log?.LogWarning("LingYongAnimSkip: hide model failed: "
                    + StringTruncator.FirstLine(e.ToString()));
            }
            _hiddenModels.Add(hidden);
        }

        private void RestoreModels()
        {
            for (int i = _hiddenModels.Count - 1; i >= 0; i--)
            {
                HiddenModel m = _hiddenModels[i];
                if (Time.unscaledTime < m.Until) continue;
                _hiddenModels.RemoveAt(i);
                try
                {
                    for (int j = 0; j < m.Renderers.Count; j++)
                    {
                        if (m.Renderers[j] != null) m.Renderers[j].enabled = m.RendererEnabled[j];
                    }
                }
                catch (Exception) { }
            }
        }

        // ============ 灵俑时间轴演出 ============

        private void ScanTimeLines()
        {
            CollectDirectors();
            for (int i = 0; i < _directors.Count; i++)
            {
                PlayableDirector pd = _directors[i];
                if (pd == null) continue;

                bool playing;
                try { playing = pd.state == PlayState.Playing; }
                catch (Exception) { continue; }

                if (!playing)
                {
                    Restore(pd);
                    continue;
                }

                if (AutoConfig.LingYongAnimMode == "hide") Hide(pd);
                else Skip(pd);
            }

            if (AutoConfig.LingYongAnimDebug) WatchPlayingDirectors();
        }

        /// <summary>瞬间跳过：把播放头推到片尾并求值，动画直接结束。</summary>
        private void Skip(PlayableDirector pd)
        {
            try
            {
                double dur = pd.duration;
                if (dur > 0 && !double.IsInfinity(dur) && !double.IsNaN(dur))
                {
                    pd.time = dur;
                    pd.Evaluate();
                }
                if (pd.state == PlayState.Playing) pd.Pause();

                if (AutoConfig.LingYongAnimDebug)
                {
                    Log?.LogInfo("LingYongAnimSkip: skipped timeline [" + Path(pd.gameObject) + "] dur="
                        + dur.ToString("0.00") + "s");
                }
            }
            catch (Exception e)
            {
                Log?.LogWarning("LingYongAnimSkip: skip failed: " + StringTruncator.FirstLine(e.ToString()));
            }
        }

        /// <summary>隐藏画面：时间轴照常播放，但渲染器 / CanvasGroup 全关，结束后还原。</summary>
        private void Hide(PlayableDirector pd)
        {
            int id = pd.GetInstanceID();
            if (_hidden.ContainsKey(id)) return;

            var state = new HiddenState();
            try
            {
                GameObject go = pd.gameObject;
                if (go != null)
                {
                    Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
                    for (int i = 0; i < rs.Length; i++)
                    {
                        if (rs[i] == null) continue;
                        state.Renderers.Add(rs[i]);
                        state.RendererEnabled.Add(rs[i].enabled);
                        rs[i].enabled = false;
                    }

                    CanvasGroup[] gs = go.GetComponentsInChildren<CanvasGroup>(true);
                    for (int i = 0; i < gs.Length; i++)
                    {
                        if (gs[i] == null) continue;
                        state.Groups.Add(gs[i]);
                        state.Alpha.Add(gs[i].alpha);
                        gs[i].alpha = 0f;
                    }
                }
            }
            catch (Exception e)
            {
                Log?.LogWarning("LingYongAnimSkip: hide failed: " + StringTruncator.FirstLine(e.ToString()));
            }
            _hidden[id] = state;

            if (AutoConfig.LingYongAnimDebug)
            {
                Log?.LogInfo("LingYongAnimSkip: hidden timeline [" + Path(pd.gameObject) + "]");
            }
        }

        private void Restore(PlayableDirector pd)
        {
            int id = pd.GetInstanceID();
            if (!_hidden.TryGetValue(id, out HiddenState state)) return;
            _hidden.Remove(id);
            try
            {
                for (int i = 0; i < state.Renderers.Count; i++)
                {
                    if (state.Renderers[i] != null) state.Renderers[i].enabled = state.RendererEnabled[i];
                }
                for (int i = 0; i < state.Groups.Count; i++)
                {
                    if (state.Groups[i] != null) state.Groups[i].alpha = state.Alpha[i];
                }
            }
            catch (Exception e)
            {
                Log?.LogWarning("LingYongAnimSkip: restore failed: " + StringTruncator.FirstLine(e.ToString()));
            }
        }

        /// <summary>
        /// 收集灵俑演出时间轴。演出组件是演出时才创建出来的，所以主要看三个静态单例；
        /// 单例还没建立时，再用场景搜索兜底。
        /// </summary>
        private void CollectDirectors()
        {
            _directors.Clear();

            try
            {
                Add(LingYongTimeLineController.Ins?.PlayableDirector);
                Add(LingYongTimeLineController2.Ins?.PlayableDirector);
                Add(LingYongTimeLineUIController.Ins?.PlayableDirector);
            }
            catch (Exception e)
            {
                Log?.LogWarning("LingYongAnimSkip: collect failed: " + StringTruncator.FirstLine(e.ToString()));
            }

            if (_directors.Count == 0 && Time.unscaledTime >= _nextSearch)
            {
                _nextSearch = Time.unscaledTime + 0.5f;
                try
                {
                    var ctors = UnityEngine.Object.FindObjectsOfType<LingYongTimeLineController>(true);
                    for (int i = 0; i < ctors.Length; i++)
                    {
                        if (ctors[i] != null) Add(ctors[i].PlayableDirector);
                    }
                    var ctors2 = UnityEngine.Object.FindObjectsOfType<LingYongTimeLineController2>(true);
                    for (int i = 0; i < ctors2.Length; i++)
                    {
                        if (ctors2[i] != null) Add(ctors2[i].PlayableDirector);
                    }
                    var uiCtrs = UnityEngine.Object.FindObjectsOfType<LingYongTimeLineUIController>(true);
                    for (int i = 0; i < uiCtrs.Length; i++)
                    {
                        if (uiCtrs[i] != null) Add(uiCtrs[i].PlayableDirector);
                    }
                }
                catch (Exception e)
                {
                    Log?.LogWarning("LingYongAnimSkip: search failed: " + StringTruncator.FirstLine(e.ToString()));
                }
            }

            if (AutoConfig.LingYongAnimDebug && !_loggedReady && _directors.Count > 0)
            {
                _loggedReady = true;
                Log?.LogInfo("LingYongAnimSkip: 灵俑演出时间轴已接管 " + _directors.Count + " 条");
            }
        }

        private void Add(GameObject go)
        {
            if (go == null) return;
            try
            {
                PlayableDirector pd = go.GetComponent<PlayableDirector>();
                if (pd == null) pd = go.GetComponentInChildren<PlayableDirector>(true);
                Add(pd);
            }
            catch (Exception e)
            {
                Log?.LogWarning("LingYongAnimSkip: add failed: " + StringTruncator.FirstLine(e.ToString()));
            }
        }

        private void Add(PlayableDirector pd)
        {
            if (pd == null) return;
            for (int i = 0; i < _directors.Count; i++)
            {
                if (_directors[i] == pd) return;
            }
            _directors.Add(pd);
        }

        /// <summary>调试用：定期报告场景里正在播放的时间轴。</summary>
        private void WatchPlayingDirectors()
        {
            if (Time.unscaledTime < _nextWatch) return;
            _nextWatch = Time.unscaledTime + 1f;
            try
            {
                var pds = UnityEngine.Object.FindObjectsOfType<PlayableDirector>(true);
                for (int i = 0; i < pds.Length; i++)
                {
                    var pd = pds[i];
                    if (pd == null) continue;
                    if (pd.state != PlayState.Playing) continue;
                    int id = pd.GetInstanceID();
                    if (_reportedPlaying.Contains(id)) continue;
                    _reportedPlaying.Add(id);
                    Log?.LogInfo("LingYongAnimSkip: 播放中 [" + Path(pd.gameObject) + "] dur="
                        + pd.duration.ToString("0.00") + " tracked=" + Tracked(pd));
                }
            }
            catch (Exception e)
            {
                Log?.LogWarning("LingYongAnimSkip: watch failed: " + StringTruncator.FirstLine(e.ToString()));
            }
        }

        private bool Tracked(PlayableDirector pd)
        {
            for (int i = 0; i < _directors.Count; i++)
            {
                if (_directors[i] == pd) return true;
            }
            return false;
        }

        private static string Path(GameObject go)
        {
            if (go == null) return "?";
            string p = go.name;
            Transform t = go.transform.parent;
            int guard = 0;
            while (t != null && guard++ < 8)
            {
                p = t.name + "/" + p;
                t = t.parent;
            }
            return p;
        }
    }
}
