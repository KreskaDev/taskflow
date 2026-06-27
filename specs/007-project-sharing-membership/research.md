# Research & Design Decisions: Project Sharing, Membership & Roles (slice 007)

**Input**: `spec.md`, `.specify/memory/constitution.md` (v4.0.0 — esp. Principle IX), `.specify/memory/product-vision.md`, `docs/architecture/adr-0003-domain-model.md` (normative), and the slice-004 substrate (the `Project` aggregate + the reserved-but-unwritable `shared` visibility value + the ownership authorization branch).

This slice is the **origin of the shared-project authorization branch** (Constitution Principle IX). It realizes the reserved `shared` visibility value, introduces the **`ProjectMembership`** entity (ENT-07) + roles, the **transfer-owner** command (FR-094), and the **membership + role** authorization branch that slices 005/008/009/016/017 build on. It **adds a table** (`project_memberships`) — so, like slice 004, **FR-051 backup-before-migration is LIVE** (R15), contrasting the named no-ops of slices 003/005. The format mirrors slice 004: each decision is **Decision / Rationale / Alternatives considered**, cross-referenced from `plan.md`, `data-model.md`, and `contracts/openapi.yaml`.

**Reference identity for examples**: an authenticated, admitted owner `A` of a personal project `P`; admitted Users `B`, `C` (members after invite); a non-member admitted User `X`. Role tokens are lowercase `owner | editor | viewer`; visibility values `personal | shared`.

**Authoritative-source note**: where the SCOUT DIGEST (context) and **ADR-0003** disagree on the role model, **ADR-0003 is normative** and wins (the digest is explicitly non-authoritative context). See R2.

---

## R1 — `ProjectMembership` (ENT-07): a new `project_memberships` table, **owned by the Project aggregate**

**Decision**: Introduce a `ProjectMembership` entity with its own table `project_memberships` — one row per `(project_id, user_id)` carrying a `role` and timestamps — **logically owned by the `Project` aggregate** (ADR-0003: the membership set is "that project's sharing state and changes transactionally with the project"). Physically it is a separate table with FKs `project_id → projects(id)` and `user_id → users(id)`; there is **no EF navigation collection** on `Project` (the slice-004 `ProjectConfiguration` deliberately has no nav props — data-model.md). The aggregate boundary is honored by **loading and mutating the membership set transactionally with the project** via a new `IProjectMembershipRepository` (mirroring `IProjectRepository`), and by the one-aggregate-per-transaction discipline (ADR-0003 Decision 1). The application-layer **Authorization policy consumes** this set to decide access (Constitution Architecture note; Principle IX).

**Rationale**: ADR-0003 places membership inside the Project's consistency boundary (sharing state changes atomically with `visibility`/`ownerId`), but the slice-004 persistence style is **ID-only references, no object-graph navigation** (ADR-0003: contexts/aggregates reference each other by ID). A separate table loaded by a repository satisfies *both*: transactional co-mutation (the aggregate boundary) without an EF collection nav (the persistence style). The Identity & Access policy reading a membership set it does not own matches "the policy consumes `ProjectMembership` (owned by the Project aggregate)" (adr-0003:52, 95).

**Alternatives considered**: (a) An EF owned-collection / navigation property on `Project` — rejected; contradicts the established no-nav-prop persistence style and would eagerly load memberships on every project read (the sidebar tree query touches every project; most are personal with zero memberships). (b) A standalone `Membership` aggregate root in Identity & Access — rejected; ADR-0003 explicitly models membership as Project sharing state, not an independent aggregate, so its invariants (last-owner, role ∈ editor/viewer, exists-only-while-shared) co-locate with the Project.

---

## R2 — The role model: **owner is the immutable `ownerId` (derived, never a stored row); the stored membership role enum is `editor | viewer`**

**Decision**: Distinguish two things that the spec prose blurs:
- **Stored role** — `project_memberships.role` is a closed enum of exactly **`editor | viewer`**. The owner has **no membership row** (ADR-0003: "owner is not a membership role … Membership entries … carry roles editor/viewer only", adr-0003:92, 127-128).
- **Effective (composed) role** — what the Authorization policy resolves and the members read-model surfaces is **`owner | editor | viewer`**, where `owner` is **derived from `Project.ownerId == caller`** (no row lookup), and `editor`/`viewer` come from the caller's membership row. A caller who is neither the owner nor a row-holder is a **non-member** (effective role = none).

