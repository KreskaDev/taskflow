# Feature Specification: Labels

**Feature Branch**: `006-labels`

**Created**: 2026-06-13

**Status**: Draft

**Input**: Slice 006 of the TaskFlow MVP. Source of truth: `.specify/memory/product-vision.md`. Goal: reusable many-to-many labels for cross-cutting categorization, applied to and removed from a task through a keyboard-driven label selector opened with `L`. Builds on the Task entity and list from slice 002 and the optional-fields task context established by slice 005.

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
- FR-101 (ARIA-live for server-initiated updates/toasts + dialog focus contract)
- FR-049 (error message + recovery action)
- FR-050 (structured error logging)
- FR-051 (auto-backup before migration — infrastructure in place)

MVP boundary confirmed:
- OOS-01..OOS-19 (full MVP out-of-scope confirmation)

Exercised-but-not-owned:
- ENT-01 (Task) — owned by slice 002 (task-capture); this slice realizes only the label side of the relation and the selector that edits it
- FR-002 (optional-fields umbrella: description, priority, due date, labels, project assignment, assignees (shared-project tasks only)) — anchored at slice 005 (daily-planning); referenced as a pointer, not owned here
- FR-029 (list-shortcuts umbrella) — owned by slice 011 (cycles); the `L` keypress is a member, but this slice realizes only the `L` label-selector behavior via US-08.AS-04

## User Scenarios & Testing *(mandatory)*

### User Story 8 - Keyboard Navigation & Shortcuts (Priority: P1)

User operates on the currently selected task using keyboard shortcuts only. In this slice the relevant contextual shortcut is `L`, which opens a label selector for adding and removing reusable labels on the selected task. Labels are many-to-many: the same label can be applied across many tasks, and a task can carry many labels.

**Why this priority**: Keyboard-first is the core principle. Without complete keyboard coverage, the app fails its primary promise.

**Independent Test**: Can be tested by selecting a task, pressing `L`, and verifying a label selector appears that allows adding and removing labels on that task entirely via keyboard.

> Scope note: this slice owns only AS-04 of US-08 (the `L` label selector). The other US-08 scenarios are owned elsewhere: AS-03 (arrow navigation), AS-07 (`?` help overlay), and AS-09 (shortcut suppression) in slice 002; AS-01, AS-02 (Inbox/Upcoming navigation) and AS-05 (`M` move-to-project) in their owning slices (004/005); AS-06 (`Del` delete) in slice 014; AS-08 (`/` search) in slice 013. The `L` keypress is a member of the FR-029 list-shortcuts umbrella owned by slice 011 — see Provenance.

**Acceptance Scenarios** (owned by this slice):

1. **(US-08.AS-04) Given** a task is selected, **When** user presses `L`, **Then** a label selector appears for adding/removing labels.

### Edge Cases

This slice introduces no new edge cases. No edge case from product-vision.md (EC-01..EC-12) is assigned to this slice.

## Requirements *(mandatory)*

### Functional Requirements (slice-specific)

This slice introduces no slice-specific functional requirements. This is a deliberate consequence of a gap in the source: labels have no standalone FR in product-vision.md. The reusable-label capability is expressed only through three other anchors — the FR-002 optional-fields umbrella ("labels (multiple)"), which is anchored at slice 005; the ENT-04 (Label) entity, owned here; and the US-08.AS-04 acceptance scenario, owned here. FR-002 and FR-029 are referenced as pointers only and are not copied or owned by this slice; no new FR number is invented to fill the gap.

### Cross-cutting Requirements (realized in this slice)

