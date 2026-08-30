# AutoContinue — 自动跳过「等玩家点一下」的环节

BepInEx 6 (IL2CPP) 小插件，配合同目录 `AutoContinue.yml`：
- `announce_enabled`：启动后的**公告界面**【继续】按钮 → 自动点击进入大厅；
- `battle_enabled`：对局加载完成后底部**【点击继续】** → 自动点击进入对局；
- `announce_delay` / `battle_delay`：按钮出现后等 N 秒再点（默认 0=立即）。

原理：扫描场景里的 `UnityEngine.UI.Button`，按其子节点 TMP 文本匹配
「继续」（公告，精确）与「点击继续」（对局，包含）两种按钮，调用 `onClick.Invoke()`。
同一屏触发一次后 3 秒冷却；按钮消失重 arm，避免重复点击（也支持每局开场再次点击）。

## 构建 / 安装

```powershell
.\build.bat      # 或 dotnet build -c Release
taskkill //F //IM "Demonic Mahjong.exe"
.\install.bat    # 拷 bin\Release\AutoContinue.dll -> 游戏\BepInEx\plugins\
```

首次启动在 dll 同目录自动生成 `AutoContinue.yml`（默认两项都开、延迟 0），改后重启生效。
不需要时可删除 dll(+yml) 即卸载。

## 关键坑

- 匹配按文本，不用 GO 名（跨版本稳）；只对 `isActiveAndEnabled` 的按钮生效。
- UI 文本是 TMP（`TMPro.TMP_Text`）；本项目引用 `UnityEngine.UI.dll` + `Unity.TextMeshPro.dll`。
- 若某版本按钮不是 `UnityEngine.UI.Button`（自定义点击组件），日志会看到
  `AutoContinue: clicked ...` 缺失 → 需改 Click 实现（改用 EventSystem 或直接调面板方法）。