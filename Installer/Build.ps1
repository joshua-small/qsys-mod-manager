# The app is published separately so Windows and Linux .NET outputs never mix.
# Example: ./Installer/Build.ps1 -PublishDirectory ./artifacts/publish -OutputDirectory ./artifacts/release -Compiler 'C:\Program Files (x86)\Inno Setup 7\ISCC.exe'
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $PublishDirectory,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [Parameter(Mandatory)][string] $Compiler,
    [string] $RuntimeCache,
    # Optional Inno SignTool command with $f placeholder. Sign app binaries first.
    [string] $SigningCommand
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$compilerPath = (Resolve-Path -LiteralPath $Compiler).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$spec = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'prerequisites.json') -Raw | ConvertFrom-Json
$compilerVersion = (& $compilerPath --version | Out-String).Trim()
if ($compilerVersion -ne $spec.compilerVersion) { throw "Use Inno Setup $($spec.compilerVersion); found $compilerVersion." }
$appVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $publish 'Q-SYS Mod Manager.dll')).Version
[xml]$project = Get-Content -LiteralPath (Join-Path $repo 'App/QdsModManager.App.csproj') -Raw
$version = $project.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if ($appVersion.ToString(3) -ne $version) { throw 'Published app version does not match the project.' }
$names = @('Q-SYS Mod Manager.exe','Q-SYS Mod Manager.dll','Q-SYS Mod Manager.deps.json','Q-SYS Mod Manager.runtimeconfig.json','QdsModManager.Core.dll','README.md','THIRD-PARTY-NOTICES.txt')
$payload = Join-Path $output "payload-$version"
if (Test-Path -LiteralPath $payload) { throw "Use a fresh output directory; payload already exists: $payload" }
New-Item -ItemType Directory -Path $payload | Out-Null
foreach ($name in $names) {
    $source = if ($name -in @('README.md','THIRD-PARTY-NOTICES.txt')) { Join-Path $repo $name } else { Join-Path $publish $name }
    Copy-Item -LiteralPath $source -Destination (Join-Path $payload $name)
}
$files = @($names | ForEach-Object { [ordered]@{ path=$_; sha256=(Get-FileHash -LiteralPath (Join-Path $payload $_)).Hash.ToLowerInvariant() } })
[ordered]@{schema=1;version=$version;files=$files} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $payload 'installed-files.json') -Encoding utf8NoBOM

if (-not $RuntimeCache) { $RuntimeCache = Join-Path $output 'prerequisites' }
New-Item -ItemType Directory -Path $RuntimeCache -Force | Out-Null
$runtime = Join-Path ([IO.Path]::GetFullPath($RuntimeCache)) ([Uri]$spec.runtimeUrl).Segments[-1]
if (-not (Test-Path -LiteralPath $runtime)) { Invoke-WebRequest -Uri $spec.runtimeUrl -OutFile $runtime }
if ((Get-FileHash -LiteralPath $runtime -Algorithm SHA512).Hash.ToLowerInvariant() -ne $spec.runtimeSha512) { throw 'Microsoft runtime SHA-512 mismatch.' }
$signature = Get-AuthenticodeSignature -LiteralPath $runtime
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') { throw 'Microsoft runtime signature verification failed.' }
if ($SigningCommand) {
    foreach ($name in $names | Where-Object { $_ -match '\.(exe|dll)$' }) {
        if ((Get-AuthenticodeSignature -LiteralPath (Join-Path $payload $name)).Status -ne 'Valid') { throw "Sign $name before building a signed installer." }
    }
}
$arguments = @(
    "--define=PayloadDir=$payload", "--define=OutputDir=$output", "--define=AppVersion=$version",
    "--define=RuntimeFile=$runtime", "--define=RuntimeVersion=$($spec.runtimeVersion)",
    "--define=RuntimePatch=$(([version]$spec.runtimeVersion).Build)",
    "--define=VersionHigh=$(($appVersion.Major -shl 16) -bor $appVersion.Minor)",
    "--define=VersionLow=$(($appVersion.Build -shl 16) -bor $appVersion.Revision)"
)
if ($SigningCommand) { $arguments += '--define=SigningCommand=1'; $arguments += "--signtool=qmm=$SigningCommand" }
$arguments += Join-Path $PSScriptRoot 'QsysModManager.iss'
& $compilerPath @arguments
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed: $LASTEXITCODE" }
$setup = Join-Path $output "Q-SYS-Mod-Manager-Setup-$version-win-x64.exe"
$portable = Join-Path $output "Q-SYS-Mod-Manager-$version-win-x64.zip"
Compress-Archive -LiteralPath @($names | ForEach-Object { Join-Path $payload $_ }) -DestinationPath $portable
$artifacts = @($setup, $portable) | ForEach-Object { [ordered]@{file=[IO.Path]::GetFileName($_);bytes=(Get-Item -LiteralPath $_).Length;sha256=(Get-FileHash -LiteralPath $_).Hash.ToLowerInvariant()} }
$artifacts | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'artifacts.json') -Encoding utf8NoBOM
$artifacts | ForEach-Object { "$($_.sha256)  $($_.file)" } | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
$artifacts | Format-Table
