# AGENT.md — DemonicMahjong Mod 工作区

本目录承载该游戏的 **BepInEx 6 (IL2CPP)** 插件开发。本文件只记录 mod 工作本身的架构事实、
坑与流程，供后续会话快速上手。

## 现状

三个 Mod 均已完成并稳定运行：

| Mod | 功能 | 关键文件 |
|-----|------|----------|
| **ScorePreview** | 左上角 IMGUI 四行 HUD：`计分:` 镜像结算 / `和牌1/2/3:` 听牌预测 | `ScoreHud.cs` / `Prediction.cs` |
| **SLMenuTrigger** | 牌堆耗尽且分数低于 Boss 时自动暂停游戏，给玩家手动 SL 时间 | `SLMenuTrigger.cs` / `Plugin.cs` |
| **AutoContinue** | 自动跳过公告【继续】与对局【点击继续】 | `AutoSkip.cs` |

共享工具库在 `Shared/`（`StringTruncator` / `NumberParser` / `TransformPath` / `YamlConfig` /
`FanTextParser` / `ScoreFormula` / `UiText` / `GitVersion`），各 `.csproj` 通过
`<Compile Include="..\Shared\*.cs" />` 引入，其中只有 `TransformPath` 依赖 UnityEngine。
纯函数另有 `Shared.Tests/`（xunit，net8.0，不依赖 interop），见下「验证口径」。

**版本号唯一事实源是 `Shared/GitVersion.cs`**：三个 csproj 的 `<Version>` 必须与它一致，
release.yml 发版时把 tag 写进两者（tag 格式 `vX.Y.Z`），本地改版本用 `set-version.ps1`（不入库）。

个人路径全部走 `.env`（仓库根 `mod/.env`，已 gitignore）：`DEMONIC_MAHJONG_DIR`。
日志：`%DEMONIC_MAHJONG_DIR%\BepInEx\LogOutput.log`。

## 目录

```
mod/
  AGENT.md                 本文件（唯一交接文档）
  .env                     <本机可改，不入库> 个人路径（DEMONIC_MAHJONG_DIR=游戏目录）
  Shared/                  共享工具库（StringTruncator / NumberParser / TransformPath / YamlConfig /
                           FanTextParser / ScoreFormula / UiText / GitVersion）
  Shared.Tests/            纯函数单元测试（xunit，不编 mod、不碰 interop）
  ScorePreview/            分数预览 mod（csproj/ScoreHud/Prediction/README）
  SLMenuTrigger/           自动暂停 mod（csproj/SLMenuTrigger/Plugin/README）
  AutoContinue/            自动跳过 mod（csproj/AutoSkip/README）
  install_mods.ps1         一键安装/卸载脚本（仓库根只留这一组入口）
  tools/modbat/            三个 mod 的 build.bat/install.bat 共用驱动；不放根目录，免得被当成入口
                           （包装里只传 mod 名；注意别用 shift —— shift 会连 %0 一起移，%~dp0 就错了）
  tools/dumptypes/         类型探查工具（Mono.Cecil 读 interop 公有成员；libs/ 为本地拷贝库，不入库）
  tools/compiler-licenses/ 捆绑编译器许可文本（release.yml 打包时复制进 tools\compiler\）
  tools/compiler/          捆绑编译器（release 包内才有，不入库；csc.exe + ref\<tfm>\ 引用程序集）
```

## 构建 / 安装 / 验证循环

