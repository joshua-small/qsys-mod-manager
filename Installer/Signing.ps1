# Shared by the build, Inno signing callback, and disposable installer tests.
# Requires Windows PowerShell 7 and the Windows SDK's x64 SignTool.
function Assert-QmmSignature {
    param([Parameter(Mandatory)][string[]] $FilePath)
    $spec = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'signing.json') -Raw | ConvertFrom-Json
    foreach ($path in $FilePath) {
        $signature = Get-AuthenticodeSignature -LiteralPath $path
        if ($signature.Status -ne 'Valid' -or $signature.SignatureType -ne 'Authenticode') {
            throw "Invalid embedded signature on ${path}: $($signature.Status)"
        }
        if ($signature.SignerCertificate.Subject -ne $spec.subject) {
            throw "Unexpected publisher on ${path}: $($signature.SignerCertificate.Subject)"
        }
        if ($null -eq $signature.TimeStamperCertificate) { throw "Missing timestamp on $path" }
    }
}

function Initialize-QmmSigning {
    param(
        [Parameter(Mandatory)][string] $Directory,
        [ValidateSet('InteractiveBrowser','AzureCli')][string] $Authentication = 'InteractiveBrowser',
        [string] $SignToolPath
    )
    if (-not $IsWindows) { throw 'Artifact Signing requires Windows.' }
    $spec = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'signing.json') -Raw | ConvertFrom-Json
    $directoryPath = [IO.Path]::GetFullPath($Directory)
    if (Test-Path -LiteralPath $directoryPath) { throw "Use a fresh signing directory: $directoryPath" }
    if (-not $SignToolPath) {
        $sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
        $SignToolPath = Get-ChildItem -LiteralPath $sdkRoot -Directory |
            Where-Object { $_.Name -match '^10\.0\.\d+\.\d+$' } |
            Sort-Object { [version]$_.Name } -Descending |
            ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
            Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
    if (-not $SignToolPath) { throw 'Install the Windows SDK with x64 SignTool, or supply -SignToolPath.' }
    $signTool = (Resolve-Path -LiteralPath $SignToolPath).Path
    $dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (-not (Test-Path -LiteralPath $dotnet)) { throw 'Install the x64 .NET 8 runtime.' }
    $runtimes = & $dotnet --list-runtimes
    if ($LASTEXITCODE -ne 0 -or -not ($runtimes -match '^Microsoft.NETCore.App 8\.')) {
        throw 'Install the x64 .NET 8 runtime for the Artifact Signing client.'
    }
    if ($Authentication -eq 'AzureCli') {
        if (-not (Get-Command az -ErrorAction SilentlyContinue)) { throw 'Install Azure CLI on Windows and sign in to the signing tenant.' }
        $tenant = & az account show --query tenantId --output tsv
        if ($LASTEXITCODE -ne 0 -or ($tenant | Out-String).Trim() -ne $spec.tenantId) {
            throw "Run az login --tenant $($spec.tenantId) before signing."
        }
    }
    New-Item -ItemType Directory -Path $directoryPath | Out-Null
    $archive = Join-Path $directoryPath 'client.zip'
    Invoke-WebRequest -Uri $spec.clientUrl -OutFile $archive
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $spec.clientSha256) {
        throw 'Artifact Signing client SHA-256 mismatch.'
    }
    $client = Join-Path $directoryPath ('client-' + $spec.clientVersion)
    Expand-Archive -LiteralPath $archive -DestinationPath $client
    $dlib = Join-Path $client 'bin\x64\Azure.CodeSigning.Dlib.dll'
    $signature = Get-AuthenticodeSignature -LiteralPath $dlib
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
        throw 'Artifact Signing client Microsoft signature verification failed.'
    }
    $credential = if ($Authentication -eq 'AzureCli') { 'AzureCliCredential' } else { 'InteractiveBrowserCredential' }
    $credentials = @('EnvironmentCredential','WorkloadIdentityCredential','ManagedIdentityCredential','SharedTokenCacheCredential',
        'VisualStudioCredential','VisualStudioCodeCredential','AzureCliCredential','AzurePowerShellCredential',
        'AzureDeveloperCliCredential','InteractiveBrowserCredential')
    $metadata = Join-Path $directoryPath 'metadata.json'
    [ordered]@{
        Endpoint=$spec.endpoint; CodeSigningAccountName=$spec.account; CertificateProfileName=$spec.profile
        ExcludeCredentials=@($credentials | Where-Object { $_ -ne $credential })
    } | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $metadata -Encoding utf8NoBOM
    $context = Join-Path $directoryPath 'context.json'
    [ordered]@{signTool=$signTool;dlib=$dlib;metadata=$metadata;tenantId=$spec.tenantId} |
        ConvertTo-Json | Set-Content -LiteralPath $context -Encoding utf8NoBOM
    return $context
}
