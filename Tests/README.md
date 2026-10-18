# Offline regression checks

Requires Python 3.12 and the .NET SDK selected by `global.json`. From the repository root:

```sh
python Tests/run_offline.py artifacts/offline-tests
```

Use a new output directory each time. The runner builds three inert, deterministic assemblies and legacy packages to exercise upgrade/downgrade, marker migration, rollback, corrupted backups, package tampering, catalog contracts and signature rejection. Generated modules are test fixtures and must never be installed in Designer or published as releases.

First-time .NET restore needs NuGet. Lib.Harmony is pinned to 2.4.2; its net10.0 assembly and license must match the existing production SHA-256 allowlist. No production approvals, catalog pins, keys or package validators are replaced. The signed catalog fixture still verifies with the bundled public key. Publishing tests use a synthetic pin in an in-memory copy of the catalog.

The explicit `--online` test mode remains an optional audit of all live historical catalog downloads. It is not a CI prerequisite: withdrawn downloads can legitimately make that audit fail. Windows UI/installer checks remain separate in the Windows workflow. Passing the offline suite does not establish Designer or hardware compatibility.
