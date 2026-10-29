param([string]$TempRoot = $env:TEMP)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$tokens = $null
$errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'install_mods.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw ($errors -join "`n") }
# Only import functions; never run the installer's menu, downloads, game or exit statements.
foreach ($function in $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
    . ([scriptblock]::Create($function.Extent.Text))
}

function Assert-True($value, [string]$message) {
    if (-not $value) { throw $message }
    $script:passed++
}

$script:passed = 0
$script:IsInteractive = $false
$catalog = @([pscustomobject]@{ Dll = 'ScorePreview.dll' })
$root = Join-Path $TempRoot ('demonic-installer-test-' + [Guid]::NewGuid().ToString('N'))
# Include spaces to exercise game directory and process working-directory handling.
$script:game = Join-Path $root 'game with spaces'
$script:plugin = Join-Path $game 'BepInEx\plugins\ScorePreview.dll'
$script:log = Join-Path $game 'BepInEx\LogOutput.log'
$interop = Join-Path $game 'BepInEx\interop'
$required = @('MaJiang.dll', 'Il2Cppmscorlib.dll', 'Il2CppSystem.Core.dll', 'UnityEngine.CoreModule.dll',
              'UnityEngine.IMGUIModule.dll', 'UnityEngine.TextRenderingModule.dll', 'UnityEngine.UI.dll',
              'Unity.TextMeshPro.dll', 'SaintsField.Runtime.dll', 'assembly-hash.txt')

# All process operations are fakes: these tests cannot start or stop a real game.
function Get-Process {
    param($Name, $ErrorAction)
    if ($script:running) { return [pscustomobject]@{ Id = 42 } }
}

function Start-Process {
    param($FilePath, $WorkingDirectory, [switch]$PassThru, $WindowStyle, $ArgumentList)
    $script:started++
    Assert-True ($WorkingDirectory -eq $script:game) 'Game working directory was not set'
    Assert-True (($ArgumentList -join ' ') -eq '-batchmode -nographics -logFile BepInEx/interop-generation-player.log') 'Startup check relies on unsupported -quit or is not headless'
    Assert-True (-not (Test-Path $script:plugin)) 'Stale plugin was loaded during generation'
    Assert-True (@(Get-ChildItem (Split-Path $script:plugin) -Filter '*.disabled.*').Count -eq 1) 'Stale plugin backup is missing'
    if ($script:throwOnStart) { throw 'simulated start failure' }
    [IO.File]::WriteAllLines($script:log, [string[]]$script:lines)
    $script:proc = [pscustomobject]@{ Id = 123; ExitCode = $script:exitCode; HasExited = $script:finished }
    $script:proc | Add-Member -MemberType ScriptMethod -Name Refresh -Value {}
    $script:proc | Add-Member -MemberType ScriptMethod -Name WaitForExit -Value {
        param($timeout)
        [IO.File]::WriteAllLines($script:log, [string[]]$script:lines)
        return $script:finished
    }
    return $script:proc
}

function Get-Date {
    # Advance fake time without waiting five real minutes for the timeout test.
    $script:clock = $script:clock.AddSeconds(150)
    return $script:clock
}

function Start-Sleep { param($Milliseconds) }

function Stop-Process {
    param($Id, [switch]$Force, $ErrorAction)
    Assert-True ($Id -eq 123) 'Stopped a process not started by the check'
    $script:stopped++
    $script:proc.HasExited = $true
}

function Check-Startup([string[]]$output, [int]$code, [bool]$expected, [bool]$finish = $true, [bool]$throws = $false) {
    $script:lines = $output
    $script:exitCode = $code
    $script:finished = $finish
    $script:throwOnStart = $throws
    $script:started = 0
    $script:stopped = 0
    $script:clock = [DateTime]::new(2026, 1, 1)
    [IO.File]::WriteAllText($script:log, 'old log')
    $result = Invoke-AutoGenerateInterop $script:game
    Assert-True ($result -eq $expected) "Unexpected startup result: $($output -join ', ') / exit $code"
    Assert-True ($script:started -eq 1) 'Existing interop bypassed the startup check'
    Assert-True ((Get-Content -LiteralPath $script:plugin -Raw) -eq 'old plugin') 'Stale plugin was not restored unchanged'
    Assert-True (@(Get-ChildItem (Split-Path $script:plugin) -Filter '*.disabled.*').Count -eq 0) 'Disabled plugin was left behind'
    if (-not $finish) { Assert-True ($script:stopped -eq 1) 'Timed-out diagnostic process was not stopped exactly once' }
}

try {
    New-Item -ItemType Directory -Path $interop, (Split-Path $plugin), (Join-Path $game 'Custom\Dlcs'), (Join-Path $game 'Custom\Mods') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $game 'Demonic Mahjong.exe'), 'not a real executable')
    [IO.File]::WriteAllText($plugin, 'old plugin')
    [IO.File]::WriteAllText((Join-Path $game 'Custom\Dlcs\keep.txt'), 'DLC content')
    [IO.File]::WriteAllText((Join-Path $game 'Custom\Mods\keep.txt'), 'official mod content')
    Assert-True (-not (Test-InteropReady $game)) 'Empty interop directory accepted'
    foreach ($file in $required) { [IO.File]::WriteAllText((Join-Path $interop $file), 'data') }
    Assert-True (Test-InteropReady $game) 'Complete interop rejected'
    foreach ($file in $required) {
        [IO.File]::WriteAllText((Join-Path $interop $file), '')
        Assert-True (-not (Test-InteropReady $game)) "Empty $file accepted"
        Remove-Item -LiteralPath (Join-Path $interop $file)
        Assert-True (-not (Test-InteropReady $game)) "Missing $file accepted"
        [IO.File]::WriteAllText((Join-Path $interop $file), 'data')
    }

    Assert-True (-not (Test-InteropStartupLog @())) 'Empty log accepted'
    Assert-True (-not (Test-InteropStartupLog @('Chainloader initialized'))) 'Premature initialized marker accepted'
    Assert-True (Test-InteropStartupLog @('Chainloader startup complete')) 'Complete startup rejected'
    Check-Startup @('Chainloader startup complete') 0 $true
    Check-Startup @('Chainloader startup complete') 0 $true $false
    Check-Startup @('Chainloader initialized') 0 $false
    Check-Startup @('Failed to generate Il2Cpp interop assemblies', 'Chainloader startup complete') 0 $false
    Check-Startup @('Interop assemblies are possibly out of date', 'Chainloader startup complete') 0 $false
    Check-Startup @('[Fatal  : Preloader] startup failed', 'Chainloader startup complete') 0 $false
    Check-Startup @('Chainloader startup complete') -1073741819 $false
    Check-Startup @() 0 $false $false
    Check-Startup @() 0 $false $true $true

    $script:running = $true
    $script:started = 0
    Assert-True (-not (Invoke-AutoGenerateInterop $game)) 'Running game accepted'
    Assert-True ($script:started -eq 0) 'Started another instance while game was running'
    Assert-True ((Get-Content -LiteralPath $plugin -Raw) -eq 'old plugin') 'Running-game check moved the plugin'
    Assert-True ((Get-Content -LiteralPath (Join-Path $game 'Custom\Dlcs\keep.txt') -Raw) -eq 'DLC content') 'Custom DLC changed'
    Assert-True ((Get-Content -LiteralPath (Join-Path $game 'Custom\Mods\keep.txt') -Raw) -eq 'official mod content') 'Custom mods changed'
    Write-Host "Installer tests passed ($script:passed assertions)." -ForegroundColor Green
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force
}
