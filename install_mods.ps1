<#
 install_mods.ps1 — 一键安装/卸载本仓库的 mod

 用法：
   powershell -ExecutionPolicy Bypass -File install_mods.ps1            # 交互式
   powershell -ExecutionPolicy Bypass -File install_mods.ps1 -Mods 1,2  # 直接装 1+2
   powershell -ExecutionPolicy Bypass -File install_mods.ps1 -u         # 卸载（询问哪些）
   powershell -ExecutionPolicy Bypass -File install_mods.ps1 -u -RemoveBepInEx   # 卸载并删 BepInEx

 参数：
   -Mods <string>    要安装的 mod 编号（逗号分隔，如 1,2），给则跳过交互菜单
   -u / -Uninstall   卸载模式：移除已装 mod 的 dll/配置文件
   -RemoveBepInEx    卸载时一并删除 BepInEx 框架与前置（winhttp/doorstop/dotnet）
   -SkipBepInEx      安装时跳过 BepInEx 依赖安装（仅装 mod 本体）
   -GameDir <string> 手动指定游戏目录（默认自动探测：Steam 注册表/libraryfolders → 仓库根 .env）

 交互规则：先问用户选哪些 mod；若一个都不选 → 直接退出，连依赖也不安装。
#>
[CmdletBinding()]
param(
    [string]$Mods = "",
    [switch]$u,
    [switch]$Uninstall,
    [switch]$RemoveBepInEx,
    [switch]$SkipBepInEx,
    [string]$GameDir = ""
)

$ErrorActionPreference = "Stop"
$ProgressPreference = 'SilentlyContinue'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

if ($u) { $Uninstall = $true }
if ($Uninstall) {
    # 这里强制走卸载流程
}
elseif ($Mods -eq "" -and $GameDir -ne "") {
    # 无提示模式下允许指定目录
}

# 仓库存档：id / 项目目录 / dll 名 / 说明
$catalog = @(
    [pscustomobject]@{ Id = 1; Project = "ScorePreview"; Dll = "ScorePreview.dll"; Cfg = "ScorePreview.yml"; Desc = "分数预览（计分/和牌两行 HUD）" },
    [pscustomobject]@{ Id = 2; Project = "AutoContinue"; Dll = "AutoContinue.dll"; Cfg = "AutoContinue.yml"; Desc = "自动跳过公告【继续】与对局【点击继续】" },
    [pscustomobject]@{ Id = 3; Project = "SLMenuTrigger"; Dll = "SLMenuTrigger.dll"; Cfg = "SLMenuTrigger.yml"; Desc = "低于 Boss 时自动暂停游戏让玩家手动 SL" }
)

Write-Host "== DemonicMahjong Mod 安装器 ==" -ForegroundColor Cyan

function Test-IsGameDir([string]$d) {
    if ($d -eq "") { return $false }
    $exe = Join-Path $d "Demonic Mahjong.exe"
    $ga = Join-Path $d "GameAssembly.dll"
    return (Test-Path $exe) -and (Test-Path $ga)
}

