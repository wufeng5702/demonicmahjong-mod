# ScorePreview — 我在地府打麻将 分数预览 Mod

对局中在屏幕左上角用两行 IMGUI 悬浮显示**预计得分**：

```
计分: 底分 x 番数 x 倍率 = 预计分     （计分按钮可用时显示其预览；计分面板打开后镜像真实结算）
和牌: 底分 x 番数 x 倍率 = 预计分     （听牌时显示，番数对多等待取最小）
```

- **番数是权威的、不推算**：直接读游戏自身显示的数字——
  - 和牌番数 = 听牌面板每个候选的 `FanNum`（`<color=#75D962>16番`，多等待取最小）；
  - 计分番数 = **计分按钮**上的合计（`JiFen/FenXing/ExpandedButton/Total`，如 `6 番`，
    与和牌预览不是同一处）。
- 底分 = 实时 `BaseScoreText`；倍率 = 听牌钩子算得的精确值（与结算一致），缺省时读玩家
  `PlayerStates/Independent`（如 2.3）。
- 原则：与游戏结算公式 `总 = 底分 × 番数 × 倍率` 一致；不修改游戏本体、不注入逻辑。

## 现状（2026-08-30）

- ✅ **和牌行已正常**：听牌时读 FanNum 面板（权威番数）→ `和牌: 145 x 16 x 2.25 = 5220`。
- ✅ **计分行已正常**：计分按钮一出现即预览（按钮上的番数）→ `计分: 145 x 30 x 2.25 = 9787.5`；
  点开计分面板后镜像真实结算（`50 x 147 x 2.25 = 49,612` 类样本已实测）。
- ✅ 听牌钩子 `PlayerPipeline.OnProcessTingResult(Auto)`（空字典=未听）仍用于：倍率精确值、
  以及 FanNum 读不到时的兜底番数。
- ✅ HUD 可下移：读 dll 同目录 `ScorePreview.yml` 的 `yoffset`（屏幕高度比例，默认 0.1），
  避开左上角生命/魂力 UI。

## 配置 — `BepInEx\plugins\ScorePreview.yml`

```
yoffset: 0.1    # HUD 距顶部的下移量 = 屏幕高度 × 该比例（1.0=满屏高），换分辨率不变形
```

无文件 = yoffset 0.1；改后重启游戏生效。上一次报错检查点：启动日志 `ScoreHud active ... yoffset=N`。

## 安装现状（已完成）

游戏实际安装目录（仓库根 `.env` 的 `DEMONIC_MAHJONG_DIR`）已装入 BepInEx 6：

```
DemonicMahjong/
  winhttp.dll / doorstop_config.ini / dotnet\      BepInEx 前置（Doorstop 启动钩子）
  BepInEx\
    core\      BepInEx + Il2CppInterop 运行时
    plugins\ScorePreview.dll         ← 本 Mod（已装）
    interop\                         ← 已生成（MaJiang.dll 等，首次启动时自动生成）
```

### 哪些不是本 Mod、可删/不必保留

目录里的大部分东西是 **BepInEx 框架本身**（前置必需），不是我们的插件加的：

- `winhttp.dll` / `doorstop_config.ini` / `dotnet\` —— BepInEx 6 IL2CPP 的加载器与 .NET 运行时，必需。
- `BepInEx\core\`、`BepInEx\unity-libs\` —— BepInEx 运行库，必需。
- `BepInEx\interop\`（约 93MB）—— 首次启动自动生成的互操作程序集，**可删**，下次启动会重新生成。
- `BepInEx\cache\chainloader_typeloader.dat`、`BepInEx\LogOutput.log`、`ErrorLog.log` —— 缓存/日志，可删。

**本 Mod 的全部内容只有两个文件**（各 ~30KB）：
`BepInEx\plugins\ScorePreview.dll` 与（可选的）`BepInEx\plugins\ScorePreview.yml`。

### 最小化安装（新机器 / 换电脑）

1. 装入 BepInEx 6（IL2CPP 版），首次启动游戏让它生成 `interop\`。
2. 把 `ScorePreview.dll`（和需要的 `ScorePreview.yml`）拷进 `BepInEx\plugins\`。
3. 不需要带 `interop\` / `cache` / 日志；`install.bat` 也只拷贝插件 dll 一个文件。

## 目录结构

```
mod/ScorePreview/
  ScorePreview.csproj    GameDir 读取环境变量 DEMONIC_MAHJONG_DIR（build.bat 从仓库根 .env 传入）
  Plugin.cs              入口：AddComponent<ScoreHud>()；Harmony 建树 PatchAll
  ScoreHud.cs            IMGUI 两行 HUD：读游戏 UI（FanNum / 计分按钮 Total / BaseScore /
                         Independent）+ 结算镜像；yoffset 配置
  Prediction.cs          听牌钩子 TingHookPatch（提供精确倍率 + 兜底小番求和）
  build.bat              编译（可选参数：游戏目录；从 .env 读 DEMONIC_MAHJONG_DIR）
  install.bat            拷贝 dll 到游戏 BepInEx\plugins\
  README.md
