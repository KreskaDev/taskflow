# Deny Matrix & Authorization Coverage — Slice 001 (Accounts & Auth)

Audit artifact for **T054 (SC-016)**, **T055 (SC-017)**, and **T060 (quickstart traceability)**.
All cited tests are part of the green suite (26 integration + 14 unit on the API; 8/8 e2e on the web).

---

## T054 — SC-016: role × operation allow/deny matrix

> **SC-016**: every data handler ships with *both* an allow and a deny test, and a role×operation
> deny matrix demonstrates that insufficient ownership/membership/role is rejected.

Slice 001 introduces exactly three data handlers. There are no application roles yet (single-tenant,
self-owned identity data), so the "role" axis is the **caller's authorization standing** relative to
the resource: *self / authenticated-owner* (allow) versus *unauthenticated, forged, expired,
non-existent-owner, or the anonymization tombstone* (deny). Each handler ships at least one allow and
one deny test; every deny leg additionally asserts no side effect (no row written / no row matched).

| Operation (handler) | Resource | Caller standing | Expect | Test method | File |
|---|---|---|---|---|---|
| `POST /api/users/ensure` (ensureUser) | own account bootstrap | valid carrier, first call | **Allow** 200 + server-minted id | `Allow_first_call_creates_a_fresh_account_with_a_server_generated_id` | EnsureUserTests.cs:27 |
| `POST /api/users/ensure` (ensureUser) | own account bootstrap | valid carrier, returning sub | **Allow** 200 + same row refreshed | `Allow_returning_call_matches_the_same_row_and_refreshes_the_profile` | EnsureUserTests.cs:44 |
| `POST /api/users/ensure` (ensureUser) | own account bootstrap | valid carrier, new sub | **Allow** 200 + distinct account | `Allow_a_previously_unseen_subject_creates_a_distinct_account` | EnsureUserTests.cs:67 |
| `POST /api/users/ensure` (ensureUser) | account bootstrap | **no JWT** | **Deny** 401 + envelope + **no row written** | `Deny_no_jwt_is_rejected_401_with_our_envelope_and_creates_no_account` | EnsureUserTests.cs:79 |
| `POST /api/users/ensure` (ensureUser) | account bootstrap | **forged signature** | **Deny** 401 + **no row written** | `Deny_invalid_signature_is_rejected_401` | EnsureUserTests.cs:104 |
| `GET /api/users/me` (getCurrentUser) | own profile | valid carrier = own id | **Allow** 200 + own profile | `Allow_returns_the_callers_own_profile` | GetCurrentUserTests.cs:18 |
| `GET /api/users/me` (getCurrentUser) | own profile | **no JWT** | **Deny** 401 + envelope | `Deny_no_jwt_is_rejected_401_with_our_envelope` | GetCurrentUserTests.cs:38 |
| `GET /api/users/me` (getCurrentUser) | own profile | **valid carrier, non-existent owner** (e.g. hard-deleted) | **Deny** 401 | `Deny_a_valid_jwt_for_a_nonexistent_user_is_rejected_401` | GetCurrentUserTests.cs:50 |
| `GET /api/users/me` (getCurrentUser) | own profile | **forged signature** | **Deny** 401 | `Deny_invalid_signature_is_rejected_401` | GetCurrentUserTests.cs:64 |
| `GET /api/users/me` (getCurrentUser) | own profile | **tombstone identity** (all-zeros GUID) | **Deny** 401 | `Deny_the_tombstone_identity_is_rejected_401` | GetCurrentUserTests.cs:72 |
| `GET /api/users/me` (getCurrentUser) | own profile | **expired carrier** | **Deny** 401 | `Deny_expired_jwt_is_rejected_401` | GetCurrentUserTests.cs:83 |
| `DELETE /api/users/me` (deleteAccount) | own account | valid carrier = own id | **Allow** 204 + hard-delete | `Allow_hard_deletes_the_callers_row_leaving_no_residual_data` | DeleteAccountTests.cs:40 |
| `DELETE /api/users/me` (deleteAccount) | own account | valid carrier = own id | **Allow** post-delete carrier no longer authenticates (401) | `Allow_a_deleted_account_can_no_longer_authenticate` | DeleteAccountTests.cs:59 |
| `DELETE /api/users/me` (deleteAccount) | own account | valid carrier = own id | **Allow** dispatches `AccountDeletionRequested` | `Allow_dispatches_AccountDeletionRequested` | DeleteAccountTests.cs:74 |
| `DELETE /api/users/me` (deleteAccount) | own account | **no JWT** | **Deny** 401 + envelope | `Deny_no_jwt_is_rejected_401_with_our_envelope` | DeleteAccountTests.cs:90 |
| `DELETE /api/users/me` (deleteAccount) | own account | **forged signature** | **Deny** 401 | `Deny_invalid_signature_is_rejected_401` | DeleteAccountTests.cs:103 |
| `DELETE /api/users/me` (deleteAccount) | own account | **expired carrier** | **Deny** 401 | `Deny_expired_jwt_is_rejected_401` | DeleteAccountTests.cs:111 |
| `DELETE /api/users/me` (deleteAccount) | another/non-existent account | **valid carrier, non-existent owner** | **Deny** 401 (never silent 204) | `Deny_a_valid_jwt_for_a_nonexistent_user_is_rejected_401` | DeleteAccountTests.cs:118 |
| `DELETE /api/users/me` (deleteAccount) | tombstone | **tombstone identity** | **Deny** 401 | `Deny_the_tombstone_identity_is_rejected_401` | DeleteAccountTests.cs:131 |