function Get-SteamLibraries {
    $list = New-Object System.Collections.ArrayList
    try {
        $reg = Get-ItemProperty -Path "HKCU:\Software\Valve\Steam" -Name SteamPath -ErrorAction SilentlyContinue
        if ($reg -and $reg.SteamPath) { [void]$list.Add($reg.SteamPath) }
    }
    catch {}
    foreach ($base in $list.ToArray()) {
        $vdf = Join-Path $base "steamapps\libraryfolders.vdf"
        if (Test-Path $vdf) {
            $m = Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"' -AllMatches
            foreach ($mm in $m) { foreach ($g in $mm.Matches) { [void]$list.Add(($g.Groups[1].Value -replace '\\\\', '\')) } }
        }
    }
    return $list | Select-Object -Unique
}

function Find-GameDir {
    foreach ($lib in Get-SteamLibraries) {
        $cand = Join-Path $lib "steamapps\common\DemonicMahjong"
        if (Test-IsGameDir $cand) { return $cand }
    }
    # 仓库根 .env
    $envf = Join-Path $scriptRoot ".env"
    if (Test-Path $envf) {
        foreach ($line in Get-Content $envf) {
            if ($line -match '^\s*DEMONIC_MAHJONG_DIR\s*=\s*"?([^"\r\n]+)"?\s*$') {
                if (Test-IsGameDir $matches[1]) { return $matches[1] }
            }
        }
    }
    return $null
}

function Resolve-GameDir {
    if ($GameDir -ne "") {
        if (-not (Test-IsGameDir $GameDir)) { throw "目录不是游戏目录: $GameDir" }
        return $GameDir
    }
    $auto = Find-GameDir
    if ($auto) {
        Write-Host "自动识别游戏目录: $auto" -ForegroundColor Green
        return $auto
    }
    $manual = Read-Host "未找到 Steam 安装，请输入游戏目录（或回车取消）"
    if ([string]::IsNullOrWhiteSpace($manual)) { return $null }
    if (-not (Test-IsGameDir $manual)) { throw "目录不是游戏目录: $manual" }
    return $manual
}

function Ask-Mods {
    Write-Host ""
    Write-Host "选择要操作的 mod（可多选）:"
    foreach ($m in $catalog) {
        Write-Host ("  [{0}] {1}  ——  {2}" -f $m.Id, $m.Project, $m.Desc)
    }
    Write-Host "  [0] 取消"
    $raw = Read-Host "输入编号（逗号/空格分隔，如 1,2）"
    $sel = @()
    foreach ($tok in ($raw -split "[,\s，]+")) {
        $n = 0
        if ([int]::TryParse($tok, [ref]$n)) {
            if ($n -eq 0) { return @() }
            foreach ($m in $catalog) { if ($m.Id -eq $n) { $sel += $m } }
        }
    }
    return $sel
}
function Parse-Mods([string]$s) {
    $sel = @()
    foreach ($tok in ($s -split "[,\s，]+")) {
        $n = 0
        if ([int]::TryParse($tok, [ref]$n)) {
            foreach ($m in $catalog) { if ($m.Id -eq $n) { $sel += $m } }
        }
    }
    return $sel
}

function Get-WithRetry([string]$url) {
    try {
        return Invoke-RestMethod -Headers $script:headers -Uri $url -TimeoutSec 60
    }
    catch {
        Write-Host "  GitHub 直连失败（$($_.Exception.Message)），改走镜像 https://gh.ddlc.top/ ..." -ForegroundColor DarkGray
        return Invoke-RestMethod -Headers $script:headers -Uri ("https://gh.ddlc.top/" + $url) -TimeoutSec 120
    }
}

function Save-WithRetry([string]$url, [string]$out) {
    try {
        Invoke-WebRequest -Headers $script:headers -Uri $url -OutFile $out -TimeoutSec 300
    }
    catch {
        Write-Host "  下载失败（$($_.Exception.Message)），改走镜像 https://gh.ddlc.top/ ..." -ForegroundColor DarkGray
        Invoke-WebRequest -Headers $script:headers -Uri ("https://gh.ddlc.top/" + $url) -OutFile $out -TimeoutSec 600
    }
}

# 隐藏 BepInEx 控制台
function Disable-Console([string]$game) {
    Write-Host "  隐藏 BepInEx 控制台" -ForegroundColor Yellow
    $cfg = Join-Path $game "BepInEx\config\BepInEx.cfg"
    if (-not (Test-Path $cfg)) {
        Write-Host "  ⚠ 未找到 BepInEx.cfg，无法隐藏控制台" -ForegroundColor Yellow
        return
    }

    try {
        # 移除只读属性（如果有）
        if ((Get-Item $cfg).IsReadOnly) {
            Set-ItemProperty -Path $cfg -Name IsReadOnly -Value $false
        }

        $lines = Get-Content -Path $cfg -Encoding UTF8
        $inConsoleSection = $false
        $modified = $false
        $newLines = @()

        foreach ($line in $lines) {
            # 检查是否进入 [Logging.Console] 节
            if ($line -match '^\s*\[Logging\.Console\]\s*$') {
                $inConsoleSection = $true
            }
            elseif ($line -match '^\s*\[.*\]\s*$') {
                # 遇到其他节，退出当前节
                $inConsoleSection = $false
            }

            if ($inConsoleSection -and $line -match '^\s*Enabled\s*=\s*true\s*$') {
                # 只修改本节的 Enabled
                $newLines += $line -replace 'true', 'false'
                $modified = $true
            }
            else {
                $newLines += $line
            }
        }

        if ($modified) {
            [System.IO.File]::WriteAllLines($cfg, $newLines, (New-Object System.Text.UTF8Encoding $true))
            Write-Host "  ✅ 已修改 BepInEx.cfg，控制台将在下次启动时隐藏。" -ForegroundColor Green
            return
        }

        # 如果未修改，检查是否已经为 false 或不存在该行
        $currentContent = Get-Content -Path $cfg -Raw -Encoding UTF8
        if ($currentContent -match '\[Logging\.Console\][^\[]*Enabled\s*=\s*false') {
            Write-Host "  ℹ️ BepInEx.cfg 中控制台设置已为 false，无需修改。" -ForegroundColor DarkGray
        }
        else {
            # 没有找到 [Logging.Console] 节或没有 Enabled，则添加
            $newContent = $currentContent.TrimEnd() + "`r`n`r`n[Logging.Console]`r`nEnabled = false`r`n"
            [System.IO.File]::WriteAllText($cfg, $newContent, (New-Object System.Text.UTF8Encoding $true))
            Write-Host "  ✅ 已添加 [Logging.Console] 节并设为 false。" -ForegroundColor Green
        }
    }
    catch {
        Write-Host "  ❌ 修改 BepInEx.cfg 失败：$($_.Exception.Message)" -ForegroundColor Red
    }
}

function Install-BepInEx([string]$game) {
    $core = Join-Path $game "BepInEx\core\BepInEx.Core.dll"
    if (Test-Path $core) {
        Write-Host "[依赖] BepInEx 已存在，跳过安装。" -ForegroundColor DarkGray
        return $true
    }

    Write-Host "[依赖] 未找到 BepInEx，从 BepInEx 开发构建页面下载指定版本..." -ForegroundColor Yellow

    # 硬编码指定版本 be.785
    $downloadUrl = "https://builds.bepinex.dev/projects/bepinex_be/785/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.785%2B6abdba4.zip"
    $zipName = "BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.785+6abdba4.zip"

    $tmp = Join-Path $env:TEMP "bepinex_download.zip"
    $tmpDir = Join-Path $env:TEMP ("bepinex_ex_" + [Guid]::NewGuid().ToString("N"))

    Write-Host "  下载 $zipName" -ForegroundColor DarkGray
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $downloadUrl -OutFile $tmp -TimeoutSec 300 -UserAgent "demonic-mahjong-mod-installer" -UseBasicParsing

        New-Item -ItemType Directory -Path $tmpDir -Force | Out-Null
        Expand-Archive -Path $tmp -DestinationPath $tmpDir -Force

        Copy-Item (Join-Path $tmpDir "BepInEx") "$game\BepInEx" -Recurse -Force
        foreach ($f in @("winhttp.dll", "doorstop_config.ini", "dotnet")) {
            $src = Join-Path $tmpDir $f
            if (Test-Path $src) {
                Copy-Item $src "$game\$f" -Recurse -Force
            }
        }

        if (-not (Test-Path (Join-Path $tmpDir "dotnet"))) {
            Write-Host "  ⚠ 压缩包内没有 dotnet（CoreCLR）—— IL2CPP 运行时可能不完整，启动异常需补 runtime。" -ForegroundColor Yellow
        }

        Remove-Item $tmp -Force -ErrorAction SilentlyContinue
        Remove-Item $tmpDir -Recurse -Force -ErrorAction SilentlyContinue

        if (-not (Test-Path $core)) {
            throw "BepInEx 复制后校验失败"
        }

        Write-Host "[依赖] BepInEx 安装完成（$zipName）。" -ForegroundColor Green
        Write-Host "  提示：首次启动游戏会自动生成 interop/，之后才能编译 mod。" -ForegroundColor DarkGray
        return $true
    }
    catch {
        Write-Host "[依赖] 安装 BepInEx 失败：$($_.Exception.Message)" -ForegroundColor Red
        return $false
    }
}

function Publish-Mod($mod, [string]$game) {
    $projDir = Join-Path $scriptRoot $mod.Project
    $dll = Join-Path $projDir "bin\Release\$($mod.Dll)"
    Write-Host ("[mod] 编译 {0} ..." -f $mod.Project) -ForegroundColor Yellow
    Push-Location $projDir
    try {
        dotnet build -c Release -p:GameDir="$game" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "dotnet build 失败（检查 interop/ 是否已生成、.NET SDK 是否安装）" }
    }
    finally { Pop-Location }
    if (-not (Test-Path $dll)) { throw "未找到产物: $dll" }
    Copy-Item $dll (Join-Path $game "BepInEx\plugins\$($mod.Dll)") -Force
    # 配置文件：不存在才写默认
    $cfgDst = Join-Path $game "BepInEx\plugins\$($mod.Cfg)"
    if (-not (Test-Path $cfgDst)) {
        $template = Default-Cfg $mod.Project
        if ($template) { [System.IO.File]::WriteAllText($cfgDst, $template, (New-Object System.Text.UTF8Encoding $true)) }
    }
    Write-Host ("[mod] 安装完成: {0} -> plugins\{1}" -f $mod.Project, $mod.Dll) -ForegroundColor Green
}

function Default-Cfg([string]$proj) {
    switch ($proj) {
        "AutoContinue" {
            return "# AutoContinue — 自动跳过「等玩家点一下」的环节（改后重启游戏生效）`r`n" +
            "`r`n" +
            "# 1. 启动后的公告界面：自动点【继续】进入大厅`r`n" +
            "announce_enabled: true`r`n" +
            "announce_delay: 2.0`r`n" +
            "`r`n" +
            "# 2. 与 Boss 对决加载完成后底部【点击继续】：自动点击进入对局`r`n" +
            "battle_enabled: true`r`n" +
            "battle_delay: 1.0`r`n" +
            "`r`n" +
            "# 3. 对局结算后的结算/查看详情界面：自动点击【继续】（默认关闭）`r`n" +
            "result_enabled: false`r`n" +
            "result_delay: 5.0`r`n"
        }
        "ScorePreview" { 
            return "# ScorePreview — 分数预览`r`n" +
            "`r`n" +
            "# HUD 距顶部的下移量 = 屏幕高度 × 该比例（1.0=满屏高），换分辨率不变形`r`n" +
            "yoffset: 0.1`r`n" 
        }
        "SLMenuTrigger" {
            return "# SLMenuTrigger — 低于 Boss 时自动打开菜单让玩家手动 SL（改后重启生效）`r`n" +
            "`r`n" +
            "enabled: true`r`n"
        }
        default { return $null }
    }
}

function Invoke-AutoGenerateInterop([string]$game) {
    $interopDir = Join-Path $game "BepInEx\interop"
    if (Test-Path $interopDir) {
        Write-Host "[interop] 已存在，跳过生成。" -ForegroundColor DarkGray
        return $true
    }

    Write-Host "[interop] 未检测到 interop，将自动启动游戏生成..." -ForegroundColor Yellow
    $exe = Join-Path $game "Demonic Mahjong.exe"
    if (-not (Test-Path $exe)) {
        Write-Host "[interop] 错误：找不到游戏可执行文件 $exe" -ForegroundColor Red
        return $false
    }

    $log = Join-Path $game "BepInEx\LogOutput.log"
    # 备份旧日志（如果存在）
    if (Test-Path $log) { Remove-Item $log -Force }

    Write-Host "  启动游戏（窗口将最小化，完成后自动关闭）..." -ForegroundColor DarkGray
    $proc = Start-Process -FilePath $exe -PassThru -WindowStyle Minimized

    $timeout = 120  # 秒，可根据网络速度调整
    $start = Get-Date
    $success = $false

    while ((Get-Date) -lt $start.AddSeconds($timeout)) {
        Start-Sleep -Milliseconds 500
        if (Test-Path $log) {
            # 读取末尾 50 行，避免大文件
            $tail = Get-Content -Path $log -Tail 50 -ErrorAction SilentlyContinue
            # 检测成功标志：Chainloader initialized 表示 interop 已生成且加载器完成
            if ($tail -match "Chainloader initialized") {
                $success = $true
                break
            }
            # 检测失败标志（可选）
            if ($tail -match "Failed to generate Il2Cpp interop assemblies") {
                $success = $false
                break
            }
        }
    }

    # 强制结束游戏进程（无论是否成功）
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue

    if ($success) {
        Write-Host "[interop] 生成成功！" -ForegroundColor Green
        return $true
    }
    else {
        Write-Host "[interop] 自动生成失败或超时。可能原因：网络慢、游戏启动需 Steam、反作弊拦截等。" -ForegroundColor Yellow
        Write-Host "  请手动启动游戏一次（等待进入主菜单后退出），然后按回车继续..." -ForegroundColor Yellow
        Read-Host
        # 再次检查 interop 是否生成
        if (Test-Path $interopDir) {
            Write-Host "[interop] 检测到手动生成成功。" -ForegroundColor Green
            return $true
        }
        else {
            Write-Host "[interop] 仍未生成，编译 mod 可能会失败。" -ForegroundColor Red
            return $false
        }
    }
}

function Uninstall-Mod($mod, [string]$game, [bool]$removeBepinex) {
    $plugins = Join-Path $game "BepInEx\plugins"
    foreach ($f in @($mod.Dll, $mod.Cfg)) {
        $p = Join-Path $plugins $f
        if (Test-Path $p) { Remove-Item $p -Force; Write-Host "[卸载] 已删除 $p" -ForegroundColor Yellow }
    }
}

# ============ 主流程 ============
if ($Uninstall) {
    $sel = @()
    if ($Mods -ne "") { $sel = Parse-Mods $Mods } else { $sel = Ask-Mods }
    if ($sel.Count -eq 0) { Write-Host "未选择任何 mod，退出。" -ForegroundColor DarkGray; exit 0 }
    $game = Resolve-GameDir
    if (-not $game) { Write-Host "未确定游戏目录，退出。" -ForegroundColor Red; exit 1 }
    foreach ($m in $sel) { Uninstall-Mod $m $game $RemoveBepInEx }
    if ($RemoveBepInEx) {
        $confirm = Read-Host "确认删除整个 BepInEx 框架与前置(winhttp/doorstop/dotnet)? [y/N]"
        if ($confirm -match '^y') {
            Remove-Item (Join-Path $game "BepInEx") -Recurse -Force -ErrorAction SilentlyContinue
            foreach ($f in @("winhttp.dll", "doorstop_config.ini", "dotnet")) {
                Remove-Item (Join-Path $game $f) -Recurse -Force -ErrorAction SilentlyContinue
            }
            Write-Host "[卸载] BepInEx 框架已删除。" -ForegroundColor Green
        }
    }
    Write-Host "卸载完成。" -ForegroundColor Green
    exit 0
}

# 安装
$sel = @()
if ($Mods -ne "") { $sel = Parse-Mods $Mods } else { $sel = Ask-Mods }
if ($sel.Count -eq 0) {
    Write-Host "未选择任何 mod，跳过依赖安装并退出。" -ForegroundColor DarkGray
    exit 0
}
Write-Host ("已选择: " + (($sel.Project) -join ", ")) -ForegroundColor Cyan

$game = Resolve-GameDir
if (-not $game) { Write-Host "未确定游戏目录，退出。" -ForegroundColor Red; exit 1 }

if (-not $SkipBepInEx) {
    if (-not (Install-BepInEx $game)) {
        Write-Host "BepInEx 依赖安装失败，中止。可用 -SkipBepInEx 跳过。" -ForegroundColor Red
        exit 1
    }
}
else {
    Write-Host "[依赖] 已跳过 BepInEx 安装（-SkipBepInEx）。" -ForegroundColor DarkGray
}

# 隐藏 BepInEx 控制台（无论是否新装）
Disable-Console $game

# 自动生成 interop（如果缺失）
Invoke-AutoGenerateInterop $game

# 继续编译 mod
$interop = Join-Path $game "BepInEx\interop"
if (-not (Test-Path $interop)) {
    Write-Host "  ⚠ BepInEx\interop 缺失：如首次装 BepInEx，需先启动一次游戏生成 interop/，否则下面 mod 编译会失败。" -ForegroundColor Yellow
}

$fail = @()
foreach ($m in $sel) {
    try { Publish-Mod $m $game } catch { Write-Host "[mod] 失败: $($_.Exception.Message)" -ForegroundColor Red; $fail += $m.Project }
}
