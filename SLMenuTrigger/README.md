# # ScorePreview — 我在地府打麻将 SLMenuTrigger Mod

在牌堆耗尽时自动检测玩家与 Boss 的分数，若玩家落后则暂停游戏，方便玩家进行 SL（Save/Load）操作或制定策略。

---

## 功能特性

- **自动检测**：当牌堆剩余数量为 `0` 时触发检测。
- **分数对比**：读取屏幕上的玩家分数和 Boss 分数。
- **智能暂停**：若玩家分数低于 Boss，自动暂停游戏（`Time.timeScale = 0`）。
- **操作提示**：暂停时屏幕中央显示提示框，告知玩家按 `ESC` 打开菜单继续游戏。
- **无缝恢复**：按 `ESC` 打开游戏菜单后关闭，游戏自动恢复，Mod 同步重置状态。
- **冷却机制**：恢复后进入 2 秒冷却，避免反复触发。

---

## 使用说明

### 正常流程
1. 正常进行游戏对局。
2. 牌堆耗尽时，Mod 自动检测分数。
3. 若玩家分数低于 Boss：
   - 游戏暂停（画面冻结）。
   - 屏幕中央显示提示框：「当前分数落后，游戏已暂停。按 ESC 打开菜单...」
4. 按 `ESC` 键打开游戏菜单。
5. 查看分数、规划策略或直接关闭菜单继续游戏。
6. 游戏恢复，Mod 进入 2 秒冷却，避免立即再次触发。

### 关于卖血策略
如果玩家故意输分（卖血）以触发某些遗物或灵俑，Mod 的自动暂停可能会干扰操作。此时只需按 `ESC` 打开菜单再关闭即可继续游戏，2 秒冷却内不会再次触发暂停。

---

## 工作原理

| 检测条件 | 触发动作 |
|----------|----------|
| 牌堆剩余 = `0` | 读取玩家分数 / Boss 分数 |
| 玩家分数 < Boss 分数 | `Time.timeScale = 0`（暂停） |
| 玩家按 `ESC` 或点击菜单恢复 | `Time.timeScale = 1`，同步状态，进入冷却 |

- **UI 读取**：通过扫描屏幕上的 `TMP_Text` 组件读取分数和牌堆数量（非内存读取，纯 UI 解析）。
- **暂停实现**：仅使用 Unity 的 `Time.timeScale`，不创建任何自定义 UI，完全兼容游戏自身菜单。

---

## 日志输出

Mod 在 BepInEx 日志中会输出关键事件：
```
[Info :SLMenuTrigger] SLMenuTrigger v0.1.0 loaded.
[Info :SLMenuTrigger] 牌堆耗尽! Player 1254 < Boss 5113. Pausing.
[Info :SLMenuTrigger] Game resumed by other means. Cooldown 2s.
```


## 兼容性

- **游戏版本**：基于 Unity 6000.3.21f1（IL2CPP）《Demonic Mahjong》开发。
- **依赖**：BepInEx 6.0.0-be.785 或更高版本。
- **冲突**：与 `ScorePreview`、`AutoContinue` 等插件无已知冲突。

> ⚠️ 如果游戏 UI 路径在后续更新中发生变化，可能需要更新 `_playerPath`、`_aiPath`、`_deckPath` 常量。

---

## 构建与开发

### 项目结构
```
SLMenuTrigger/
├── PluginInfo.cs               # 插件元数据
├── Plugin.cs                   # BepInEx 入口
├── SLMenuTrigger.csproj        # 项目文件
└── SLMenuTrigger.cs            # 核心逻辑
```

### 编译
使用 `build.bat` 脚本编译（需配置 `DEMONIC_MAHJONG_DIR` 环境变量或 `.env` 文件）：

```bash
build.bat
```