### SC-016 verdict: PASS

Each of the three data handlers ships **both** an allow and a deny test:

| Handler | Allow test(s) | Deny test(s) |
|---|---|---|
| ensureUser | 3 (lines 27, 44, 67) | 2 (lines 79, 104) |
| getCurrentUser | 1 (line 18) | 5 (lines 38, 50, 64, 72, 83) |
| deleteAccount | 3 (lines 40, 59, 74) | 5 (lines 90, 103, 111, 118, 131) |

Deny-by-default is proven structurally, not just by status code:
- **ensureUser** is the sharpest probe — its handler reads the subject from the request body and never
  dereferences `currentUser.Id`, so a failure to weave authentication middleware would *not* surface
  as a 500; it would create a user. The deny test therefore asserts both *exactly 401* and *no row
  written* (EnsureUserTests.cs:88, 99-100), proving unauthenticated account creation is impossible.
- A carrier "always names itself" (sub = own id), so **delete-another-user is structurally
  impossible**; the non-existent-owner deny (DeleteAccountTests.cs:118) confirms an unmatched carrier
  yields 401 rather than a silent idempotent 204.
- The **tombstone** all-zeros GUID is explicitly rejected for both read and delete
  (GetCurrentUser.cs:31, DeleteAccount.cs:41) so the seeded "Deleted User" sentinel can never be
  authenticated as a real account.

---

## T055 — SC-017: account deletion leaves no residual personally-attributable data

> **SC-017**: deleting an account removes/anonymizes all personal data per the FR-085 cascade — no
> residual personally attributable data beyond the defined tombstone identity; the user's own User
> record is **hard-deleted**, leaving no residual row.

Proven by `Allow_hard_deletes_the_callers_row_leaving_no_residual_data` (DeleteAccountTests.cs:40-56):

```csharp
// SC-017: the row is HARD-deleted (no soft-delete column), and only the seeded tombstone remains.
(await db.Users.AnyAsync(u => u.Id == UserId.From(created.Id)))
    .Should().BeFalse("account deletion hard-deletes the User row, leaving no residual row");   // :52-53
(await db.Users.AnyAsync(u => u.Id == UserId.Tombstone))
    .Should().BeTrue("the anonymization tombstone is a separate seeded sentinel, never deleted"); // :54-55
```

