# TaskFlow — Product Vision

**Status:** Source of truth for all active feature specs.
**Scope:** Full MVP product, sliced into sequential implementation specs (see `specs/`).
**Mutability:** Canonical. Changes require re-slicing of active specs that reference modified IDs.
**ID convention:** Stable IDs (US-NN, US-NN.AS-NN, FR-NNN, EC-NN, SC-NNN, ENT-NN, ASM-NN, OOS-NN) are the trace anchor between this document and slice specs. Acceptance scenarios are numbered positionally within each user story: scenario *k* under US-NN is US-NN.AS-0k.

---

## 1. Product Pitch

TaskFlow MVP — core task management application combining Todoist simplicity with Linear speed and aesthetics. Keyboard-first, single-user, web-based (connected client + backend).

---

## 2. User Stories (US-01..US-10 + acceptance scenarios)

### US-01 — Daily Task Capture (Priority: P1)

User opens the application and immediately captures a new task using only the keyboard. They press `C`, type a task title with optional natural language date (e.g., "Kupic mleko po 17"), and press Enter. The task lands in the Inbox with the parsed due date. The entire flow completes in under 3 seconds without touching the mouse.

**Why this priority**: Capture is the most frequent action in any task manager. If adding a task is slow or friction-heavy, users abandon the tool. This is the atomic unit of value.

**Independent Test**: Can be fully tested by launching the app, pressing `C`, typing a task with a date phrase, pressing Enter, and verifying the task appears in Inbox with the correct due date.

**Acceptance Scenarios**:

1. **Given** the app is open on any view, **When** user presses `C`, **Then** a task creation input appears with focus on the title field within 16ms of keypress.
2. **Given** the task creation input is focused, **When** user types "Kupic mleko po 17" and presses Enter, **Then** a task titled "Kupic mleko" is created in Inbox with today's due date at 17:00.
3. **Given** the task creation input is focused, **When** user types "Raport jutro" and presses Enter, **Then** a task titled "Raport" is created with due date set to tomorrow.
4. **Given** the task creation input is focused, **When** user types "Meeting piatek" and presses Enter, **Then** a task titled "Meeting" is created with due date set to the next occurring Friday.
5. **Given** the task creation input is focused, **When** user types "Zakupy za 3 dni" and presses Enter, **Then** a task titled "Zakupy" is created with due date 3 days from now.
6. **Given** the task creation input is focused, **When** user types a task title without any date expression and presses Enter, **Then** the task is created with no due date.
7. **Given** the task creation input is focused, **When** user presses Esc, **Then** creation is cancelled, no task is created, and focus returns to the previous view.

---

### US-02 — Daily Planning Session (Priority: P1)

User opens the "Today" view to see all tasks due today, grouped by project and sorted by priority. Using only keyboard shortcuts, they navigate between tasks, change priorities, edit details, reschedule, and mark tasks as done.

**Why this priority**: Daily planning is the second core loop after capture. Users need a reliable, fast way to review and triage their day.

**Independent Test**: Can be tested by creating several tasks with today's due date across different projects and priorities, navigating to Today view, and performing triage operations entirely via keyboard.

**Acceptance Scenarios**:

1. **Given** the user is on any view, **When** they press `G T`, **Then** the Today view opens showing only tasks with due date equal to today.
2. **Given** the Today view is open with tasks from multiple projects, **When** the view renders, **Then** tasks are grouped by project and sorted by priority (P0 first) within each group.
3. **Given** the Today view is open with a task selected, **When** user presses `Space`, **Then** the task status toggles to "done" and `completed_at` is recorded.
4. **Given** the Today view is open with a task selected, **When** user presses `1`, **Then** the task priority changes to P0 with immediate visual feedback.
5. **Given** the Today view is open with a task selected, **When** user presses `T`, **Then** a date picker/input appears; user types "jutro" and presses Enter; the task's due date changes to tomorrow and it disappears from Today view.
6. **Given** the Today view is open with a task selected, **When** user presses `E`, **Then** a task editor opens with the title field focused, allowing inline editing.
7. **Given** the task editor is open, **When** user presses `Ctrl+Enter`, **Then** changes are saved and the editor closes.
8. **Given** the task editor is open, **When** user presses `Esc`, **Then** changes are discarded and the editor closes.

