# RoleGrowth — 角色主动技能局内成长持久化

修复「角色主动技能的局内永久成长，重启游戏后丢失」的 bug。首个目标：水晶兰·贩酒。

## 现象

水晶兰主动技能「贩酒」每次卖祭品会永久提升倍率（+5%/7%/10%）。实测攒到 +0.8 后
正常退出游戏、重开、读档，成长归零、倍率回退到 1。

## 根因

- 「贩酒」的实现类 `FanJiuSkillDisplay` 把成长存在 `Saver.CharaterSkillData.Count`。
- 该字段所在的 `_charaterSkillData` **没有 `[JsonProperty]`**，游戏存盘时不写进存档。
- 对比：角色被动（如陆之·换面）走 `FakeXcp`，游戏会正常存档，所以被动不丢。

## 方案

mod 自己持久化成长值到 `BepInEx/plugins/RoleGrowthState.json`，格式：

```json
{"0":{"20036":1.8}}
```

外层 key = 存档ID，内层 key = 技能ID（20036 = 贩酒），value = 成长值（对应 `CharaterSkillData.Count`）。
只服务「继续游戏」（重启恢复）；开始新游戏会重置该存档ID的值。

**事件驱动，不轮询写盘**：

- 启动：`FanJiuSkillDisplay.Init` 时读 JSON，写进 `Saver.CharaterSkillData`（权威值）。
  游戏以该值为基数，用技能后自然累加（如 0.8 → 1.4），面板只显示一行。
- 存盘：`GameSaverUtil.SaveAsync` 时从 `Saver` 读回成长值，写回 JSON。
- 新游戏：`OutsideGameAction.OnNewGameStart` 时重置该存档ID的成长值。

## 为什么写 Saver 而不是注入 BuffList payload

早期版本曾构造 `FloatBuffPayload` 直接注入 `BuffList._independentBuff`，导致 mod 的 0.8
和游戏用技能生成的 0.5 变成两行叠加。改为写 `Saver.CharaterSkillData` 后，游戏用自己的
基数计算，一行自然累加，问题解决。

## 崩溃坑（重要）

以下 IL2CPP 补丁写法实测会让游戏崩溃，不要加：

- `FanJiuSkillDisplay.CompleteUse` / `ExecuteDirectly` 的 Postfix（参数含 struct）
- `GetDynamicDescription` 的 `ref string __result` Postfix
- `OnMaJiangRoundEnd` 的补丁（含 struct 参数）
- 直接枚举 `Saver._playFakeXiaoChouSaveData`（native Dictionary 的 `_entries` / 反射 get_Item）

当前 mod 只用三个稳定补丁：`FanJiuSkillDisplay.Init`、`GameSaverUtil.SaveAsync`、
`OutsideGameAction.OnNewGameStart`（都是无参或简单签名）。

## 配置 / 文件

- 状态文件：`BepInEx/plugins/RoleGrowthState.json`（mod 自动创建、自动读写，无需手改）
- 无 yml 配置项

## 验证口径

日志搜 `[RoleGrowth]`：

- `[RoleGrowth] v<版本> loaded`、`loaded N slots from ...`
- 进对局：`Init restored slot=0 id=20036 Count=0.8 (Saver)`
- 存盘：`SaveAsync wrote slot=0 id=20036 Count=1.8`
- 新游戏：`reset slot=0 (new game)`

游戏内：水晶兰倍率面板应显示一行「贩酒 lv.3 +<值>」，且重启后值保留。
