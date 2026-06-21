import { v7 } from "uuid";

/**
 * Mints a new task id as a UUIDv7 (T021). v7 is time-ordered, so ids sort by
 * creation; minting client-side lets an optimistic capture carry its own stable
 * identity into the store and the API request.
 *
 * MUST stay on uuid's v7 — never crypto.randomUUID(), which emits a v4 and would
 * silently drop the time-ordering guarantee the rest of the slice relies on.
 */
export function newTaskId(): string {
  return v7();
}