---

### US-03 — Project Kanban Workflow (Priority: P2)

User navigates to a specific project and views tasks on a Kanban board with columns for Backlog, Todo, In Progress, and Done. They move tasks between columns using keyboard arrows and manage the project workflow visually.

**Why this priority**: Project-level organization is essential for users managing work beyond simple daily lists, but depends on core task and project entities being functional first.

**Independent Test**: Can be tested by creating a project, adding tasks to it with different statuses, opening the project Board view, and moving tasks between columns using arrow keys.

**Acceptance Scenarios**:

1. **Given** user is on any view, **When** they press `G P`, **Then** a project list appears for selection.
2. **Given** the project list is open, **When** user selects a project, **Then** the project view opens in the last-used mode (List or Board).
3. **Given** the project Board view is open, **When** the view renders, **Then** tasks are displayed in columns: Backlog, Todo, In Progress, Done.
4. **Given** a task is selected on the Board view, **When** user presses right arrow, **Then** the task moves one column to the right (e.g., Todo to In Progress) and its status updates accordingly.
5. **Given** a task is in the Done column, **When** user presses right arrow, **Then** nothing happens (Done is the last column).
6. **Given** a task is selected on the Board view, **When** user presses left arrow, **Then** the task moves one column to the left.
7. **Given** the project List view is open, **When** the view renders, **Then** tasks are displayed as a flat list, groupable by cycle, status, or priority via a group-by control.

---

### US-04 — Command Palette & Search (Priority: P2)

User presses `Ctrl+K` to open a command palette that provides fuzzy search across tasks, projects, labels, and actions. They can instantly find and navigate to any item or execute any action.

**Why this priority**: The command palette is the central navigation hub for keyboard-first users. It ties all features together and enables power-user speed.

**Independent Test**: Can be tested by creating tasks, projects, and labels with known names, opening the command palette, typing partial matches, and verifying results appear with correct navigation.

**Acceptance Scenarios**:

1. **Given** the app is open on any view, **When** user presses `Ctrl+K`, **Then** a command palette overlay appears with a focused search input within 16ms.
2. **Given** the command palette is open, **When** user types "mleko", **Then** matching tasks (by title or description) appear in the results list in under 50ms.
3. **Given** the command palette shows task results, **When** user selects a task and presses Enter, **Then** the app navigates to that task's location (project or Inbox).
4. **Given** the command palette is open, **When** user types "create project", **Then** the "Create Project" action appears in results with its keyboard shortcut displayed.
5. **Given** the command palette is open, **When** user types a project name, **Then** matching projects appear and selecting one navigates to that project's view.
6. **Given** the command palette is open, **When** user presses Esc, **Then** the palette closes and focus returns to the previous view.
7. **Given** the app has 10,000+ tasks, **When** user types in the command palette, **Then** results appear in under 50ms.

---

### US-05 — Cycle Management & Review (Priority: P3)

User works with 2-week cycles (sprints). They assign tasks to the current cycle, track progress via metrics (% done, days remaining, status breakdown), and at cycle end, roll unfinished tasks to the next cycle or back to backlog.

**Why this priority**: Cycles add structured time-boxing on top of basic task management. Valuable for disciplined planning but not required for basic daily use.

**Independent Test**: Can be tested by creating a cycle, assigning tasks, completing some, closing the cycle, and verifying rollover behavior.

**Acceptance Scenarios**:

