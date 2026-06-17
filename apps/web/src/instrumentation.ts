/**
 * Next.js startup hook (`register()` runs once per runtime when the server boots). The actual
 * startup work (session-table DDL + admission fail-fast, FR-087/T024/T041) lives in
 * `instrumentation-node.ts` and is loaded only in the Node.js runtime — keeping the Node-only `pg`
 * dependency out of the edge bundle.
 */
export async function register(): Promise<void> {
  if (process.env.NEXT_RUNTIME === "nodejs") {
    const { registerNode } = await import("./instrumentation-node");
    await registerNode();
  }
}
