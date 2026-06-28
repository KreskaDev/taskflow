import { z } from "zod";

/**
 * Task title validation (Constitution VI: "Zod at every trust boundary").
 *
 * Mirrors the server-side FluentValidation rule: the title is trimmed, must be
 * non-empty after trimming, and may be at most 500 characters long.
 */
export const taskTitleSchema = z.string().trim().min(1).max(500);

export type TaskTitle = z.infer<typeof taskTitleSchema>;

/**
 * Create-payload validation (research R8 pairing invariant, Constitution VI
 * "Zod at every trust boundary"). The capture flow resolves a Polish date phrase
 * to a UTC instant + `has_time` (see `lib/dates.ts`), so `dueDate` is carried as a
 * resolved `Date`. Both `dueDate` and `dueHasTime` are optional, but they pair:
 * either both present (a due date) or both absent (a dateless task). `dueHasTime`
 * is meaningless without a date, and a date without `dueHasTime` is ambiguous —
 * this mirrors the server's FluentValidation pairing rule.
 */
export const createTaskSchema = z
  .object({
    title: taskTitleSchema,
    dueDate: z.date().optional(),
    dueHasTime: z.boolean().optional(),
  })
  .refine(
    (value) => (value.dueDate === undefined) === (value.dueHasTime === undefined),
    { message: "dueDate and dueHasTime must both be present or both be absent" },
  );

export type CreateTaskInput = z.infer<typeof createTaskSchema>;

/**
 * Task priority (slice 005, R2). The closed token set `P0`–`P3`, validated at the client trust
 * boundary in lockstep with the server's FluentValidation closed-set rule (Constitution VI). `null` =
 * unprioritized (a valid value — sorts last in the Today/Upcoming order). `P0` is the highest urgency.
 */
export const prioritySchema = z.enum(["P0", "P1", "P2", "P3"]).nullable();

export type Priority = z.infer<typeof prioritySchema>;

/** The maximum description length (markdown source), mirroring the server (R3). */
export const MAX_DESCRIPTION_LENGTH = 8000;

/**
 * The full task-editor payload (slice 005, AS-06/07/08, R4) — a WHOLE-OBJECT replace validated at the
 * trust boundary (Constitution VI). Mirrors the server's `EditTaskValidator`: title bounds, the closed
 * priority set, description ≤ 8000 chars, and the slice-003 due-date pairing invariant (both present or
 * both absent). `dueDate` is carried as a resolved `Date` (the `T` input resolves the Polish phrase via
 * `lib/dates.ts`); `projectId` is null for the Inbox.
 */
export const editTaskSchema = z
  .object({
    title: taskTitleSchema,
    description: z.string().trim().max(MAX_DESCRIPTION_LENGTH).nullable(),
    priority: prioritySchema,
    dueDate: z.date().nullable(),
    dueHasTime: z.boolean().nullable(),
    projectId: z.string().uuid().nullable(),
  })
  .refine((value) => (value.dueDate === null) === (value.dueHasTime === null), {
    message: "dueDate and dueHasTime must both be present or both be absent",
    path: ["dueDate"],
  });

export type EditTaskInput = z.infer<typeof editTaskSchema>;

/**
 * Assignee-set validation (slice 008, R2). A set of member user ids (uuids), no duplicates, bounded — the
 * client trust boundary mirroring the server's `SetTaskAssigneesValidator`. Membership-validity is a
 * server-side cross-row check (the picker only offers members, so the client cannot be authoritative).
 */
export const MAX_ASSIGNEES = 50;

export const assigneeSetSchema = z
  .array(z.string().uuid())
  .max(MAX_ASSIGNEES)
  .refine((ids) => new Set(ids).size === ids.length, { message: "Assignee ids must not contain duplicates" });

export type AssigneeSet = z.infer<typeof assigneeSetSchema>;
