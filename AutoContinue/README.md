# AutoContinue — 自动跳过等待环节

自动跳过游戏中的「等玩家点一下」环节，并可跳过局外灵俑的演出动画，减少重复操作。

## 功能特性

- **公告界面**：启动后自动点击【继续】进入大厅。
- **Boss 战加载**：加载完成后自动点击【点击继续】进入对局。
- **结算界面**：对局结算后自动点击【继续】（默认关闭）。
- **局外灵俑动画**：灵俑效果触发时的大厅演出（消耗金币 → 毕方，销毁灵俑 → 鹿蜀）可跳过 / 隐藏。
- **可配延迟**：每个环节独立配置延迟秒数，避免过早点击。

## 使用说明

安装后首次启动，dll 同目录自动生成 `AutoContinue.yml` 配置文件。

| 场景 | 默认行为 | 配置项 |
|------|----------|--------|
| 启动公告 | 自动点【继续】，延迟 2 秒 | `announce_enabled` / `announce_delay` |
| Boss 战加载 | 自动点【点击继续】，延迟 1 秒 | `battle_enabled` / `battle_delay` |
| 对局结算 | 默认关闭，延迟 5 秒 | `result_enabled` / `result_delay` |
| 局外灵俑动画 | 跳过（瞬间放完模型动画） | `lingyong_anim_enabled` / `lingyong_anim_mode` / `lingyong_anim_scope` |

改配置后重启游戏生效。

## 局外灵俑动画

灵俑（游戏内部叫「小丑牌 / XiaoChouPai」）在局外触发效果时，大厅会播一段演出：

- **毕方**（id 111）：金币每减少 2，倍率 +1% → 花钱时触发
- **鹿蜀**（id 166）：局外每销毁一个灵俑，独立倍率 +15% → 销毁灵俑时触发

本 mod 盯两处载体：

| 载体 | 处理方式 |
|------|----------|
| 灵俑模型上的 Animator 演出（`XiaoChouPaiDisplay.modelObj`） | 状态一换就 `speed × 60` 瞬间放完，或把模型藏起来 |
| 灵俑时间轴演出（`LingYongTimeLineController` 系列的 `PlayableDirector`） | 播放头推到片尾，或把画面藏起来 |

| 配置项 | 取值 | 说明 |
|--------|------|------|
| `lingyong_anim_enabled` | `true` / `false` | 总开关 |
| `lingyong_anim_mode` | `skip` / `hide` | `skip`=跳过（瞬间放完）；`hide`=隐藏画面（时序不变） |
| `lingyong_anim_scope` | `lobby` / `all` | `lobby`=只处理局外（大厅灵俑页那批）；`all`=对局内也处理 |
| `lingyong_anim_debug` | `true` / `false` | 打印灵俑数量 / 动画状态变化 / 时间轴播放 |

> 局外判定：大厅 `PlayerInfoCtr` 挂着的灵俑显示对象。灵俑页没打开时该面板不存在，
> 此时不动任何动画；想对局内也生效就把 `lingyong_anim_scope` 改成 `all`。

## 工作原理

### 自动点按钮

每 0.25 秒扫描场景中所有 `UnityEngine.UI.Button`，按子节点 TMP 文本匹配按钮：

| 按钮文本 | 匹配方式 | 所属场景 |
|----------|----------|----------|
| `点击继续` | 包含匹配 | Boss 战加载界面 |
| `继续` | 精确匹配（排除结算界面） | 公告界面 |
| `继续` | 精确匹配 + 结算路径检测 | 结算界面 |

同一按钮触发一次后 3 秒冷却，避免重复点击。按钮消失后重新 arm。

### 灵俑动画

纯托管侧轮询，**不挂 Harmony**（原因见 AGENT.md 坑 18）：

1. 每 0.2 秒 `FindObjectsOfType<XiaoChouPaiDisplay>`，取每个灵俑的 `modelObj` 与其 `Animator`；
2. 记录当前 `AnimatorStateInfo.shortNameHash` 作基线，状态一变说明开始播新动画；
3. `skip` 模式把 `Animator.speed` 拉高（动画瞬间放完，0.5 秒后还原）；
   `hide` 模式关掉模型下的 `Renderer`（0.6 秒后还原）；
4. 时间轴演出另外盯 `LingYongTimeLineController` / `LingYongTimeLineController2` /
   `LingYongTimeLineUIController` 的 `PlayableDirector`，在播就推到片尾（`skip`）或隐藏画面（`hide`）。

## 日志输出

```
[Info :AutoContinue] AutoContinue cfg: announce=True/d=2 battle=True/d=1 result=False/d=5 lingyong_anim=True/mode=skip/scope=lobby debug=False
[Info :AutoContinue] AutoContinue: clicked Battle btn=[xxx] text=[点击继续]
[Info :AutoContinue] LingYongAnimSkip: 灵俑 display=8 范围内=8 有模型=8 有Animator=8 大厅面板=有
[Info :AutoContinue] LingYongAnimSkip: 模型动画 [BiFang] state=1234 → 加速
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

# 局外灵俑演出动画
lingyong_anim_enabled: true
lingyong_anim_mode: skip      # skip=跳过 / hide=隐藏
lingyong_anim_scope: lobby    # lobby=只局外 / all=含对局内
lingyong_anim_debug: false
```

## 兼容性

- **游戏版本**：Unity 6000.3.21f1（IL2CPP）《Demonic Mahjong》。
- **依赖**：BepInEx 6.0.0-be.785 或更高版本。
- **冲突**：与 `ScorePreview`、`SLMenuTrigger` 等插件无已知冲突。

> ⚠️ 若某版本按钮不是 `UnityEngine.UI.Button`（自定义点击组件），日志会缺少 `AutoContinue: clicked ...` → 需改用 EventSystem 或直接调面板方法。

## 构建与开发

```
AutoContinue/
├── Plugin.cs           BepInEx 入口 + PluginInfo
├── AutoSkip.cs         核心逻辑（自动点按钮）
├── AutoConfig.cs       AutoContinue.yml 的统一读取（默认值 + 解析）
├── LingYongAnimSkip.cs 局外灵俑演出动画跳过/隐藏
├── AutoContinue.csproj 项目文件
├── build.bat           编译
└── install.bat         拷贝 dll 到游戏 BepInEx\plugins\
```

Linux（Proton）下编译用：

```bash
dotnet build -c Release -p:GameDir="$DEMONIC_MAHJONG_DIR"
```