- **Hard delete, no residual row**: the deleted user's row is absent post-delete (`AnyAsync(... ==
  created.Id) == false`). There is no soft-delete `deleted_at` column (confirmed by the spec
  clarification, spec.md:72, and by the entity having no such field), so nothing personally
  attributable is retained.
- **Only the tombstone survives**: the all-zeros `UserId.Tombstone` row remains
  (`AnyAsync(... == UserId.Tombstone) == true`). It is a non-personal sentinel ("Deleted User",
  email `deleted-user@taskflow.invalid`) seeded by the initial migration
  (20260617103742_InitialCreate.cs:34) / `UserConfiguration.cs:69-72`, and is the single permitted
  residual — exactly the SC-017 exception.
- **Sessions purged**: the BFF `sessions.user_id ON DELETE CASCADE` removes the session rows; the
  deleted carrier can no longer authenticate, asserted independently by
  `Allow_a_deleted_account_can_no_longer_authenticate` (DeleteAccountTests.cs:59-71, 401).
- **One transaction + erasure event**: deletion hard-deletes the row and dispatches
  `AccountDeletionRequested` via the Wolverine outbox (so later slices repoint authored content to the
  tombstone), asserted by `Allow_dispatches_AccountDeletionRequested` (DeleteAccountTests.cs:74-87).
- **Tombstone integrity** independently smoke-tested: `FoundationSmokeTests.cs:33-36` confirms the
  seeded "Deleted User" row exists at the tombstone id.

### SC-017 verdict: PASS — hard-delete + tombstone-survives assertions present and green.

---

## T060 — Quickstart validation-scenario traceability

> Confirm each `quickstart.md` validation scenario maps to a passing automated test. The app behavior
> is already proven green; this is a traceability confirmation.

| Quickstart scenario | Covering automated test | File:line |
|---|---|---|
| **US-11.AS-01** Admitted sign-in (account created, lands in workspace) | `AS-01: an admitted, email-verified account signs in → account created, lands in workspace` | auth-signin.spec.ts:16 |
| **US-11.AS-01** Non-admitted denied (rejection, no User created) | `AS-01: a non-admitted account is rejected with a recoverable message and NO account` + `AS-01: an unverified email is rejected even when the address is on the allowlist (no account)` | auth-signin.spec.ts:47, 71 |
| **US-11.AS-02** Sign-out (session ended, protected routes inaccessible) | `AS-02: sign-out ends the session and protected views become inaccessible` | auth.spec.ts:65 |
| **US-11.AS-03** Unauthenticated denied (redirect to sign-in) | `AS-03: unauthenticated access to a protected route redirects to sign-in` + `AS-03: the proxy denies an unauthenticated API call (401)` | auth.spec.ts:43, 57 |
| **US-11.AS-04** Profile display (Google name + avatar shown) | `AS-04: settings shows the Google display name + avatar` | auth.spec.ts:13 |
| **US-17.AS-02** Account deletion (session ended, hard-deleted, fresh re-sign-in) | `delete + confirm ends the session and hard-deletes the row; re-sign-in is a fresh account` | delete-account.spec.ts:17 |
| **SC-016** Allow test (`GET /api/users/me` valid JWT → 200 + profile) | `Allow_returns_the_callers_own_profile` | GetCurrentUserTests.cs:18 |
| **SC-016** Deny test (`GET /api/users/me` no JWT → 401) | `Deny_no_jwt_is_rejected_401_with_our_envelope` | GetCurrentUserTests.cs:38 |
| **SC-016** Deleted deny (`GET /api/users/me` JWT for hard-deleted user → 401) | `Deny_a_valid_jwt_for_a_nonexistent_user_is_rejected_401` (and end-to-end via `Allow_a_deleted_account_can_no_longer_authenticate`, DeleteAccountTests.cs:59) | GetCurrentUserTests.cs:50 |

### T060 verdict: PASS — every quickstart row maps to a named, green automated test.

---

## Summary

| Task | Outcome |
|---|---|
| **T054 / SC-016** | PASS — 3/3 data handlers ship both allow and deny tests; deny-by-default proven structurally (no-row-written, tombstone reject, non-existent-owner reject). |
| **T055 / SC-017** | PASS — hard-delete asserted (no residual row), tombstone-survives asserted, sessions cascade-purged, erasure event dispatched. |
| **T060** | PASS — all 9 quickstart validation rows trace to named green tests. |
