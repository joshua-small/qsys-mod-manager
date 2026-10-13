# Building the Windows installer

Publish the application first with .NET 10, keeping Windows and Linux build outputs separate:

```powershell
dotnet publish App/QdsModManager.App.csproj -c Release --artifacts-path artifacts/dotnet -o artifacts/publish -p:DebugType=None -p:DebugSymbols=false
```

On Windows, use PowerShell 7 and the pinned Inno Setup version in `prerequisites.json`:

```powershell
./Installer/Build.ps1 -PublishDirectory artifacts/publish -OutputDirectory artifacts/signed-release -Compiler 'C:\InnoSetup\ISCC.exe' -Sign
```

Use a fresh output directory for each build. The signed build signs the payload copy of the executable and both application DLLs, generates `installed-files.json` from their signed bytes, and uses Inno's signing callback for its uninstaller and setup executable. It verifies the expected Joshua Small publisher, Windows trust, and timestamp before producing the portable ZIP and final SHA256 sums. `signatures.json` records the verified files; the signed uninstaller is retained in `signed-uninstaller/`. The input publish directory is not modified.

## Authentication and signing dependencies

`signing.json` holds the public Avitex account/profile details and a pinned Microsoft Artifact Signing client download. The build verifies the download hash and Microsoft's DLL signature. Install the x64 .NET 8 runtime and a current Windows SDK with x64 SignTool; use `-SignToolPath` if the SDK isn't in its standard location.

The default authentication opens Microsoft's browser sign-in in the personal Joshua tenant. Choose Joshua's personal Microsoft account. Shared IDE/cache credentials and other credential types are excluded. The personal account needs the **Artifact Signing Certificate Profile Signer** role on `avitex-signing/mod-manager`. Signatures use Joshua Small as the publisher; Avitex branding does not change the verified identity. The build requests Microsoft's SHA256 RFC3161 timestamp for every signed file.

For an already configured Windows Azure CLI session, use `-SigningAuthentication AzureCli`. Sign in with `az login --tenant c3b65de8-21c8-428b-9758-33fcacf85b1a`; the build checks the current CLI tenant and excludes other authentication methods. Do not copy token caches between WSL and Windows.

## CI and validation

Without `-Sign`, the build produces unsigned development artifacts and needs no Azure access. The existing Windows workflow continues to use this mode. Unattended signing requires an explicitly configured Azure identity with a GitHub OIDC trust and a role limited to the signing profile; no service identity, secret, or new GitHub permission is created by this change.

Run lifecycle validation **only on a disposable Windows machine**. With a signed installer, add `-RequireSigned` to `Installer/Test.ps1` so it verifies setup and the installed app, DLLs, and uninstaller along with the existing installation, repair, migration, and removal checks. The existing workflow deliberately removes its runner's desktop runtime and must never be run on a personal workstation.

The build does not publish a release. Publish only the final setup and portable ZIP after validation; do not redistribute an intermediate payload or unsigned CI build as a signed release.

The previous low-level `-SigningCommand` option is replaced by `-Sign`; manual pre-signing is no longer required.

References: [Microsoft signing integration](https://learn.microsoft.com/en-us/azure/artifact-signing/how-to-signing-integrations), [Inno SignTool](https://jrsoftware.org/ishelp/topic_setup_signtool.htm), [signed uninstallers](https://jrsoftware.org/ishelp/topic_setup_signeduninstaller.htm).