Accessibility (per Constitution Principle II):
- **FR-031**: Single-key shortcuts MUST be suppressed when a text input is focused; only modifier-based shortcuts remain active during text input.
- **FR-042**: Every focusable element MUST have a visible focus indicator.
- **FR-043**: All interactive elements MUST have correct ARIA roles and labels for screen reader compatibility.
- **FR-044**: Text contrast ratio MUST be at least 4.5:1 (3:1 for large text).
- **FR-045**: Custom keyboard shortcuts MUST NOT collide with native assistive-technology bindings.
- **FR-046**: No content may be accessible only via hover — all tooltips and popovers MUST have a keyboard/focus-triggered equivalent.
- **FR-047**: Animations MUST respect the `prefers-reduced-motion` user preference; when reduced motion is active, transitions MUST be instant or under 100ms.
- **FR-101**: Server-initiated updates and toasts MUST be conveyed to assistive technology via an appropriate ARIA live region without stealing focus, and confirmation/command-palette dialogs MUST follow the dialog focus contract (set initial focus, trap focus, dismiss on Esc, return focus to the invoker on close).

Error Handling & Data Integrity (per Constitution Principle VII):
- **FR-049**: All errors MUST be presented to the user with a clear message and an actionable recovery suggestion. No operation may fail silently.
- **FR-050**: Errors MUST be logged with structured context (severity level, operation context, and error details) for debugging purposes.
- **FR-051**: Before any data migration, the system MUST automatically create a backup of user data. The user MUST be able to restore from this backup.

Access control (realized in this slice) (per Constitution Principle IX):

Authorization is deny-by-default and dispatched by the containing resource's visibility — never a conjunction of tiers. Two distinct surfaces apply in this slice:

- **The Label entity is per-user**, so label CRUD (create, rename, recolor, list, delete) authorizes on **ownership** (`ownerId`), with queries scoped to the caller (FR-065). A caller can read or edit only their own labels; acting on another user's label is denied.
- **Applying or removing a label on a task follows the task's OWN authorization**, dispatched by that task's visibility: a personal/unprojected task authorizes on ownership; a shared-project task authorizes on current `ProjectMembership` + role (editor/owner to mutate, viewer read-only) per FR-067. Leave/remove/unshare revokes ALL access to that project's tasks (FR-066), so the label-application surface there inherits the task's dispatch with no contradiction with the per-user label entity (the task-side authorization, owned by the task/sharing slices, governs the application). `createdBy`/assignee are provenance only and confer no standalone grant.

- **FR-065**: Authorization MUST be dispatched by the containing resource's visibility (not a conjunction of tiers): personal/unprojected data authorizes on ownership (`createdBy`/`ownerId`) with queries scoped to the caller; shared-project entities authorize on current `ProjectMembership` + role. Every query MUST be scoped accordingly (per-user isolation).
- **FR-066**: Access to a shared project's data MUST require current membership in that project. `createdBy` and assignee are provenance only and confer NO standalone access; on leave/remove/unshare a user MUST lose ALL access to that project's data regardless of authorship or assignment.
- **FR-067**: Each operation on a shared-project resource MUST require sufficient role (viewer=read, editor=write, owner=manage); insufficient role MUST be denied.
- **FR-068**: Authorization MUST be deny-by-default and enforced at the API/handler layer for every read and write.

This slice's command and query handlers ENFORCE these at the handler layer — they do not merely reference them. Every label list/read query and every apply-label / remove-label mutation runs the deny-by-default check: label-entity operations are scoped to the caller's `ownerId`, and label-on-task operations resolve the task's visibility and dispatch to ownership (personal) or membership+role (shared) accordingly. Both an allow and a deny path are covered by integration tests (Principle VIII; SC-016 mechanism).

### Key Entities

- **ENT-04 — Label**: A tag that can be applied to multiple tasks for cross-cutting categorization. Has a name, optional color, and an `ownerId` (the owning User; labels are per-user/Tier A). Applying a label to a task follows the task's own authorization tier. Many-to-many relationship with tasks.

> Slice scope for ENT-04: this slice owns the Label entity (including its `ownerId`) and its many-to-many relationship with tasks. The Task entity (ENT-01) itself is owned by slice 002 and is not claimed here; this slice only realizes the label side of the relation and the selector that edits it. Note the distinction the entity makes explicit: the Label entity is per-user (authorizes on `ownerId`), but APPLYING a label to a task follows that task's own authorization (personal task → ownership; shared-project task → membership + role) — so there is no contradiction with the task-side authorization owned elsewhere.

