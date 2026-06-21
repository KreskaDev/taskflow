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
