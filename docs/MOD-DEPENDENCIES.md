# Dependencies and Schematic Tools migration

Manager 0.8 adds package schema 3. Existing schema-1/2 archives, profiles, identities, ownership rules, and journals remain valid. Older managers reject schema-3 approval layouts; publish the manager update before extending the signed feed.

An approved layout declares `dependencies` and `conflicts`, each an array of `{ "id": "mods-core", "minimum": "0.1.0", "before": "0.2.0" }` ranges. `minimum` is inclusive, optional `before` is exclusive, and versions normalize to four components. Empty lists must be explicit. A package must reproduce its approved lists exactly; it cannot omit a dependency. Only known IDs are accepted, with no self references or duplicate IDs. Enabled package graphs must satisfy every range and contain no dependency cycles or matching conflicts.

Single install can resolve dependencies from verified available packages and preview the complete resulting profile. Existing compatible pins remain selected. Missing or incompatible packages, removing/disabling an in-use dependency, and enabling a consumer without its dependency fail before any write. Native shared-file hash rules still apply. Dependency package code is never loaded by the manager.

Core owns `Qmm.mods-core.Runtime.dll` as a private file. Consumers depend on `mods-core` rather than carrying their own runtime copy. This permits Core updates without relaxing the immutable hashes of shared files such as Harmony.

Schema-3 layouts also declare minimumVersion and an optional beforeVersion for the consuming package. Disjoint ranges can carry new dependency policies while retaining historical pins; overlapping layouts are rejected.

## Bundle migration

The **Split Schematic Tools** action is visible for an installed, managed bundle earlier than 0.17. It resolves Core and the seven feature packages, then previews one grouped transaction. It preserves the profile ID/name, unrelated exact pins, disabled choices, and suspension state. The old loader is removed as the new loaders are written. An injected failure rolls back the files and profile; Undo restores the original exact bundle package. Historical archives and journal data remain intact.

Ordinary updates cannot replace the legacy bundle with the diagnostics-only successor. Direct installs and grouped profiles must complete the split migration first, so the old bundle cannot silently lose its user features.

The new packages conflict with `schematic-tools` versions before 0.17. The 0.17 successor is optional developer diagnostics and can coexist. Unmanaged conflicting bundle files must be adopted before migration; they are never overwritten implicitly.

Preferences are owned by the mods, outside the installation transaction. Named Signal Colors reads the legacy palette and saves it to the new settings document only on a successful Preferences save. The old file is preserved, so Undo can use the old bundle's settings. Gap previously reset to 20 on startup and was not persistent; the new default remains 20. New preferences are not deleted on Undo.

## Release order and checks

1. Publish Manager 0.8 with schema-3 support; keep its bundled historical approvals unchanged.
2. Create the Core/module repositories and prepare their source changes; keep stable package releases pending.
3. Merge the catalog approvals and update its pinned publishing tool to the reviewed manager commit. Preserve all previous identities/layouts and release records. The signed feed can approve a mod before it has a stable package.
4. Merge and publish Core and the seven modules with their source-owned presentation and immutable package records, using the signed approvals.
5. Refresh the new manager's signed catalog, close the selected Designer, and preview the migration.

No unsigned runtime approval route was added. `Catalog.ForTool` remains restricted to build tools/tests. A local lab test of prospective approvals is not a production catalog approval.

Validation: 151 existing package/transaction/recovery checks, 31 dependency/migration checks, source compilation, and approval preservation. The dependency checks cover missing and incompatible Core, cycles, tampered manifest policy, disabled consumers, legacy conflicts, atomic migration failure, exact-pin Undo, and the compatible developer-only successor. Native Designer acceptance is tracked separately from manager filesystem checks.

[Implementation tracking](https://github.com/joshua-small/qsys-mod-manager/issues/18).