## Success Criteria *(mandatory)*

### Measurable Outcomes

This slice introduces no new slice-specific success criteria. The relevant measurable outcomes are owned by slice 002 and continue to apply: SC-003 (opening the label selector and toggling a label paint their optimistic result within 16ms of the keypress; the server reconciles or rolls back asynchronously) and SC-004 (label management depends on no third-party runtime data services — only the app's own API and PostgreSQL database).

## Constitution Compliance

This slice is evaluated against constitution v4.0.0. Cross-cutting principles realized here:

- **I. Keyboard-First**: the label selector is opened with `L` on the selected task and labels are added/removed entirely via keyboard (US-08.AS-04); no mouse interaction is required.
- **II. Accessibility (WCAG 2.1 AA)**: FR-042 (focus indicator on the selector and its options), FR-043 (ARIA roles/labels for the selector list and toggle state), FR-044 (contrast ≥ 4.5:1, including any label color chip — color is never the sole carrier of meaning), FR-045 (no AT-binding collisions), FR-046 (the selector popover is focus/keyboard-triggered, never hover-only), FR-047 (prefers-reduced-motion). FR-031 keeps single-key shortcuts (including `L`) from hijacking text entry while a label-search/name input is focused. FR-101: the label selector is a dialog/popover and MUST follow the dialog focus contract (set initial focus into it, trap focus, dismiss on Esc, return focus to the invoking task row on close); any server-initiated label change pushed to an open shared view (a SignalR label patch) or surfaced as a live toast MUST be announced via a polite ARIA live region without stealing focus.
- **III. Instant Response**: opening the selector and toggling a label paint their optimistic result within 16ms of the keypress, while the server remains the source of truth and reconciles (or rolls back) the mutation asynchronously within a p95 < 200ms server-mutation budget (SC-003, owned by slice 002); skeleton placeholders are permitted while server state resolves. Per Principle III, an inbound server-initiated SignalR patch to a task's labels resolves under last-write-wins but MUST yield to a pending local optimistic label toggle until that mutation's server acknowledgement resolves, then reconcile — a remote update MUST NOT clobber an in-flight local edit.
- **IV. Minimalist UI**: skeleton placeholders are permitted for the genuine network-bound initial load of label data (per Principle IV); they MUST NOT mask a label toggle whose optimistic result could be shown immediately.
- **V. Connected, Server-Authoritative**: label data is persisted server-side in PostgreSQL through the app's own API (the client holds no authoritative copy), edited via optimistic mutations that the server reconciles (SC-004, owned by slice 002); ASM-08 (documented, inspectable relational PostgreSQL schema) governs label storage. No third-party runtime data service is used; the single permitted external runtime dependency is Google OAuth for authentication only (Principle IX), never for label storage.
- **VI. Type Safety End-to-End**: Label types are generated from the schema (source of truth), with runtime validation at the label-name input and storage-deserialization boundaries; API failures surface through the ProblemDetails-based error contract (ADR-0009) modeled by the generated client.
- **VII. Data Integrity & Resilience**: FR-049 (error + recovery) and FR-050 (structured logging) cover label operation failures; FR-051 keeps the backup hook in place ahead of the schema change that adds the Label entity (with its `ownerId`) and its task association.
- **VIII. Test-First**: the owned acceptance scenario above (US-08.AS-04) is independently testable (Red-Green-Refactor); the label command/query handlers' authorization is covered by integration tests with both an allow and a deny path (Principle VIII; SC-016 mechanism) — a caller is denied acting on another user's label entity, and label-on-task operations are denied when the task's dispatch (ownership for personal, membership+role for shared) is not satisfied.
- **IX. Authentication & Authorization**: this slice authorizes its operations deny-by-default at the API/handler layer (FR-068), dispatched by the containing resource's visibility — not a conjunction of tiers. The Label entity is per-user, so label CRUD authorizes on ownership (`ownerId`) with queries scoped to the caller (FR-065). APPLYING or removing a label on a task follows that task's OWN authorization: a personal/unprojected task dispatches on ownership; a shared-project task dispatches on current `ProjectMembership` + role (editor/owner to mutate, viewer read-only; FR-067), and leave/remove/unshare revokes ALL access to that project's tasks (FR-066). `createdBy`/assignee are provenance only and never a standalone grant. This is the key distinction: the per-user label entity does not contradict the task-side authorization, because the application surface inherits the task's dispatch.
- **X. Time & Timezone**: label timestamps (if any) are stored in UTC; labels carry out no date-relative computation — there is no Today/Upcoming membership, cycle boundary, or recurrence rollover surface in this slice — so the single instance reference timezone (`Europe/Warsaw`, ASM-12) governs storage uniformly without further treatment here.
- **XI. Privacy & Personal Data**: a label is per-user personal data (`ownerId`), so it falls under Principle XI's erasure obligation — the account-deletion path (US-17; cascade enumerated at FR-085, owned by slice 015) MUST include a deleted user's owned labels in its erasure, and the data-retention stance (FR-086, retained until account deletion) applies to labels.
- **XII. Security by Default**: the label name is untrusted user-authored content and MUST be output-encoded/sanitized so raw HTML injection is impossible (FR-099); the CSP and standard security response headers required in production apply to the views that render the selector.

**Known source gap (noted at slicing time):** Labels have no standalone functional requirement in product-vision.md; the capability is anchored only via the FR-002 optional-fields umbrella (at slice 005), ENT-04, and US-08.AS-04. No FR is invented here to close that gap — the entity (ENT-04) and the selector scenario (US-08.AS-04) are the realized contract for this slice, and the `L` keypress remains a member of the FR-029 list-shortcuts umbrella owned by slice 011.

## Assumptions

This slice introduces no new slice-specific assumptions. No assumption from product-vision.md (ASM-01..ASM-09) is assigned to this slice.

## Out of Scope

This slice confirms the full MVP out-of-scope boundary (OOS-01..OOS-19 from product-vision.md):

- **OOS-01**: [PROMOTED to in-scope in v3.0.0 — see US-11, US-12] Multi-user collaboration, sharing, permissions
- **OOS-02**: Cross-device sync, cloud storage
- **OOS-03**: Mobile application, PWA
- **OOS-04**: AI features (auto-categorization, summaries, suggestions)
- **OOS-05**: External integrations (calendar, Slack, GitHub, email)
- **OOS-06**: [PARTIALLY promoted in v3.0.0] In-app notifications are now in scope (US-16); push/device notifications and reminders remain out of scope.
- **OOS-07**: File attachments on tasks
- **OOS-08**: Subtasks (task nesting)
- **OOS-09**: Custom views, saved filters
- **OOS-10**: Custom theming beyond dark/light mode
- **OOS-11**: Automations (if X then Y)
- **OOS-12**: Plugin or extension system
- **OOS-13**: Email notifications
- **OOS-14**: Push/device notifications and reminders
- **OOS-15**: Presence indicators and activity/audit feed
- **OOS-16**: Anonymous/guest access and public share links
- **OOS-17**: Organizations / multi-tenancy beyond the single team, and non-Google SSO / additional identity providers
- **OOS-18**: Pending / pre-account invitations (invites are by email resolved against existing signed-in Users only)
- **OOS-19**: Per-user timezones (the instance uses a single reference timezone, ASM-12)

Also out of scope for this slice specifically (deferred to later slices): fuzzy search across labels in the command palette (US-04 / FR-032) is owned by slice 013 (command-palette-search); carrying labels forward onto generated recurring-task instances (FR-008) is owned by slice 012 (recurring-tasks); label columns in CSV/JSON export and Todoist label import mapping (FR-036, FR-038) are owned by slice 015 (data-export-import). This slice covers reusable many-to-many labels and the `L` label selector only.
