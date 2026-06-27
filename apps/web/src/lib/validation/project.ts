import { z } from "zod";

import { PROJECT_COLORS, PROJECT_ICONS } from "@/lib/projectPresets";

/**
 * Project validation (T024, Constitution VI "Zod at every trust boundary"; research R4/R10).
 *
 * Mirrors the server-side FluentValidation rules and the OpenAPI contract shapes
 * (`CreateProjectRequest` / `EditProjectRequest`). The API-tier checks are the load-bearing
 * ones (a value outside a preset set is a 422); these schemas are the web-tier convenience that
 * keeps the form constrained and the optimistic write well-formed before it ever leaves the page.
 */

/** A project name: trimmed, non-empty, ≤ 200 chars (API `MaxNameLength = 200` — NOT the task 500). */
export const projectNameSchema = z.string().trim().min(1).max(200);

/**
 * The FROZEN preset color/icon enums (ASM-04, R10). Built from the `projectPresets` tuples so the
 * two stay byte-consistent; `z.enum` yields the literal union (and rejects any out-of-set value)
 * rather than a `.refine` that would erase the inferred type. The tuples are `readonly … as const`,
 * so they are spread into a mutable copy to satisfy `z.enum`'s `[string, ...string[]]` signature.
 */
export const projectColorSchema = z.enum([...PROJECT_COLORS] as [string, ...string[]]);
export const projectIconSchema = z.enum([...PROJECT_ICONS] as [string, ...string[]]);

/**
 * The task disposition (FR-014/EC-03, R5) chosen when deleting a project that has tasks:
 * `cascade` (soft-delete the tasks too) | `move_to_inbox` (un-project them) | `archive_with_tasks`
 * (archive the project instead of deleting, keeping its tasks). Token-consistent with the API.
 */
export const taskDispositionSchema = z.enum(["cascade", "move_to_inbox", "archive_with_tasks"]);

/**
 * The child disposition (AS-10, R5) chosen when archiving/deleting a parent that has children:
 * `cascade` (children share the parent's fate) | `orphan_to_top` (promote them to top-level).
 */
export const childDispositionSchema = z.enum(["cascade", "orphan_to_top"]);

/**
 * Create payload (`PUT /api/projects/{id}` body). `parentId` is OPTIONAL — an absent parent means
 * a top-level project (one-level rule enforced cross-row in the API handler). The client-minted id
 * rides on the route, not the body, so it is not part of this schema.
 */
export const createProjectSchema = z.object({
  name: projectNameSchema,
  color: projectColorSchema,
  icon: projectIconSchema,
  parentId: z.string().uuid().nullish(),
});

/**
 * Edit payload (`PATCH /api/projects/{id}` body). WHOLE-OBJECT replace (R4): `parentId` is
 * REQUIRED-but-NULLABLE — `.nullable()` (NOT `.optional()`/`.nullish()`) so the key MUST be present.
 * A name-only edit re-sends the current parent (`null` = top-level); an OMITTED `parentId` is a
 * validation failure, never a silent un-parent. `version` is the optimistic-concurrency token.
 */
export const editProjectSchema = z.object({
  name: projectNameSchema,
  color: projectColorSchema,
  icon: projectIconSchema,
  parentId: z.string().uuid().nullable(),
  version: z.number(),
});

export type CreateProjectInput = z.infer<typeof createProjectSchema>;
export type EditProjectInput = z.infer<typeof editProjectSchema>;
export type TaskDisposition = z.infer<typeof taskDispositionSchema>;
export type ChildDisposition = z.infer<typeof childDispositionSchema>;