`owner` is therefore **not a freely assignable value** anywhere in the contract: it never appears as a writable `role` in invite or change-role payloads, and it never exists as a stored enum value. Ownership moves **only** via the transfer-owner command (R6, FR-094).

**Rationale**: This is the load-bearing consistency decision — it propagates into `data-model.md` (the `role` CHECK/enum), `openapi.yaml` (the `MembershipRole` schema for writes vs the `EffectiveRole` schema for reads), and every authorization test. Modeling `owner` as a stored role value would reintroduce exactly the multi-owner / "promote to owner" mess the single-immutable-owner model (ADR-0003 Decision 7: "a project always has exactly one owner") exists to prevent, and would contradict FR-061/FR-062 ("owner is the immutable `ownerId` and is NOT a freely assignable role … never by promoting a member to an 'owner' role"). Where the SCOUT DIGEST's consistency-token list and the spec's ENT-07 one-liner say "role (owner/editor/viewer)", they describe the **effective** set; ADR-0003 is normative on the **stored** set.

**Alternatives considered**: (a) Store the owner as a `role='owner'` row — rejected (above); breaks the single-owner invariant and the last-owner guard's simplicity (R7). (b) A single 3-value enum used for both storage and the wire — rejected; it would make `owner` a syntactically valid invite/change-role input that the handler must then reject at runtime, instead of making the illegal state unrepresentable in the write schema.

---

## R3 — Share / Unshare: `personal ↔ shared` reversibly; on re-personalize, memberships drop, **owner + tasks retained**

**Decision**: Two owner-only commands flip `Project.Visibility` (now writable — the slice-004 column was reserved at `personal` only, R11/R15):
- **`ShareProject`** — `personal → shared`. Adds a new `Share(utcNow)` behavior method to the `Project` aggregate (the first legal write of `Visibility = "shared"`). A freshly shared project has **zero membership rows** (the owner is implicit via `ownerId`); members are added by separate invites (R4). Sharing does **not** require an initial invite (AS-01 chains share-then-invite in the UI, but they are distinct operations).
- **`UnshareProject`** — `shared → personal` (FR-058). Adds an `Unshare(utcNow)` method that flips `Visibility` back to `personal` and **removes all membership rows** in the same transaction. The **owner (`ownerId`) is retained**; the project's **tasks are preserved** (only membership state and non-owner assignments are affected — FR-059). Raises the `ProjectUnshared` domain event (R13).

Both are confirmation-gated (R12); both bump `Project.Version` (R11).

**Rationale**: FR-057/FR-058 mandate a reversible round-trip. Re-personalizing is the inverse of sharing: the project returns to exactly its slice-004 personal shape (owner-only, ownership authorization branch), with its task rows untouched. Removing membership rows on unshare (rather than tombstoning them) is correct because memberships "exist only while `visibility = shared`" (ADR-0003 invariant, adr-0003:127); a later re-share starts clean. The assignment-clearing half of FR-059 is a downstream event effect, **vacuous in this slice** (R13 — `Task.assignees` arrives in slice 008).

**Alternatives considered**: (a) Soft-delete membership rows on unshare (keep tombstones for re-share) — rejected; memberships are sharing state, not user content with an undo window, and re-share is a deliberate fresh invite flow (FR-064 — no undo). (b) Block unshare while members remain (force remove-all first) — rejected; FR-058 makes unshare a single owner action whose blast radius the confirmation dialog already states (Principle VII).

---

## R4 — Invite by email: resolved against **existing admitted Users only**; unknown email → 422, no new code (OOS-18)

**Decision**: `InviteMember` (owner-only, on a `shared` project) takes an **email** and an assignable `role ∈ {editor, viewer}` (R2). The handler resolves the email **server-side** against the existing admitted `users` set (exact, case-normalized match). Outcomes:
- **Resolves to a User who is not yet a member** → create a `project_memberships` row at the given role. Raises no notification this slice (notifications = slice 017).
- **Resolves to no admitted User** → reject with **422 `validation_failed`**, field-level message on `email` ("No admitted user with that email — ask them to sign in once first", FR-049). **No** pending/pre-account record is created (OOS-18).
- **Resolves to the owner, or to an already-existing member** → **422 `validation_failed`** with a distinct field message ("already the owner" / "already a member — change their role instead"). Not a new code; not a silent no-op.

Self-invite (owner inviting their own email) collapses into the "already the owner" case.

