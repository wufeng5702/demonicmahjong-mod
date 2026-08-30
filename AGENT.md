# AGENT.md — DemonicMahjong Mod 工作区

本目录承载该游戏的 **BepInEx 6 (IL2CPP)** 插件开发。根目录 `AGENT.md` 是资产提取项目的
全局说明；本文件只记录 mod 工作本身的架构事实、坑与流程，供后续会话快速上手。

## 现状

- 项目：**ScorePreview**（左上角 IMGUI 两行 HUD：`计分:` 镜像结算 / `和牌:` 听牌预测）。
- 阶段：**和牌行已正常**（番数直接读游戏听牌面板 FanNum，见「番数与 FanNum」）；计分行靠结算
  文本镜像，动画期间会闪 0（见「关键坑」）。无需再把番型灌回 `GetTotalScore`。
- 个人路径全部走 `.env`（仓库根 `mod/.env`，已 gitignore）：`DEMONIC_MAHJONG_DIR`。
  日志：`%DEMONIC_MAHJONG_DIR%\BepInEx\LogOutput.log`。

## 目录

```
mod/
  AGENT.md                 本文件（唯一交接文档；HANDOFF-*.md 已并入此文件后删除）
  .env                     <本机可改，不入库> 个人路径（DEMONIC_MAHJONG_DIR=游戏目录）
  ScorePreview/            插件源码（csproj/Plugin/ScoreHud/Prediction/README/build/install）
  tools/dumptypes/         类型探查工具（Mono.Cecil 读 interop 公有成员；libs/ 为本地拷贝库，不入库）
```

## 构建 / 安装 / 验证循环

```powershell
# 游戏目录来源：命令行参数 > 环境变量 DEMONIC_MAHJONG_DIR > .env 同名字段 > 旧硬编码兜底
# 1) 一键安装/卸载（交互式选 mod + Steam 自动探测 + BepInEx 依赖自动补装）
.\install_mods.bat
#    非交互：.\install_mods.bat   （对 install_mods.ps1 透传参数）
#    powershell -ExecutionPolicy Bypass -File install_mods.ps1 -Mods 1,2 -SkipBepInEx
#    powershell -ExecutionPolicy Bypass -File install_mods.ps1 -u -RemoveBepInEx
# 2) 手动编译/安装
.\build.bat                 # 或 dotnet build -c Release（编译物在 bin\Release\）

# 3) 安装（必须先关游戏，否则"另一个程序正在使用此文件"）
taskkill //F //IM "Demonic Mahjong.exe"   # exe 名带空格！勿用错名
.\install.bat               # 拷贝到 游戏\BepInEx\plugins\

# 4) 启动游戏并验证
start "" "%DEMONIC_MAHJONG_DIR%\Demonic Mahjong.exe"
# 看日志（别直接 tail 整个文件）：
grep -aE "ScorePreview|ting hook|uiFan|D: |Error" "%DEMONIC_MAHJONG_DIR%\BepInEx\LogOutput.log" | tail
```

验证口径：
- 构建 0 错误；install 后 `plugins\ScorePreview.dll` 时间戳 = 刚编译（装前忘关游戏会残留旧 dll，
  症状 = 日志行为与源码不符）。
- 加载：`Loading [ScorePreview …]` + `ScoreHud active`。
- 听牌：钩子 `D: calls=N` 递增；`ting hook -> none: 原因` = 未成功。HUD 两行各自独立出数。

警惕一坑：BepInEx 6 只作为 **prerelease** 发布，`/releases/latest` 只会命中旧 5.x → 依赖下载必须用
`releases` 列表 + 资产名匹配 `(?i)il2cpp`+`x64`+非 `x86/linux/macos/unix`；直连失败自动换镜像
`https://gh.ddlc.top/<原GitHub地址>`（install_mods.ps1 内 Get-WithRetry/Save-WithRetry）。
首次装 BepInEx 后 interop/ 未生成，mod 编译必失败 → 先启动一次游戏（或用同版本开发拷贝的
`BepInEx\interop` + `unity-libs` + `config` 补齐），再跑脚本。

## 番数真相与 FanNum（最重要）

- 游戏听牌按钮/结算显示的番数 = **小番**。`FanZhongPayload.fan` 是大分值（88/64/16…，
  不可直接用）；`FanZhongPayload.number` 才是每番种的小番（如 id1/2/7 → num=5/5/5，
  5+5+5=15 精确等于同刻 FanNum=15番）。
- **权威读法：听牌面板每个候选有 `FanNum` TMP（GO 名为 `FanNum`）**，文本如
  `<color=#75D962>16番`[Count=×4]`。ScoreHud.TryFanNumMin 直接 FindObjectsOfType<TMP_Text>
  解析「数字+番」，**多等待取最小** → 和牌行。日志 `uiFanMin=N from [16,15,16]` 可核对。
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
    FillFromSet 用参数扫描：`base∈{0x10,0x18} × stride∈{12,16} × valOff∈{0,4,8}`，
    选全部落在 [0,1024] 且分数最高的组合；`slotcfg ... vals=[0:17,1:…]` 进日志。
11. **结算数字动画**：`TweenMultiplyNumbersNumber` 改的是文本，动画中 0/1234567/中间值
    （如 `150 x 0 x 2.81`）。计分行用**稳定后的文本**（含 `sprite name` + ReadyNum 才采信），
    只镜像 `LastSettleFactors`，面板关闭(签名→false)即归 `--`。`_curNumbers`/`_totalNumber`
    RawDecimals 原生读待验证，别依赖。
12. **HUD 中文**：IMGUI 默认字体仅 ASCII，中文会渲染成方块 → HUD 文案用中文标签「计分/和牌」
    只在日志里，屏显如 `和牌: 150 x 16 x 2.25 = 5400`（纯 ASCII）。两行由 `\n` 拼接。
13. `FanZhong` 枚举 id 前缀匹配：`FanZhongCtr=箭刻2/风刻2/全带幺4…` + `FanNum=X番` 是强旁证。

## 工具 / 常用命令

```powershell
dotnet build -c Release                                          # 编译插件（mod\ScorePreview）
.\build.bat / .\install.bat                                      # 快捷构建/安装（读 .env 游戏目录）
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
      确认动画完成前不显示 `x 0`。重放 `150x147x2.25=49,612` 与 `160x29x2.25=10,440`。
- [ ] 计分随时可点（非听牌）场景：打出区番数 → 计分镜像同源覆盖。
- [ ] Boss 分（`RoundStatisticsBase.AiTotalScore`）、每局明细（GetTotalScore 四元组）V2。

## 开发规范

- **阶段性修改及时提交**：完成一个功能/修复后立即 `git commit`
- 提交信息格式：`feat/fix/chore: 简要描述`