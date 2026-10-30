# LingPreview — 幻灵台牌灵提示

进入**幻灵台**（附牌灵）时，用 IMGUI 悬浮面板把「这个牌灵会把牌变成什么」直接写出来：

```
┌ 牌灵 ────────────────────────────────┐
│  象灵   确认 1/1                        │
│  数字 +2 循环（数牌/风牌/三元牌；数牌 9→2）│
│  数牌　　1→3 | 2→4 | 3→5 | 4→6 | 5→7 …   │
│                                        │
│  风牌　　东风→西风 | 南风→北风 | …        │
│                                        │
│  三元牌　红中→发财 | 白板→红中 | …        │
│ ▶ 北风  →  南风                          │
└────────────────────────────────────────┘
```

- **只在幻灵台界面开着时出现**，其它界面（含对局中）完全不显示。
- 覆盖范围：**幻灵台这个 GameEvent 节点**（地图上「幻灵台」事件给的一次附灵机会），
  入口与魏征/玉兔被动等给的机会都是同一个节点 → 一律能提示。
  ⚠️ **不覆盖**背包里的「牌面工作台」（`PaiMianWorktable` 的 Add 模式）——那是另一套界面。
- 只读游戏状态，不修改游戏本体、不注入计算逻辑。

## 支持的牌灵与规则

| 牌灵 | 规则 | 作用范围 |
|------|------|----------|
| **象灵** | 数字 **+2**（1 起算循环：8万→1万、9万→2万） | 数牌 / 风牌 / 三元牌 |
| **鼠灵** | 数字 **-2**（1 起算循环：1→8、2→9） | 数牌 / 风牌 / 三元牌 |
| **马灵** | 相邻两数互换：1↔2 3↔4 5↔6 7↔8，**9→8** | 只对数牌，风牌/三元牌无效 |

- 象/鼠的偏移量取自游戏自己的 `BaoLingPayload.numberDiff`（方向按灵定），游戏改数值会自动跟随；
  马灵游戏侧没有对应字段，规则写死在 `Shared/LingRules.cs`。
- 其它牌灵（万/筒/索/风/山/顺/狐/鬼/龙灵等）暂不逐张展开，面板只显示**游戏自己的说明文案**。

## 配置 — `BepInEx\plugins\LingPreview.yml`

月圆奇谭版本需 BepInEx `be.791` 和本仓库的 `Compatibility` 启动补丁。
关闭游戏后运行根目录的 `install_mods.bat` 可自动安装、更新，勿将 DLL 放进 `Custom/Mods`。

```yaml
enabled: true    # 是否显示提示面板
fontsize: 18     # 面板字体大小
linespacing: 10  # 行高 = max(字号 + 行距, 字号×1.25)（行距越大越松）
xoffset: 0.5     # 水平锚点 = 屏幕宽度 × 该比例（0.5 = 顶部居中）
yoffset: 0.0     # 垂直锚点 = 屏幕高度 × 该比例（0 = 贴顶）
detail: full     # full = 完整映射表（默认）；brief = 只一行摘要 + 当前牌
debug: false     # true = 输出诊断日志（含 [probe] 幻灵台状态/灵配置/牌面枚举）
```

无文件 = 上面默认值（首次运行自动生成）；改后重启游戏生效。
换分辨率/窗口化不用重调——锚点是比例。

### 两种详细度

`detail: full`（默认）——完整映射表：

```
鼠灵  确认 1/1
数字 -2 循环（数牌/风牌/三元牌；数牌 9→7）
牌面数字-2。（如果是字牌，牌面循环变化）
数牌　　1→8 | 2→9 | 3→1 | 4→2 | 5→3 | 6→4 | 7→5 | 8→6 | 9→7

风牌　　东风→西风 | 南风→北风 | 西风→东风 | 北风→南风

三元牌　红中→白板 | 白板→发财 | 发财→红中
▶ 白板  →  发财
```

`detail: brief` —— 只留一行摘要 + 当前牌：

```
鼠灵  确认 1/1
数字 -2 循环（数牌/风牌/三元牌；数牌 9→7）
牌面数字-2。（如果是字牌，牌面循环变化）
▶ 白板  →  发财
```

### 版式

- 正文**一律白色**，组与组之间用**空行**分隔（靠间距分组，不靠颜色）。
- 映射项之间用**竖线**分隔：`1→8 | 2→9 | 3→1`——单空格/双空格都容易把相邻两项连读。
- **三组的映射项起始 x 对齐**：组名按汉字宽度用全角空格（U+3000，与汉字同宽）补齐，
  所以「数牌 / 风牌 / 三元牌」三行的第一个映射项从同一列开始。
- 只有两种强调色：标题/当前选中牌=黄加粗，游戏自带说明=灰。
- 行距可配：`linespacing`（行高 = max(字号 + 行距, 字号×1.25)，下限保证不裁字）。

## 工作原理

