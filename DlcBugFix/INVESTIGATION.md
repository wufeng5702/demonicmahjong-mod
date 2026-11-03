# DLC 交互调查与验证记录

日期：2026-11-03。承接根目录的 Windows 交接文档。代码基线已更新到远程 `397c871`（包含 DLC 兼容提交 `51c59a9`），版本同步为 `0.5.0`。

## 本机基线

- Steam build：`26325171`；Unity：`6000.3.21f1`；元数据版本：39。
- `GameAssembly.dll` SHA256：`009F499F6D0D98103E3FBCCD1C657AD7416FA1234093C104A3F9AE19A53847E0`。
- `global-metadata.dat` SHA256：`5B08C1DC4F833A5F53ECC186B37ECBF45D7597E942122014622D5B3810D5ACBD`。
- 原 interop 为 DLC 之前生成，缺少 `GeShengDisplay`、`XunDaoZhiJianDisplay`、麒麟的成长接口。Windows 重新生成后 `MaJiang.dll` 为 15,951,872 字节，可供构建。
- 启动时另发现框架元数据钩子的指针截断崩溃。按用户要求，统一使用远程仓库的 [Compatibility](../Compatibility/README.md)，在预加载阶段停用不兼容的元数据索引钩子。

原生调查使用 Il2CppDumper v39 分支的 `dump.cs` / `script.json` 对照实际 PE 指令，**不是反编译 interop 存根推断调用链**。
Dumper 下载包 SHA256：`AAC8B49B7C8C972852066E196DB8F1FB4C02DFCFC1B3B46DC979C7A2BF83A808`。

## 麒麟：已获得失败堆栈

| 原生位置（RVA） | 已核对的行为 |
| --- | --- |
| `GeShengDisplay.CompleteUse` `0x212C190` | 从成长候选及对应 `geShengAdd` 数据随机选择，调用 `IXiaoChouLevelUp.OnXiaoChouLevelUp` |
| `QiLinDisplay.OnXiaoChouLevelUp` `0x21D9CF0` | 开头对 `_data` 调用 `Clear()`，随后处理 Buff 并启动成长协程 |
| `QiLinDisplay.CheckKeAndShunAndDui` `0x21D88B0` | 直接读取 `_data[Ke]`，缺键即抛异常 |
| `QiLinDisplay.CheckForBuff` `0x21D7A40` | 自然出牌检查；自然成长成功后在 `0x21D854B` 重建 `{Shun:false, Ke:false, Dui:false}` |
| `QiLinDisplay.OnInitFinished` `0x21D9510` | 初始化同样建立三个键 |
| `QiLinDisplay.<ActiveTween>d__33.MoveNext` `0x21F31B0` | 播放成长效果、置 `_level=-1`、等待 0.5 秒后关闭进度特效 |

在独立运行时组件上调用原版歌声，再调用原版麒麟出牌路径，捕获：

```text
System.Collections.Generic.KeyNotFoundException: The given key 'Ke' was not present in the dictionary.
QiLinDisplay.CheckKeAndShunAndDui(...)  QiLinDisplay.cs:148
QiLinDisplay.CheckForBuff(...)        QiLinDisplay.cs:261
```

Harmony 的 native→managed trampoline 会记录并捕获这类异常，因此外层 `OnPlaying` 调用不一定向托管调用者抛出。
验证需同时检查异常日志和状态，不能仅以“调用返回了”为成功。

实现：成长 Postfix 补齐三个缺失键；`CheckForBuff` Prefix 兼容已损坏的进度。
仅补缺键，不重新执行成长、增加倍率或重新播放动画。

## 殉道之剑：已确认负命写入边界

`XunDaoZhiJianDisplay.OnMaJiangRoundEnd` 实际为空；扣命位于 **`PlayerPipeline.OnMaJiangRoundEnd`**：

1. 仅 `RoundEndState.AIWin`（值 3）分支扣普通 1 命：`0x2089696` → `PlayerHp.ChangedHp`。
2. 统计 `displayId == XunDaoZhiJian`（192）数量，再扣对应点数：`0x208977D` → `PlayerHp.ChangedHp`。
3. `0x20897B3` 将 `PlayerHp.Hp` 写入 `Saver.Hp`。
4. `SimpleDataIntHasLimitValue.FormSaver` 直接读入存档值，未在此将负命归零。
5. `GameMapMgr.DoBattleOverFlow` 和 `OnHpChange` 实际判断 **Hp ≤ 0**，并非此前猜测的 `== 0`。
6. 平安扣 `DoReviveFunction` 对命执行 **+1**，然后将上限设为 1；凤凰蛋对命／上限执行 **+1**。负命会使“+1”不能恢复到预期的正命。

### 按用户要求修订为逐点伤害／连续复活

用户明确：普通扣命使血量归零后先复活，复活完成后继续承受每把剑的伤害；每次归零都可再触发可用的复活道具。
最终稳定在 0 命且不能再复活时判负。此前“直接抹掉负命”的方案会抵消剩余伤害，已替换。

当前实现保留游戏原始总扣命结果。例如 `1 - 1 - 2 = -2`，其中 `2` 就是**第一次归零之后仍需结算的伤害**。
在 `NeedTryRevive` 的迭代开始时将命设为 0，并将这 2 点保留为待扣伤害：

1. 调用原版 `NeedTryRevive`，由原版选道具、恢复属性及消费道具。
2. 等其协程结束，再通过 `PopUIManager.AddCallBack` 等待销毁／孵化等弹出队列完成。
3. 逐点执行剩余伤害；再次归零时重复复活。道具不能再使血量回到正数即结束。
4. 只把最终生死结果回传给外层游戏流程；中途复活成功不等于最终存活。

