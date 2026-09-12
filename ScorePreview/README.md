# ScorePreview — 分数预览

对局中在屏幕左上角用 IMGUI 悬浮显示**预计得分**：

```
计分: 底分 x 番数 x 倍率 = 预计分     （计分按钮可用时显示其预览；计分面板打开后镜像真实结算）
和牌1/2/3: 底分 x 番数 x 倍率 = 预计分 （听牌时显示，按和牌得分从小到大取前三）
```

## 功能特性

- **番数是权威的、不推算**：计分番数直接读游戏 UI，和牌番数优先使用听牌钩子复用游戏结算计算出的结果，并以听牌面板 `FanNum` 作为兜底。
- **底分精确**：优先从 `PlayerRoundStatistics` Buff 系统读取，UI 文本（`K/M` 缩写）只作后备。
- **倍率实时**：优先读取 `IndependentText`（玩家独立倍率），避免 `GetTotalScore` 在当前运行时的错误解码。
- **结算镜像**：计分面板打开后，镜像真实结算数据（`底分 x 番数 x 倍率 = 总分`）。
- **HUD 可下移**：配置 `yoffset`（屏幕高度比例），避开左上角生命/魂力 UI。

## 配置 — `BepInEx\plugins\ScorePreview.yml`

```yaml
yoffset: 0.1    # HUD 距顶部的下移量 = 屏幕高度 × 该比例（1.0=满屏高），换分辨率不变形
```

无文件 = yoffset 0.1；改后重启游戏生效。

## 工作原理

| 数据来源 | 读取方式 | 用途 |
|----------|----------|------|
| `FanNum`（听牌面板） | UI TMP 文本扫描 | 和牌番数兜底 |
| `TingHookPatch`（听牌钩子） | Harmony patch `PlayerPipeline.OnProcessTingResult` | 和牌底分/番数/倍率精确值 |
| `JiFen/FenXing/ExpandedButton/Total` | UI TMP 文本扫描 | 计分番数 |
| `PlayerRoundStatistics` | Buff 系统读取 | 底分精确值 |
| `IndependentText` | UI TMP 文本扫描 | 实时倍率 |
| 结算面板 | `LastSettleFactors` | 面板打开后镜像结算 |

- 原则：与游戏结算公式 `总 = 底分 × 番数 × 倍率` 一致；不修改游戏本体、不注入逻辑。

## 日志输出

```
[Info :ScorePreview] Loading [ScorePreview 0.1.0]
[Info :ScorePreview] ScoreHud active ... yoffset=0.1
[Info :ScorePreview] Diag: uiFanMin=N from [16,15,16]       # 和牌番数取自听牌面板
[Info :ScorePreview] Diag: jfFan=N from [6 番]              # 计分番数取自计分按钮
[Info :ScorePreview] hud -> 计分: ... | 和牌1: ... | 和牌2: ... | 和牌3: ...
```

## 兼容性

- **游戏版本**：Unity 6000.3.21f1（IL2CPP）《Demonic Mahjong》。
- **依赖**：BepInEx 6.0.0-be.785 或更高版本。
- **冲突**：与 `SLMenuTrigger`、`AutoContinue` 等插件无已知冲突。

## 已知问题

- 计分行精度：底分从游戏 UI 文本读取（如 `500M`），解析时可能丢失被折叠的精度。
- 结算数字是动画的（`TweenMultiplyNumbersNumber`）：文本会出现 0 / 中间值，计分行只在数字稳定后采信。

## 技术说明（改代码必读）

- **游戏主体代码在 `MaJiang.dll`，不是 `Assembly-CSharp.dll`**。
- **游戏 exe 名带空格**：`Demonic Mahjong.exe`。
- **不要显式调 `Il2CppInterop.HarmonySupport` 的 `AddHarmonySupport`**：BepInEx6 已自动注册，再调抛重复键异常。直接用 `new Harmony(GUID); harmony.PatchAll(asm);`。
- **泛型接口门面无成员**（`IReadOnlyDictionary`/`IReadOnlyList`/`IEnumerable` 不能 foreach/`.Count`）。数据从具体类取。
- **interop 对象间类型转换必须用原生 `Cast<T>()`**（`Il2CppInterop.Runtime`）；托管强转 / `as` / `(object)` 对 interop 门面一律失败。
- `HuResult.FanZhongs` 运行时是 **`HashSet<FanZhong>`**；结算小番 = `payload.number` 之和（样本：num 5+5+5=15 == FanNum 15）。
- **HashSet.Slot 原生布局不稳定**：interop `Slot.value` 有时读到脏值；`FillFromSet` 用参数扫描。
- `Il2CppSystem.Decimal` 用 `lo/mid/hi/flags` 位布局重建 `System.Decimal`。
- IMGUI 默认字体仅 ASCII，中文会渲染成方块 → HUD 只显示短标签（数值均为 ASCII）。

## 构建与开发

```
ScorePreview/
├── Plugin.cs           BepInEx 入口：AddComponent<ScoreHud>()；Harmony 建树 PatchAll
├── ScoreHud.cs         IMGUI 四行 HUD：读游戏 UI + 结算镜像；yoffset 配置
├── Prediction.cs       听牌钩子 TingHookPatch（提供精确倍率 + 兜底小番求和）
├── ScorePreview.csproj 项目文件
├── build.bat           编译
└── install.bat         拷贝 dll 到游戏 BepInEx\plugins\
```

```bash
build.bat          # 编译
install.bat        # 拷贝 dll 到游戏 BepInEx\plugins\
```

## 卸载

删除 `BepInEx\plugins\ScorePreview.dll`（和 `ScorePreview.yml`）；不再需要 BepInEx 时删除 `BepInEx\`、`winhttp.dll`、`doorstop_config.ini`、`dotnet\` 即可，游戏本体不受影响。

## 游戏更新后

BepInEx 每次启动会自动重新生成 `interop/`。若签名变化导致编译失败，重跑 `build.bat` + `install.bat`；若 UI 结构变了导致读不到 FanNum/Total，看日志 `uiFanMin`/`jfFan` 是否消失即可定位。