1. **Given** the user is on any view with a task selected, **When** they press `#`, **Then** a cycle selector appears listing available cycles and a "backlog" option.
2. **Given** the cycle selector is open, **When** user selects a cycle, **Then** the task is assigned to that cycle.
3. **Given** the user navigates to the current cycle view (`G C`), **When** the view renders, **Then** it shows: percentage of tasks done, days remaining in the cycle, and a breakdown of tasks by status.
4. **Given** a cycle has ended with unfinished tasks, **When** user opens the cycle review, **Then** they see a list of incomplete tasks with options to: move all to next cycle, move all to backlog, or handle individually.
5. **Given** the cycle review shows incomplete tasks, **When** user selects "move all to next cycle" and confirms, **Then** all incomplete tasks are reassigned to the next cycle in a single operation.
6. **Given** no next cycle exists when rollover is attempted, **When** user selects "move to next cycle", **Then** the system prompts to create a new cycle first.
7. **Given** an active cycle exists, **When** user attempts to delete it, **Then** the system prevents deletion and shows a message explaining the cycle must be closed first.

---

### US-06 — Recurring Tasks (Priority: P3)

User creates tasks that repeat on a schedule (daily, every N days, specific weekdays, monthly). When a recurring task instance is completed, the next instance is automatically generated according to the recurrence rule.

**Why this priority**: Recurring tasks are important for habits and routine work but are an enhancement to the core task model.

**Independent Test**: Can be tested by creating a recurring task, completing it, and verifying the next instance is generated with the correct due date.

**Acceptance Scenarios**:

1. **Given** a user is creating or editing a task, **When** they set a recurrence rule (e.g., "every day"), **Then** the task is marked as recurring with a visual indicator.
2. **Given** a recurring task with rule "every day" and due date today, **When** user marks it as done, **Then** a new task instance is automatically created with due date tomorrow and the same title, description, priority, labels, and project.
3. **Given** a recurring task with rule "every Monday and Wednesday" and due date Monday, **When** user marks it as done on Monday, **Then** the next instance is created with due date Wednesday.
4. **Given** a recurring task with due date tomorrow, **When** user marks it as done today (before due date), **Then** no new instance is generated yet (to avoid premature spawning).
5. **Given** a recurring task with rule "every 3 days" and due date June 10, **When** user marks it as done on June 12, **Then** the next instance is created with due date June 13 (3 days from the original due, not from completion).
6. **Given** a recurring task, **When** user cancels it (status = cancelled), **Then** no further instances are generated.

---

### US-07 — Data Export & Import (Priority: P3)

User can export all their data in JSON (lossless) and CSV (human-readable) formats, and import data from a TaskFlow JSON export or a Todoist CSV export with field mapping preview.

**Why this priority**: Data portability is a trust requirement for a connected app but is not part of daily usage flow.

**Independent Test**: Can be tested by creating sample data, exporting to JSON, clearing data, importing from JSON, and verifying all data is restored identically.

**Acceptance Scenarios**:

1. **Given** user has tasks, projects, labels, and cycles, **When** they export to JSON, **Then** a single JSON file is generated containing all entities with all fields preserved losslessly.
2. **Given** user has tasks, **When** they export to CSV, **Then** a CSV file is generated with human-readable columns (title, status, priority, due date, project name, labels, created date, completed date).
3. **Given** user has a TaskFlow JSON export file, **When** they import it, **Then** all entities are restored with all fields intact and relationships preserved.
4. **Given** user has a Todoist CSV export, **When** they start import, **Then** a preview screen shows the column mapping (Todoist projects to TaskFlow projects, Todoist p1-p4 to TaskFlow P0-P3, Todoist labels to TaskFlow labels).
5. **Given** the import preview is shown, **When** user accepts the mapping, **Then** data is imported according to the displayed mapping.
6. **Given** the Todoist CSV contains columns that cannot be mapped, **When** the preview renders, **Then** unmapped columns are clearly shown as "will not be imported" and user can proceed or cancel.
7. **Given** user imports data, **When** a conflict exists (e.g., duplicate task titles), **Then** imported items are created as new entries (no deduplication, user resolves manually).

---

### US-08 — Keyboard Navigation & Shortcuts Across All Views (Priority: P1)

User navigates the entire application using keyboard shortcuts. Global shortcuts work from any view, navigation shortcuts switch between views, and contextual shortcuts operate on the currently selected item.

