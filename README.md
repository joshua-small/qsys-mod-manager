# Q-SYS Mod Manager

Mod descriptions, artwork and release notes are owned by each approved mod repository. See [catalog publishing](docs/CATALOG-PUBLISHING.md) for the separate [approved catalog](https://github.com/joshua-small/qsys-mod-catalog), release workflow, approval process and historical-version guarantees. Starting with 0.7.0, new approved mods are loaded from its signed feed without a manager rebuild. Modern packages use no enable-marker files; historical packages retain the files required to restore their exact versions.

Find, install, and manage mods for Q-SYS Designer.

1. Download **Q-SYS-Mod-Manager-Setup** from [Releases](https://github.com/joshua-small/qsys-mod-manager/releases/latest), run it, and open **Q-SYS Mod Manager** from Start. Setup includes the .NET Desktop Runtime and asks for administrator permission. Windows x64 is required.
2. Choose your **Designer installation**, or use **Browse…** to locate it. Type in **Search mods…** to narrow the list of compatible mods.
3. Select a mod and version, then choose **Download & install**, or check several mods and choose **Install selected**. Allow the administrator prompt.
4. Start Designer to use your mods.

Use the **Installed**, **Enabled**, **Disabled**, **Compatible** or **Updates** filter, or select **All**. Mods are sorted alphabetically. **Reset** clears search and filters. Select a row to open its details; **Close details** restores the wider list. Row actions and details let you install, enable, disable, remove or change versions. **Manage existing** registers an exact matching installation. **Import package…** retains the offline `.qdsmod` workflow.

**Install selected** installs the latest available version of each checked mod in one undoable change. Search and filters retain your selections; **Clear selection** clears them. When extra dependencies are required, QMM lists them in an **Install required dependencies?** dialog before downloading. Choose **Install all** to include them, or Cancel to leave the installation unchanged. Conflicts name the blocking mod and the action needed to resolve them.

Downloads can run while Designer is open. Before changing mod files, QMM prompts you to save your designs and close the selected Designer installation, then choose **Retry**. Cancel leaves its mod files unchanged and keeps downloaded packages for later. QMM never closes Designer for you. Restart Designer after installation to load the new mods.

The sidebar groups Designer installation, Profile, Mod updates and Tools. **New profile** creates a blank profile; **Manage** opens Duplicate, Rename, Delete, Import and Export. Selecting a profile that differs from the applied setup reveals **Apply profile**, which resolves exact packages and applies the complete change for the selected installation. With previews enabled, this becomes **Preview & apply**. Exports contain metadata only; supply missing packages separately. Existing managed installations get an initial profile. Profiles assigned to an installation cannot be deleted.

**Managed mods off** temporarily suspends managed mods while keeping profile choices. Turn them on to restore the enabled set. Unmanaged modules remain and are identified. **Mod updates** reviews old/new versions and release notes before Update Selected or Update All. Pins change only after success. Other installations keep their configuration. **Undo last change** restores a whole grouped change. Recovery remains visible when an operation was interrupted.

**Launch Designer** opens the selected installation and is blocked during changes or pending recovery. Hover over an unavailable action for its explanation. The author username links to their GitHub profile; **Source & help on GitHub** opens the mod repository. Failed installation actions show a dialog explaining the problem. Progress and interrupted-change recovery also appear above the mod list when needed; there is no permanent footer.

**Settings & diagnostics** contains Light, Dark and Follow System themes, copying the mod list, opening backups and exporting a local diagnostic report. Reports include installation paths, manager/build identity, managed state, compatibility and operation errors; nothing is uploaded.

**Preview changes before applying** is off by default. Install and other changes apply directly unless you enable it in **Settings & diagnostics**. Required dependency, untested-version and close-Designer prompts still appear when needed.

**Settings & diagnostics → Startup & background** adds optional **Start with Windows** and **Close window to system tray**. Both are off until you enable them. Startup opens QMM quietly after you sign in; the tray icon beside the clock provides **Open QMM**, **Check for updates**, **Launch Designer** and **Exit**. Opening QMM again restores the running window. Closing to tray keeps work running; Exit is unavailable during installation. Use Exit before running Setup or uninstalling. Installed mods work inside Designer even when QMM is closed.

**Check for updates in the background** is on by default while QMM is running. It checks on startup and every six hours for manager updates and compatible mod updates in the last selected Designer installation. Each newly available version is announced once per session; Windows notification settings can suppress the popup. Checks never install updates automatically. Turn this option off to stop periodic checks and tray notifications; opening the main app and manual checks remain available. Startup uses your Windows account, needs no administrator permission, and can also be disabled in Windows Startup apps. Portable users should enable startup again after moving QMM to a new folder.

Manager self-updates are separate from mod updates. Q-SYS Mod Manager checks for its own update when it opens; **Settings & diagnostics → Check manager update** checks again. Choose **Install** and allow the administrator prompt; with previews enabled, confirm **Update & restart** first. Settings and mods are kept; Designer can stay open. Profile/state format and transaction contracts are described in [Profiles](docs/PROFILES.md).

Tested with the Q-SYS Designer 10.5 build on Windows. Other Designer versions can be selected and managed, with **WARNING: Untested on this version. Use at your own risk.** displayed in the installation panel. You must accept the warning before applying a change without a matching saved opt-in. When Q-SYS Mods Core is enabled, that acceptance is saved for this installation and its exact Designer build, allowing a Core version that supports the shared opt-in to initialize after restart. Older Core releases and individual mods may still enforce their own build checks. Changing Designer builds requires new acceptance. The **All** filter includes these mods; **Compatible** shows only matches to the tested build. These are unofficial Designer extensions; nothing is installed on a Core device.

The warning does not establish compatibility or bypass package verification, dependency checks, protection of unmanaged files, backups, or recovery. Missing Designer files still block changes. If the Designer build changes while preparing an operation, refresh and try again.

To uninstall, use **Settings → Apps → Installed apps → Q-SYS Mod Manager**. Your settings, downloaded packages, backups, and installed Designer mods are kept. To remove mods too, remove them in the manager first.

The **Windows ZIP** remains available for portable use; extract it before opening the app. The ZIP requires the .NET 10 Desktop Runtime (Windows x64) separately.

Starting with version 0.5.1, the app, installer, and uninstaller are digitally signed with **Joshua Small** as the verified publisher. Windows SmartScreen may still show a reputation warning for a new release. Download only from the release page above.

Build instructions, including signed installers, are in the [installer build guide](https://github.com/joshua-small/qsys-mod-manager/blob/main/Installer/README.md).

Dependency-aware package installation and bundle migration are described in [Mod dependencies](docs/MOD-DEPENDENCIES.md).
