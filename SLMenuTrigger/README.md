# SLMenuTrigger — 自动暂停（SL 辅助）

牌堆耗尽时自动检测玩家与 Boss 分数，若玩家落后则暂停游戏，方便执行 SL（Save/Load）操作或制定策略。

## 功能特性

- **自动检测**：牌堆剩余 `0` 时触发分数对比。
- **智能暂停**：玩家分数 < Boss 分数 → `Time.timeScale = 0`（画面冻结）。
- **操作提示**：暂停时屏幕中央显示提示框，告知玩家按 `ESC` 打开菜单继续。
- **冷却机制**：恢复后进入 2 秒冷却，避免反复触发。

## 使用说明

1. 正常进行游戏对局。
2. 牌堆耗尽时，Mod 自动检测分数。
3. 若玩家分数 < Boss 分数：
   - 游戏暂停（画面冻结）。
   - 屏幕中央显示提示框：「当前分数落后，游戏已暂停。按 ESC 打开菜单...」
4. 按 `ESC` 打开游戏菜单，查看分数 / 规划策略 / 直接关闭菜单继续游戏。
5. 游戏恢复至之前保存的倍速，Mod 进入 2 秒冷却。

> **卖血策略**：若玩家故意输分触发遗物/灵俑，暂停会干扰操作。按 `ESC` 打开菜单再关闭即可，2 秒冷却内不会再次触发。

## 工作原理

| 检测条件 | 触发动作 |
|----------|----------|
| 牌堆剩余 = `0` | 读取玩家分数 / Boss 分数 |
| 玩家分数 < Boss 分数 | `Time.timeScale = 0`（暂停） |
| 玩家按 ESC 或点击菜单恢复 | `Time.timeScale = 1`，进入冷却 |
| Mod 被禁用 | `Time.timeScale = 1`，重置状态 |

- **UI 读取**：通过扫描 `TMP_Text` 组件读取分数和牌堆数量（纯 UI 解析，非内存读取）。
- **暂停实现**：仅使用 Unity 的 `Time.timeScale`，不创建自定义 UI，完全兼容游戏自身菜单。

## 日志输出

```
[Info :SLMenuTrigger] SLMenuTrigger v0.1.0 loaded.
[Info :SLMenuTrigger] 牌堆耗尽! Player 1254 < Boss 5113. Pausing.
[Info :SLMenuTrigger] Game resumed by other means. Cooldown 2s.
```

## 配置 — `BepInEx\plugins\SLMenuTrigger.yml`

```yaml
enabled: true    # 是否启用自动暂停（false = 关闭）
```

无文件 = 默认启用；改后重启游戏生效。

## 兼容性

- **游戏版本**：Unity 6000.3.21f1（IL2CPP）《Demonic Mahjong》。
- **依赖**：BepInEx 6.0.0-be.785 或更高版本。
- **冲突**：与 `ScorePreview`、`AutoContinue` 等插件无已知冲突。

> ⚠️ 游戏 UI 路径在后续更新中变化时，可能需要更新 `PlayerPath`、`AiPath`、`DeckPath`、`BossDeckPath` 常量。

## 构建与开发

```
SLMenuTrigger/
├── PluginInfo.cs          插件元数据
├── Plugin.cs              BepInEx 入口
├── SLMenuTrigger.cs       核心逻辑
└── SLMenuTrigger.csproj   项目文件
```

```bash
build.bat          # 编译
install.bat        # 拷贝 dll 到游戏 BepInEx\plugins\
```