**Why this priority**: Keyboard-first is the core principle. Without complete keyboard coverage, the app fails its primary promise.

**Independent Test**: Can be tested by navigating to every view, performing every action, and verifying no operation requires a mouse.

**Acceptance Scenarios**:

1. **Given** the app is open, **When** user presses `G I`, **Then** the Inbox view opens.
2. **Given** the app is open, **When** user presses `G U`, **Then** the Upcoming view opens showing tasks for the next 7 days grouped by day.
3. **Given** any list view is open, **When** user presses up/down arrows, **Then** selection moves between tasks with visible focus indicator.
4. **Given** a task is selected, **When** user presses `L`, **Then** a label selector appears for adding/removing labels.
5. **Given** a task is selected, **When** user presses `M`, **Then** a project selector appears for moving the task to a different project.
6. **Given** a task is selected, **When** user presses `Del`, **Then** the task is deleted with a 30-second undo toast notification.
7. **Given** the app is open, **When** user presses `?`, **Then** a keyboard shortcuts help overlay appears listing all available shortcuts.
8. **Given** the app is open, **When** user presses `/`, **Then** a search input is focused for filtering the current view.
9. **Given** the user is typing in a text input (task title, search, etc.), **When** they press a shortcut key (e.g., `C`, `E`, `1`), **Then** the character is typed into the input, not interpreted as a shortcut.

---

### US-09 — Undo Destructive Actions (Priority: P2)

When user performs a destructive action (delete task, delete project, bulk move), the system provides a 30-second undo window via a toast notification. Pressing undo fully restores the previous state.

**Why this priority**: Undo is essential for a keyboard-first app where rapid actions increase the chance of mistakes.

**Independent Test**: Can be tested by deleting a task, verifying the undo toast appears, pressing undo within 30 seconds, and verifying the task is fully restored.

**Acceptance Scenarios**:

1. **Given** a user deletes a task, **When** the deletion executes, **Then** a toast notification appears with an "Undo" action and a 30-second countdown.
2. **Given** the undo toast is visible, **When** user clicks or keyboard-activates "Undo" within 30 seconds, **Then** the task is fully restored to its previous state including all fields, project assignment, and cycle assignment.
3. **Given** the undo toast is visible, **When** 30 seconds elapse without user action, **Then** the toast disappears and the deletion becomes permanent.
4. **Given** a user deletes a project containing tasks, **When** they choose "move tasks to Inbox" and then undo, **Then** the project is restored and all tasks are moved back to the project.
5. **Given** a user performs a bulk move of tasks between cycles, **When** they undo, **Then** all moved tasks return to their original cycle assignments.

---

### US-10 — Project Management (Priority: P2)

User creates, edits, archives, and organizes projects with one level of nesting (parent/child). Each project has a color and icon from a preset. Archived projects disappear from default views but remain searchable.

**Why this priority**: Projects are the organizational backbone for grouping tasks beyond the Inbox.

**Independent Test**: Can be tested by creating parent and child projects, assigning tasks, archiving a project, and verifying it disappears from the sidebar but remains in search.

**Acceptance Scenarios**:

1. **Given** the user wants to create a project, **When** they use the command palette or dedicated action, **Then** a project creation form appears with fields for name, color (preset), icon (preset), and optional parent project.
2. **Given** a parent project exists, **When** user creates a child project under it, **Then** the child appears nested under the parent in the sidebar (max 1 level).
3. **Given** a user tries to create a grandchild project (child of a child), **When** they attempt to set a child project as parent, **Then** the system prevents it and shows a message that only one level of nesting is allowed.
4. **Given** a project has tasks assigned to it, **When** user chooses to delete the project, **Then** a dialog asks: delete tasks cascading, move tasks to Inbox, or archive the project with its tasks.
5. **Given** a project is archived, **When** user views the sidebar and default project list, **Then** the archived project is not visible.
6. **Given** a project is archived, **When** user searches for it in the command palette, **Then** it appears in results and can be unarchived.

