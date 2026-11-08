# Implementation Plan: Recurring Tasks

**Branch**: `012-recurring-tasks` | **Date**: 2026-11-08 | **Spec**: `specs/012-recurring-tasks/spec.md`

**Input**: Feature specification from `/specs/012-recurring-tasks/spec.md` (Clarifications
session 2026-11-08: undo = reopen semantics, no occurrence skipping, editor-hosted rule,
due-date guard).

## Summary

Give a task a typed **recurrence rule** (ENT-05: daily / every N days / weekdays / monthly with
end-of-month clamping) stored in the slice-002 reserved `tasks.recurrence_rule` jsonb column,
edited through a shared „Powtarzanie" field in the `E` editor and the details drawer, and
shown as a text chip on every row. Completing an instance **on or after its due day
(Europe/Warsaw)** spawns the next instance inside the same `SetTaskStatus` transaction
(FR-008 carry-forward incl. rule + anchor, membership-re-validated assignees, all owners'
labels, no cycle); completing **early** schedules a durable Wolverine message
(`SpawnRecurrenceSuccessor`) for the start of the due day, whose handler re-checks state and
no-ops otherwise (FR-009/EC-05). A `spawned_from_id` link with a partial unique index makes
"≤1 live successor per instance" a DB invariant and powers undo (reopen soft-deletes the
successor, or detaches it when already done). Clearing the due of a recurring task is refused
(`recurrence_requires_due_date`). No new endpoints; one additive migration. Decisions D1–D17:
`research.md`.

## Technical Context

**Language/Version**: TypeScript 5.7 (strict) on Next.js 15 App Router / React 19; C# on
.NET 9 (nullable + analyzers-as-errors)

**Primary Dependencies**: web — TanStack Query v5 (existing optimistic factories in
`useTaskMutations.ts`), slice-019 `ui/` catalog + `tokens.css`, `lib/timezone.ts`
(date-fns-tz, Europe/Warsaw) for the preview mirror; api — ASP.NET Core, Wolverine 6.11
(HTTP→bus, durable PG outbox, `DeliveryOptions.ScheduledTime`), EF Core/Npgsql, NodaTime 3.2
(already referenced — `WarsawDayBounds`) for the occurrence calendar

**Storage**: PostgreSQL — ONE migration `AddRecurrenceLink`: `tasks.spawned_from_id` (FK SET
NULL) + partial unique index `WHERE deleted_at IS NULL`; `tasks.recurrence_rule jsonb`
already exists (typed via a value converter now)

**Testing**: xUnit domain unit tests (`RecurrenceRuleTests` — the shared fact table) +
Testcontainers integration tests in NEW namespace `TaskFlow.IntegrationTests.Recurrence` →
dedicated ci shard (D14), frozen `TimeProvider` + tracked `SendAsync` for the deferred job
(D15); Vitest (`lib/recurrence.ts` fact table mirror, `RecurrenceField`, row chip, inventory
gate); Playwright E2E (`recurrence.spec.ts`; editor/drawer join axe ×4 and [V])

**Target Platform**: web, desktop-first ≥768px (chromium-tested); single-origin Next.js BFF →
.NET API

**Project Type**: web monorepo — `apps/web` + `apps/api`

**Performance Goals**: the done toggle keeps its one-frame optimistic paint (SC-003); the
spawn adds one insert (+ label/assignee copies) to the status write — p95 < 200 ms budget
holds at ASM-10 scale; the deferred job is one message per early completion

**Constraints**: CSP intact; tokens-only styling; Polish copy only; Lucide-only icons; every
new UI behavior needs an `INV-###` row + tagged test (019 gate); Europe/Warsaw for ALL
recurrence math via NodaTime (server) / date-fns-tz (client preview only) — FR-092; no custom
shortcuts (OOS-20); quick-add grammar untouched (Clarifications Q3); SignalR fan-out stays
with slice 016; `EditTaskRequest` becomes stricter (required `recurrenceRule`) so every
caller is forced through typecheck

