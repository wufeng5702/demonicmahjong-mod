param([string]$TempRoot = $env:TEMP)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$tokens = $null
$errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'install_mods.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw ($errors -join "`n") }
foreach ($function in $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
    . ([scriptblock]::Create($function.Extent.Text))
}

$root = Join-Path $TempRoot ('demonic-updater-test-' + [Guid]::NewGuid().ToString('N'))
$scriptRoot = Join-Path $root 'project with spaces'
$GameDir = Join-Path $root 'game with spaces'
$Compiler = 'csc'
$SkipBepInEx = $true
$catalog = @(
    [pscustomobject]@{ Id = 1; Project = 'ScorePreview'; Dll = 'ScorePreview.dll'; Cfg = 'ScorePreview.yml' },
    [pscustomobject]@{ Id = 5; Project = 'BanLingYongLimit'; Dll = 'BanLingYongLimit.dll'; Cfg = 'BanLingYongLimit.yml' }
)
$originalTemp = $env:TEMP
$script:passed = 0
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Assert-True($value, [string]$message) {
    if (-not $value) { throw $message }
    $script:passed++
}

function Assert-Throws([scriptblock]$action, [string]$message) {
    $threw = $false
    try { & $action | Out-Null } catch { $threw = $true }
    Assert-True $threw $message
}

function New-Package([string]$name, [string]$version = '0.6.0', [switch]$MissingInstaller, [switch]$Traversal) {
    $dir = Join-Path $root $name
    New-Item -ItemType Directory -Path $dir | Out-Null
    foreach ($file in @('install_mods.ps1', 'Compatibility\Compatibility.csproj', 'tools\compiler\csc.exe',
                        'tools\compiler\ref\net6.0\System.Runtime.dll', 'ScorePreview\ScorePreview.csproj',
                        'BanLingYongLimit\BanLingYongLimit.csproj', 'Shared\GitVersion.cs')) {
        $path = Join-Path $dir $file
        New-Item -ItemType Directory -Path (Split-Path $path) -Force | Out-Null
        [IO.File]::WriteAllText($path, 'fixture')
    }
    [IO.File]::WriteAllText((Join-Path $dir 'Shared\GitVersion.cs'), "static class GitVersion { internal const string Version = `"$version`"; }")
    if ($MissingInstaller) { Remove-Item (Join-Path $dir 'install_mods.ps1') }
    $zip = "$dir.zip"
    [IO.Compression.ZipFile]::CreateFromDirectory($dir, $zip)
    if ($Traversal) {
        $archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Update)
        try { $entry = $archive.CreateEntry('../escape.txt'); $entry.Open().Dispose() }
        finally { $archive.Dispose() }
    }
    return $zip
}

function Reset-Scenario {
    $script:requests = [Collections.Generic.List[string]]::new()
    $script:failedHosts = @()
    $script:hostPackages = @{}
    $script:badHashHosts = @()
    $script:htmlHosts = @()
    $script:defaultPackage = $script:goodPackage
    $script:apiFails = $false
    $script:releaseTag = 'v0.6.0'
    $script:running = $false
    $script:startDuringDownload = $false
    $script:launches = 0
    $script:exitCode = 0
    $script:arguments = @()
    $script:versions = @{ 'ScorePreview.dll' = [version]'0.4.1.0'; 'BanLingYongLimit.dll' = [version]'0.5.0.0' }
}

# All network, compiler and child-installer execution is fake; real files/ZIPs exercise validation and cleanup.
function Invoke-WebRequest {
    param($Uri, $OutFile, $TimeoutSec, $UserAgent, [switch]$UseBasicParsing)
    $script:requests.Add($Uri)
    $hostName = ([uri]$Uri).Host
    if ($hostName -eq 'api.github.com') {
        if ($script:apiFails) { throw 'offline API' }
        return [pscustomobject]@{ Content = (@{ tag_name = $script:releaseTag; draft = $false; prerelease = $false } | ConvertTo-Json) }
    }
    if ($script:failedHosts -contains $hostName) { throw 'simulated timeout/404' }
    $package = if ($script:hostPackages.ContainsKey($hostName)) { $script:hostPackages[$hostName] } else { $script:defaultPackage }
    if ($Uri.EndsWith('.sha256')) {
        $hash = (Get-FileHash $package -Algorithm SHA256).Hash
        if ($script:badHashHosts -contains $hostName) { $hash = '0' * 64 }
        $text = "$hash  DemonicMahjongMod.zip`n"
        if ($script:htmlHosts -contains $hostName) { $text = '<html>proxy error</html>' }
        [IO.File]::WriteAllText($OutFile, $text)
    }
    else {
        Copy-Item -LiteralPath $package -Destination $OutFile
        if ($script:startDuringDownload) { $script:running = $true }
    }
}