---

## 3. Functional Requirements (FR-001..FR-051)

### Task Management
- **FR-001**: System MUST allow creating a task with a mandatory title field.
- **FR-002**: System MUST support optional task fields: description (markdown), priority (P0/P1/P2/P3), due date, labels (multiple), and project assignment.
- **FR-003**: System MUST track task status as one of: backlog, todo, in_progress, done, cancelled. New tasks MUST default to "backlog" status.
- **FR-004**: System MUST automatically record `created_at`, `updated_at`, and `completed_at` timestamps on tasks.
- **FR-005**: System MUST parse natural language date expressions in Polish ("jutro", "piatek", "za 3 dni", "po 17", "30.06") and set the due date accordingly.
- **FR-006**: When the natural language date parser cannot interpret input, the system MUST retain the previous date value and display a red error message "nie rozpoznano" below the date field.
- **FR-007**: System MUST support recurring tasks with the following schedules: daily, every N days, specific weekdays, monthly.
- **FR-008**: When a recurring task instance is marked as done (on or after its due date), the system MUST automatically generate the next instance with the appropriate due date according to the recurrence rule. The new instance MUST carry forward all fields from the completed instance (title, description, priority, labels, project) except: status (reset to backlog), timestamps (new created_at, no completed_at), and cycle assignment (new instance is unassigned to any cycle — the user assigns it manually).
- **FR-009**: When a recurring task instance is marked as done before its due date, the system MUST NOT generate the next instance immediately. The next instance MUST be generated when the application is opened on or after the original due date (checked at application startup and periodically while running).
- **FR-010**: When a recurring task is cancelled, the system MUST stop generating further instances.

### Project Management
- **FR-011**: System MUST allow creating projects with a name, color (from preset), and icon (from preset).
- **FR-012**: System MUST support one level of project nesting (parent-child) and MUST prevent creation of grandchild projects.
- **FR-013**: System MUST allow archiving projects, which hides them from default views while keeping them accessible via search.
- **FR-014**: When deleting a project that contains tasks, the system MUST prompt the user with three options: cascade delete, move tasks to Inbox, or archive the project with its tasks.

### Cycle Management
- **FR-015**: System MUST support cycles (sprints) with a default duration of 2 weeks, configurable in application settings.
- **FR-016**: Each task MUST belong to at most one cycle (or no cycle, meaning backlog).
- **FR-017**: The active cycle MUST be visible in the sidebar.
- **FR-018**: When a cycle is closed with incomplete tasks, the system MUST provide rollover options: move all to next cycle, move all to backlog (remove cycle assignment), keep all in the closed cycle with a "carried over" flag, or handle individually (per-task choice among the same three options).
- **FR-019**: System MUST prevent deletion of an active cycle; the cycle must be closed first.
- **FR-020**: System MUST allow the user to create, edit (name, start/end dates), close, and delete cycles. Active cycles cannot be deleted (must be closed first, per FR-019). Planned and closed cycles may be deleted only if they have no assigned tasks.

### Views
- **FR-021**: The Inbox view MUST show tasks not assigned to any project, sorted by newest first.
- **FR-022**: The Today view MUST show all tasks with due date equal to today, grouped by project and sorted by priority within each group.
- **FR-023**: The Upcoming view MUST show tasks for the next 7 days, grouped by day.
- **FR-024**: The Project List view MUST display a project's tasks as a flat list, groupable by cycle, status, or priority.
- **FR-025**: The Project Board view MUST display a project's tasks in a Kanban layout with columns mapping directly to statuses: Backlog (backlog), Todo (todo), In Progress (in_progress), Done (done). Tasks with status "cancelled" MUST be hidden from the Board view.
- **FR-026**: The Cycle view MUST display: percentage of tasks completed, days remaining, and breakdown by status.

