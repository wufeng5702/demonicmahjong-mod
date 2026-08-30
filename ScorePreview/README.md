# ScorePreview — 我在地府打麻将 分数预览 Mod

对局中在屏幕左上角实时显示**预计得分**：
```
Est: 底分 x 番数 x 倍率 = 预计分
```
- 仅在**计分 / 听牌**（玩家手牌存在可胡结果）时显示，非听牌状态显示 `Est: --`。
- 底分 / 番数 / 倍率 由游戏自身结算管线
  `MaJiang.PlayMaJiang.RoundStatistics.PlayerRoundStatistics.GetTotalScore(...)`
  求出（返回 底分, 番数, 倍率, 总分 四元组），三者相乘即预计分，保证与游戏结算一致。
- 番数有多个可能值（不同胡牌拆解/不同出铳牌）时**取其最小值**；
  倍率即游戏规则 `(1+Σ基础倍率) × Π(1+独立倍率)`（游戏内直接可见的"倍率总和"）。

基于 **BepInEx 6 (IL2CPP)** + IMGUI 悬浮 HUD；不修改游戏本体、不注入逻辑、不参与计算。

## 现状（2026-08-30）

- ✅ 听牌入口已打通：Harmony 钩 `PlayerPipeline.OnProcessTingResult(...)`，听牌时能拿到
  `IReadOnlyDictionary<PaiMianPayload, IReadOnlyList<HuResult>>`（真实听牌时字典有数据，
  开牌/未听时为空字典）。已确认 `FanZhongs` 运行时是 `HashSet<FanZhong>`，
  成功走通每条可胡牌的 HashSet 取值提取。
- ⏳ 当前卡点：把每张可胡牌的番型集合重新灌进 `GetTotalScore(IEnumerable<IEnumerable<FanZhong>>, ...)`
  时，多层泛型容器须每层 T 与参数接口完全一致（用 `List<IEnumerable<FanZhong>>` 作外层 + 
  全部经原生 `Cast<T>()`），原生合成该容器是否成功待最后验证。
- ❌ 计分预览面板镜像方案搁置：候选组件
  `MaJiang.PlayMaJiang.RoundStatistics.PlayerHuPanel`（公开 `_baseScoreText/_fanText/
  _independentText/_totalScoreText`）在场景中 FindObjectsOfType 找不到，留待后续定位真实面板。

## 安装现状（已完成）

游戏实际安装目录 `E:\Program Files (x86)\Steam\steamapps\common\DemonicMahjong` 已装入 BepInEx 6：

```
DemonicMahjong/
  winhttp.dll / doorstop_config.ini / dotnet\      BepInEx 前置（Doorstop 启动钩子）
  BepInEx\
    core\      BepInEx + Il2CppInterop 运行时
    plugins\ScorePreview.dll         ← 本 Mod（已装）
    interop\                         ← 已生成（MaJiang.dll 等，首次启动时自动生成）
```

仓库内的 `DemonicMahjong/` 是同一版本的开发拷贝（GameAssembly.dll 与 global-metadata.dat 哈希一致），
仅供开发/测试 interop 用，不在其中玩游戏。

## 目录结构

```
mod/ScorePreview/
  ScorePreview.csproj    GameDir 默认指向 Steam 实际安装目录，可 -p:GameDir=... 覆盖
  Plugin.cs              入口：AddComponent<ScoreHud>()；Harmony 建树 PatchAll
  ScoreHud.cs            IMGUI HUD：读预测快照 → 左上角 "Est: 底分 x 番数 x 倍率 = 预计"
  Prediction.cs          听牌钩子 TingHookPatch + Comp.Try 计算（HashSet 提取 + GetTotalScore）
  build.bat              编译（可选参数：游戏目录；纯 ASCII，兼容任何代码页）
  install.bat            拷贝 dll 到游戏 BepInEx\plugins\（可选参数：游戏目录；纯 ASCII）
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

- 启动游戏后看 `BepInEx\LogOutput.log`：应有 `Loading [ScorePreview 0.1.0]`、`ScoreHud active`、
  `D: calls=N hits=M`（calls=钩子触发次数，hits=成功算出预测次数）。
- 听牌时出现：`ting hook #N -> Est: ...`（成功）或 `ting hook -> none: 原因`（诊断）。
- 未听牌 / 主界面时显示 `Est: --`。

## 技术说明 / 坑（重要，改代码必读）

- **游戏主体代码在 `MaJiang.dll`，不是 `Assembly-CSharp.dll`**（后者主要是编辑器/演示脚本）。
- **游戏 exe 名带空格**：`Demonic Mahjong.exe`；taskkill/start 用带空格的完整原名。
- **不要显式调 `Il2CppInterop.HarmonySupport` 的 `AddHarmonySupport`**：BepInEx6 已自动注册
  HarmonySupportComponent，再调会抛 `ArgumentException: An item with the same key has already
  been added`。直接用 `new Harmony(GUID); harmony.PatchAll(asm);`。
- **泛型接口门面无成员**：新版 Il2CppInterop 的 `IReadOnlyDictionary`/`IReadOnlyList`/
  `IEnumerable` 接口门面不暴露任何成员、不能 foreach/`.Count`。数据从**具体类**取：
  `Dictionary<K,V>`(`_count`+`_entries[i].key/value`)、`List<T>`(`Count`/`Item`)、
  `HashSet<T>`(`_buckets`/`_slots[i].value/next`+`_count`)。
- **interop 对象间类型转换必须用原生 `Cast<T>()`**（`Il2CppInterop.Runtime`），托管
  强转/`as`/`(object)` 对 interop 门面一律失败——即使 T 完全相同（如
  `List<FanZhong> → IEnumerable<FanZhong>` 都会抛 InvalidCastException）。
- `HuResult.FanZhongs` 运行时是 **`HashSet<FanZhong>`**（不是 List）。
- **多层泛型参数需层内 T 与接口完全一致**：`GetTotalScore(IEnumerable<IEnumerable<FanZhong>>,…)`
  的外层容器必须是 `List<IEnumerable<FanZhong>>`（元素本征类型=接口），每层过 `.Cast<T>()`；
  `List<List<FanZhong>>` 会因外层 T（`List<FanZhong>`）≠ 参数 T（`IEnumerable<FanZhong>`）失败。
- 听牌结果源是事件入口 `PlayerPipeline.OnProcessTingResult`（场景对象 + 公有方法，可被
  Harmony PatchAll 找到）；`PlayerHandPaiMianContainer.CanHuPaiMianPayloads` 对局内**恒为空**。
- `Il2CppSystem.Decimal` 用公开字段 `lo/mid/hi/flags` 按 CLR decimal 位布局重建 `System.Decimal`
  （`lo/mid/hi` 无符号，`flags` 高位=scale/符号位）。
- `PlayerRoundStatistics` 直接继承第三方基类 `SaintsMonoBehaviour`，需引用 `SaintsField.Runtime.dll`；
  引用 `TMP_Text` 需 `Unity.TextMeshPro.dll` + `UnityEngine.UI.dll`（MaskableGraphic 基类）。
- IMGUI 默认字体仅保证 ASCII，中文会渲染成方块，故显示 `Est: a x b x c = d`。

## 卸载

删除游戏 `BepInEx\plugins\ScorePreview.dll`；不再需要 BepInEx 时删除 `BepInEx\`、`winhttp.dll`、
`doorstop_config.ini`、`dotnet\` 即可，游戏本体不受影响。

## 游戏更新后

BepInEx 每次启动会自动重新生成 `interop/`。若签名变化导致编译失败，重跑 `build.bat` + `install.bat`。