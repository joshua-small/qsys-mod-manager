# Repository-owned content and approved catalog

The live approval policy and aggregated catalog now live in [qsys-mod-catalog](https://github.com/joshua-small/qsys-mod-catalog). Follow its [onboarding process](https://github.com/joshua-small/qsys-mod-catalog#add-a-mod) and [package contract](https://github.com/joshua-small/qsys-mod-catalog/blob/main/PACKAGE-CONTRACT.md).

This repository owns the native manager, package verifier, packer and reusable publishing tools. Its `catalog/` files are a frozen offline/bootstrap snapshot and the legacy endpoint for older managers. Do not add live releases here. The separate catalog's main branch owns approved package definitions; its published branch owns generated releases.

## Runtime

QMM fetches a signed catalog feed containing approved definitions, the index and presentation metadata. It verifies ECDSA signatures with an embedded public key before activating or caching a feed. Descriptions, GitHub author identities, help links, image URLs/hashes and release notes come from each mod repository's immutable release record. No text is summarized. Image bytes are bounded and hash-checked before decoding.

The UI carries the verified feed with an operation preview. The elevated worker verifies it again before reading the package or changing any file. Unsigned local approval files are accepted only by build/publishing tools, not by the application. An offline package for a newly approved mod requires a previously verified catalog snapshot.

Definitions contain the mod's identity, repository, module and exact allowed file layouts. A new mod does not need a manager recompile. The package verifier still rejects unsafe paths, conflicting ownership, unknown files, incorrect shared hashes, wrong assembly names/versions, unsupported Designer builds and oversized archives. Imported packages retain the existing verified-layout workflow; a valid local package is not a proof of author provenance.

## Activation and history

Schema 2 has no enable markers and does not require Harmony for every mod. The manager uses the actual private files in each verified manifest when enabling, disabling, removing, suspending or applying profiles.

Schema-1 packages remain supported for exact historical restoration. Their legacy markers are removed during a managed upgrade to schema 2, including grouped updates and disabled profile entries. Preview/cancel, rollback, recovery and undo retain the old package's complete file set. Unmanaged markers are preserved. Journals use their original file set so adding a catalog definition does not invalidate earlier recovery or undo.

## Publishing tools

- `source <repo> <source-dir>`: validate repository-owned presentation.
- `publish <repo> <tag> <commit> <source-dir> <package> <asset> <output>`: produce a verified descriptor locally.
- `release <repo> <tag> <commit> <source-dir> <output-dir>`: verify an existing stable GitHub release and its immutable source, then produce its descriptor.
- `sync <catalog-dir> <cache-dir>`: collect approved repositories and append verified releases; preserve historical pins and receipts.
- `check <candidate-dir> <cache-dir> <baseline-dir>`: verify and reproduce a generated proposal from its receipts.
- `approvals <candidate.json> <baseline.json>`: validate approval data and preserve old identities/layouts.
- `sign <catalog-dir> <private-key-file> <output-feed>`: sign and verify a fully generated feed with the configured catalog key.
- `verify-feed <feed.json>`: verify the runtime feed.
- Prefix author commands with `--approved-feed <feed.json>` to use the signed live approval snapshot. Trusted collector commands use `--approvals <approvals.json>` from the reviewed catalog main branch.

The reusable workflow fetches the separate signed catalog, validates metadata without executing mod code, and uploads a descriptor without replacing an existing package or record. Both the reusable workflow reference and its tooling input are pinned to the same reviewed manager commit. Routine collection runs in the separate catalog repository and publishes automatically; initial mod/file-layout approval remains a maintainer review.

## Migration and delivery

The initial separate catalog retains the existing v0.15.0 Schematic Tools and v0.1.1 Automatic Geometry archive pins. Their bootstrap presentation predates author-owned records. Authors can publish explicit historical descriptors with a merged content commit, or publish their next version with content at its release tag. Do not rewrite historical packages.

The markerless mod changes target Schematic Tools 0.15.1 and Automatic Geometry 0.1.2. Merge and release a manager with schema-2/signed-feed support before publishing those packages for users. Older managers continue reading the frozen legacy endpoint and cannot install schema-2 packages. This source change does not itself release or replace an installed application.

The dedicated catalog signing key is separate from Windows Authenticode signing. Repository-secret configuration and public-key rotation are documented in the catalog repository.
