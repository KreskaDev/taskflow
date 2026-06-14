# Feature Specification: Data Export & Import

**Feature Branch**: `011-data-export-import`

**Created**: 2026-06-13

**Status**: Draft

**Input**: Slice 011 of the TaskFlow MVP. Source of truth: `.specify/memory/product-vision.md`. Goal: full data portability — lossless JSON export and human-readable CSV export, plus import from a TaskFlow JSON export and a Todoist CSV export with a field-mapping preview the user accepts before any data is written. Import is additive with no deduplication, and an automatic local backup runs before a bulk import.

## Provenance

This slice realizes the following items from product-vision.md:

Slice-specific:
- US-07 (Data Export & Import) — full: AS-01, AS-02, AS-03, AS-04, AS-05, AS-06, AS-07
- FR-035 (full data export in JSON, lossless)
- FR-036 (full data export in CSV, human-readable)
- FR-037 (import from TaskFlow JSON with full fidelity)
- FR-038 (import from Todoist CSV with best-effort mapping; additive, no deduplication)
- FR-039 (mapping preview for user acceptance before proceeding)
- EC-07 (import with unmappable columns)
- SC-005 (export-to-JSON then re-import yields an identical dataset)

Cross-cutting (realized in this slice):
- FR-031 (suppress single-key shortcuts in text inputs)
- FR-042 (visible focus indicator)
- FR-043 (ARIA roles/labels)
- FR-044 (text contrast ≥ 4.5:1)
- FR-045 (no collision with assistive-technology bindings)
- FR-046 (no hover-only content)
- FR-047 (prefers-reduced-motion)
- FR-049 (error message + recovery action)
- FR-050 (structured error logging)
- FR-051 (auto-backup before migration — meaningfully realized here: the backup runs before a bulk import, and the user can restore from it)

MVP boundary confirmed:
- OOS-01..OOS-12 (full MVP out-of-scope confirmation)

Depends on:
- Slice 003 (project-management) — provides projects, the Inbox definition, and the project entity that export serializes and import maps into
- Slice 005 (labels) — provides reusable labels that export serializes and import maps into
- Slice 007 (cycles) — provides cycles that the lossless JSON export must serialize and restore
- Slice 008 (recurring-tasks) — provides recurrence rules that the lossless JSON export must serialize and restore

> Scope note: import is additive with no deduplication — a conflict such as a duplicate task title produces a new entry that the user resolves manually (FR-038 mapping; US-07.AS-07). The FR-051 automatic local backup runs before a bulk import, so the user has a restore point if an import is not what they wanted.

## User Scenarios & Testing *(mandatory)*

### User Story 7 - Data Export & Import (Priority: P3)

User can export all their data in JSON (lossless) and CSV (human-readable) formats, and import data from a TaskFlow JSON export or a Todoist CSV export with field mapping preview.

**Why this priority**: Data portability is a trust requirement for offline-first apps but is not part of daily usage flow.

**Independent Test**: Can be tested by creating sample data, exporting to JSON, clearing data, importing from JSON, and verifying all data is restored identically.

**Acceptance Scenarios** (owned by this slice):

1. **(US-07.AS-01) Given** user has tasks, projects, labels, and cycles, **When** they export to JSON, **Then** a single JSON file is generated containing all entities with all fields preserved losslessly.
2. **(US-07.AS-02) Given** user has tasks, **When** they export to CSV, **Then** a CSV file is generated with human-readable columns (title, status, priority, due date, project name, labels, created date, completed date).
3. **(US-07.AS-03) Given** user has a TaskFlow JSON export file, **When** they import it, **Then** all entities are restored with all fields intact and relationships preserved.
4. **(US-07.AS-04) Given** user has a Todoist CSV export, **When** they start import, **Then** a preview screen shows the column mapping (Todoist projects to TaskFlow projects, Todoist p1-p4 to TaskFlow P0-P3, Todoist labels to TaskFlow labels).
5. **(US-07.AS-05) Given** the import preview is shown, **When** user accepts the mapping, **Then** data is imported according to the displayed mapping.
6. **(US-07.AS-06) Given** the Todoist CSV contains columns that cannot be mapped, **When** the preview renders, **Then** unmapped columns are clearly shown as "will not be imported" and user can proceed or cancel.
7. **(US-07.AS-07) Given** user imports data, **When** a conflict exists (e.g., duplicate task titles), **Then** imported items are created as new entries (no deduplication, user resolves manually).

### Edge Cases

- **EC-07 — Import with unmappable columns**: The import preview clearly marks unmapped columns. User can proceed (ignoring them) or cancel.

## Requirements *(mandatory)*

### Functional Requirements (slice-specific)

- **FR-035**: System MUST support full data export in JSON format (lossless, all entities and fields).
- **FR-036**: System MUST support full data export in CSV format (human-readable).
- **FR-037**: System MUST support import from TaskFlow JSON export with full fidelity.
- **FR-038**: System MUST support import from Todoist CSV with best-effort mapping: projects to projects, labels to labels, priority p1 (highest) to P0 (highest), p2 to P1, p3 to P2, p4 (lowest) to P3 (lowest).
- **FR-039**: When importing CSV with unmappable columns, the system MUST show a preview with the mapping for user acceptance before proceeding.

