import { z } from "zod";

/**
 * Label validation (slice 006, Constitution VI: "Zod at every trust boundary"). Mirrors the server-side
 * FluentValidation rules: the name is trimmed, non-empty after trimming, and ≤ 50 chars. Per-owner name
 * uniqueness and preset-color membership are server-side cross-row/closed-set checks (the client cannot be
 * authoritative — the selector offers presets and the roster, the server enforces).
 */
export const MAX_LABEL_NAME_LENGTH = 50;

export const labelNameSchema = z.string().trim().min(1).max(MAX_LABEL_NAME_LENGTH);

export type LabelName = z.infer<typeof labelNameSchema>;

/**
 * Label-set validation (slice 006, R2): a set of the caller's label ids (uuids), no duplicates, bounded —
 * the client trust boundary mirroring the server's `SetTaskLabelsValidator`. Label OWNERSHIP is a server-side
 * cross-row check (the selector only offers the caller's labels, so the client cannot be authoritative).
 */
export const MAX_LABELS_PER_TASK = 50;

export const labelSetSchema = z
  .array(z.string().uuid())
  .max(MAX_LABELS_PER_TASK)
  .refine((ids) => new Set(ids).size === ids.length, { message: "Label ids must not contain duplicates" });

export type LabelSet = z.infer<typeof labelSetSchema>;