### Keyboard Interactions
- **FR-027**: System MUST support all specified global shortcuts: `C` (create task), `Ctrl+K` (command palette), `/` (search), `?` (shortcuts help).
- **FR-028**: System MUST support all specified navigation shortcuts: `G I` (Inbox), `G T` (Today), `G U` (Upcoming), `G P` (Projects), `G C` (Current cycle).
- **FR-029**: System MUST support all specified list shortcuts: arrows (move), `E` (edit), `Space` (toggle done), `1-4` (priority), `T` (date), `L` (label), `M` (move to project), `#` (move to cycle), `Del` (delete).
- **FR-030**: System MUST support task editor shortcuts: `Ctrl+Enter` (save), `Esc` (cancel).
- **FR-031**: Single-key shortcuts MUST be suppressed when a text input is focused; only modifier-based shortcuts remain active during text input.

### Command Palette
- **FR-032**: The command palette MUST provide fuzzy search across tasks (title and description), projects, labels, and actions.
- **FR-033**: Each action in the command palette MUST display its assigned keyboard shortcut.
- **FR-034**: Selecting a result in the command palette MUST navigate to the item or execute the action.

### Data Management
- **FR-035**: System MUST support full data export in JSON format (lossless, all entities and fields).
- **FR-036**: System MUST support full data export in CSV format (human-readable).
- **FR-037**: System MUST support import from TaskFlow JSON export with full fidelity.
- **FR-038**: System MUST support import from Todoist CSV with best-effort mapping: projects to projects, labels to labels, priority p1 (highest) to P0 (highest), p2 to P1, p3 to P2, p4 (lowest) to P3 (lowest).
- **FR-039**: When importing CSV with unmappable columns, the system MUST show a preview with the mapping for user acceptance before proceeding.
- **FR-040**: System MUST provide a 30-second undo window for all destructive and irreversible actions, including but not limited to: task deletion, project deletion, bulk moves, bulk status changes, and cycle rollover operations.
- **FR-041**: All task data MUST be persisted server-side in PostgreSQL through the application's own API; the client holds no authoritative copy. The application MUST depend on no third-party runtime data service — only its own API and database.

### Accessibility (per Constitution Principle II)
- **FR-042**: Every focusable element MUST have a visible focus indicator.
- **FR-043**: All interactive elements MUST have correct ARIA roles and labels for screen reader compatibility.
- **FR-044**: Text contrast ratio MUST be at least 4.5:1 (3:1 for large text).
- **FR-045**: Custom keyboard shortcuts MUST NOT collide with native assistive-technology bindings.
- **FR-046**: No content may be accessible only via hover — all tooltips and popovers MUST have a keyboard/focus-triggered equivalent.
- **FR-047**: Animations MUST respect the `prefers-reduced-motion` user preference; when reduced motion is active, transitions MUST be instant or under 100ms.

### Appearance
- **FR-048**: System MUST support dark and light visual modes. The user MUST be able to switch between modes via the command palette or application settings. The system SHOULD detect and follow the operating system's color scheme preference by default.

### Error Handling (per Constitution Principle VII)
- **FR-049**: All errors MUST be presented to the user with a clear message and an actionable recovery suggestion. No operation may fail silently.
- **FR-050**: Errors MUST be logged with structured context (severity level, operation context, and error details) for debugging purposes.

### Data Integrity (per Constitution Principle VII)
- **FR-051**: Before any data migration, the system MUST automatically create a backup of user data. The user MUST be able to restore from this backup.

---

## 4. Edge Cases (EC-01..EC-12)