**Scale/Scope**: 0 new routes, 1 widened request, 1 widened response family (TaskResponse +
3 flattened rows), 1 scheduled message + handler + queue, 1 domain value object, 1 Task
invariant + 3 guarded transitions, 1 migration; web: 1 shared field component (2 hosts),
1 pure lib, 1 chip on 2 row components, 1 toast path; ~30 integration facts, ~13 shared
occurrence facts ×2 suites, ~10 web unit specs, 1 E2E spec + axe/[V] additions

## Constitution Check

*GATE: evaluated pre-Phase-0 and re-checked post-Phase-1 — PASS (no violations; Complexity
Tracking empty). Constitution v5.1.0 (the spec's compliance section is aligned to it in this
planning pass).*

| Principle | Verdict | How the design complies |
|---|---|---|
| I. UI-First Operability | PASS | The rule is a visible labelled field in the editor and the drawer (both reachable by click: „Edytuj"/`E` on daily views, „Szczegóły i komentarze" everywhere); completion/undo/cancel are the existing visible checkbox/menu actions; nothing is shortcut-only |
| II. Accessibility (WCAG 2.1 AA) | PASS | Native `<select>`/checkbox/number controls with labels; chip is icon + TEXT (never icon/color-only); guard messages inline + toast; dialogs keep the 019 focus contract; editor/drawer with controls open join axe ×4; LiveRegion announces the spawn toast (FR-101); reduced-motion untouched |
| III. Instant Response | PASS | Done toggle stays optimistic (SC-003); the successor is server-authoritative and lands on settle via the existing invalidation (D13 — spec III wording aligned); rule edits ride `editTask`'s optimistic factory with rollback on 422 |
| IV. Minimalist UI | PASS | One field (shared component), one chip, one toast; no new route, menu item or chrome |
| V. Connected, Server-Authoritative | PASS | Spawn + scheduling happen in the API/PG (outbox-enrolled); the client never spawns; the web occurrence mirror is a preview only |
| VI. Type Safety End-to-End | PASS | Closed `RecurrenceRule` union typed in Domain, DTO, OpenAPI and Zod; validated at deserialization; required request field forces every caller; `spawned_from_id` stays raw `Guid?` (011 D2 lesson); migration is schema truth |
| VII. Data Integrity & Resilience | PASS | Spawn atomic with the status flip; ≤1 live successor by partial unique index; deferred job idempotent by state re-check; undo soft-deletes through the reaper path (restorable); rule never dropped silently (D9 guard, 422 with recovery copy); FR-050 structured logs on the job; FR-051 backup posture unchanged |
| VIII. Test-First | PASS | Shared fact table red first on both sides; every AS/EC in the matrix; allow+deny for every touched write; INV rows precede tests |
| IX. Authentication & Authorization | PASS | All HTTP writes keep `LoadWritableTaskAsync(Editor)` dispatch; the job runs in the predecessor's context (owner/project inherited, assignees re-validated against current membership, no notification) and is exempt from the HttpContext middleware exactly like the reapers |
| X. Time & Timezone | PASS | Occurrence math and the on/after-due boundary in Europe/Warsaw via NodaTime; timed dues keep wall-clock across DST (`InZoneLeniently`); date-only keeps the Warsaw-midnight convention; `TimeProvider` injected (freezable) |
| XI. Privacy & Personal Data | PASS | No PII in the rule; successors are ordinary tasks under the existing deletion cascade (`created_by` CASCADE; the self-FK SET NULL) |
| XII. Security by Default | PASS | Rule input validated at the boundary (closed set, ranges); copied text fields are already output-sanitized where rendered; no new secrets/headers |

**Post-design re-check (after Phase 1)**: no artifact introduced a violation — no new
project/dependency/route; one additive migration; one durable queue in the established
pattern. Complexity Tracking empty.

## Project Structure

### Documentation (this feature)

