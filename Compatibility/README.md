# DemonicMahjong Compatibility — 启动兼容补丁

这是四个 mod 共用的框架兼容补丁，不是可选的第五个 mod。
安装器会先编译它，再复制到 `BepInEx/patchers/DemonicMahjong.Compatibility.dll`；
手动 `build.bat` / `install.bat` 也会构建、安装它。

## 原因与范围

月圆奇谭更新的游戏使用 Unity `6000.3.21f1`、IL2CPP metadata v39 和 HybridCLR。
本机干净副本在只安装 BepInEx `be.791`、没有任何插件时也能复现 `0xC0000005`：

```text
MetadataCache_GetTypeInfoFromTypeDefinitionIndex_Hook.Hook(Int32)
Class_FromName_Hook.Hook(...)
IL2CPPChainloader.SetupUnityLogging()
```

HybridCLR 改动了类型句柄路径，Il2CppInterop 1.5.3 的 xref 遍历误把它识别成整数索引函数，
钩子截断原生指针后导致崩溃。因此补丁在 Preloader 的 `Finalizer()` 中，
早于 Unity 日志委托和普通插件注册，只移除 `InjectorHelpers.Setup` 中这个钩子的安装调用。
其他注入和 Harmony 功能不变，不替换框架 DLL、不修改游戏文件。
相关上游讨论：[Il2CppInterop #260](https://github.com/BepInEx/Il2CppInterop/pull/260)。

仅对 **Unity 6.3 + `ScriptingAssemblies.json` 中的 `HybridCLR.Runtime.dll` + Il2CppInterop 1.5.3** 生效。
普通运行时及未来版本不应用补丁；目标 IL 结构不匹配时拒绝猜测。
`Class::Init ... using a substitute` 警告不是本次崩溃原因，未为消除警告改动其他原生钩子。

启动日志应出现：

```text
HybridCLR detected: incompatible metadata-index hook disabled before Chainloader startup.
Chainloader startup complete
```

## 验证

```powershell
dotnet test Shared.Tests/Shared.Tests.csproj -c Release
powershell -File tools/tests/installer.Tests.ps1
dotnet build Compatibility/Compatibility.csproj -c Release -p:GameDir="游戏目录"
```

单元测试覆盖运行时识别、只移除目标调用、保留 IL 标签/异常块和拒绝未知布局；
安装器测试覆盖空/不完整 interop、过期缓存、失败日志、进程崩溃/超时、旧插件恢复和保留 `Custom` 内容。
实际启动检查必须验证完整程序集和 `Chainloader startup complete`（已自然退出还必须验证退出码），
完整加载后仅关闭安装器启动的检查进程，不能依赖独立播放器处理 `-quit`。
不能只看到目录或 `Chainloader initialized` 就判成功。
