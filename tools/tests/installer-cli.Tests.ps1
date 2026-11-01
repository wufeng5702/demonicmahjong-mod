param([string]$TempRoot = $env:TEMP)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$source = [IO.File]::ReadAllText((Join-Path $repo 'install_mods.ps1'))
# Override the update action only in fixtures; keep real CLI/menu routing without network access.
$marker = '# ============ ' + [char]0x4e3b + [char]0x6d41 + [char]0x7a0b + ' ============'
$source = $source.Replace($marker,
    'function Invoke-ModUpdate([switch]$CheckOnly) { Write-Host "fixture-update:$CheckOnly" }' + "`n" + $marker)
$root = Join-Path $TempRoot ('demonic-installer-cli-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$plain = Join-Path $root 'install.ps1'
$interactive = Join-Path $root 'interactive.ps1'
$encoding = [Text.UTF8Encoding]::new($true)
[IO.File]::WriteAllText($plain, $source, $encoding)
# Force only the fixture copy to read redirected input as interactive console input.
[IO.File]::WriteAllText($interactive,
    $source.Replace('$script:IsInteractive = -not [Console]::IsInputRedirected',
        '$script:IsInteractive = $true; [Console]::InputEncoding = [Text.UTF8Encoding]::new($false)'), $encoding)
$exe = (Get-Command powershell.exe).Source
$script:passed = 0

function Assert-True($value, [string]$message) {
    if (-not $value) { throw $message }
    $script:passed++
}

function Test-Uninstall([string]$name, [string]$options, [string]$inputLines, [int]$remaining, [bool]$framework = $true,
                        [int]$exitCode = 0, [bool]$menu = $false, [switch]$DirectArray) {
    $game = Join-Path $root "$name game with spaces"
    New-Item -ItemType Directory -Path (Join-Path $game 'BepInEx\plugins'), (Join-Path $game 'BepInEx\core'),
        (Join-Path $game 'dotnet'), (Join-Path $game 'Custom\Mods') -Force | Out-Null
    foreach ($file in @('Demonic Mahjong.exe', 'GameAssembly.dll', 'winhttp.dll', 'doorstop_config.ini',
                        'BepInEx\core\BepInEx.Core.dll', 'dotnet\keep.txt', 'Custom\Mods\keep.txt')) {
        [IO.File]::WriteAllText((Join-Path $game $file), 'fixture')
    }
    foreach ($mod in @('ScorePreview', 'AutoContinue', 'SLMenuTrigger', 'LingPreview', 'BanLingYongLimit')) {
        foreach ($extension in @('dll', 'yml')) {
            [IO.File]::WriteAllText((Join-Path $game "BepInEx\plugins\$mod.$extension"), 'fixture')
        }
    }

    $file = if ($menu) { $interactive } else { $plain }
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $exe
    if ($DirectArray) {
        $command = "& '$($file.Replace("'", "''"))' -u -Mods @(1,2,3,4,5) -GameDir '$($game.Replace("'", "''"))'"
        $info.Arguments = "-NoProfile -ExecutionPolicy Bypass -Command `"$command`""
    }
    else { $info.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$file`" $options -GameDir `"$game`"" }
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $proc = [Diagnostics.Process]::new()
    $proc.StartInfo = $info
    try {
        [void]$proc.Start()
        $output = $proc.StandardOutput.ReadToEndAsync()
        $errorOutput = $proc.StandardError.ReadToEndAsync()
        $inputBytes = [Text.Encoding]::UTF8.GetBytes($inputLines)
        $proc.StandardInput.BaseStream.Write($inputBytes, 0, $inputBytes.Length)
        $proc.StandardInput.BaseStream.Flush()
        $proc.StandardInput.Close()
        if (-not $proc.WaitForExit(15000)) { $proc.Kill(); throw "${name}: CLI did not finish" }
        $text = $output.GetAwaiter().GetResult() + $errorOutput.GetAwaiter().GetResult()
        Assert-True ($proc.ExitCode -eq $exitCode) "$name returned $($proc.ExitCode), expected $exitCode`n$text"
        if ($name -like 'update-*' -and $exitCode -eq 0) {
            Assert-True ($text -match 'fixture-update:False') "$name did not request an update"
        }
        if ($name -like 'check-*') {
            Assert-True ($text -match 'fixture-update:True') "$name did not request a check"
        }
        if ($exitCode -ne 0) {
            Assert-True ($text -notmatch 'fixture-update:') "$name executed an invalid update request"
        }
        $dlls = @(Get-ChildItem -Path (Join-Path $game 'BepInEx\plugins') -Filter '*.dll' -ErrorAction SilentlyContinue)
        $cfgs = @(Get-ChildItem -Path (Join-Path $game 'BepInEx\plugins') -Filter '*.yml' -ErrorAction SilentlyContinue)
        Assert-True ($dlls.Count -eq $remaining -and $cfgs.Count -eq $remaining) "$name removed an unexpected set of mods`n$text"
        if ($name -eq 'new-mod-only') {
            Assert-True (-not (Test-Path (Join-Path $game 'BepInEx\plugins\BanLingYongLimit.dll'))) 'New mod was not uninstalled'
            Assert-True (Test-Path (Join-Path $game 'BepInEx\plugins\ScorePreview.dll')) 'Uninstalling new mod removed another mod'
        }
        Assert-True ((Test-Path (Join-Path $game 'BepInEx')) -eq $framework) "$name removed or preserved the wrong framework state"
        Assert-True ((Test-Path (Join-Path $game 'winhttp.dll')) -eq $framework) "$name mishandled winhttp.dll"
        Assert-True ((Get-Content (Join-Path $game 'Custom\Mods\keep.txt') -Raw) -eq 'fixture') "$name changed Custom content"
        Write-Host "CLI case passed: $name"
    }
    finally {
        if ($proc.Id -and -not $proc.HasExited) { $proc.Kill() }
        $proc.Dispose()
    }
}

$originalInputEncoding = [Console]::InputEncoding
try {
    # Process.StandardInput inherits the parent's encoding; a UTF-8 BOM would prefix the first menu choice.
    [Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
    Test-Uninstall 'u-menu' '-u' "1,2,3,4,5`n`n" 0 -menu $true
    Test-Uninstall 'long-menu' '-Uninstall' "1;2;3;4;5`n`n" 0 -menu $true
    Test-Uninstall 'unicode-menu' '-u' ((1..5 -join [char]0x3001) + "`n`n") 0 -menu $true
    Test-Uninstall 'remove-confirmed' '-u -RemoveBepInEx' "1,2,3,4,5`ny`n`n" 0 -framework $false -menu $true
    Test-Uninstall 'remove-declined' '-u -RemoveBepInEx' "1,2,3,4,5`nn`n`n" 0 -menu $true
    Test-Uninstall 'retry-menu' '-u' "invalid`n1,2`n`n" 3 -menu $true
    Test-Uninstall 'explicit-mods' '-u -Mods 1,2,3,4,5' '' 0
    Test-Uninstall 'new-mod-only' '-u -Mods 5' '' 4
    Test-Uninstall 'direct-array' '' '' 0 -DirectArray
    Test-Uninstall 'invalid-mods' '-u -Mods 1,invalid,2' '' 5 -exitCode 1
    Test-Uninstall 'cancel-menu' '-u' "0`n`n" 5 -menu $true
    Test-Uninstall 'eof-menu' '-u' '' 5 -exitCode 1 -menu $true
    Test-Uninstall 'redirected-menu' '-u' '' 5
    Test-Uninstall 'check-explicit' '-CheckUpdate' '' 5
    Test-Uninstall 'check-menu' '' "c`n`n" 5 -menu $true
    Test-Uninstall 'update-explicit' '-Update' '' 5
    Test-Uninstall 'update-menu' '' "u`n`n" 5 -menu $true
    Test-Uninstall 'update-uninstall-conflict' '-Update -u' '' 5 -exitCode 1
    Test-Uninstall 'update-check-conflict' '-Update -CheckUpdate' '' 5 -exitCode 1
    Test-Uninstall 'update-mods-conflict' '-Update -Mods 1' '' 5 -exitCode 1
    Test-Uninstall 'update-framework-conflict' '-Update -RemoveBepInEx' '' 5 -exitCode 1
    Write-Host "Installer CLI tests passed ($script:passed assertions)." -ForegroundColor Green
}
finally {
    [Console]::InputEncoding = $originalInputEncoding
    Remove-Item -LiteralPath $root -Recurse -Force
}
