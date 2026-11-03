# DlcBugFix — 月圆奇谭交互修复（实验版）

目标版本：Steam build **26325171**，BepInEx 6 IL2CPP，`net6.0`。

状态：已通过编译、15 项调度测试及游戏内组件／协程集成。完整存档中的实际道具消费、孵化和战斗输入验收见下方调查记录。

## 修复内容

- **歌声／歌声＋与麒麟**：麒麟接受成长后补齐“顺子、刻子、对子”三个进度键，随后可以继续正常出牌成长。歌声照常选择麒麟，已有成长值及原版动画由游戏处理。另在出牌前补齐缺失的键，保留仍存在的进度。
- **殉道之剑与复活**：把扣到 0 之后仍欠的伤害保存为待结算点数。每次复活及道具动画／消耗完成后，再逐点扣剩余伤害；再次归零就继续走原版复活流程。最终只向游戏报告一次生死结果。

例如，1 命、1 把剑、只有凤凰蛋：`1 → 0 → 蛋复活到 1 → 剑扣到 0 → 无道具，判负`。
若还可触发平安扣则再复活到 1；有 2 把剑时，仍需再承受 1 点伤害。

调查依据、验证结果和剩余实机用例见 [INVESTIGATION.md](INVESTIGATION.md)。

## 构建、安装

1. 关闭游戏，在根目录 `.env` 配置 `DEMONIC_MAHJONG_DIR`。
2. 首次启动游戏生成匹配版本的 `BepInEx/interop/MaJiang.dll`。本次旧 interop 缺少 DLC 接口，重新生成后才可编译。
3. 启动兼容使用远程仓库已有的 [Compatibility](../Compatibility/README.md)；下面的构建／安装脚本会一并处理。
4. 在仓库根运行：

```powershell
.\DlcBugFix\build.bat
.\DlcBugFix\install.bat
```

成功日志：`[DlcBugFix] v0.5.0 loaded; qilin=True sword=True debug=False`。

## 配置

首次加载在插件 DLL 旁生成 `DlcBugFix.yml`，改后重启：

```yaml
qilin_growth: true
sequential_sword_damage: true
debug: false
```

`debug: true` 记录麒麟成长／出牌前后状态，以及复活选择、动画完成、复活完成的**实际回调**。
修复实际生效时，即使不开 debug，也会记录 `[qilin] ... restored ...` 或 `[life] sequential defeat ...`。

实验插件使用上述独立安装步骤。卸载时关闭游戏，删除 `BepInEx/plugins/DlcBugFix.dll` 及对应 yml。