```

## 日常构建 / 安装

```
build.bat        → 编译
install.bat      → 覆盖安装到游戏 BepInEx\plugins\
```

改动源码或想装到别的机器时，可用 `build.bat D:\其他\游戏目录`、`install.bat D:\其他\游戏目录`。

⚠️ 游戏在跑时安装会因文件占用失败（install.bat 报"另一个程序正在使用此文件"）——先
`taskkill //F //IM "Demonic Mahjong.exe"` 再 install。

## 验证方式

- 启动后看 `BepInEx\LogOutput.log`：应有 `Loading [ScorePreview 0.1.0]`、`ScoreHud active ... yoffset=N`。
- 听牌出现时：`Diag: uiFanMin=N from [16,15,16]`（和牌番数取自听牌面板）。
- 计分按钮出现时：`Diag: jfFan=N from [6 番]`（计分番数取自计分按钮）。
- HUD 每轮打印：`hud -> 计分: ... | 和牌: ...`。
- 结算镜像：`[settle] ... mul0/mul1/mul2/total` 与游戏面板一致。

## 技术说明 / 坑（重要，改代码必读）

- **游戏主体代码在 `MaJiang.dll`，不是 `Assembly-CSharp.dll`**。
- **游戏 exe 名带空格**：`Demonic Mahjong.exe`；taskkill/start 用带空格的完整原名。
- **不要显式调 `Il2CppInterop.HarmonySupport` 的 `AddHarmonySupport`**：BepInEx6 已自动注册，
  再调抛重复键异常。直接用 `new Harmony(GUID); harmony.PatchAll(asm);`。
- **泛型接口门面无成员**（`IReadOnlyDictionary`/`IReadOnlyList`/`IEnumerable` 不能
  foreach/`.Count`）。数据从具体类取：`Dictionary<K,V>`(`_count`+`_entries[i]`)、
  `List<T>`(`Count`/`Item`)、`HashSet<T>`(`_buckets`/`_slots`/`_count`)。
- **interop 对象间类型转换必须用原生 `Cast<T>()`**（`Il2CppInterop.Runtime`）；托管强转
  /`as`/`(object)` 对 interop 门面一律失败。
- `HuResult.FanZhongs` 运行时是 **`HashSet<FanZhong>`**；`FanZhongPayload.fan` 是大分值、
  不一定等于结算番数，**结算小番 = `payload.number` 之和**（样本：num 5+5+5=15 == FanNum 15）。
- **HashSet.Slot 原生布局不稳定**：interop `Slot.value` 有时读到脏值；`FillFromSet` 用
  参数扫描（base∈{0x10,0x18} × stride∈{12,16} × valOff∈{0,4,8}，取全落在枚举区间的一组）。
- **结算数字是动画的**（`TweenMultiplyNumbersNumber`）：文本会出现 0/`1234567`/中间值；
  计分行只在数字稳定（含 `sprite name`）后采信 `LastSettleFactors`，面板关闭即归 `--`。
- `Il2CppSystem.Decimal` 用 `lo/mid/hi/flags` 位布局重建 `System.Decimal`。
- `PlayerRoundStatistics` 继承 `SaintsMonoBehaviour` → 引 `SaintsField.Runtime.dll`；
  读 `TMP_Text` 引 `Unity.TextMeshPro.dll` + `UnityEngine.UI.dll`。
- IMGUI 默认字体仅 ASCII，中文会渲染成方块 → HUD 只显示 `计分:`/`和牌:` 两个短标签
  （数值均为 ASCII），其余说明文字放中文没问题但只在日志里。

### 历史路径（不再走，勿回退）

旧方案是把番型集合 re-inject 进 `GetTotalScore(IEnumerable<IEnumerable<FanZhong>>, …)`，
依赖外层容器 `List<IEnumerable<FanZhong>>`（T 与接口完全一致 + 原生 `Cast<T>()`）。
现在改为直接读游戏自身 UI 数字（FanNum / 计分按钮 Total），更稳、免推算；该泛型坑仅在
自行调 `GetTotalScore` 时需要注意。

## 卸载

删除游戏 `BepInEx\plugins\ScorePreview.dll`（和 `ScorePreview.yml`）；不再需要 BepInEx 时
删除 `BepInEx\`、`winhttp.dll`、`doorstop_config.ini`、`dotnet\` 即可，游戏本体不受影响。

## 游戏更新后

BepInEx 每次启动会自动重新生成 `interop/`。若签名变化导致编译失败，重跑
`build.bat` + `install.bat`；若 UI 结构/文本变了导致读不到 FanNum/Total，看日志
`uiFanMin`/`jfFan` 是否消失即可定位。