### Cross-cutting Requirements (realized in this slice)

Accessibility (per Constitution Principle II):
- **FR-031**: Single-key shortcuts MUST be suppressed when a text input is focused; only modifier-based shortcuts remain active during text input.
- **FR-042**: Every focusable element MUST have a visible focus indicator.
- **FR-043**: All interactive elements MUST have correct ARIA roles and labels for screen reader compatibility.
- **FR-044**: Text contrast ratio MUST be at least 4.5:1 (3:1 for large text).
- **FR-045**: Custom keyboard shortcuts MUST NOT collide with native assistive-technology bindings.
- **FR-046**: No content may be accessible only via hover — all tooltips and popovers MUST have a keyboard/focus-triggered equivalent.
- **FR-047**: Animations MUST respect the `prefers-reduced-motion` user preference; when reduced motion is active, transitions MUST be instant or under 100ms.

Error Handling & Data Integrity (per Constitution Principle VII):
- **FR-049**: All errors MUST be presented to the user with a clear message and an actionable recovery suggestion. No operation may fail silently.
- **FR-050**: Errors MUST be logged with structured context (severity level, operation context, and error details) for debugging purposes.
- **FR-051**: Before any data migration, the system MUST automatically create a local backup of user data. The user MUST be able to restore from this backup.

### Key Entities

This slice introduces no new entity and owns no entity attribute. Export serializes — and import maps into — the entities realized by the depended-on slices (Task, Project, Label, Cycle, Recurrence Rule); their canonical definitions live in product-vision.md and are owned by their respective slices (001, 003, 005, 007, 008).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-005**: Exporting all data to JSON and re-importing produces an identical dataset with zero data loss.

## Constitution Compliance

This slice is evaluated against constitution v1.1.0. Cross-cutting principles realized here:

- **I. Keyboard-First**: export and import are initiated and driven from the keyboard — selecting a format, navigating the mapping preview, and accepting or cancelling (US-07.AS-05, AS-06) require no mouse; FR-031 keeps single-key shortcuts from hijacking text entry in the import flow.
- **II. Accessibility (WCAG 2.1 AA)**: FR-042 (focus indicator), FR-043 (ARIA roles/labels), FR-044 (contrast ≥ 4.5:1), FR-045 (no AT-binding collisions), FR-046 (no hover-only content — the "will not be imported" marking on unmapped columns is conveyed without relying on hover), FR-047 (prefers-reduced-motion).
- **III. Instant Response**: the mapping preview and accept/cancel controls respond within one animation frame; non-blocking progress is not surfaced via spinners for local operations.
- **V. Offline-Only, Local-First**: export and import are purely local file operations with zero network calls; the exported format is documented and human-inspectable (JSON lossless, CSV human-readable), consistent with data sovereignty.
- **VI. Type Safety End-to-End**: import is a trust boundary the constitution names explicitly — deserialization from storage and JSON/CSV parsing. The TaskFlow JSON importer and the Todoist CSV importer MUST runtime-validate their input before any data is written, so a malformed or partial file is rejected with a typed, recoverable error rather than corrupting the store.
- **VII. Data Integrity & Resilience**: FR-049 (a failed or partially-mappable import reports a clear message and an actionable next step — proceed, cancel, or fix the file), FR-050 (import/export failures are logged with structured context), and FR-051 realized meaningfully — an automatic local backup is created before a bulk import runs, and the user can restore from it if the additive import (FR-038; US-07.AS-07) is not what they wanted. Export/Import availability itself satisfies the constitution's Export/Import requirement under this principle.
- **VIII. Test-First**: each owned acceptance scenario above, plus EC-07 and the SC-005 round-trip, is independently testable (Red-Green-Refactor).

**Deferral note (accepted at slicing time):** Principle VII's 30-second undo for destructive actions (FR-040) is owned by slice 010 (undo). Import here is additive — it deletes nothing — so undo is not the recovery path for this slice; the pre-import FR-051 backup is. No compliance gap is introduced.

## Assumptions

This slice introduces no new slice-specific assumptions. The MVP-wide assumptions owned in product-vision.md (ASM-01..ASM-09) continue to apply unchanged.

## Out of Scope

This slice confirms the full MVP out-of-scope boundary (OOS-01..OOS-12 from product-vision.md):

- **OOS-01**: Multi-user collaboration, sharing, permissions
- **OOS-02**: Cross-device sync, cloud storage
- **OOS-03**: Mobile application, PWA
- **OOS-04**: AI features (auto-categorization, summaries, suggestions)
- **OOS-05**: External integrations (calendar, Slack, GitHub, email)
- **OOS-06**: Push notifications, reminders
- **OOS-07**: File attachments on tasks
- **OOS-08**: Subtasks (task nesting)
- **OOS-09**: Custom views, saved filters
- **OOS-10**: Custom theming beyond dark/light mode
- **OOS-11**: Automations (if X then Y)
- **OOS-12**: Plugin or extension system

Additionally, note that external integrations (OOS-05) remain out of scope: importing a Todoist CSV is a one-time file-based import, not a live or recurring integration with Todoist or any external service.
