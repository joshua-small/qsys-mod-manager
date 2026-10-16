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

Without `-Sign`, the local build produces unsigned development artifacts and needs no Azure access. Pull request checks use this mode and never log in to Azure. Main-branch builds and manual runs of the Windows installer workflow use GitHub OIDC, then `-Sign -SigningAuthentication AzureCli`. Both portable ZIP variants are signed, and all three downloadable packages are included in `SHA256SUMS.txt` after signing.

The dedicated `avitex-mod-manager-github` Entra app has only the Certificate Profile Signer role at `avitex-signing/mod-manager`. Its permanent federated credential trusts issuer `https://token.actions.githubusercontent.com`, audience `api://AzureADTokenExchange`, and the exact subject `repo:joshua-small@15166975/qsys-mod-manager@1414943179:ref:refs/heads/main`. This includes GitHub's immutable owner and repository IDs. PR tokens cannot satisfy that subject. The repository variables `SIGNING_AZURE_CLIENT_ID`, `SIGNING_AZURE_TENANT_ID`, and `SIGNING_AZURE_SUBSCRIPTION_ID` contain public identifiers; no client secret is stored. Azure Login is pinned to a release commit and cleans up its CLI login after the job.

Manual runs on other branches require an explicitly provisioned matching federated credential. Temporary bootstrap branch trust must be removed after validation and merge. A future repository transfer to Avitex requires updating the exact OIDC subject before signed builds can resume.

Run lifecycle validation **only on a disposable Windows machine**. With a signed installer, add `-RequireSigned` to `Installer/Test.ps1` so it verifies setup and the installed app, DLLs, and uninstaller along with the existing installation, repair, migration, and removal checks. The signed GitHub workflow enables this check. The workflow deliberately removes its runner's desktop runtime and must never be run on a personal workstation.

The build does not publish a release. Publish only the final setup and portable ZIP after validation; do not redistribute an intermediate payload or unsigned CI build as a signed release.

Tray mode retains the application mutex while the window is hidden; use **Exit** in the tray before setup or uninstall. Startup is opt-in per user through `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\QsysModManager`, with a quoted executable path and `--background`. Repair/upgrades retain it. Uninstall removes exact matching values in loaded user hives, including other signed-in users, and preserves registrations belonging to other portable copies. Unloaded user hives are not mounted: a signed-out account can retain a stale entry after machine-wide uninstall and should remove it in Windows Startup apps. StartupApproved is owned by Windows and is never rewritten by QMM.

The previous low-level `-SigningCommand` option is replaced by `-Sign`; manual pre-signing is no longer required.

References: [Microsoft signing integration](https://learn.microsoft.com/en-us/azure/artifact-signing/how-to-signing-integrations), [Inno SignTool](https://jrsoftware.org/ishelp/topic_setup_signtool.htm), [signed uninstallers](https://jrsoftware.org/ishelp/topic_setup_signeduninstaller.htm).
