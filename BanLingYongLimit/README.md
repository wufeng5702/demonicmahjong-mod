# BanLingYongLimit — 封禁灵俑额度

为已解锁的四种禁咒各增加 **10** 个封禁名额：**实际额度 = 游戏内置额度 + max(0, count)**。
当前版本满级禁咒默认变为普通 **40**、稀有 **45**、史诗 **40**、传说 **19**。
可在原有收藏界面选择并保存更多封禁灵俑。

## 效果预览

玩家实机截图：收藏界面中已封禁的灵俑显示封印标记。

![封禁灵俑 Mod 实机效果](../assets/images/e5fa339e348a620aa8889842b4fcc38eea6aaa76ca66b68107ff141c71e0f360.jpg)

## 安装与使用

关闭游戏，运行根目录 `install_mods.bat` 并选择 **5**，或：

```powershell
powershell -ExecutionPolicy Bypass -File install_mods.ps1 -Mods 5
```

需 BepInEx 6 `be.791` 和本仓库的 Compatibility 启动补丁，安装器会处理这些依赖。

1. 进入收藏界面的封禁设置，已解锁稀有度的额度应为内置额度加上 `count`。
2. 选择灵俑并保存方案。
3. 战前选择刚保存的自定义方案再开局。**角色推荐方案不会自动用满新增额度。**

禁咒至少升到 1 级才生效；原本额度为 0 的稀有度仍为 0。
额度是各稀有度的可选数量上限，实际能选择的数量取决于已解锁的灵俑。
天赋详情页仍显示原天赋参数；实际额度以收藏/战前设置为准。

## 配置

`BepInEx/plugins/BanLingYongLimit.yml`（修改后重启游戏）：

```yaml
enabled: true
count: 10
```

- `enabled`：开启或关闭额度调整。
- `count`：四种稀有度各自的额外额度，默认 `10`；负数按 `0` 处理，`0` 表示使用内置额度。
  非整数或超出整数范围的值回退到 `10`。
  例如内置额度为 30、`count: 10` 时实际额度为 40；天赋升级后会随内置额度一起增加。
  极大数值的相加结果最多为 `int.MaxValue`，避免整数溢出。

降低额度或关闭 Mod 后，已有方案和当前对局的封禁列表不会由本 Mod 自动裁剪。
需要减少封禁时，在收藏界面手动取消并重新保存方案。

## 日志与验证

查看 `BepInEx/LogOutput.log`：

```text
BanLingYongLimit cfg: enabled=True count=10 (extra quota; unlocked talents only)
harmony patches applied
BanLingYongLimit: BanLingYong1 quota 30 -> 40
```

最后一类日志在各稀有度首次查询到已解锁额度时输出一次。
验证重点：额度显示为内置额度加 `count`、满额后不能继续添加、取消封禁正常、保存重启后仍能使用方案。

追加额度版本已通过 SDK/捆绑 csc 构建与编译产物的 20 项配置、额度检查，覆盖默认 10、负数归零、
零额外额度、无效输入回退、四种稀有度、未解锁和非禁咒天赋、整数溢出。
玩家已提供收藏界面封禁效果截图；保存重启和开局池过滤的完整操作仍待进一步验证。

## 实现

Harmony Postfix 挂钩 `TalentManager.TalentInt(TalentType)`，仅为 400–403 且原额度大于 0 的返回值加上 `count`。
每次查询都基于游戏本次返回的内置额度计算，不会因重复打开界面而累加。
收藏界面的计数显示、封禁判定、粘贴检查和战前设置继续使用游戏自身流程。

卸载：在安装器选择卸载 **5**，或删除对应的 DLL 与 YAML 文件。