串行结算期间拦截该地图管理器的 `OnHpChange` 自动复活入口，避免与调度器重复启动复活。
普通 0 命复活及无剑情况继续由原入口处理。调度器只在迭代开始时占有流程，销毁／结束时释放。

| 初始状态（1 命） | 按新规则的最终结果 |
| --- | --- |
| 1 把剑 + 仅凤凰蛋 | 蛋复活到 1，再扣到 0，判负 |
| 1 把剑 + 仅平安扣 | 扣复活到 1，再扣到 0，判负 |
| 1 把剑 + 凤凰蛋、平安扣 | 两次复活，最终 1 命 |
| 2 把剑 + 凤凰蛋、平安扣 | 两次复活后再扣到 0，判负 |
| 更多剑 | 继续逐点结算，直至伤害结清或不能再复活 |

## 验证口径

- 新插件、远程 Compatibility、五个既有 Mod 及回归工具均构建 0 警告、0 错误。
- 已在远程 Compatibility 下实机加载并跑完组件回归；日志包含 `HybridCLR detected: incompatible metadata-index hook disabled before Chainloader startup.`。
- `Shared.Tests`：99 项通过；`DlcBugFix.Tests`：15 项通过。
- 远程安装器测试通过：installer 120、installer-cli 117、updater 127 项断言。
- ScorePreview 的 `_curHuPaiJieSuan` 适配与框架兼容均采用远程版本。
- 麒麟的运行时组件回归通过，工具已保存到 [tools/DlcBugProbe](../tools/DlcBugProbe/README.md)。其结论不等同于完整战斗／地图流程验收。
- 顺序调度纯托管测试在 `DlcBugFix.Tests/`；检查 E/P 的独立消耗、孵化计数、上限／魂力、等待动画、一次终态回调、无进展及整数边界。

### 已通过的顺序结算游戏内集成

以下在远程兼容补丁下验证了真实 Harmony 入口、托管／原生协程桥接、弹出队列完成屏障及血量监听防重入。
E/P 为测试用复活效果（E=凤凰蛋，P=平安扣），**实际道具效果没有在这组用例里调用**。

| 总扣命后的原始值 | 测试复活道具 | 最终命／结果 | 消费 |
| --- | --- | --- | --- |
| -1（1 命、1 剑） | E | 0，失败 | E |
| -1 | EP | 1，存活 | EP |
| -2（1 命、2 剑） | EP | 0，失败 | EP |
| -3 | EP | 0，失败 | EP |
| -4 | EEEEE | 1，存活 | EEEEE |
| -1 | 无 | 0，失败 | 无 |

全部用例检查终态回调恰好一次、下一点伤害等待消费结束、无重复自动复活、调度所有权释放，以及真实 Saver 命值未被测试改动。

### 已通过的原生组件用例

| 用例 | 结果 |
| --- | --- |
| 撤下麒麟修复，歌声后再出对子 | 复现 `KeyNotFoundException: Ke`，作为失败对照 |
| 歌声及歌声＋，各重复两轮“歌声 → 对子 → 顺子 → 刻子” | 正确增加成长值；每轮自然成长恰好触发一次，三项进度重置 |
| 成长协程 | 等待 0.6 秒后进度粒子对象均关闭 |
| 麒麟只自然成长 | 增长及重置正常 |
| 缺少两个键、仍有 `Dui=true` | 补键后保留原有进度，继续正常检测顺子 |

本机歌声两个等级的麒麟成长配置 `accMultiIndependent` 均为 1；实测两轮 Buff 值为
`1 → 2（歌声）→ 3（自然）→ 4（歌声）→ 5（自然）`。

麒麟组件用例使用真实原生 `CompleteUse` / `OnPlaying`，分数 UI 刷新和数字弹出仅对测试对象使用桩。
新的生命集成用例使用真实 Harmony 入口、协程桥接和原生队列屏障，HP 写入及复活道具是测试桩；
原版道具销毁／孵化仍需完整存档回归。运行结束检查真实 Saver 引用及命值未改变。

成功日志摘要：

```text
PASS baseline reproduced KeyNotFoundException in native natural-play path
PASS song level=0, repeat song -> pair -> sequence -> triplet
PASS song level=1, repeat song -> pair -> sequence -> triplet
PASS natural-only and partial-key recovery controls
PASS ALL sequential life integration cases
PASS ALL component regressions. Full map revival/consumption and battle UI still require in-game cases.
```

### 完整存档回归仍需覆盖

| 用例 | 检查 |
| --- | --- |
| 1 命 + 1 把剑 + 平安扣 | 消费一次，复活后再扣到 0，正确结束游戏 |
| 1 命 + 1 把剑 + 凤凰蛋 | 正确恢复属性、蛋销毁并获得雏凤；再扣到 0，正确结束游戏 |
| 2 命 + 剑 + 任一复活道具 | 恰好扣到 0 的路径正常 |
| 1 命 + 1 把剑，同时持有扣和蛋 | 串行消费两件道具，最终 1 命，继续操作 |
| 1 命 + 2 把或更多剑，同时持有扣和蛋 | 两次复活之后继续扣命，最终判负，无重复消费／卡死 |
| 非致死、无保命道具、无剑 | 扣命与结束流程正常 |
| 歌声、歌声＋ → 麒麟 → 自然成长，重复触发 | 倍率、三类进度、动画完成及战斗输入状态 |

回归时开启 `DlcBugFix.yml` 的 `debug`，保存 `BepInEx/LogOutput.log` 和 Unity `Player.log`。
