# TaskFlow

A keyboard-first, offline-only, single-user task manager — combining Todoist's simplicity with Linear's speed and aesthetics.

This repository holds the **spec-driven product definition** for the TaskFlow MVP. There is no application code yet; the work here is the specification layer, organized as sequential vertical slices.

## Structure

```
.specify/
  memory/
    constitution.md            # Project constitution v1.1.0 (8 principles)
    product-vision.md          # Canonical source of truth — the full MVP, with stable IDs
    archive/                   # Read-only historical snapshot of the initial monolithic draft
  templates/                   # Spec / plan / tasks templates
  feature.json                 # Active feature pointer
specs/
  001-task-capture/            # Slice 001 — keyboard capture + single list
  002-natural-language-dates/  # Slice 002 — Polish natural-language due dates
  003-project-management/      # Slice 003 — projects, nesting, archive, Inbox
  004-daily-planning/          # Slice 004 — Today/Upcoming, priorities, editor
  005-labels/                  # Slice 005 — reusable labels
  006-project-board-kanban/    # Slice 006 — Kanban board
  007-cycles/                  # Slice 007 — 2-week cycles
  008-recurring-tasks/         # Slice 008 — recurrence rules
  009-command-palette-search/  # Slice 009 — Ctrl+K palette & search
  010-undo/                    # Slice 010 — 30s undo for destructive actions
  011-data-export-import/      # Slice 011 — JSON/CSV export, import with mapping
  012-appearance-theming/      # Slice 012 — dark/light theming
docs/
  blog-orchestration-token-economics.md   # Notes on the multi-agent workflow used to build these specs
```

## How it's organized

- **`product-vision.md` is the single source of truth.** It carries the full MVP (10 user stories, 51 functional requirements, 12 edge cases, 12 success criteria, 5 entities, 9 assumptions, 12 out-of-scope items) under stable IDs.
- **Each slice in `specs/`** is an independently shippable increment whose `spec.md` opens with a **Provenance** section — a pure-ID trace anchor back to `product-vision.md` — followed by the full requirement text for the IDs that slice realizes.
- **Cross-cutting requirements** (accessibility, error handling, data integrity) are realized in every slice to which they apply; the full out-of-scope boundary is confirmed in each slice.

## Status

Specification phase. Slices are spec'd but not yet planned or implemented.