**Rationale**: OOS-18 fixes invites to existing signed-in Users — there is no invitation entity, only membership creation against a resolved User id. The "ask them to sign in once first" message is the spec's explicit FR-049 recovery (spec:90). Reusing `validation_failed` (with the human text in the field-`errors` envelope) keeps the error contract unchanged (R16), mirroring slice-004's R12 posture.

**On the non-disclosure rationale (Constitution XII)**: the *generic* recovery message does reveal that a given email has not onboarded — but the discloser is an **admitted, authenticated insider** on a ~10-person single-team instance (ASM-13), and Constitution XII's non-disclosure protects the **membership boundary** (never leaking *another shared project's* data or membership across users), not whether an inviter can tell that an email they typed hasn't signed in yet. The boundary we must not cross is leaking project/membership existence to a **non-member** (R9, the 404 posture) — invite resolution runs only for the owner of the project in question, so it discloses nothing across that boundary. The malformed-email and unknown-email cases share the same 422 field-error shape, so there is no finer enumeration oracle than "this email is not an admitted member."

**Alternatives considered**: (a) A dedicated `unknown_user` / `not_admitted` code — rejected; `not_admitted` is the **sign-in** admission gate (Principle IX), not an invite outcome, and a new code would grow `ErrorCodes` + `ERROR_UX` for a message the 422 field-error already carries (R16). (b) Pending invitations keyed by email (email arrives → auto-join) — rejected; OOS-18 explicitly out of scope.

---

## R5 — Change role / Remove member / Leave: three commands over the membership set

**Decision**: Three confirmation-gated (R12) commands mutate the membership set, each carrying the **Project `version`** token (R11):
- **`ChangeMemberRole`** (owner-only) — sets a member's row `role` to the *other* assignable value (`editor ↔ viewer`, R2). The target must be a **current member row** (not the owner — guarded, R7). On a **demotion** (`editor → viewer`) it raises **`MembershipRevoked`** through the outbox (ADR-0003:174 — a demotion revokes the editor capability), the authority signal slices 008 (clear the demoted member's now-illegal assignments) and 016 (live re-auth) consume **without modifying this command** (the additive-seam promise, R13). A **promotion** (`viewer → editor`) is access-additive → no revoke event.
- **`RemoveMember`** (owner-only) — deletes a member's row; the removed user **loses ALL access** to the project's data immediately (R10). Raises `MembershipRevoked` (R13). The assignment-clearing half of FR-062 is a vacuous-this-slice event effect (R13).
- **`LeaveProject`** (any **non-owner** member, self-service) — deletes the caller's own row; same revoke-all + `MembershipRevoked` as remove. The owner attempting to leave hits the last-owner guard (R7).

**Rationale**: These are the AS-02/AS-04/AS-05 surfaces. `ChangeMemberRole` is a toggle within `{editor, viewer}` because `owner` is unreachable as a role (R2) — promotion to owner is the transfer command (R6). `RemoveMember` and `LeaveProject` are the same membership-row deletion differing only in **who is authorized** (owner targeting another vs member targeting self), so they share the `MembershipRevoked` event and the revoke-all semantics (R10).

**Alternatives considered**: (a) A single `SetMemberRole(role)` that also accepts the *same* role (idempotent) — acceptable but folded into the toggle for the two-value enum; documented as set-to-target, not strictly toggle, so a stale UI sending the current value is a no-op + version bump rather than an error. (b) Merge remove + leave into one command discriminated by a body flag — rejected; they have **different authorization** (owner-only vs self-only) and merging would obscure the deny-test matrix (SC-016).

---

## R6 — Transfer ownership (FR-094): moves the immutable `ownerId`, **demotes prior owner to `editor`**

**Decision**: `TransferOwnership` (owner-only) reassigns `Project.ownerId` to a **named current member** — the only legal mutation of the otherwise-immutable `ownerId` (a new `TransferOwnerTo(UserId newOwner, utcNow)` behavior method on the aggregate). Effects, in one transaction:
1. The new owner's existing membership **row is removed** (the owner has no membership row — R2).
2. The **prior owner is demoted to `editor`** — a new `editor` membership row is inserted for them (ADR-0003 Decision 7 / `OwnerTransferred`: "the prior owner becomes an editor", adr-0003:173).
3. `Project.Version` bumps; the `OwnerTransferred` event is raised (R13).

