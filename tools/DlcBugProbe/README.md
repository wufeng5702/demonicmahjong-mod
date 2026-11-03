# DlcBugProbe — 游戏内组件回归工具

开发用 BepInEx 插件。需要已安装并启用 [DlcBugFix](../../DlcBugFix/README.md) 的两个修复，使用匹配版本的 interop。

## 运行

先关闭游戏，在仓库根编译并安装：

```powershell
# $game 为游戏目录；也可通过 DEMONIC_MAHJONG_DIR 环境变量供构建使用
dotnet build tools/DlcBugProbe/DlcBugProbe.csproj -c Release "-p:GameDir=$game"
Copy-Item tools/DlcBugProbe/bin/Release/net6.0/DlcBugProbe.dll "$game/BepInEx/plugins/"
```

启动游戏进入**主菜单**，执行：

```powershell
Set-Content "$game/BepInEx/DlcBugProbe.command" run
```

工具等地图管理器完成初始化后执行一次，结果写入 `BepInEx/LogOutput.log`。完成标志为
`PASS ALL component regressions`。用例失败会输出 `FAIL` 或异常堆栈。
仅跑顺序扣命集成用例可将命令改为 `life`，成功标志为 `PASS ALL sequential life integration cases`。

## 覆盖范围

- 临时撤下麒麟的两处修复，调用原生 `GeShengDisplay.CompleteUse` 和 `QiLinDisplay.OnPlaying`，确认原版缺键异常。
- 恢复修复，歌声／歌声＋各重复两次成长，然后依次打出对子、顺子、刻子；检查倍率增加、三项进度和 `ActiveTween` 的结束状态。
- 自然成长对照、只有部分进度键时的恢复。
- 顺序扣命集成：仅凤凰蛋、蛋＋扣、多把剑及多次复活；检查终态回调一次、剩余伤害逐点执行、等待消费完成、阻止血量监听重入。

这些是**组件回归**：使用游戏的真实道具配置、成长与出牌逻辑；独立组件的分数 UI 刷新和成长数字弹出被桩替代，粒子对象与成长协程实际运行。
生命测试临时替换 HP 入口及复活效果为桩，执行真实 Harmony 入口、协程桥接和原生弹出队列屏障，结束后恢复原 HP 入口。
真实道具消耗、孵化和完整战斗输入仍按调查记录中的存档用例验收。

工具创建独立临时对象，不替换玩家存档；退出用例时恢复麒麟补丁和 Unity 随机状态。
原版对照会产生一条**预期**的 `KeyNotFoundException`；修复用例要求无 native→managed trampoline 错误。

测试结束后关闭游戏，删除 `BepInEx/plugins/DlcBugProbe.dll`。工具为测试启用的后台运行设置随游戏退出结束。