function Get-Process {
    param($Name, $ErrorAction)
    if ($script:running) { [pscustomobject]@{ Id = 42 } }
}

function Get-ModFileVersion([string]$file) { return $script:versions[(Split-Path $file -Leaf)] }

function Invoke-Tool([string]$exe, [string[]]$argList) {
    $script:launches++
    $script:arguments = $argList
    Assert-True ($exe -like '*powershell.exe') 'Updater did not use a separate PowerShell process'
    $installer = $argList[[Array]::IndexOf($argList, '-File') + 1]
    Assert-True ((Test-Path $installer) -and -not $installer.StartsWith($scriptRoot)) 'Updater ran the old installer'
    Assert-True ($argList -notcontains '-Update' -and $argList -notcontains '-CheckUpdate') 'Updater recursion flags forwarded'
    return [pscustomobject]@{ Code = $script:exitCode; Lines = @('fixture installer output') }
}

function Assert-Clean {
    Assert-True (@(Get-ChildItem $root -Directory -Filter 'demonic_mod_update_*').Count -eq 0) 'Download workspace leaked'
    Assert-True ((Get-Content "$GameDir\BepInEx\plugins\ScorePreview.yml" -Raw) -eq 'custom: keep') 'Existing config changed'
    Assert-True ((Get-Content "$scriptRoot\.env" -Raw) -eq 'local-project-config') 'Project config changed'
}

