# Satisfactory-inspired update validation

Implemented for the 0.6.0 candidate in [PR #13](https://github.com/joshua-small/qsys-mod-manager/pull/13). Selected scope lives in [#11](https://github.com/joshua-small/qsys-mod-manager/issues/11); deferred ideas and their triggers live in [#12](https://github.com/joshua-small/qsys-mod-manager/issues/12).

Reference: Satisfactory Mod Manager 3.1.0 desktop library, details, update review, settings and profile creation. Q-SYS retains native WPF, blue accent, themes, exact-build support and explicit installation previews.

## Completed evidence

- 115 native Windows package, catalog, profile, transaction and recovery checks, using real 0.14/0.15 Schematic Tools packages and Automatic Geometry 0.1.1. Coverage includes exact pins, disabled mods, missing/incompatible packages, canceled preparation, separate installations, suspension/resume, migration, assigned-profile deletion, shared/vendor/user-file preservation, stale previews, changed managed files, injected failures after every grouped file/state write, interrupted process recovery and whole-group undo. Saved profile reconciliation is checked after undo and across two installations.
- 90 native self-update checks against the packaged candidate, including keeping profile files, packages, settings and unrelated files.
- Native WPF smoke checks in Light, Dark and System with installed, disabled, update-available and empty states. Compact and expanded details, minimum 680×360 window, long names/descriptions, independent scrolling and 100/125/150/200% layout transforms were checked. Launch/apply blocking and recovery visibility were exercised.
- Interactive Windows checks: profile dialog, visible Tab focus, Escape cancellation, settings/diagnostic controls, concise accessible row names, named row icons, mod update versions/release notes, and the complete grouped-change preview. Canceling that update preserved the exact installation state SHA-256. Only copied native fixtures were used; their Designer executable was not launched.
- The Windows installer workflow builds and verifies the candidate on a disposable runner, including the bundled runtime, fresh install, repair, running/locked-file refusal, downgrade refusal, portable upgrade, uninstallation and retained user files. It also runs profile/recovery checks using current published packages plus a clearly labeled synthetic older payload. The fixture is never shipped or loaded into Designer.

Local screenshots, test receipts, fixture files and unsigned candidate packages are ignored under `artifacts/`. The PR's Windows checks and artifacts retain portable CI evidence. Publishing/signing a release is separate from this implementation.

## Limits

Layout transforms exercise scale-sensitive layout; they do not substitute for a full physical-monitor DPI matrix or assistive-technology certification. Native controls and accessible labels were inspected, but no formal screen-reader audit is claimed. These checks do not establish new Designer, Viewer, Core or hardware support. Existing exact-build and supported-mod restrictions remain in force.