The target **must be a current member** of the shared project (a non-member or the current owner → rejected). Confirmation-gated (R12).

**Rationale**: FR-094 requires an explicit transfer command precisely because ownership is not a freely assignable role (R2). Demoting the prior owner to `editor` (not removing them) preserves their access at the highest non-owner capability, matching ADR-0003 and the spec's AS-02 vocabulary note ("the transfer reassigns the immutable `ownerId` and demotes the prior owner to editor", spec:81). Requiring the new owner to already be a member keeps the operation a pure reassignment within the existing access set (no implicit invite).

**Alternatives considered**: (a) Transfer to any admitted User (implicit invite) — rejected; couples two operations and lets ownership land on a non-member, contradicting the membership-required access model (R10). (b) Leave the prior owner with no access after transfer — rejected; ADR-0003 mandates demotion to editor, and silently dropping the prior owner's access is a worse, surprising UX.

---

## R7 — Last-owner guard: under the single-owner model it degenerates to **"the owner cannot leave / be removed / be demoted"** — reuse `last_owner`

**Decision**: Because a project always has **exactly one owner** held in `ownerId` with **no membership row** (R2, ADR-0003 Decision 7), the spec's "last owner" guard (FR-061/FR-094) degenerates to: **any operation whose target is the `ownerId` must be rejected** unless it is the transfer command. Enforced as a **pure static guard** `EnsureNotLastOwner(project, targetUserId)` (mirroring slice-004's `EnsureNestingAllowed` — a stateless guard called by the handler), raising a recoverable **`last_owner`** error (the code already provisioned in `ErrorCodes` + `ERROR_UX`, R16) with the FR-049 recovery "transfer ownership to another member first". Enumerated surfaces:
- **`LeaveProject` where caller == `ownerId`** → `last_owner`. This is the guard's **primary real surface**, checked **before** any row lookup (the owner has no row to find, so without this explicit check the path would otherwise fall through to a confusing 404).
- **`RemoveMember` / `ChangeMemberRole` where the target == `ownerId`** → `last_owner` (not a structural 404), so the owner-as-target case yields the actionable "transfer first" message rather than an opaque not-found. (These two also have no row for the owner, so the `ownerId` check precedes the row lookup.)

**Rationale**: The single-immutable-owner model means there is never a *set* of owners to count — "last owner" is always "the owner". Making `last_owner` an explicit `ownerId`-equality check at the top of leave/remove/change-role (before the membership-row lookup) gives the user the correct recovery path (transfer) instead of a 404 that reads as "no such member". This is the precise behavior the role×operation deny matrix (SC-016) must assert for the last-owner rows.

**Alternatives considered**: (a) Let owner-targeted remove/change-role fall through to 404 (since the owner has no row) — rejected; it returns a misleading, non-actionable error for a guard the spec wants surfaced with a recovery action (FR-049). (b) A countable multi-owner model with a `>= 1 owner` invariant — rejected; contradicts ADR-0003's single-owner aggregate invariant and over-builds for the team scope.

---

## R8 — Authorization **dispatched by visibility** (FR-065): one policy entry point, two branches

**Decision**: Extend the application-layer `IResourceAuthorizationPolicy` (the slice-004 deny-by-default seam whose doc already names "membership + role — added in slice 007") with a single dispatch entry point that **branches on `Project.Visibility`** — NOT a conjunction of tiers (FR-065):
- `Visibility == "personal"` → the existing **ownership** branch: `RequireOwnership(ownerId)` (caller must equal `ownerId`).
- `Visibility == "shared"` → the new **membership + role** branch: resolve the caller's **effective role** (R2) from `ownerId` + the membership set, then require it meets the operation's `RequiredRole`.

Concretely the policy gains:
- `ResolveEffectiveRole(Project, IReadOnlyCollection<ProjectMembership>, UserId) → owner | editor | viewer | none`.
- `RequireRole(Project, memberships, caller, RequiredRole)` — dispatches on visibility internally and throws per R9.

Handlers load the project **and** (when shared) its membership set, then call one policy method; the owner is **never** taken from the wire (coerced from `ICurrentUser.Id`, slice-004 posture). Per Constitution VIII + the governance gate, **every** data handler ships an **allow** and a **deny** integration test through the real DB, and the slice ships a **role×operation deny matrix** (SC-013, SC-016).

