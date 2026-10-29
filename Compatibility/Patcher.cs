using System;
using System.IO;
using BepInEx;
using BepInEx.Preloader.Core.Patching;
using HarmonyLib;
using Il2CppInterop.Runtime.Startup;

namespace DemonicMahjong.Compatibility
{
    [PatcherPluginInfo("wufeng.demonicmahjong.compatibility", "DemonicMahjong Compatibility", GitVersion.Version)]
    public class Patcher : BasePatcher
    {
        public override void Finalizer()
        {
            string assemblies = Path.Combine(Paths.GameDataPath, "ScriptingAssemblies.json");
            if (!File.Exists(assemblies)) return;
            var runtime = typeof(Il2CppInteropRuntime).Assembly;
            if (!RuntimeDetection.NeedsMetadataHookWorkaround(File.ReadAllText(assemblies),
                Il2CppInteropRuntime.Instance.UnityVersion, runtime.GetName().Version))
                return;

            // HybridCLR 的类型句柄路径不是 int 索引；原框架把它误识别为索引函数，截断指针后崩溃。
            // 必须早于 Chainloader 的 Unity 日志委托注册执行，放在普通插件 Load 中已经太晚。
            var helpers = runtime.GetType("Il2CppInterop.Runtime.Injection.InjectorHelpers", true);
            var setup = AccessTools.Method(helpers, "Setup");
            if (setup == null) throw new MissingMethodException(helpers.FullName, "Setup");
            new Harmony(Info.GUID).Patch(setup,
                transpiler: new HarmonyMethod(typeof(InteropPatch), nameof(InteropPatch.SkipMetadataHook)));
            Log.LogInfo("HybridCLR detected: incompatible metadata-index hook disabled before Chainloader startup.");
        }
    }
}
