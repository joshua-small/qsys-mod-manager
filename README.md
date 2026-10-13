# Q-SYS Mod Manager

Find, install, and manage mods for Q-SYS Designer.

1. Download **Q-SYS-Mod-Manager-Setup** from [Releases](https://github.com/joshua-small/qsys-mod-manager/releases/latest), run it, and open **Q-SYS Mod Manager** from Start. Setup includes the .NET Desktop Runtime and asks for administrator permission. Windows x64 is required.
2. Choose your **Designer installation**, or use **Browse…** to locate it. Type in **Search mods…** to narrow the list of compatible mods.
3. Close Designer, select a mod and version, then choose **Download & install**. Review the change and allow the administrator prompt.
4. Start Designer to use your mods.

Use **Installed mods** to enable, disable, remove, or change a mod's version. **Undo last change** restores the previous setup. **Manage existing** registers an already installed, matching mod. You can also import a `.qdsmod` file.

Hover over an unavailable mod action to see why. **View on GitHub** opens the selected mod's repository in your browser.

Choose **Light**, **Dark**, or **Follow System** from the Theme menu. Your choice is remembered.

Q-SYS Mod Manager checks for its own updates when it opens. You can also choose **Check for updates**. When an update is available, choose **Install** beside its version, then **Update & restart** and allow the administrator prompt. Your settings and mods are kept; Designer can stay open.

Currently supports the compatible Q-SYS Designer 10.5 build on Windows. These are unofficial Designer extensions; nothing is installed on a Core.

To uninstall, use **Settings → Apps → Installed apps → Q-SYS Mod Manager**. Your settings, downloaded packages, backups, and installed Designer mods are kept. To remove mods too, remove them in the manager first.

The **Windows ZIP** remains available for portable use; extract it before opening the app. The ZIP requires the .NET 10 Desktop Runtime (Windows x64) separately.

Starting with version 0.5.1, the app, installer, and uninstaller are digitally signed with **Joshua Small** as the verified publisher. Windows SmartScreen may still show a reputation warning for a new release. Download only from the release page above.

Build instructions, including signed installers, are in the [installer build guide](https://github.com/joshua-small/qsys-mod-manager/blob/main/Installer/README.md).