- **EC-01 — Empty Inbox**: When the Inbox has no tasks, a helpful empty state is shown with a hint to press `C` to create a task.
- **EC-02 — Natural language date parsing failure**: When the date parser cannot interpret the user's input, the date field retains its previous value (or remains empty for new tasks) and a red error message "nie rozpoznano" appears below the field.
- **EC-03 — Deleting a project with tasks**: User is prompted with three options: cascade delete, move to Inbox, or archive with tasks.
- **EC-04 — Deleting an active cycle**: The system prevents this; the cycle must be closed first.
- **EC-05 — Recurring task early completion**: If a recurring task is completed before its due date, no new instance is spawned immediately. The system generates the deferred instance on the next application startup on or after the due date, or during periodic checks while the app is running.
- **EC-06 — 10,000+ tasks performance**: List views use virtualization to maintain 60fps scrolling. Search returns results in under 50ms regardless of dataset size.
- **EC-07 — Import with unmappable columns**: The import preview clearly marks unmapped columns. User can proceed (ignoring them) or cancel.
- **EC-08 — Keyboard shortcuts in text inputs**: When a text input is focused, single-key shortcuts (C, E, 1-4, etc.) are treated as text input, not commands. Only modifier-based shortcuts (Ctrl+K, Ctrl+Enter) remain active.
- **EC-09 — Multiple rapid undo actions**: Each destructive action creates its own independent undo entry. Multiple undos can be active simultaneously, each with its own 30-second timer.
- **EC-10 — "Backlog" naming distinction**: The term "backlog" is used in two different contexts: (1) task status "backlog" refers to a task's workflow state on the Kanban board (first column, meaning the task has not yet been triaged into active work); (2) cycle backlog refers to tasks not assigned to any cycle. These are independent dimensions — a task can have status "todo" while being in the cycle backlog, or have status "backlog" while assigned to a cycle.
- **EC-11 — Cancelled tasks on Board view**: Tasks with status "cancelled" are not displayed on the Kanban Board. They remain accessible via the List view, search, and command palette.
- **EC-12 — Archiving a project with cycle-assigned tasks**: When archiving a project whose tasks are assigned to an active cycle, the tasks remain in the cycle but are hidden from project-based views. They remain visible in the Cycle view and can be moved to another project or unassigned.

---

## 5. Success Criteria (SC-001..SC-012)