try {
    New-Item -ItemType Directory -Path "$scriptRoot\Shared", "$GameDir\BepInEx\plugins" -Force | Out-Null
    $env:TEMP = $root
    [IO.File]::WriteAllText("$scriptRoot\Shared\GitVersion.cs", 'static class GitVersion { internal const string Version = "0.5.0"; }')
    [IO.File]::WriteAllText("$scriptRoot\.env", 'local-project-config')
    foreach ($file in @('Demonic Mahjong.exe', 'GameAssembly.dll', 'BepInEx\plugins\ScorePreview.dll',
                        'BepInEx\plugins\BanLingYongLimit.dll', 'BepInEx\plugins\OtherMod.dll')) {
        [IO.File]::WriteAllText((Join-Path $GameDir $file), 'old file')
    }
    [IO.File]::WriteAllText("$GameDir\BepInEx\plugins\ScorePreview.yml", 'custom: keep')
    $script:goodPackage = New-Package 'good'
    $oldPackage = New-Package 'old' '0.4.1'
    $missingPackage = New-Package 'missing' -MissingInstaller
    $traversalPackage = New-Package 'traversal' -Traversal
    $corruptPackage = Join-Path $root 'corrupt.zip'
    [IO.File]::WriteAllText($corruptPackage, 'not a ZIP')

    Assert-True ((ConvertTo-ModVersion 'v0.5.0+abcdef') -eq [version]'0.5.0.0') 'Release build metadata was not normalized'
    Assert-True ((ConvertTo-ModVersion '0.5.0.0') -eq (Get-ProjectVersion $scriptRoot)) 'DLL and project versions compare differently'
    Assert-Throws { ConvertTo-ModVersion 'invalid' } 'Invalid version accepted'

    Reset-Scenario
    Assert-True ((@(Get-InstalledMods $GameDir).Id -join ',') -eq '1,5') 'Installed-mod detection included unknown files'
    $script:running = $true
    Invoke-ModUpdate -CheckOnly
    Assert-True ($script:requests.Count -eq 1 -and $script:launches -eq 0) 'Check-only downloaded/executed installer'
    Assert-Clean

    Reset-Scenario
    $script:running = $true
    Assert-Throws { Invoke-ModUpdate } 'Update allowed while game was running'
    Assert-True ($script:requests.Count -eq 0) 'Running-game update used the network'

    Reset-Scenario
    $script:releaseTag = 'v0.4.1'
    Invoke-ModUpdate
    Assert-True ($script:requests.Count -eq 1 -and $script:launches -eq 0) 'Updater downgraded relative to current project'
    Assert-Clean

    Reset-Scenario
    $script:versions['ScorePreview.dll'] = [version]'0.6.0.0'
    $script:versions['BanLingYongLimit.dll'] = [version]'0.7.0.0'
    Invoke-ModUpdate
    Assert-True ($script:requests.Count -eq 1 -and $script:launches -eq 0) 'Up-to-date/newer mods were reinstalled'

    Reset-Scenario
    $script:versions['BanLingYongLimit.dll'] = [version]'0.7.0.0'
    Invoke-ModUpdate
    Assert-True ($script:launches -eq 1) 'Update did not launch new installer exactly once'
    Assert-True ($script:arguments[[Array]::IndexOf($script:arguments, '-Mods') + 1] -eq '1') 'Newer installed mod was selected'
    Assert-True ($script:arguments[[Array]::IndexOf($script:arguments, '-GameDir') + 1] -eq $GameDir) 'Game path with spaces was split'
    Assert-True ($script:arguments -contains '-SkipBepInEx' -and $script:arguments -contains 'csc') 'Install options were not forwarded'
    Assert-True (@($script:requests | Where-Object { $_ -like '*releases/latest/download*' }).Count -eq 0) 'Known release was not pinned by tag'
    Assert-Clean

    Reset-Scenario
    $script:apiFails = $true
    $script:failedHosts = @('github.com')
    Invoke-ModUpdate
    Assert-True ($script:launches -eq 1) 'API/direct-download failure prevented mirror update'
    Assert-True (@($script:requests | Where-Object { $_ -like 'https://gh.monlor.com/https://github.com/*releases/latest/download*' }).Count -eq 2) 'Latest-package mirror fallback failed'
    Assert-Clean

    foreach ($failedCount in @(2, 3)) {
        Reset-Scenario
        $hosts = @('github.com', 'gh.monlor.com', 'git.yylx.win', 'github.geekery.cn')
        $script:failedHosts = $hosts[0..($failedCount - 1)]
        Invoke-ModUpdate
        Assert-True ($script:launches -eq 1 -and ([uri]$script:requests[$script:requests.Count - 1]).Host -eq $hosts[$failedCount]) 'Mirror fallback order was wrong'
        Assert-Clean
    }

    foreach ($failure in @('hash', 'html', 'version', 'corrupt', 'structure', 'traversal')) {
        Reset-Scenario
        switch ($failure) {
            'hash' { $script:badHashHosts = @('github.com') }
            'html' { $script:htmlHosts = @('github.com') }
            'version' { $script:hostPackages['github.com'] = $oldPackage }
            'corrupt' { $script:hostPackages['github.com'] = $corruptPackage }
            'structure' { $script:hostPackages['github.com'] = $missingPackage }
            'traversal' { $script:hostPackages['github.com'] = $traversalPackage }
        }
        Invoke-ModUpdate
        Assert-True ($script:launches -eq 1 -and ([uri]$script:requests[$script:requests.Count - 1]).Host -eq 'gh.monlor.com') "Invalid $failure package did not fall back"
        Assert-True (@(Get-ChildItem $root -Recurse -Filter 'escape.txt').Count -eq 0) 'Archive escaped extraction directory'
        Assert-Clean
    }

    foreach ($failure in @('offline', 'bad-hashes')) {
        Reset-Scenario
        $hosts = @('github.com', 'gh.monlor.com', 'git.yylx.win', 'github.geekery.cn')
        if ($failure -eq 'offline') { $script:failedHosts = $hosts; $script:apiFails = $true }
        else { $script:badHashHosts = $hosts }
        Assert-Throws { Invoke-ModUpdate } "All-source $failure was reported as success"
        Assert-True ($script:launches -eq 0) 'Unverified package was executed'
        Assert-Clean
    }

    Reset-Scenario
    $script:apiFails = $true
    $script:defaultPackage = $oldPackage
    Invoke-ModUpdate
    Assert-True ($script:launches -eq 0) 'API fallback downgraded project'
    Assert-Clean

    Reset-Scenario
    $script:startDuringDownload = $true
    Assert-Throws { Invoke-ModUpdate } 'Game started during download but installer was launched'
    Assert-True ($script:launches -eq 0) 'Updater raced a running game'
    Assert-Clean

    Reset-Scenario
    $script:exitCode = 1
    Assert-Throws { Invoke-ModUpdate } 'Child installer failure was ignored'
    Assert-Clean

    # Exercise the real publish/config-preservation path with only the compiler replaced.
    function Invoke-CscBuild($projDir, $game, $outDll) {
        New-Item -ItemType Directory -Path (Split-Path $outDll) -Force | Out-Null
        [IO.File]::WriteAllText($outDll, 'new DLL')
    }
    Publish-Mod $catalog[0] $GameDir
    Assert-True ((Get-Content "$GameDir\BepInEx\plugins\ScorePreview.dll" -Raw) -eq 'new DLL') 'DLL was not replaced'
    Assert-Clean

    Reset-Scenario
    Remove-Item "$GameDir\BepInEx\plugins\ScorePreview.dll", "$GameDir\BepInEx\plugins\BanLingYongLimit.dll"
    Invoke-ModUpdate
    Assert-True ($script:requests.Count -eq 0 -and $script:launches -eq 0) 'Empty installation triggered downloads'
    Write-Host "Updater tests passed ($script:passed assertions)." -ForegroundColor Green
}
finally {
    $env:TEMP = $originalTemp
    Remove-Item -LiteralPath $root -Recurse -Force
}
