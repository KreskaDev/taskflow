/**
 * A role badge that conveys the effective role by TEXT + ICON glyph — never color alone (FR-044,
 * Constitution II). Used in the members roster and the sharing surface. The glyph is decorative
 * (aria-hidden); the visible role word is the meaning.
 */
const ROLE_GLYPH: Record<string, string> = {
  owner: "★",
  editor: "✎",
  viewer: "👁",
};

const ROLE_LABEL: Record<string, string> = {
  owner: "Owner",
  editor: "Editor",
  viewer: "Viewer",
};

export function RoleBadge({ role }: { role: string }) {
  return (
    <span className="tf-role-badge" data-role={role}>
      <span className="tf-role-badge__icon" aria-hidden="true">
        {ROLE_GLYPH[role] ?? "•"}
      </span>
      <span className="tf-role-badge__label">{ROLE_LABEL[role] ?? role}</span>
    </span>
  );
}
