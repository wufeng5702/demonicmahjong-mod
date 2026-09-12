# AutoContinue — 自动跳过等待环节

自动跳过游戏中的「等玩家点一下」环节，减少重复操作。

## 功能特性

- **公告界面**：启动后自动点击【继续】进入大厅。
- **Boss 战加载**：加载完成后自动点击【点击继续】进入对局。
- **结算界面**：对局结算后自动点击【继续】（默认关闭）。
- **可配延迟**：每个环节独立配置延迟秒数，避免过早点击。

## 使用说明

安装后首次启动，dll 同目录自动生成 `AutoContinue.yml` 配置文件。

| 场景 | 默认行为 | 配置项 |
|------|----------|--------|
| 启动公告 | 自动点【继续】，延迟 2 秒 | `announce_enabled` / `announce_delay` |
| Boss 战加载 | 自动点【点击继续】，延迟 1 秒 | `battle_enabled` / `battle_delay` |
| 对局结算 | 默认关闭，延迟 5 秒 | `result_enabled` / `result_delay` |

改配置后重启游戏生效。

## 工作原理

每 0.25 秒扫描场景中所有 `UnityEngine.UI.Button`，按子节点 TMP 文本匹配按钮：

| 按钮文本 | 匹配方式 | 所属场景 |
|----------|----------|----------|
| `点击继续` | 包含匹配 | Boss 战加载界面 |
| `继续` | 精确匹配（排除结算界面） | 公告界面 |
| `继续` | 精确匹配 + 结算路径检测 | 结算界面 |

同一按钮触发一次后 3 秒冷却，避免重复点击。按钮消失后重新 arm。

## 日志输出

```
[Info :AutoContinue] AutoContinue v0.1.0 loaded
[Info :AutoContinue] AutoContinue cfg: announce=True/d=2 battle=True/d=1 result=False/d=5
[Info :AutoContinue] AutoContinue: clicked Battle btn=[xxx] text=[点击继续]
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
```

## 兼容性

- **游戏版本**：Unity 6000.3.21f1（IL2CPP）《Demonic Mahjong》。
- **依赖**：BepInEx 6.0.0-be.785 或更高版本。
- **冲突**：与 `ScorePreview`、`SLMenuTrigger` 等插件无已知冲突。

> ⚠️ 若某版本按钮不是 `UnityEngine.UI.Button`（自定义点击组件），日志会缺少 `AutoContinue: clicked ...` → 需改用 EventSystem 或直接调面板方法。

## 构建与开发

```
AutoContinue/
├── PluginInfo.cs       插件元数据
├── Plugin.cs           BepInEx 入口
├── AutoSkip.cs         核心逻辑
└── AutoContinue.csproj 项目文件
```

```bash
build.bat          # 编译
install.bat        # 拷贝 dll 到游戏 BepInEx\plugins\
```
