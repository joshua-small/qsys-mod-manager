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

The UI portion of [issue #21](https://github.com/joshua-small/qsys-mod-manager/issues/21) uses one `BrandBlue` value, Avitex `#0080FF`, in Light, Dark and Follow System. `Accent` and `Selected` keep that exact value. Hover and pressed colors mix it with 10% and 20% black; links mix it with white in Dark and black in Light. List selection and dropdown candidate tints blend the base blue with the corresponding theme surface. These values are calculated when the theme is applied, so future base-color edits propagate through the palette.

Dropdowns distinguish the committed `IsSelected` item from the candidate indicated by `IsHighlighted` or keyboard focus. Selection wins when the same item is also highlighted/focused; disabled styling wins over both. The candidate tint does not commit another value. This uses WPF's documented [highlight state](https://learn.microsoft.com/en-us/dotnet/api/system.windows.controls.comboboxitem.ishighlighted?view=windowsdesktop-10.0) and [template triggers](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/properties/dependency-property-value-precedence).

Accent surfaces choose black or white text according to their actual relative luminance, including the darker button states. Native checks verify at least 4.5:1 contrast for these text pairs and author links; disabled colors retain their neutral palette. This is a scoped contrast check, not a whole-application accessibility certification.

Validation: the Release WPF app and `UiTests` build through WSL with zero warnings/errors. The Windows test executable passes 72 assertions over Light, Dark, System and a return to Light using the same live controls. It checks exact base color, distinct action shades, text contrast, primary/pressed/disabled controls, committed versus highlighted dropdown items, overlapping selection/highlight, disabled precedence, and retained selection on close. Native Light/Dark/System control and dropdown renders were inspected; local evidence is in `artifacts/ui-blue-native-verified/`. The fixture sets protected pressed/highlight properties directly to test presentation without controlling the desktop mouse or keyboard; it does not claim a physical-input/DPI sweep. CI runs the same checks and uploads the renders under `artifacts/ui-colors/`.

Reproduce with `dotnet build UiTests/QdsModManager.UiTests.csproj -c Release --artifacts-path artifacts/ui-dotnet`, then run `artifacts/ui-dotnet/bin/QdsModManager.UiTests/release_win-x64/QdsModManager.UiTests.exe artifacts/ui-colors` on Windows. Keep platform build outputs separate. Icon source/generated assets and installation are outside this UI-only change and remain open in issue #21.