```powershell
# 游戏目录来源：命令行参数 > 环境变量 DEMONIC_MAHJONG_DIR > .env 同名字段 > 旧硬编码兜底
# 1) 一键安装/卸载（交互式选 mod + Steam 自动探测 + BepInEx 依赖自动补装）
#    BepInEx 版本探测：读 core\BepInEx.Core.dll 的 ProductVersion 取 be 构建号，低于 be.785 会询问
#    是否覆盖升级（升级前清 core/patchers，保留 config/plugins/interop，升级后清 cache 并复核版本）
.\install_mods.bat
#    非交互：.\install_mods.bat   （对 install_mods.ps1 透传参数）
#    powershell -ExecutionPolicy Bypass -File install_mods.ps1 -Mods 1,2 -SkipBepInEx
#    powershell -ExecutionPolicy Bypass -File install_mods.ps1 -u -RemoveBepInEx
#    -Compiler auto|sdk|csc：auto(默认)=有 .NET SDK 用 SDK，否则用包内 tools\compiler 的 csc；
#    SDK 编译失败且 auto 时自动退回包内 csc。csproj 的 HintPath/LangVersion/Nullable/Version
#    全部由 Get-CsprojInfo 从 csproj 解析，csc 用 rsp 传参（-noconfig 必须写在命令行、ref 路径
#    用正斜杠+引号），另生成 AssemblyInfo.generated.cs 对齐 csproj 的 AssemblyVersion。
# 2) 手动编译/安装（需 .NET SDK；用户机器不装 SDK 也能装 mod，见上面 -Compiler）
cd ScorePreview
.\build.bat                 # 或 dotnet build -c Release（编译物在 bin\Release\；三个 mod 目录各有一份）

# 3) 安装（必须先关游戏，否则"另一个程序正在使用此文件"）
taskkill //F //IM "Demonic Mahjong.exe"   # exe 名带空格！勿用错名
.\install.bat               # 拷贝到 游戏\BepInEx\plugins\（根目录没有这两个 bat，必须在 mod 目录内执行）

# 4) 纯函数单元测试（改 Shared/ 后必跑；不需要游戏目录）
dotnet test Shared.Tests/Shared.Tests.csproj

# 5) 启动游戏并验证
start "" "%DEMONIC_MAHJONG_DIR%\Demonic Mahjong.exe"
# 看日志（别直接 tail 整个文件）：
grep -aE "ScorePreview|SLMenuTrigger|AutoContinue|ting hook|Error" "%DEMONIC_MAHJONG_DIR%\BepInEx\LogOutput.log" | tail
```

验证口径：
- **构建 0 警告 0 错误**；`dotnet test Shared.Tests/` 全绿；install 后 dll 时间戳 = 刚编译
  （装前忘关游戏会残留旧 dll）。
- csc 路径（`-Compiler csc`）：日志有 `编译器: csc` + `用包内 csc 编译（N 源文件，M 引用）`；
  产物与 SDK 产物的引用集、公有 API、AssemblyName/Version 必须逐项一致（Mono.Cecil 对比）。
- ScorePreview：`[ScorePreview] v<版本> loaded` → `ScoreHud active yoffset=… fontsize=… debug=…`；
  开 `debug` 后再看 `[diag] Comp.Try …ms` 与 `hud -> 计分: …`。
- SLMenuTrigger：`[SLMenuTrigger] v<版本> loaded` → `SLMenuTrigger cfg: enabled=True fontsize=24`
  → 牌堆耗尽时 `牌堆耗尽! Player < Boss. Pausing. TimeScale=0`。
- AutoContinue：`[AutoContinue] v<版本> loaded` → `AutoContinue cfg: announce=True/d=2 …`
  → 点击时 `AutoContinue: clicked …`。

