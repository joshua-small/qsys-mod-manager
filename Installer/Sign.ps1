# Called directly for the app payload and by Inno Setup for setup/uninstall.
#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string[]] $FilePath,
    [Parameter(Mandatory)][string] $ContextPath
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Signing.ps1')
$context = Get-Content -LiteralPath $ContextPath -Raw | ConvertFrom-Json
$targets = @($FilePath | ForEach-Object { (Resolve-Path -LiteralPath $_).Path })
$previousTenant = $env:AZURE_TENANT_ID
try {
    $env:AZURE_TENANT_ID = $context.tenantId
    & $context.signTool sign /v /fd SHA256 /tr 'http://timestamp.acs.microsoft.com' /td SHA256 /dlib $context.dlib /dmdf $context.metadata @targets
    if ($LASTEXITCODE -ne 0) { throw "Artifact Signing failed: $LASTEXITCODE" }
    & $context.signTool verify /pa /all /v /tw @targets
    if ($LASTEXITCODE -ne 0) { throw "SignTool verification failed: $LASTEXITCODE" }
    Assert-QmmSignature -FilePath $targets
    # Inno deletes its temporary uninstaller after embedding the signature.
    # Preserve the exact verified bytes for the build receipt, before returning.
    foreach ($target in $targets) {
        if ([IO.Path]::GetFileName($target) -match '^uninst\.e(32|64)\.tmp$') {
            Copy-Item -LiteralPath $target -Destination ([IO.Path]::ChangeExtension($target, '.exe'))
        }
    }
}
finally { $env:AZURE_TENANT_ID = $previousTenant }
