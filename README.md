# DemonicMahjong Mods — 我在地府打麻将 模组合集

为《我在地府打麻将》编写的 **BepInEx 6 (IL2CPP)** 模组库。不修改游戏本体、不注入逻辑、不参与计算，
所有数据直接读取游戏自身显示结果，但**不保证**与游戏结算一致。

## 包含的 Mod

| Mod | 功能 | 说明 |
| --- | --- | --- |
| **ScorePreview** | 对局左上角实时两行**分数预览** | `计分: 底分 x 番数 x 倍率 = 预计分`；领取计分按钮 / 听牌面板 / 结算面板的实时数据 |
| **AutoContinue** | 自动跳过「等玩家点一下」的环节 | 自动点公告【继续】进入大厅、自动点 Boss 战【点击继续】进入对局 |

各自的详细文档见：
- [ScorePreview/README.md](ScorePreview/README.md)
- [AutoContinue/README.md](AutoContinue/README.md)

## 快速开始（一键安装 / 卸载）
1. **下载**  
   下载最新版代码： [**`DemonicMahjongMod.zip`**](https://github.com/wufeng5702/demonicmahjong-mod/releases/latest/download/DemonicMahjongMod.zip)。

2. **解压**  
   将下载的 `DemonicMahjongMod.zip` 文件解压到**任意文件夹**（建议路径不含中文和空格，例如 `D:\`）。

3. **运行**  
   进入解压后的文件夹，**双击** `install_mods.bat`。  
   - 如果 Windows 提示“无法验证发布者”，请点击“仍要运行”。  
   - 脚本会显示菜单，输入要安装的 Mod 编号（多个用逗号隔开，例如 `1,2`），按回车即可。  
   - 全程自动化：自动识别游戏目录 → 自动补装 BepInEx → 自动编译并安装选中的 Mod。  
   - 如果电脑没有 `.NET SDK`，脚本会提示，请先安装 [.NET 6.0 SDK](https://dotnet.microsoft.com/download/dotnet/6.0) 后再重试。

运行后脚本会：

1. **询问你要装哪些 mod**（可多选）；一个都不选则直接退出，连依赖也不安装。
2. **自动探测游戏目录**：Steam 注册表 + `libraryfolders.vdf`（支持多磁盘库）→ 兜底仓库根 [.env](.env)
   的 `DEMONIC_MAHJONG_DIR` → 兜底手动输入。
3. **自动补装依赖**：检测到缺 BepInEx 时从 BepInEx 开发构建页面下载所需依赖（`BepInEx\` + `winhttp.dll` +
   `doorstop_config.ini` + `dotnet\`）；完成后会自动隐藏 BepInEx 日志控制台黑窗口。
4. **逐个编译并安装**选中的 mod 到 `游戏\BepInEx\plugins\`，缺失的配置文件自动生成默认模板。

常用参数（透传给 `install_mods.ps1`）：

```bat
install_mods.bat                      :: 交互式选择
powershell -File install_mods.ps1 -Mods 1,2                        :: 跳过菜单直接装 1+2
powershell -File install_mods.ps1 -u -RemoveBepInEx                :: 卸载并连 BepInEx 框架一起删除
powershell -File install_mods.ps1 -Mods 1,2 -SkipBepInEx           :: 已有 BepInEx，只装 mod
powershell -File install_mods.ps1 -Mods 1,2 -GameDir D:\其他\目录   :: 手动指定游戏目录
```

> 首次装 BepInEx 后，`interop\` 要等**启动一次游戏**才会自动生成（此后才能编译 mod）：
> 先启动游戏退出，再重跑 `install_mods.bat`。



## 手动构建 / 安装

环境要求：Windows + .NET SDK（编译 mod 用）。

```bat
# 1) 编译并安装（先关闭游戏，否则文件被占用）
taskkill //F //IM "Demonic Mahjong.exe"
cd ScorePreview && build.bat && install.bat     # 或 cd AutoContinue
```

`build.bat` / `install.bat` 从仓库根 [.env](.env) 读取 `DEMONIC_MAHJONG_DIR`，也可传参数覆盖：
`build.bat D:\其他\游戏目录`。

## 目录约定

```
mod/
  install_mods.ps1 / install_mods.bat   一键安装 / 卸载脚本
  ScorePreview/                        分数预览 mod（源码 + 各自 README）
  AutoContinue/                        自动跳过 mod（源码 + 各自 README）
  tools/dumptypes/                     反编译类型转储工具（开发用）
  .env                                 DEMONIC_MAHJONG_DIR=<游戏安装目录>（不入库，本机才需要）
  AGENT.md                             写给 AI/协作者的开发笔记（架构事实、坑、验证口径）
```

真实路径只存在 [.env](.env)（已被 `.gitignore` 忽略）；代码、脚本、文档一律不含硬编码路径。

## 卸载

```bat
install_mods.bat -u                      :: 只移除已装 mod 的 dll / 配置文件
install_mods.bat -u -RemoveBepInEx       :: 连同 BepInEx 框架与前置（winhttp/doorstop/dotnet）一起删除
```

或手动删除：`游戏\BepInEx\plugins\*.dll`（和对应 yml）；不要 BepInEx 时删掉
`BepInEx\`、`winhttp.dll`、`doorstop_config.ini`、`dotnet\`。游戏本体不受影响。

## 游戏更新后

BepInEx 每次启动会自动重新生成 `interop\`；若签名变化导致编译失败，重跑 `build.bat` + `install.bat`。
UI 结构调整导致读取不到数字时，看 `BepInEx\LogOutput.log` 的提示（README 内「验证方式」一节有日志关键字）。

## License

[MIT](LICENSE) © 2026 WuFeng