**Rationale**: FR-065 mandates dispatch *by the containing resource's visibility*, so the seam must read `Visibility` first and pick a branch — never AND the two. Centralizing resolution in the policy (not scattered in handlers) is Constitution IX ("authorization MUST live in the application layer … not scattered ad hoc"). Slice 005 already shaped a "dispatch-by-visibility seam" leaving the membership arm as a named not-yet-realized branch; this slice realizes that arm.

**Alternatives considered**: (a) Per-handler inline membership lookups + comparisons — rejected; duplicates the dispatch logic across every shared-project handler and makes the deny matrix unverifiable in one place (SC-016). (b) A DB row-level-security / Postgres policy approach — rejected; the constitution fixes authorization in the application layer, and the dispatch-by-visibility logic (derive owner from `ownerId`, compose effective role) is not naturally a single SQL predicate.

---

## R9 — Role × operation capability matrix; **non-member → 404, insufficient-role member → 403** (FR-066/067/068)

**Decision**: The required role per operation class, and the deny shape:

| Operation class (on a `shared` project) | Required effective role | viewer | editor | owner | non-member |
|---|---|---|---|---|---|
| Read project / list its tasks / list members | `viewer`+ | allow | allow | allow | **404** |
| Write a task (create/edit/move/complete in the project) | `editor`+ | **403** | allow | allow | **404** |
| Manage: invite / change-role / remove / unshare / transfer / delete project | `owner` | **403** | **403** | allow | **404** |
| Leave the project | any non-owner member (self) | allow | allow | **`last_owner`** (R7) | **404** |

**Deny-shape rule** (the load-bearing posture):
- **Non-member** (effective role = none — neither owner nor a row-holder, including User `X` and a *removed* member) → **404 `not_found`**. Existence is **not disclosed** across the membership boundary (Constitution XII; same posture as the slice-004 ownership 404).
- **Member with insufficient role** (e.g. viewer attempting a write, editor attempting manage) → **403 `forbidden`**. The member already knows the project exists, so there is nothing to hide — the honest answer is "you lack the role" (FR-067).

**Rationale**: This is the centre of the slice (Principle IX). The 404-vs-403 split follows directly from the leak boundary: disclosure to a **non-member** is a Principle IX/XII violation (→ 404), whereas denying a **member** a higher-privilege action leaks nothing (→ 403). FR-068 makes every read *and* write deny-by-default; the matrix is the mechanically-verifiable artifact SC-016 demands.

**Alternatives considered**: (a) 403 uniformly (including non-members) — rejected; discloses the shared project's existence to outsiders. (b) 404 uniformly (including insufficient-role members) — rejected; a viewer who can read the project but gets 404 on write is misled about the project's existence and their own role, defeating the FR-049 recovery ("you have viewer access; ask the owner for editor").

---

## R10 — Revoke-ALL on leave / remove / unshare: **provenance is not access** (FR-066)

**Decision**: The moment a user's membership ends — via `LeaveProject`, `RemoveMember`, or `UnshareProject` (which ends *all* non-owner memberships) — that user **loses ALL access to that project's data**, evaluated **live** on the next request by R8/R9 (no membership row + not owner → non-member → 404). This holds **regardless of authorship or assignment**: `Task.createdBy` and (from slice 008) `assignee` are **provenance only** and confer **no standalone access** (FR-066, Constitution IX). A removed editor who *created* a task in the project can no longer read or write it.