| 数据 | 来源 | 用途 |
|------|------|------|
| 幻灵台是否开着 | 控制器 `BaoLingTai`：`gameObject.activeInHierarchy` + `IsRunning`；界面 `PaiMianAdditionUI`：`gameObject.activeInHierarchy` + `!IsHide` + `!Finished` | 面板显示与否（界面一关就撤掉，不会残留） |
| 当前选中的灵 | 两个来源：`PaiMianAdditionUI.CurSelected` 与 `BaoLingTai.CurSelectEnhance.Enhance`（`IItemInfo`）→ 原生 `Cast<BaoLingPayload>()` | 灵 id、名字、`numberDiff`、说明文案 |
| 灵是否有效 | 上面两个来源**都**要和 `PaiMianAdditionUI.CurButtons`（本轮展示的灵按钮）核对 | 两者一致 → 就是当前选择（被动直送、不在按钮里的灵也走这里）；不一致 → 由按钮列表裁定；都不在 → 认为是上一轮残留，当没选灵（面板不出现） |
| 当前选中的牌 | `BaoLingTai.CurSelectPaiMian.Item`（`PaiMianPayload`） | 「▶ 北风 → 南风」 |
| 牌面名 | `GlobalStaticDataManager.PaiMianTotalList`（游戏全部 34 张牌）的 `displayName`，并用 `PaiMianAdditionUI.paiMianRoot.Payloads` 补齐 | 数牌用游戏自己的汉字名；结果牌不在幻灵台列表里也能显示 |

变换本身由 `Shared/LingRules.cs` 的纯函数算出（有单元测试），不调用游戏的变换函数、不改游戏数据。

## 日志输出

```
[Info :LingPreview] [LingPreview] v0.3.2 loaded
[Info :LingPreview] LingPanel active xoffset=0.5 yoffset=0 fontsize=18 linespacing=10 detail=full
[Info :LingPreview] ling -> 象灵   确认 1/1 | 数字 +2 循环（数牌/风牌/三元牌；数牌 9→2） | … | ▶ 北风  →  南风
```

`debug: true` 时每个灵探一次（状态一变就打一行）：

```
[Info :LingPreview] [probe] state running=True ctrlActive=True ui=active=True hide=False finished=False enhance=…(id=XiangLing,nd=2,bao=False,name=象灵) tile=北风
[Info :LingPreview] [probe] ling=XiangLing name=象灵 numberDiff=2 menLei=Wan rule=Shift diff=2 desc=[牌面数字+2。…]
[Info :LingPreview] [probe] tiles: {hua=1,num=8,feng=0,jian=0}=八万  {hua=0,num=0,feng=4,jian=0}=北风  …
```

- `state` 行 = 幻灵台当前状态：四个判据旗标（`running` 节点在跑 / `ctrlActive` 控制器在场景里 /
  `ui=active` 界面可见 / `hide`、`finished` 未被隐藏或结束）+ 界面认的灵（`curSelected`）
  + 本轮按钮列表（`buttons`）+ 控制器认的灵 + 选中的牌。
  **面板不出现时先看这行**——它用 `FindController()` 查控制器，所以界面没开时也会打，
  四个旗标能直接指出是哪一条不满足。
- `ling=` 行 = 该灵在游戏里的配置（偏移量 `numberDiff`、门类 `menLei`、游戏自带说明）。
- `tiles:` 行 = 幻灵台里展示的牌面（枚举值 + 游戏自己的显示名），用来核对命名与顺序。

> 注意：游戏里的 `PaiMianWorktable.GetData(payload, ling)` **并不做牌面变换**（实测原样返回该牌），
> 所以没有拿它做交叉校验。本 mod 的规则来源是**玩家实机验证**，并由 `Shared/LingRules.cs`
> 的单元测试固化。

## 已知问题

- **牌名一律用游戏自己的显示名**（红中/白板/发财、东风/南风/西风/北风、四万/六万…）。
  字牌另有一份按枚举的固定名（`Jian`: 中=1/白=2/发=3；`Feng`: 东南西北=1-4）只作兜底——
  正常都用游戏文案，取不到时才用兜底名。
- **数牌名用游戏自己的显示名**（四万/六万/三筒…，汉字），名字表从游戏全部 34 张牌取
  （`GlobalStaticDataManager.PaiMianTotalList`），幻灵台列表里没有的结果牌也能显示。
- **数字与牌名的分工**：牌名一律汉字（和画面一致），只有映射表里的**序号**用阿拉伯数字
  （`数牌 1→8 2→9 …`）——那是「同花色内的第几号」，列全名要 27 行反而不好扫。
- **马灵规则是写死的**：游戏改数值时不会自动跟随（payload 里没有对应字段）。
- **随机类牌灵**（顺灵/狐灵等）不逐张展开：结果依赖牌库/手牌，预览没有意义，只显示游戏说明。
- 面板在幻灵台打开后约 0.15s 内出现（轮询间隔），不是零延迟。

## 构建与开发

```
LingPreview/
├── Plugin.cs        BepInEx 入口 + PluginInfo：AddComponent<LingPanel>()
├── LingPanel.cs     IMGUI 面板：配置、轮询幻灵台状态、绘制
├── LingTai.cs 与游戏的对接层：读状态、算映射、拼文案、debug 探针
├── LingPreview.csproj
├── build.bat / install.bat
```

规则纯函数在 `../Shared/LingRules.cs`，单测在 `../Shared.Tests/LingRulesTests.cs`。

## 卸载

删除 `BepInEx\plugins\LingPreview.dll`（和 `LingPreview.yml`）；不再需要 BepInEx 时删除 `BepInEx\`、
`winhttp.dll`、`doorstop_config.ini`、`dotnet\` 即可，游戏本体不受影响。
