# Run elevated on a disposable Windows test machine. Uses a unique app folder;
# refuses to touch an already registered installation or existing common shortcut.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Setup,
    [Parameter(Mandatory)][string] $LegacyZip,
    [Parameter(Mandatory)][string] $NativeChecks,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [switch] $RequireSigned
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Signing.ps1')
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$key = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{B8AD941B-5D52-4BDA-B744-80586E33E3E1}_is1'
$shortcut = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'Q-SYS Mod Manager.lnk'
$desktop = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'Q-SYS Mod Manager.lnk'
$target = Join-Path $env:ProgramFiles ('QSC\QMM Installer Validation-' + [Guid]::NewGuid().ToString('N'))
$checks = [Collections.Generic.List[string]]::new()
function Check($condition, [string]$label) { if (-not $condition) { throw $label }; $checks.Add($label); $label | Add-Content -LiteralPath (Join-Path $output 'progress.txt') }
function RunSetup([string]$label, [string[]]$extra = @(), [bool]$success = $true) {
    $log = Join-Path $output ($label + '.log')
    $arguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/DIR="' + $target + '"'),('/LOG="' + $log + '"')) + $extra
    $p = Start-Process -FilePath $Setup -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $p.WaitForExit(180000)) { throw "Setup timed out: $label" }
    Check (($p.ExitCode -eq 0) -eq $success) "$label exit code: $($p.ExitCode)"
}
function InstalledUninstaller {
    $registered = (Get-ItemProperty -LiteralPath $key).UninstallString
    $path = [IO.Path]::GetFullPath($registered.Trim().Trim('"'))
    if ([IO.Path]::GetDirectoryName($path) -ne $target -or [IO.Path]::GetFileName($path) -notmatch '^unins\d{3}\.exe$') {
        throw 'Registered uninstaller is outside the unique test installation'
    }
    return $path
}
function VerifyPayload {
    $manifest = Get-Content -LiteralPath (Join-Path $target 'installed-files.json') -Raw | ConvertFrom-Json
    foreach ($f in $manifest.files) { Check ((Get-FileHash -LiteralPath (Join-Path $target $f.path)).Hash.ToLowerInvariant() -eq $f.sha256) ("Installed bytes: " + $f.path) }
    if ($RequireSigned) {
        foreach ($name in @('Q-SYS Mod Manager.exe','Q-SYS Mod Manager.dll','QdsModManager.Core.dll')) {
            Assert-QmmSignature -FilePath (Join-Path $target $name)
            Check $true ("Installed publisher and timestamp: " + $name)
        }
        Assert-QmmSignature -FilePath (InstalledUninstaller)
        Check $true 'Installed uninstaller publisher and timestamp'
    }
    Check ((Get-ItemProperty -LiteralPath $key).DisplayVersion -eq $manifest.version) 'Windows Installed apps shows the installed version'
    return $manifest
}
function Uninstall([string]$label) {
    $uninstaller = InstalledUninstaller
    $p = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $output ($label + '.log')) + '"')) -WindowStyle Hidden -PassThru
    if (-not $p.WaitForExit(60000)) { throw 'Uninstall timed out' }
    Check ($p.ExitCode -eq 0) "$label exit code"
    # Inno's temporary uninstaller can finish cleanup after the launcher exits.
    for ($i = 0; $i -lt 150 -and ((Test-Path -LiteralPath $key) -or (Test-Path -LiteralPath $uninstaller)); $i++) { Start-Sleep -Milliseconds 100 }
    Check (-not (Test-Path -LiteralPath $key)) 'Uninstall removes Windows registration'
    Check (-not (Test-Path -LiteralPath $uninstaller)) 'Uninstaller finishes removing itself before the next installation'
    Check (-not (Test-Path -LiteralPath $shortcut)) 'Uninstall removes the common Start shortcut'
    Check (-not (Test-Path -LiteralPath $desktop)) 'Uninstall removes the optional desktop shortcut'
    Check (-not (Test-Path -LiteralPath (Join-Path $target 'Q-SYS Mod Manager.exe'))) 'Uninstall removes the manager executable'
}
try {
    if ($RequireSigned) { Assert-QmmSignature -FilePath $Setup; Check $true 'Installer publisher and timestamp verified' }
    Check (-not (Test-Path -LiteralPath $key)) 'No pre-existing registered installation'
    Check (-not (Test-Path -LiteralPath $shortcut)) 'No pre-existing common Start shortcut'
    Check (-not (Test-Path -LiteralPath $desktop)) 'No pre-existing common desktop shortcut'
    $settings = Join-Path $env:LOCALAPPDATA 'QDS Mod Manager\settings.json'
    $settingsHash = if (Test-Path -LiteralPath $settings) { (Get-FileHash -LiteralPath $settings).Hash } else { $null }
    $runtimeBefore = Test-Path 'HKLM:\SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App'
    RunSetup 'fresh-install' @('/TASKS=desktopicon')
    $manifest = VerifyPayload
    & $NativeChecks $target
    Check ($LASTEXITCODE -eq 0) 'In-app updates refresh only their own Windows registration'
    Check (Test-Path -LiteralPath $shortcut) 'Start menu shortcut created'
    Check (Test-Path -LiteralPath $desktop) 'Optional desktop shortcut created'
    $shell = New-Object -ComObject WScript.Shell
    Check ($shell.CreateShortcut($shortcut).TargetPath -eq (Join-Path $target 'Q-SYS Mod Manager.exe')) 'Start shortcut targets the installed app'
    $preserve = @('Packages\keep.qdsmod','.manager-updates\keep.txt','settings.json','profiles.json','owner-notes.txt')
    foreach ($file in $preserve) {
        $path = Join-Path $target $file
        New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force | Out-Null
        'preserve this file' | Set-Content -LiteralPath $path
    }
    $app = Start-Process -FilePath (Join-Path $target 'Q-SYS Mod Manager.exe') -WindowStyle Hidden -PassThru
    try {
        for ($i=0; $i -lt 100; $i++) { $app.Refresh(); if ($app.HasExited -or $app.MainWindowHandle -ne 0) { break }; Start-Sleep -Milliseconds 100 }
        Check (-not $app.HasExited -and $app.MainWindowTitle -like 'Q-SYS Mod Manager*') 'Installed app starts successfully'
        RunSetup 'running-app-rejected' @() $false
    } finally { if (-not $app.HasExited) { $null = $app.CloseMainWindow(); if (-not $app.WaitForExit(15000)) { throw 'Test app did not close' } } }
    $locked = [IO.File]::Open((Join-Path $target 'Q-SYS Mod Manager.exe'), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
    try { RunSetup 'locked-file-rejected' @() $false } finally { $locked.Dispose() }
    $appDll = Join-Path $target 'Q-SYS Mod Manager.dll'
    $original = [IO.File]::ReadAllBytes($appDll)
    try {
        # The Core assembly's 1.0.0 file version is a higher-version fixture.
        Copy-Item -LiteralPath (Join-Path $target 'QdsModManager.Core.dll') -Destination $appDll -Force
        RunSetup 'downgrade-rejected' @() $false
        Check ([bool](Select-String -LiteralPath (Join-Path $output 'downgrade-rejected.log') -SimpleMatch 'A newer Q-SYS Mod Manager is already installed')) 'Downgrade reports the newer installed version'
    } finally { [IO.File]::WriteAllBytes($appDll, $original) }
    'damaged file' | Set-Content -LiteralPath (Join-Path $target 'README.md')
    RunSetup 'repair'
    $null = VerifyPayload
    Uninstall 'fresh-uninstall'
    foreach ($file in $preserve) { Check ((Get-Content -LiteralPath (Join-Path $target $file) -Raw).Trim() -eq 'preserve this file') ("Uninstall preserves " + $file) }
    if ($settingsHash) { Check ((Get-FileHash -LiteralPath $settings).Hash -eq $settingsHash) 'User settings remain unchanged' }
    # Upgrade an actual older, differently named portable release in place.
    Expand-Archive -LiteralPath $LegacyZip -DestinationPath $target
    $old = Get-ChildItem -LiteralPath $target -Filter '*.dll' | Where-Object Name -in @('QDS Mod Manager.dll','Q-SYS Mod Manager.dll') | Select-Object -First 1
    Check ($null -ne $old) 'Real legacy release extracted'
    RunSetup 'portable-upgrade'
    $null = VerifyPayload
    Check (-not (Test-Path -LiteralPath (Join-Path $target 'QDS Mod Manager.dll'))) 'Legacy assembly name removed'
    foreach ($file in $preserve) { Check ((Get-Content -LiteralPath (Join-Path $target $file) -Raw).Trim() -eq 'preserve this file') ("Upgrade preserves " + $file) }
    Uninstall 'upgraded-uninstall'
    # Preserve the unique test folder and receipts for inspection; never recurse-delete.
    [ordered]@{status='passed';target=$target;runtimePresentBefore=$runtimeBefore;checks=$checks} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'result.json')
} catch {
    [ordered]@{status='failed';target=$target;error=$_.ToString();checks=$checks} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'result.json')
    throw
}
