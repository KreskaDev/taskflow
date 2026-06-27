/**
 * The FROZEN, closed preset color + icon sets for projects (ASM-04, research R10).
 *
 * This is the WEB MIRROR of the authoritative API constant
 * (`apps/api/src/TaskFlow.Application/TaskManagement/ProjectPresets.cs`). The two lists MUST stay
 * byte-consistent: `projectSchema` (T024) builds its color/icon enums from these tokens, and the
 * `project-validation` tests (T023) assert them verbatim. The API-tier FluentValidation membership
 * rule is the load-bearing check (a value outside the set is a 422); this mirror is the web-tier
 * convenience that keeps the form constrained (Principle XII — colors/icons are never free-form).
 *
 * Tokens are abstract names, not raw hex/SVG: the web maps each token to its concrete muted color
 * value and icon glyph at render time, so the wire/storage value is always the constrained token.
 */

/** The closed set of preset color tokens (ASM-04). Keep in sync with `ProjectPresets.Colors` (API). */
export const PROJECT_COLORS = [
  "slate",
  "gray",
  "red",
  "orange",
  "amber",
  "green",
  "teal",
  "blue",
  "indigo",
  "violet",
  "pink",
  "rose",
] as const;

/** The closed set of preset icon tokens (ASM-04). Keep in sync with `ProjectPresets.Icons` (API). */
export const PROJECT_ICONS = [
  "folder",
  "inbox",
  "briefcase",
  "home",
  "star",
  "flag",
  "bookmark",
  "calendar",
  "rocket",
  "target",
  "heart",
  "tag",
] as const;

export type ProjectColor = (typeof PROJECT_COLORS)[number];
export type ProjectIcon = (typeof PROJECT_ICONS)[number];

/** Whether `color` is a member of the frozen preset color set. */
export function isValidProjectColor(color: string): color is ProjectColor {
  return (PROJECT_COLORS as readonly string[]).includes(color);
}

/** Whether `icon` is a member of the frozen preset icon set. */
export function isValidProjectIcon(icon: string): icon is ProjectIcon {
  return (PROJECT_ICONS as readonly string[]).includes(icon);
}
