import { Crown, Eye, Pencil } from "lucide-react";
import styles from "./RoleBadge.module.css";

/**
 * A role badge that conveys the effective role by TEXT + ICON — never color alone (FR-044,
 * Constitution II). Used in the members roster and the sharing surface. The Lucide glyph is
 * decorative (aria-hidden — FR-105: system iconography is Lucide, emoji stay user-chosen
 * project icons only); the visible role word is the meaning.
 */
const ROLE_LABEL: Record<string, string> = {
  owner: "Właściciel",
  editor: "Edytor",
  viewer: "Podgląd",
};

function RoleGlyph({ role }: { role: string }) {
  const props = { size: 12, strokeWidth: 1.75, "aria-hidden": true } as const;
  switch (role) {
    case "owner":
      return <Crown {...props} />;
    case "viewer":
      return <Eye {...props} />;
    default:
      return <Pencil {...props} />;
  }
}

export function RoleBadge({ role }: { role: string }) {
  return (
    <span className={styles.badge} data-role={role}>
      <span aria-hidden="true">
        <RoleGlyph role={role} />
      </span>
      <span>{ROLE_LABEL[role] ?? role}</span>
    </span>
  );
}