```text
specs/012-recurring-tasks/
├── plan.md                    # This file
├── research.md                # Phase 0 — decisions D1–D17
├── data-model.md              # Phase 1 — RecurrenceRule, Task additions, instance-pair lifecycle, successor matrix
├── quickstart.md              # Phase 1 — validation scenarios + commands
├── contracts/
│   ├── recurrence-api.md      # EditTask/Reschedule/SetTaskStatus deltas, scheduled message, fact table, test matrix
│   └── ui-recurrence.md       # RecurrenceField, row chip, completion feedback, INV + determinism notes
└── tasks.md                   # Phase 2 (/speckit-tasks — NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
apps/web/src/
├── lib/recurrence.ts                       # NEW — zod rule schema, nextOccurrence (preview mirror), recurrenceLabel (pure, unit-tested)
├── lib/validation/task.ts                  # editTaskSchema + recurrenceRule
├── lib/api/client.ts                       # ERROR_UX + recurrence_requires_due_date
├── hooks/useTaskMutations.ts               # editTask carries recurrenceRule; toggleDone settle → spawn toast
├── components/tasks/RecurrenceField.tsx    # NEW (+ .module.css) — shared field (D10)
├── components/tasks/TaskEditor.tsx         # + RecurrenceField, clear-due guard
├── components/tasks/TaskDrawer.tsx         # + RecurrenceField, reschedule-to-null guard, passes rule through editTask
├── components/tasks/TaskRow.tsx            # + recurrence chip (Repeat icon + label)
├── components/tasks/BoardCard.tsx          # + recurrence chip
└── tests/  unit/ (recurrence lib fact table, RecurrenceField, row chip, inventory gate) · e2e/recurrence.spec.ts · axe/[V] additions

apps/api/src/
├── TaskFlow.Domain/TaskManagement/RecurrenceRule.cs           # NEW value object + NextDueAfter (NodaTime)
├── TaskFlow.Domain/TaskManagement/Task.cs                     # typed RecurrenceRule, SpawnedFromId, SetRecurrence, SpawnSuccessor, DetachSuccessorLink, D9 guard
├── TaskFlow.Domain/TaskManagement/Events/SpawnRecurrenceSuccessor.cs   # NEW scheduled message
├── TaskFlow.Domain/TaskManagement/RecurrenceRequiresDueDateException.cs # NEW (→ 422)
├── TaskFlow.Application/TaskManagement/Commands/SetTaskStatus.cs       # spawn-now / schedule / undo (D4, D6); TimeProvider
├── TaskFlow.Application/TaskManagement/Recurrence/                     # NEW — RecurrenceSuccessor builder (D5), SpawnRecurrenceSuccessorHandler (D7), RecurrenceRuleDto + mapping
├── TaskFlow.Application/TaskManagement/EditTask.cs, EditTaskRequest.cs # + RecurrenceRule (D11)
├── TaskFlow.Application/TaskManagement/RescheduleDueDate.cs            # D9 guard surfaces
├── TaskFlow.Application/TaskManagement/AdjacentPosition.cs             # NEW — extracted from DuplicateTask (D5)
├── TaskFlow.Application/TaskManagement/TaskResponse.cs + flattened rows # + recurrenceRule (D12)
├── TaskFlow.Application/TaskManagement/Labels/ITaskLabelRepository.cs  # + CopyAllAsync
├── TaskFlow.Infrastructure/Persistence/                                # value converter, TaskLabelRepository.CopyAllAsync, AddRecurrenceLink migration, ProblemDetails mapping
└── TaskFlow.Api/Program.cs                                             # recurrence-spawn queue + auth exemption
    TaskFlow.Api/OpenApi/TaskFlowDocumentTransformer.cs                 # + recurrence_requires_due_date (D16)

apps/api/tests/TaskFlow.UnitTests/Domain/TaskManagement/RecurrenceRuleTests.cs   # NEW — fact table
apps/api/tests/TaskFlow.IntegrationTests/Recurrence/                             # NEW namespace → ci shard (D14)
.github/workflows/ci.yml                                                         # + recurrence shard; identity-labels-infra complement
specs/019-ui-design-system/feature-inventory.md                                  # + slice-012 INV section (gate input)
apps/web/src/lib/api/generated/schema.d.ts                                       # regenerated (openapi-sync)
```

**Structure Decision**: existing web monorepo, no new projects. Recurrence stays inside the
TaskManagement bounded context (a Task attribute + one message); UI additions are a shared
field component and a chip on the existing row catalog; all styling co-located CSS Modules
on semantic tokens.

## Complexity Tracking

No constitution violations to justify — table intentionally empty.