- **SC-001**: User can perform a complete daily workflow (capture task, review today's tasks, reprioritize, reschedule, mark done) without using the mouse at any point.
- **SC-002**: Application reaches first contentful paint in under 1 second and time-to-interactive in under 2.5 seconds on a broadband connection from a warm backend.
- **SC-003**: Every user action on a task (create, edit, complete, delete, move, reprioritize) paints its optimistic result within 16ms of the triggering keypress; the server reconciles or rolls back asynchronously.
- **SC-004**: Application depends on no third-party runtime data services — only its own API and PostgreSQL database; there are no external SaaS data dependencies at runtime.
- **SC-005**: Exporting all data to JSON and re-importing produces an identical dataset with zero data loss.
- **SC-006**: All five primary user journeys (daily capture, planning session, project work, cycle review, search & command) pass end-to-end automated tests.
- **SC-007**: Codebase enforces strict type safety with no bypasses, per Constitution Principle VI.
- **SC-008**: Every main view passes automated accessibility audit at WCAG 2.1 AA level.
- **SC-009**: Fuzzy search across 10,000+ tasks returns results in under 50ms.
- **SC-010**: List views maintain smooth scrolling (60fps) with 10,000 items loaded via client-side virtualization.
- **SC-011**: Browser tab memory usage stays below 300 MB with 10,000 tasks loaded.
- **SC-012**: Server data operations (single-entity reads/writes) complete within a p95 of 200ms against a representative dataset.

---

## 6. Key Entities (ENT-01..ENT-05)

- **ENT-01 — Task**: The core work item. Has a title (required), description, priority (P0-P3), status (backlog/todo/in_progress/done/cancelled), due date, labels, project reference, cycle reference, recurrence rule, and system timestamps (created_at, updated_at, completed_at). New tasks default to "backlog" status. A recurring task has a linked recurrence rule that generates successor instances.
- **ENT-02 — Project**: An organizational container for tasks. Has a name, color, icon, optional parent project reference, and archived flag. Supports one level of nesting. Contains zero or more tasks.
- **ENT-03 — Cycle**: A time-boxed period (default 2 weeks) for organizing work. Has a start date, end date, and status (active/closed/planned). Contains zero or more tasks. Only one cycle can be active at a time. A task not assigned to any cycle is considered "in the cycle backlog" (distinct from the task status "backlog" — cycle backlog refers to cycle assignment, not workflow state). Tasks remaining in a closed cycle may carry a "carried over" flag indicating they were not completed during that cycle's timeframe.
- **ENT-04 — Label**: A tag that can be applied to multiple tasks for cross-cutting categorization. Has a name and optional color. Many-to-many relationship with tasks.
- **ENT-05 — Recurrence Rule**: Defines a repeating schedule attached to a task. Specifies frequency type (daily, every N days, specific weekdays, monthly) and parameters. Used to generate the next task instance upon completion.

---

## 7. Assumptions (ASM-01..ASM-09)

- **ASM-01 — Single user only**: The application serves exactly one user. No authentication, multi-tenancy, or sharing features are needed.
- **ASM-02 — Web platform**: The MVP targets modern desktop browsers. Native mobile apps, PWA/offline operation, and cross-device sync are explicitly out of scope.
- **ASM-03 — Polish language UI for date parsing**: Natural language date input supports Polish expressions as the primary language. Error messages related to date parsing (e.g., "nie rozpoznano") are in Polish. Additional language support may be added later but is not required for MVP.
- **ASM-04 — Preset colors and icons**: Project colors and icons are selected from a predefined set, not custom user values.
- **ASM-05 — No subtasks**: Tasks are flat entities. Only projects support hierarchy (one level). Task nesting (subtasks) is explicitly out of scope.
- **ASM-06 — No notifications**: Push notifications, reminders, and alerts are out of scope. The app is passive — the user checks it when they want to.
- **ASM-07 — Dark/light theme only**: Visual theming is limited to dark and light modes. Custom themes are out of scope.
- **ASM-08 — Data format**: The relational schema in PostgreSQL is documented and inspectable; full export/import (Principle VII) keeps user data portable, consistent with the data-sovereignty principle.
- **ASM-09 — Recurrence based on due date**: Next recurring task instance is calculated from the original due date, not the completion date, ensuring consistent scheduling.

---

## 8. Out of Scope (OOS-01..OOS-12)

The following are explicitly excluded from this MVP iteration:

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

---

## 9. Slicing Strategy

The MVP is delivered through 12 sequential vertical slices, each independently shippable. See `specs/001-task-capture` through `specs/012-appearance-theming`. Slicing rationale follows constitution Principles I (Keyboard-First), III (Instant Response), and VIII (Test-First) — small slices keep feedback loops tight and constitution-compliance verifiable per increment.

Cross-cutting requirements are realized (not merely referenced) in every slice to which their scope applies: UI accessibility (FR-031, FR-042–FR-047) in every slice that renders UI, and resilience (FR-049, FR-050, FR-051) in every slice that modifies data. The full out-of-scope boundary (OOS-01–OOS-12) is confirmed in every slice.

High-level mapping (slice → coverage):
- 001 task-capture — keyboard capture, single task list, core navigation/done/inline-rename/delete, server-side persistence, accessibility & resilience foundation
- 002 natural-language-dates — Polish natural-language due-date parsing and parser-failure UX
- 003 project-management — projects with one-level nesting, archive, Inbox definition, move-to-project
- 004 daily-planning — Today & Upcoming views, priorities, full task editor, the mouse-free daily loop
- 005 labels — reusable many-to-many labels and the label selector
- 006 project-board-kanban — project Kanban board with status columns, groupable project list
- 007 cycles — 2-week cycles, assignment, metrics, rollover, deletion guards
- 008 recurring-tasks — recurrence rules and next-instance generation
- 009 command-palette-search — Ctrl+K fuzzy search across tasks/projects/labels/actions, view filter
- 010 undo — 30-second undo window for all destructive actions
- 011 data-export-import — JSON/CSV export, TaskFlow/Todoist import with mapping preview
- 012 appearance-theming — dark/light modes following the OS color scheme

Detailed per-ID mapping is maintained in each slice's spec.md Provenance section.
