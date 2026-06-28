import { z } from "zod";

/**
 * Membership validation (slice 007, T039; Constitution VI "Zod at every trust boundary"; research R2/R4).
 *
 * Mirrors the server-side FluentValidation rules and the OpenAPI contract shapes (`InviteMemberRequest`
 * / `ChangeMemberRoleRequest` / `TransferOwnershipRequest`). The API-tier checks are load-bearing (the
 * server resolves the email and re-validates the role); these schemas keep the dialogs constrained and
 * the non-optimistic write well-formed before it leaves the page.
 */

/**
 * The WRITABLE role vocabulary (research R2): exactly `editor | viewer`. `owner` is NOT a member of
 * this enum — ownership is the immutable `ownerId`, reached only via transfer-owner — so the illegal
 * "promote to owner" state is unrepresentable in every invite / change-role payload.
 */
export const membershipRoleSchema = z.enum(["editor", "viewer"]);

/** Invite payload (`POST /api/projects/{id}/members` body): a well-formed email + an assignable role. */
export const inviteSchema = z.object({
  email: z.string().trim().min(1).max(320).email(),
  role: membershipRoleSchema,
  version: z.number(),
});

/** Change-role payload (`PATCH /api/projects/{id}/members/{userId}` body). */
export const changeRoleSchema = z.object({
  role: membershipRoleSchema,
  version: z.number(),
});

/** Transfer-ownership payload (`PATCH /api/projects/{id}/owner` body): the target member + the token. */
export const transferSchema = z.object({
  userId: z.string().uuid(),
  version: z.number(),
});

export type MembershipRole = z.infer<typeof membershipRoleSchema>;
export type InviteInput = z.infer<typeof inviteSchema>;
export type ChangeRoleInput = z.infer<typeof changeRoleSchema>;
export type TransferInput = z.infer<typeof transferSchema>;
