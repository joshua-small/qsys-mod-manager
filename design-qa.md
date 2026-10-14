# Sidebar refinement design QA

Scope: [issue #14](https://github.com/joshua-small/qsys-mod-manager/issues/14), refining the native 0.6.0 UI against the owner's annotated screenshot and Satisfactory reference. This is an intentional redesign of the identified surfaces, not a pixel-for-pixel clone of Satisfactory's game catalog.

## Evidence and normalization

- Source visual truth: owner-supplied `2026-10-13_10h55_30.png` (annotated Q-SYS) and `2026-10-13_10h55_32.png` (Satisfactory). Local copies: `artifacts/design-final-comparison/source-qsys.png` and `source-satisfactory.png`.
- Both sources are 1280×1392 pixels. Excluding the 32-pixel Windows title bar gives a 1280×1360 client viewport. The native WPF capture uses exactly 1280×1360 content pixels at 96 DPI, with no resizing. CSS viewport/deviceScaleFactor do not apply to WPF.
- Implementation: `artifacts/design-final-comparison/manager-reference-details.png`, dark theme, Schematic Tools selected, installed/latest 0.15.0.0, both supported mods enabled. A separate copied installation preserves the real installed configuration.
- Full-view comparison: `artifacts/design-final-comparison/before-after.png`, old/new client images together in one 2560×1360 image.
- Focused comparisons: `sidebar-comparison.png` (Satisfactory / old Q-SYS / revised Q-SYS) and `details-comparison.png` (old / revised details), in that same directory. These retain native pixel sizes and were inspected together, not inferred from separate image views.
- Normal, minimum, long-name and scaled theme evidence: `artifacts/design-verified-{Dark,Light,System}/`. Final normalized dark evidence: `artifacts/design-final-comparison/`. Screenshots and copied fixtures remain local and ignored.

## Findings and iteration history

1. **[P1, fixed] Weak sidebar hierarchy and loose profile commands.** The supplied Q-SYS screenshot repeats the app title, gives headings the same weight as status labels, and spreads six text-like commands across two lines. The implementation removes the duplicate title, uses 14px semibold section headings with 20px group gaps, filled 32px controls, and New profile / Manage. The menu retains all five secondary commands. The final focused comparison confirms distinct groups and consistent alignment.
2. **[P2, fixed] Repeated status and unused controls.** The old screenshot repeats the active profile and app version, presents an always-visible preview button, and reserves a footer for an idle instruction. These are removed or conditional. Pending profile selection exposes Preview & apply; operations and recovery use an inline notice. Already-installed details show Disable / Remove rather than irrelevant disabled Install / Manage existing controls. The final full comparison confirms an uninterrupted browsing area and anchored Launch Designer.
3. **[P2, fixed] Author identity is plain display text.** The old details say “By Joshua Small.” The new details show the underlined `joshua-small` GitHub profile link. Legacy display-name metadata falls back to the trusted repository owner. The focused detail comparison and URI/name checks confirm the result.
4. **[P2, fixed during comparison] Soft fallback logo.** The first render enlarged the ICO's first low-resolution frame. Selecting its largest embedded frame produces a crisp supplied QMM asset. The final images confirm this; no approximate logo was drawn.
5. **[P2, fixed during native inspection] Help clipped at normal height.** The first interactive candidate required sidebar scrolling to reach the end of Help at 1280×740. Removing the redundant ready-state line and adjusting group gaps keeps all normal sidebar actions visible while retaining scrolling at smaller sizes. The normal-size verified capture confirms the fix.

The sidebar refinement retained the original blue accent, native typeface, themes, installation guards, preview-before-apply behavior and managed-file contracts. A quieter selected-row tint is intentional; selection and keyboard focus remain distinct. The named QMM fallback artwork is intentionally retained until actual mod thumbnails are supplied. The later UI accent update is recorded below.

## Required fidelity surfaces

- **Typography:** Segoe UI retained; 14px semibold group headings, 12px sidebar controls, 15px mod names, 21px detail heading. Long names truncate in rows/selectors and wrap in details. The visual hierarchy and labels were inspected at native scale.
- **Spacing/layout:** consistent control surfaces and group gaps; no adjoining outlined Browse/Refresh buttons or decorative sidebar focus rectangle; profile tools occupy one row; full-height browsing without a permanent footer. Independent scrolling and details replacement at narrow widths remain.
- **Colors/tokens:** Q-SYS blue remains primary. Selected rows use theme-specific blue tint; author links use blue plus underline. Light/Dark/System rendering was checked. No formal contrast certification is claimed.
- **Image quality:** actual bundled QMM ICO and existing Fluent/GitHub assets; largest ICO frame for fallback thumbnails. Final native screenshots show sharp assets at their intended sizes.
- **Copy/content:** heading capitalization is consistent; only actionable/relevant status stays visible. Mod version and enabled state remain in rows. Author username/profile link and repository link have different purposes and accessible names.

## Interaction and layout verification

- Native publish succeeds. Each theme passes 22 UI checks and 57 layout assertions covering default/minimum sizes, empty and filtered lists, long names/descriptions, details scrolling, 100/125/150/200% layout transforms, profile selection, author URI, operation notice, busy guards and inline recovery bounds. Final dark checks were rerun after the capture/background adjustment.
- Windows interaction: Manage opens a themed menu; Down gives a visible focus outline; Enter invokes Duplicate; the profile name is selected on opening; Escape cancels without saving. All work uses copied fixtures and isolated preview data.
- Existing installed copy and Designer configuration were not changed by this refinement. No new Designer/Core/hardware acceptance is claimed. Layout transforms are not a physical-monitor DPI matrix, and no formal screen-reader certification is claimed.
- Browser console checks are inapplicable to this native WPF app. No website prototype, backend change, transaction change or new release is part of this refinement.

No actionable P0/P1/P2 findings remain in the scoped visual comparison. Future features remain in issue #12; this report is validation evidence, not a backlog.

final result: passed

## UI accent update (2026-10-14)

The UI portion of [issue #21](https://github.com/joshua-small/qsys-mod-manager/issues/21) changes the shared `Accent` and `Selected` resources to Avitex `#0080FF` in Light, Dark and Follow System. Primary actions, checked controls, selection and focus indicators consume those resources. Hover (`#0073E6`) and pressed (`#0066CC`) remain darker variants; links and selected-row tints use the same hue with theme-specific lightness. Existing foreground and disabled colors are retained.

Validation: the Release WPF application build through WSL succeeded with zero warnings/errors, and `git diff --check` passed. The native screenshots and interaction checks above describe the earlier sidebar revision; this color update has not had a new native visual review or formal contrast assessment. Icon source/generated assets, installer packaging and installation are outside this UI-only change and remain open in issue #21.
