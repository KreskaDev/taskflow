import { z } from "zod";

/**
 * Task title validation (Constitution VI: "Zod at every trust boundary").
 *
 * Mirrors the server-side FluentValidation rule: the title is trimmed, must be
 * non-empty after trimming, and may be at most 500 characters long.
 */
export const taskTitleSchema = z.string().trim().min(1).max(500);

export type TaskTitle = z.infer<typeof taskTitleSchema>;
