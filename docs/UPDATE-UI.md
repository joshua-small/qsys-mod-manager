# Update indicators and actions

Manager availability comes from the existing manager release check. Mod counts
come from newer installable releases for the selected installation's applied
profile, including disabled mods and untested Designer builds. Exact-build
acknowledgement remains required by the existing operation workflow; an update
badge does not certify compatibility. Split migrations retain their separate flow.
The installation selector reads the complete version from Designer's host binary,
including the patch component (10.5.0 versus 10.5.1). Refresh also updates that
label after an in-place upgrade that kept the same folder. Warnings in the sidebar,
details and change prompts name the detected version. Missing metadata is labeled
unavailable, never inferred from the folder. These labels do not change the
hash-based compatibility/consent contract or existing saved profile names.
The taskbar overlay, tray badge, sidebar buttons and shell action labels use
this same state. Merely checking for updates never installs anything.

Taskbar Jump List tasks use two fixed arguments: `--install-manager-update`
and `--install-mod-updates`. They carry no version, package path or download
URL. A second process signals the existing per-user instance;
a first launch completes initialization and checks before handling the action.
Each action checks current availability and uses the existing Manager install
or mod update review, honoring the Preview changes preference. Downloads, signature/hash verification, compatibility,
running-Designer checks, grouped transactions and rollback remain in their
existing workflows. Modal dialogs and native file pickers block shell actions.
Notification clicks and Check for updates reveal the Manager install button or
mod update review. They never begin Manager installation, even with previews off.

Busy operations temporarily remove install tasks from the taskbar menu and
disable tray install items. Badges remain while updates exist. Closing a dialog
restores applicable actions. Changing installations or completing updates
refreshes the counts. Normal application exit clears its Jump List tasks;
an old task retained after an abnormal exit still has to pass current checks.

The implementation uses WPF's [TaskbarItemInfo overlay](https://learn.microsoft.com/en-us/dotnet/api/system.windows.shell.taskbariteminfo.overlay)
and [Jump List tasks](https://learn.microsoft.com/en-us/dotnet/api/system.windows.shell.jumptask).
Windows presentation settings can hide overlays or notification-area icons.

## Native regression checks

Build with separate outputs for each platform:

```text
dotnet build UiTests/QdsModManager.UiTests.csproj -c Release --artifacts-path artifacts/ui-dotnet
```

On Windows, run the resulting test executable with:

```text
QdsModManager.UiTests.exe --updates artifacts/update-checks
QdsModManager.UiTests.exe --tray artifacts/tray-checks
```

The update suite uses synthetic in-memory availability, rejects network access,
isolates settings, and substitutes Jump List publication so it does not replace
the installed application's shell menu. It checks Manager-only, mod-only,
combined and cleared states; compatible and incompatible installations;
busy/recovery/dialog guards; preview cancellation; fixed-command parsing and
single-instance signaling; retained exact-build opt-in routes; all themes; compact Settings bounds; minimum window
size and 100–200% layout transforms. The existing tray suite also checks normal,
update, session-ending and download shutdown scenarios.

These checks do not establish a physical-monitor DPI/input matrix or an actual
Explorer right-click acceptance run. Before release, verify a normal executable's
taskbar menu and overlay, tray menu/badge at small icon sizes, hidden-window
activation, canceled previews and cleared availability in a Windows session.

## Isolated Explorer preview

For manual Windows shell acceptance, use the test executable with a new empty
absolute output folder:

```text
QdsModManager.UiTests.exe --shell-preview "C:\Temp\qmm-shell-preview"
```

This uses the built product window and shell adapters with synthetic availability,
a distinct Windows application identity and per-folder instance channel. A small
control panel selects Manager-only, mod-only, combined, cleared, busy, recovery
and untested-build states. The taskbar tasks relaunch this same isolated preview;
they never launch the installed manager. Events and the product DLL hash are
recorded in the output folder. Existing application data is rejected.

Network requests, Manager downloads/installations, mod application and Designer
launch are blocked. Confirming the Manager preview displays a no-install notice;
cancel the mod review after inspecting versions. Startup registration is disabled.
Exit through the preview's tray menu, which also clears its Jump List. Do not pin
the preview or treat synthetic availability as real release/compatibility data.
The `--shell-fixture-check` mode validates setup and seven states offscreen with
Jump List publication substituted; it does not establish Explorer acceptance.
