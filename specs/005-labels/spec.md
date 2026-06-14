# Feature Specification: Labels

**Feature Branch**: `005-labels`

**Created**: 2026-06-13

**Status**: Draft

**Input**: Slice 005 of the TaskFlow MVP. Source of truth: `.specify/memory/product-vision.md`. Goal: reusable many-to-many labels for cross-cutting categorization, applied to and removed from a task through a keyboard-driven label selector opened with `L`. Builds on the Task entity and list from slice 001 and the optional-fields task context established by slice 004.

## Provenance

This slice realizes the following items from product-vision.md:

Slice-specific:
- US-08 (Keyboard Navigation & Shortcuts) — subset: AS-04 (`L` label selector)
- ENT-04 (Label)

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
- FR-051 (auto-backup before migration — infrastructure in place)

MVP boundary confirmed:
- OOS-01..OOS-12 (full MVP out-of-scope confirmation)

Exercised-but-not-owned:
- ENT-01 (Task) — owned by slice 001 (task-capture); this slice realizes only the label side of the relation and the selector that edits it
- FR-002 (optional-fields umbrella: description, priority, due date, labels, project assignment) — anchored at slice 004 (daily-planning); referenced as a pointer, not owned here
- FR-029 (list-shortcuts umbrella) — owned by slice 007 (cycles); the `L` keypress is a member, but this slice realizes only the `L` label-selector behavior via US-08.AS-04

## User Scenarios & Testing *(mandatory)*

### User Story 8 - Keyboard Navigation & Shortcuts (Priority: P1)

User operates on the currently selected task using keyboard shortcuts only. In this slice the relevant contextual shortcut is `L`, which opens a label selector for adding and removing reusable labels on the selected task. Labels are many-to-many: the same label can be applied across many tasks, and a task can carry many labels.

**Why this priority**: Keyboard-first is the core principle. Without complete keyboard coverage, the app fails its primary promise.

**Independent Test**: Can be tested by selecting a task, pressing `L`, and verifying a label selector appears that allows adding and removing labels on that task entirely via keyboard.

> Scope note: this slice owns only AS-04 of US-08 (the `L` label selector). The other US-08 scenarios are owned elsewhere: AS-03 (arrow navigation), AS-07 (`?` help overlay), and AS-09 (shortcut suppression) in slice 001; AS-01, AS-02 (Inbox/Upcoming navigation) and AS-05 (`M` move-to-project) in their owning slices (003/004); AS-06 (`Del` delete) in slice 010; AS-08 (`/` search) in slice 009. The `L` keypress is a member of the FR-029 list-shortcuts umbrella owned by slice 007 — see Provenance.

**Acceptance Scenarios** (owned by this slice):

1. **(US-08.AS-04) Given** a task is selected, **When** user presses `L`, **Then** a label selector appears for adding/removing labels.

### Edge Cases

This slice introduces no new edge cases. No edge case from product-vision.md (EC-01..EC-12) is assigned to this slice.

## Requirements *(mandatory)*

### Functional Requirements (slice-specific)

This slice introduces no slice-specific functional requirements. This is a deliberate consequence of a gap in the source: labels have no standalone FR in product-vision.md. The reusable-label capability is expressed only through three other anchors — the FR-002 optional-fields umbrella ("labels (multiple)"), which is anchored at slice 004; the ENT-04 (Label) entity, owned here; and the US-08.AS-04 acceptance scenario, owned here. FR-002 and FR-029 are referenced as pointers only and are not copied or owned by this slice; no new FR number is invented to fill the gap.

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

- **ENT-04 — Label**: A tag that can be applied to multiple tasks for cross-cutting categorization. Has a name and optional color. Many-to-many relationship with tasks.

> Slice scope for ENT-04: this slice owns the Label entity and its many-to-many relationship with tasks. The Task entity (ENT-01) itself is owned by slice 001 and is not claimed here; this slice only realizes the label side of the relation and the selector that edits it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

This slice introduces no new slice-specific success criteria. The relevant measurable outcomes are owned by slice 001 and continue to apply: SC-003 (visible feedback within 16ms of keypress — opening the label selector and toggling a label must produce feedback within one frame) and SC-004 (zero network calls — label management runs fully locally).

## Constitution Compliance

This slice is evaluated against constitution v1.1.0. Cross-cutting principles realized here:

- **I. Keyboard-First**: the label selector is opened with `L` on the selected task and labels are added/removed entirely via keyboard (US-08.AS-04); no mouse interaction is required.
- **II. Accessibility (WCAG 2.1 AA)**: FR-042 (focus indicator on the selector and its options), FR-043 (ARIA roles/labels for the selector list and toggle state), FR-044 (contrast ≥ 4.5:1, including any label color chip — color is never the sole carrier of meaning), FR-045 (no AT-binding collisions), FR-046 (the selector popover is focus/keyboard-triggered, never hover-only), FR-047 (prefers-reduced-motion). FR-031 keeps single-key shortcuts (including `L`) from hijacking text entry while a label-search/name input is focused.
- **III. Instant Response**: opening the selector and toggling a label produce visible feedback within one animation frame (SC-003, owned by slice 001).
- **V. Offline-Only, Local-First**: label data is stored and edited entirely on-device with no network calls (SC-004, owned by slice 001); ASM-08 (documented, inspectable local format) governs the label storage.
- **VI. Type Safety End-to-End**: Label types are generated from the schema (source of truth), with runtime validation at the label-name input and storage-deserialization boundaries.
- **VII. Data Integrity & Resilience**: FR-049 (error + recovery) and FR-050 (structured logging) cover label operation failures; FR-051 keeps the backup hook in place ahead of the schema change that adds the Label entity and its task association.
- **VIII. Test-First**: the owned acceptance scenario above (US-08.AS-04) is independently testable (Red-Green-Refactor).

**Known source gap (noted at slicing time):** Labels have no standalone functional requirement in product-vision.md; the capability is anchored only via the FR-002 optional-fields umbrella (at slice 004), ENT-04, and US-08.AS-04. No FR is invented here to close that gap — the entity (ENT-04) and the selector scenario (US-08.AS-04) are the realized contract for this slice, and the `L` keypress remains a member of the FR-029 list-shortcuts umbrella owned by slice 007.

## Assumptions

This slice introduces no new slice-specific assumptions. No assumption from product-vision.md (ASM-01..ASM-09) is assigned to this slice.

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

Also out of scope for this slice specifically (deferred to later slices): fuzzy search across labels in the command palette (US-04 / FR-032) is owned by slice 009 (command-palette-search); carrying labels forward onto generated recurring-task instances (FR-008) is owned by slice 008 (recurring-tasks); label columns in CSV/JSON export and Todoist label import mapping (FR-036, FR-038) are owned by slice 011 (data-export-import). This slice covers reusable many-to-many labels and the `L` label selector only.
