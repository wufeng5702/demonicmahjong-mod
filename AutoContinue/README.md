# AutoContinue — 自动跳过等待环节

自动跳过游戏中的「等玩家点一下」环节，减少重复操作。

## 功能特性

- **公告界面**：启动后自动点击【继续】进入大厅。
- **Boss 战加载**：加载完成后自动点击【点击继续】进入对局。
- **结算界面**：对局结算后自动点击【继续】（默认关闭）。
- **随机模式角色轮播**：开始随机模式时把角色轮播总时长压到接近 0，直接落在随机角色上（默认关闭）。
- **预战面板快捷键**：↑/↓ 一键跳到随机角色卡；空格等同点击【开始游戏】（限定预战面板）。键位可配。
- **可配延迟**：每个环节独立配置延迟秒数，避免过早点击。

## 使用说明

安装后首次启动，dll 同目录自动生成 `AutoContinue.yml` 配置文件。

| 场景 | 默认行为 | 配置项 |
|------|----------|--------|
| 启动公告 | 自动点【继续】，延迟 2 秒 | `announce_enabled` / `announce_delay` |
| Boss 战加载 | 自动点【点击继续】，延迟 1 秒 | `battle_enabled` / `battle_delay` |
| 对局结算 | 默认关闭，延迟 5 秒 | `result_enabled` / `result_delay` |
| 随机模式角色轮播 | 默认关闭，轮播时长压到 0.05 秒 | `random_scroll_enabled` / `random_scroll_time` |
| 预战面板直跳随机角色 | 默认开启，↑/↓ 键 | `random_jump_enabled` / `random_jump_key` |
| 预战面板开始游戏 | 默认开启，空格键 | `start_game_enabled` / `start_game_key` |

改配置后重启游戏生效。

## 工作原理

每 0.25 秒扫描场景中所有 `UnityEngine.UI.Button`，按子节点 TMP 文本匹配按钮：

| 按钮文本 | 匹配方式 | 所属场景 |
|----------|----------|----------|
| `点击继续` | 包含匹配 | Boss 战加载界面 |
| `继续` | 精确匹配（排除结算界面） | 公告界面 |
| `继续` | 精确匹配 + 结算路径检测 | 结算界面 |

同一按钮触发一次后 3 秒冷却，避免重复点击。按钮消失后重新 arm。

随机模式角色轮播使用同一轮询节奏：每 0.25 秒查找场景中的 `CharacterSelectorCtrl`（预战面板的角色选择器），
把它的 `randomScrollTime`（轮播总时长）压到 `random_scroll_time`（默认 0.05 秒）。游戏仍负责
随机选角并触发后续流程，只是动画近乎瞬时完成。配置值下限为 0.01 秒（避免除零）。

预战面板快捷键每帧轮询（`GetKeyDown` 不能走 0.25 秒节奏）：

- **直跳随机角色**：查找 `CharacterSelectorCtrl.cardInstances` 中 `TryCast<CharacterRandomCard>()`
  成功的卡（兜底按游戏对象名含 `Random`/`随机` 判定），调用 `selector.OnCardClick(index)` 选中，
  与正常点击卡牌走同一路径。日志：`AutoContinue: prewar jump -> random card index=N/M`。
- **开始游戏**：查找 transform 路径含 `PreWar` 且按钮文本（归一化后）含「开始」的 `Button`，
  触发 `onClick.Invoke()`——限定 `PreWar` 路径避免误触其他面板里的「开始」。日志：`AutoContinue: clicked Start btn=...`。
- 键位是**逗号分隔的 KeyCode 名**，大小写不敏感，如 `UpArrow,DownArrow`、`Space`、`Mouse3`（第一个侧键，后退）
  、`Mouse4`（第二个侧键，前进）、`Alpha1`、`KeypadEnter`。无效名会在日志里告警并跳过。
- 若游戏关闭了 legacy Input 后端，热键会一次性禁用并告警（`prewar hotkeys disabled`），不影响其他功能。

## 日志输出

```
[Info :AutoContinue] [AutoContinue] v0.6.0 loaded
[Info :AutoContinue] AutoContinue cfg: announce=True/d=2 battle=True/d=1 result=False/d=5 random_scroll=False/t=0.05 random_jump=True/UpArrow+DownArrow start_game=True/Space
[Info :AutoContinue] AutoContinue: clicked Battle btn=[xxx] text=[点击继续]
[Info :AutoContinue] AutoContinue: prewar jump -> random card index=9/9
```

## 配置 — `BepInEx\plugins\AutoContinue.yml`

```yaml
# 公告界面
announce_enabled: true
announce_delay: 2.0

# Boss 战加载
battle_enabled: true
battle_delay: 1.0

# 对局结算（默认关闭）
result_enabled: false
result_delay: 5.0

# 随机模式角色轮播（默认关闭；开启后压到接近 0 即直接落到随机角色）
random_scroll_enabled: false
random_scroll_time: 0.05

# 预战面板快捷键（逗号分隔的 KeyCode；Mouse3=第一个侧键 Mouse4=第二个侧键）
random_jump_enabled: true
random_jump_key: UpArrow,DownArrow
start_game_enabled: true
start_game_key: Space
```

## 兼容性

- **游戏版本**：Unity 6000.3.21f1（IL2CPP）《Demonic Mahjong》。
- **依赖**：BepInEx 6.0.0-be.791；月圆奇谭版本还需本仓库的 `Compatibility` 启动补丁（安装器自动处理）。
- **冲突**：与 `ScorePreview`、`SLMenuTrigger` 等插件无已知冲突。

> ⚠️ 若某版本按钮不是 `UnityEngine.UI.Button`（自定义点击组件），日志会缺少 `AutoContinue: clicked ...` → 需改用 EventSystem 或直接调面板方法。

## 构建与开发

```
AutoContinue/
├── Plugin.cs           BepInEx 入口 + PluginInfo
├── AutoSkip.cs         核心逻辑
├── AutoContinue.csproj 项目文件
├── build.bat           编译
└── install.bat         拷贝 dll 到游戏 BepInEx\plugins\
```