警惕一坑：BepInEx 6 只作为 **prerelease** 发布，`/releases/latest` 只会命中旧 5.x → 依赖下载必须用
`releases` 列表 + 资产名匹配 `(?i)il2cpp`+`x64`+非 `x86/linux/macos/unix`。
下载到本地一律校验 **SHA256**（`$BepInExZipSha256`；release.yml 也校验 nupkg），解压后还必须
确认 `BepInEx\` + `BepInEx\core\BepInEx.Core.dll` 存在才允许动目标目录——否则坏包会把已有
安装改坏。GitHub 镜像代理（如 gh.ddlc.top）只反代 github.com，对 `builds.bepinex.dev` /
`api.nuget.org` 无意义，不要依赖。首次装 BepInEx 后 interop/ 未生成，mod 编译必失败 →
`install_mods.ps1` 检测到缺失时会**自动生成**：最小化启动游戏，等日志出现
`Chainloader initialized` 后强杀进程（`Invoke-AutoGenerateInterop`；超时/Steam 拦截时会退化为
提示手动启动一次游戏再继续）。也可手动启动一次游戏，或用同版本开发拷贝的 `BepInEx\interop` +
`unity-libs` + `config` 补齐。

捆绑编译器（release.yml 的 `Fetch bundled compiler` 步骤，打包前执行，产物只进 zip 不入库）：
- `microsoft.net.compilers.toolset` 4.8.0 → 只留 `tasks/net472/` 的 csc.exe + csc.exe.config +
  11 个依赖 dll（不含 VB/DiaSymReader/Scripting）；`microsoft.netcore.app.ref` 6.0.36 →
  `ref/net6.0/*.dll`（159 个）+ 两份 LICENSE/NOTICES，约 19MB。
- 许可：Roslyn 是 MIT，文本在 `tools/compiler-licenses/Microsoft.Net.Compilers.Toolset.txt`（入库），
  打包时复制进 `tools\compiler\`；ref pack 的许可直接从 nupkg 取。
- 新增 TFM/升 Roslyn 版本时要同步 release.yml 的裁剪清单与 `Get-CscDefines`。

## 番数真相与 FanNum（最重要）

- 游戏听牌按钮/结算显示的番数 = **小番**。`FanZhongPayload.fan` 是大分值（88/64/16…，
  不可直接用）；`FanZhongPayload.number` 才是每番种的小番（如 id1/2/7 → num=5/5/5，
  5+5+5=15 精确等于同刻 FanNum=15番）。
- **权威读法：听牌面板每个候选有 `FanNum` TMP（GO 名为 `FanNum`）**，文本如
  `<color=#75D962>16番`[Count=×4]`。ScoreHud.TryFanNumMin 直接 FindObjectsOfType<TMP_Text>
  解析「数字+番」，**多等待取最小** → 和牌行。
- 兜底路径：`payload.number` 求和（`FanSum`），再兜底错误兜底 Comp.Try。
- 结算公式（多次实证）：`总 = 底分(MultiplyNumbers[0]) × 番数([1]) × 倍率([2])`，
  倍率 = (1+Σ基础倍率) × Π(1+独立倍率)。样本：`150 x 147 x 2.25 = 49,612`、
  `160 x 29 x 2.25 = 10,440`（均与按钮 FanNum 小番同刻一致）。

## 关键架构事实（实测）

- 游戏主体代码在 **MaJiang.dll**（interop 于 `BepInEx\interop\`，BepInEx 首次启动自动生成）；
  不是 Assembly-CSharp.dll。
- **不存在手牌常驻可胡结果**：`PlayerHandPaiMianContainer.CanHuPaiMianPayloads` 对局内恒为空。
  必须走事件入口。
- **听牌数据入口**：`PlayerPipeline.OnProcessTingResult(IReadOnlyDictionary<PaiMianPayload,
  IReadOnlyList<HuResult>>)`（场景 MonoBehaviour 公有方法）。开牌/未听以空字典调用；真听有数据。
  Harmony：`[HarmonyPatch(typeof(PlayerPipeline), nameof(PlayerPipeline.OnProcessTingResult))]` Prefix。
- Tuple 四元组原生可读：天然 `(底分?, 番, 倍率, …)`，`Item2`=倍率（0.9/2.25/2.81 与结算一致）。
- **结算面板**：`HuPaiJieSuan`（RoundStatistics 下），`MultiplyNumbers` 数组 +
  `_curNumbers`(List<Decimal>) + `_totalNumber`(Decimal) + `TweenMultiplyNumbersNumber(int,float,Decimal)`
  数字动画。面板出现时 TMP 含 `sprite name`/`底分`/`倍率`/`Title`/`计分视为打出` 等签名。
- HashSet/HuResult 桶链取值：
  ```csharp
  for (int b = 0; b < set._buckets.Length; b++)
  {
      int i = set._buckets[b];
      while (i >= 0 && i < set._slots.Length)
      {
           // set._slots[i].value 即 FanZhong（interop 可能脏，见关键坑 10）
           i = set._slots[i].next;
      }
  }
  ```

## 关键坑（改代码必读）

1. **泛型接口门面无成员**：新版 Il2CppInterop 的 `IReadOnlyDictionary`/`IReadOnlyList`/
   `IEnumerable` 接口门面不暴露任何成员，不能 foreach/`.Count`。数据从**具体类**取：
   - `Dictionary<K,V>`：`_count` + `_entries[i].key/value`（`Entry{TKey,TValue}` 公开字段）
   - `List<T>`：`Count` / `Item`
   - `HashSet<T>`：见上桶链
2. **interop 对象间转换必须用原生 `Cast<T>()`**（`using Il2CppInterop.Runtime`）。
   托管强转/`as`/`(object)` 对 interop 门面一律失败——即使 T 完全相同。走 `il2cpp_class_is_assignable_from`。
3. **多层泛型参数要求层内 T 与接口完全一致**（旧 GetTotalScore 卡点，现已绕过不删）。
4. `HuResult.FanZhongs` 运行时是 `HashSet<FanZhong>`（非 List）。
5. **别显式调 `AddHarmonySupport`**（BepInEx6 已自动注册，再调抛重复键异常）。只用
   `new Harmony(GUID); harmony.PatchAll(asm);`。
6. `Il2CppSystem.Decimal` 不能直转 CLR `decimal`；用公开字段 `lo/mid/hi/flags` 位重建
   （`flags<0` 为负，`(flags>>16)&0x7F` 为 scale）。
7. `Il2CppSystem.ValueTuple`4` 有 `Item1..Item4`。
8. `PlayerRoundStatistics` 继承 `SaintsMonoBehaviour` → 引 `SaintsField.Runtime.dll`；
   读 `TMP_Text` 引 `Unity.TextMeshPro.dll` + `UnityEngine.UI.dll`。csproj 已配好。
9. **装 dll 前必须关游戏**（文件锁）。
10. **HashSet.Slot 原生布局漂移**：interop `Slot.value` 偶尔读到脏值（如 854339984）。
    `FillFromSet` 扫 `base∈{0x18,0x10} × stride∈{12,16} × valOff∈{0,4,8,12}`，
    **用 `KnownFanIds`（`_fanZhongPayloadList` 的 id 集合）做真值校验**：一个布局读出的 n 个值
    必须全部落在集合内才算通过，按 distinct 值数选最优（stride 大者优先）。真值集合本身为空
    （payload 列表未填充的时机窗口）或全部验不过 → 返回 false，调用方**跳过该胡型**
    （宁可不出预测也不显示错分数）。`slotcfg … verified=…`
    进日志，`debug=false` 时只留一行 `no layout validated`。
11. **结算数字动画**：`TweenMultiplyNumbersNumber` 改的是文本，动画中 0/1234567/中间值
    （如 `150 x 0 x 2.81`）。计分行用**稳定后的文本**（含 `sprite name` + ReadyNum 才采信），
    只镜像 `LastSettleFactors`，面板关闭(签名→false)即归 `--`。`_curNumbers`/`_totalNumber`
    RawDecimals 原生读待验证，别依赖。
12. **HUD 中文**：IMGUI 中文可正常渲染（已实测，字体回退到系统字体），`计分/和牌` 标签直接
    上屏。个别机器显示方块属字体回退缺失，不是代码问题。
13. `FanZhong` 枚举 id 前缀匹配：`FanZhongCtr=箭刻2/风刻2/全带幺4…` + `FanNum=X番` 是强旁证。
14. **NumberParser.CleanNumber**：保留逗号（ScorePreview 需要格式化数字）；SLMenuTrigger
    需要 `.Replace(",","")` 后再 `long.TryParse`。
15. **YamlConfig.Load**：接受文件名（如 `"Mod.yml"`），自动从 dll 所在目录加载。配置丢失时
    自动从 `defaults` 生成默认文件。三个 Mod 统一使用此接口。

## 工具 / 常用命令

```powershell
dotnet build -c Release                                          # 编译插件（各 mod 目录）
dotnet test Shared.Tests/Shared.Tests.csproj                     # 纯函数单测（任何目录都能跑）
.\build.bat / .\install.bat                                      # 快捷构建/安装（在各 mod 目录内执行，读 .env 游戏目录；
                                                                 #  根目录没有这两个入口，只有 install_mods.bat/.ps1）
   （各 mod 目录内的 build.bat/install.bat 只是调 ..\tools\modbat\build.bat / install.bat 的薄包装）
dotnet run --no-build -c Release -- "<interop.dll>" "<类型全名>"   # mod\tools\dumptypes 探查类型
taskkill //F //IM "Demonic Mahjong.exe"                          # 关游戏（带空格 exe 名）
```

dumptypes 用法细节：
- `*` 前缀 = 包含匹配，**勿带尾 `*`**；泛型全名含反引号，bash 用单引号包裹
  （如 `'Il2CppSystem.Collections.Generic.HashSet`1'`）。
- 可探查 `MaJiang.dll` / `Il2CppSystem.Core.dll`（HashSet 在此）。
- 开发拷贝：`E:\DemonicMahjong\DemonicMahjong\`（GameAssembly/global-metadata 哈希一致，
  可做 interop 试验）——路径走 .env 可加 `DEMONIC_COPY_DIR`。

## 待办（按优先级）

- [x] 和牌行番数 = 听牌按钮 FanNum 小番（多等待取最小）；已验证与结算一致。
- [ ] 交叉核对 `uiFanMin` 与 `payload.number` 求和（fanmap 日志）在若干对局中都相等；
      若总一致，可考虑去掉 UI 扫描（省 0.5s 关卡）。
- [ ] 计分行：`_curNumbers`/`_totalNumber` 原生读验证，替换「稳定文本」拿到权威值；
      确认动画完成前不显示 `x 0`。
- [ ] Boss 分（`RoundStatisticsBase.AiTotalScore`）、每局明细（GetTotalScore 四元组）V2。

## 开发规范

- **阶段性修改及时提交**：完成一个功能/修复后立即 `git commit`
- 提交信息格式：`feat/fix/chore: 简要描述`
- 改 `Shared/` 纯函数 → 补/改 `Shared.Tests/` 用例并跑 `dotnet test`（CI 会跑同一命令）。
- 三个 mod 目录下 `dotnet build` 必须 **0 警告 0 错误** 才算改完。
