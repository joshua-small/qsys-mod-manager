# QMM application icon

`../qmm.svg` is the large-size master: the Q path extracted from the official Q-SYS wordmark, filled white, on the brandmark's blue **#0166ff**. The Q's contour and proportions are unchanged at 24 px and above; the icon applies only uniform scale and placement in a rounded-square tile.

The application's approved UI state palette remains active **#0073e6**, hover **#0059b3**, and pressed **#003f80**, with white labels. The icon's Q-SYS blue is an intentional, owner-approved distinction.

`qmm-16.svg` and `qmm-20.svg` are the small optical variants, derived from the same path. At 16 px the outline is strengthened by a 0.24 px white stroke and the tail is extended 0.40 px below the bowl; at 20 px these adjustments are 0.16 px and 0.25 px. No other sizes modify the letter.

`../qmm.ico` contains authored 16, 20, 24, 32, 40, 48, 64, 96, 128 and 256 px frames. Preserve these individual frames when editing; do not downsample the large master to replace the two optical sizes. The application project embeds this ICO in its executable and WPF resources, and `Installer/QsysModManager.iss` uses it for setup. Installed shortcuts use the executable icon.

Source artwork: the owner-provided official `qsys-logo.svg`, with the Q in `wordmark` and the blue in `brandmark` class `.st1`.

- Source SVG SHA-256: `37118f882a6c51acbdb36869ee0ab390b78228f3f6c3487e949a4a1d96ee04e8`
- Unmodified Q path SHA-256: `73c123d1ed07872b90983d0af6347f0f868a9f2320bbc410822c3ef14bdd30da`

The approved exports were checked at native sizes on light and dark surfaces, and every ICO frame was decoded by Windows/WPF and compared with its authored PNG. The icon update is included in Q-SYS Mod Manager 0.8.1.
