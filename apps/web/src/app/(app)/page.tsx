/**
 * Empty workspace home (T044, EC-01). A first-time admitted user lands here with an accessible,
 * quiet empty state — no onboarding wizard or modal interruption (Constitution IV). Task content
 * arrives in later slices.
 */
export default function WorkspaceHome() {
  return (
    <section aria-labelledby="workspace-heading" className="tf-workspace">
      <h1 id="workspace-heading">Your workspace</h1>
      <p className="tf-workspace__empty">
        Nothing here yet. Your tasks and projects will appear here as you create them.
      </p>
    </section>
  );
}