**Rationale**: Authorization is computed from **current** membership at request time, never cached on the entity or derived from provenance — so revocation is immediate and total the instant the row is gone (or visibility flips to personal). This is the explicit FR-066 rule and the reason `createdBy`/assignee are documented everywhere as provenance, not grants. It is directly testable this slice: **removed-member-loses-access** (a former member's read → 404) is a first-class row in the deny matrix (R9, SC-016), and it is realizable for the **access** dimension now (the **assignment-clearing** dimension is the slice-008 seam — R13).

**Alternatives considered**: (a) Let a task's `createdBy` retain read access after removal — rejected; flatly contradicts FR-066 and is the exact provenance-≠-access leak the principle forbids. (b) Soft-revoke (grace window) — rejected; FR-064 makes these confirmation-gated, not undoable, so revocation is immediate.

---

## R11 — Concurrency: the **Project `version`** is the token for membership mutations (aggregate boundary)

**Decision**: Membership mutations (`ShareProject`, `UnshareProject`, `InviteMember`, `ChangeMemberRole`, `RemoveMember`, `LeaveProject`, `TransferOwnership`) carry the caller's last-seen **`Project.Version`** and bump it on success; a stale token → `VersionConflictException` → **409 `version_conflict`** (existing code). Membership **rows do not carry their own concurrency token** — they are part of the Project aggregate (R1), so the Project's version guards the whole sharing state. The members read-model (R17) surfaces the project `version` so a non-owner calling `LeaveProject` has the token.

**Rationale**: ADR-0003 models membership as sharing state that "changes transactionally with the project" — so the aggregate's single optimistic-concurrency token is the natural and correct guard (one consistency boundary, one version). For a ~10-user team where the owner is almost always the sole manager of a given project's membership, contention on the single token is negligible, and the rare concurrent-management conflict is *correctly* a 409 (two managers editing the same sharing state). This reuses the slice-002/004 `version`/`version_conflict` machinery unchanged (R16).

**Alternatives considered**: (a) A per-row `version` on `project_memberships` — rejected; two managers changing *different* members would not conflict, but it splinters the aggregate's consistency boundary (ADR-0003 wants membership changes atomic with the project) and adds a second concurrency token to the contract for a contention case that doesn't arise at team scale. (b) No optimistic concurrency on membership (last-write-wins) — rejected; loses the conflict signal the rest of the app guarantees and could silently clobber a concurrent role change.

---

## R12 — Confirmation-gated, blast-radius dialogs, **NOT under the 30s undo** (FR-064)

**Decision**: Invite, role-change, transfer, remove, leave, and unshare are **confirmation-gated** and **NOT covered by the 30-second data undo** (FR-064; the undo itself is slice 014). Each confirmation dialog **states its blast radius** (which members lose access, which assignments will be cleared — Principle VII). On the web, these mutations are therefore **non-optimistic** (or optimistic-after-confirm): the action takes effect only on the confirmed server round-trip, and there is **no undo toast** — distinct from the slice-002/004 optimistic project/task mutations. Errors (`last_owner`, `forbidden`, `validation_failed`, `version_conflict`) surface via the existing global ARIA-live announcer + `ERROR_UX` map (FR-049/FR-101). The dialogs follow the dialog focus contract (FR-101); the invite **email input** suppresses single-key shortcuts while focused (FR-031).

**Rationale**: FR-064 is explicit that these high-consequence, hard-to-reverse actions get a confirmation + blast-radius preview *instead of* the cheap undo path. Optimistically painting a removal and then rolling back would contradict "takes effect only after confirmation" and risk a flash of revoked access. The non-optimistic recipe is the deliberate departure from the slice-004 `useProjectMutations` optimistic template; a new `useMembershipMutations` family on a `['projects', id, 'members']` query key invalidates on settle rather than snapshot/rollback.

**Alternatives considered**: (a) Reuse the optimistic + 30s-undo recipe — rejected; FR-064 carves these out of the undo window precisely. (b) Optimistic with rollback but no undo toast — acceptable for low-blast actions (role change), but unify on confirm-then-apply so the blast-radius preview is always the gate (consistency + Principle VII).

---

## R13 — Cross-aggregate effects via domain events; the **assignment-clearing handler is a slice-008 seam** (vacuous here)

**Decision**: Membership mutations are **single-aggregate** (the `Project` + its membership set, one transaction — ADR-0003 Decision 1). Cross-aggregate effects are **named as domain events** raised by this slice but **consumed by later slices**:
- `ProjectShared` (R3), `ProjectUnshared` (R3), `OwnerTransferred` (R6), `MembershipRevoked` (R5/R10) — raised through the Wolverine transactional outbox.

The **"unassigned from tasks"** half of AS-04/AS-05/AS-06 / FR-059/FR-062/FR-063 is **vacuous in slice 007**: `Task.assignees` does not exist until slice 008 (task-assignment). So this slice **performs the membership mutation + revokes access** (real and testable now — removed-member → 404, R10), **raises** `MembershipRevoked`/`ProjectUnshared`, and **names** the assignment-clearing event handler as the seam that **ships with slice 008**. This slice does **not** wire that handler (there is nothing to clear) — the same "name the seam, don't build it" posture as the real-time seam (R14).

**Rationale**: Honest scoping. The access-revocation effect (the security-critical half) is fully realized and tested here; the assignment-clearing effect has no data to act on until slice 008 introduces `assignees`. Raising the events now establishes the **authority signal** (membership change) that 008 (assignment clearing), 016 (SignalR eviction), and 017 (notifications) consume — without pulling their handlers forward. Writing "007 clears assignments" would be a fiction (no column exists); writing "007 raises the event 008 consumes" is accurate.

**Alternatives considered**: (a) Defer raising the events until a consumer exists — rejected; the events are the membership-authority contract those slices were planned against (ADR-0003 lists them), and raising them now keeps 008/016/017 additive (a handler subscribes; no change to 007's commands). (b) Add `Task.assignees` here to make the clearing non-vacuous — rejected; assignment is OOS (slice 008) and would balloon this slice.

---

## R14 — Real-time re-authorization is a **named seam** (FR-095, slice 016), not built here

**Decision**: A membership/role change that revokes access means the affected user must receive no further live patches and be forced to a no-access re-sync (Constitution IX; spec edge case). The **enforcement mechanism — SignalR subscription eviction (FR-095) — is slice 016**. Slice 007 **defines membership as the authority that change consumes** (the `MembershipRevoked`/`ProjectUnshared`/`OwnerTransferred` events, R13) and **names** the re-auth seam; it builds **no** SignalR hub, eviction handler, or live transport.

**Rationale**: Mirrors the slice-004/005 discipline of naming a forward seam without pulling its machinery forward. The membership set this slice makes authoritative is exactly what slice 016's subscription re-authorization will read; defining the events now is sufficient for 016 to attach later without a change to 007.

**Alternatives considered**: Build a minimal eviction now — rejected; FR-095 and the entire real-time transport are slice 016's owned scope, and there is no live transport in the app yet to evict from.

---

## R15 — Migration `AddProjectMemberships`: **FR-051 is LIVE**; `Visibility` becomes writable

**Decision**: One EF Core migration, `AddProjectMemberships`, that creates the `project_memberships` table — `project_id → projects(id) ON DELETE CASCADE`, `user_id → users(id) ON DELETE CASCADE`, `role` (`text`, CHECK `IN ('editor','viewer')`, R2), `created_at`/`updated_at` (`timestamptz`), a **unique** `(project_id, user_id)` (whose `project_id` prefix also serves "resolve a project's members" — **no standalone `(project_id)` index**), and a single `(user_id)` index (resolve "shared projects I belong to"). No change to the `projects` table — `Visibility` is already a column (slice-004 R11); this slice only makes the **`shared` value writable** via the new `Share`/`Unshare` behavior methods (no DDL). **FR-051 backup-before-migration is LIVE** (this slice migrates) — the automatic pre-migration backup **and** the CI restore-test gate (Constitution VII) must actually run against `AddProjectMemberships`, contrasting the named no-ops of slices 003/005.

**Rationale**: A pure additive table (no rewrite of existing rows — expand/contract / forward-only). **Both** FKs `ON DELETE CASCADE`: deleting a project removes its memberships (the rows are that project's sharing state, R1), and deleting a User (account erasure, Constitution XI) removes their memberships — erasure parity with the `tasks.created_by` / `projects.owner_id` cascade posture. The unique `(project_id, user_id)` enforces "at most one membership per user per project" at the DB (a second invite of an existing member is the R4 422, but the constraint is the backstop). The owner has no row, so no constraint references `ownerId`.

**Alternatives considered**: (a) `ON DELETE RESTRICT` / `SET NULL` on the FKs — rejected; a membership row is meaningless without its project or user, so cascade is correct (no orphan rows, no nullable composite key). (b) A surrogate `id` PK vs the composite `(project_id, user_id)` PK — a surrogate `id` (UUIDv7) PK with a separate unique `(project_id, user_id)` is fine and consistent with the app's client-generated-id style; chosen for parity with other entities. (c) Defer the `(user_id)` index — rejected; the "projects I'm a member of" query (sidebar for shared projects) needs it.

---

## R16 — Error contract: **no new code** — `forbidden` + `last_owner` now *used*; reuse `validation_failed` / `not_found` / `version_conflict`

**Decision**: This slice adds **no** new `ErrorCode`. It finally **uses** two codes pre-provisioned (but unused) since slice 004:
- **`forbidden` (403)** — a member with insufficient role (R9).
- **`last_owner` (409)** — the last-owner guard (R7).

And reuses existing codes: **`not_found` (404)** for non-member/foreign/absent project ids (existence not disclosed, R9); **`validation_failed` (422)** for unknown-invite-email, self/duplicate invite, transfer-to-non-member (R4/R6); **`version_conflict` (409)** for stale membership/sharing mutations (R11). Because both `forbidden` and `last_owner` already exist in the API `ErrorCodes` array **and** the web `ERROR_UX` map (`satisfies Record<ErrorCode, ErrorUx>`), the exhaustiveness gate stays green with **no change** — same posture as slice-004 R12.

**Rationale**: Every failure shape maps onto an existing code; the human specifics (which email, which role) ride in the ProblemDetails field-`errors`. No `TaskFlowDocumentTransformer` `ErrorCodes` edit and no `ERROR_UX` growth. The `forbidden` UX copy ("You don't have access to this resource.") may want a role-specific refinement for viewer-denied-write (FR-049 actionability) — that is **copy, not a new code**.

**Alternatives considered**: A dedicated `insufficient_role` or `unknown_user` code — rejected (R4/R9); `forbidden` + the field-error message carry the distinction the client needs, and a new code costs a two-place protocol edit (transformer + `ERROR_UX`) for no behavioral gain.

---

## R17 — Read models: `ProjectResponse.role` for shared projects; a **members** read-model composing owner ∪ rows

**Decision**:
- **`ProjectResponse`** (slice-004 read model) gains a nullable **`role`** field — the **caller's effective role** (`owner | editor | viewer`) for a `shared` project, `null`/`owner` for a `personal` project (always the caller). This drives client-side UI gating (viewer sees read-only). `ownerId` and `deletedAt` remain **hidden** on the lean response (the slice-004 read-model leak rule).
- **New members read-model** — `GET /api/projects/{id}/members` returns the composed membership set: the **owner** (from `Project.ownerId`, effective role `owner`) **∪** the membership rows (`editor`/`viewer`). Each entry: `{ userId, displayName, role, isOwner }`. It **does not echo member emails** (Constitution XI privacy — invite is *by* email, but the roster need not expose addresses), and it surfaces the project **`version`** so `leave`/`change-role`/`transfer` callers have the concurrency token (R11). Readable by any current member (viewer+, R9).

**Rationale**: A shared-project member needs (a) their own role to gate the UI — cheaply on `ProjectResponse` (the security boundary remains the server, FR-068) — and (b) the roster to manage/see who's in. Composing the owner from `ownerId` (not a stored row, R2) at read time keeps the single-owner model intact in the read surface. Hiding emails on the roster respects Principle XI while invite-by-email remains the *input* path (R4). Surfacing `version` is what makes the non-owner `leave` concurrency-safe without a separate project fetch (R11).

**Alternatives considered**: (a) Expose `ownerId` directly on `ProjectResponse` for shared projects — rejected; the roster read-model is the right place for owner identity, and widening the lean response re-opens the leak-rule the slice-004 read model closed. (b) Echo member emails on the roster — rejected; unnecessary PII exposure (Principle XI) for a team that already shares displayNames.

---

## Resolved unknowns summary

| Plan unknown | Resolved by |
|---|---|
| `ProjectMembership` shape: own table vs owned collection (ADR-0003) | R1 |
| Role model: stored `editor|viewer` vs effective `owner|editor|viewer`; owner = `ownerId` | R2 |
| Convert `personal ↔ shared`; what happens to membership on re-personalize | R3 |
| Invite-by-email resolution; unknown-email leak posture (OOS-18) | R4 |
| Change-role / remove / leave command surface | R5 |
| Transfer-ownership command (FR-094); demote prior owner to editor | R6 |
| Last-owner guard; reuse `last_owner` code | R7 |
| Authorization dispatch-by-visibility entry point (FR-065) | R8 |
| Role×operation matrix; non-member 404 vs insufficient-role 403 (FR-066/067/068) | R9 |
| Revoke-ALL on leave/remove/unshare; provenance ≠ access (FR-066) | R10 |
| Concurrency token for membership mutations | R11 |
| Confirmation-gated, blast-radius, not-under-undo (FR-064) | R12 |
| Cross-aggregate events; assignment-clearing = slice-008 seam | R13 |
| Real-time re-auth seam (FR-095, slice 016) | R14 |
| Migration `AddProjectMemberships`; FR-051 now LIVE; `shared` writable | R15 |
| Error contract: no new code; `forbidden` + `last_owner` now used | R16 |
| Read models: `ProjectResponse.role`; members roster read-model | R17 |
</content>
</invoke>
