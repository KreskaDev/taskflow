"use client";

import { useState } from "react";
import styles from "./Avatar.module.css";

type AvatarSize = "sm" | "md" | "lg";

interface AvatarProps {
  /** Stable identity — drives the deterministic background pick (FR-105). */
  userId: string;
  displayName: string;
  /** Google photo URL; on missing/failed load the initials fallback renders. */
  avatarUrl?: string | null;
  /** sm 20px (dense rows) · md 26px (comments/pickers) · lg 32px (topbar). */
  size?: AvatarSize;
  className?: string;
}

/** The AA-safe avatar background tokens (≥4.5:1 for white initials — tokens.css). */
const AVATAR_TOKENS = ["a", "b", "c"] as const;

/** Deterministic pick from the AA-safe palette — same userId, same color, always. */
function tokenFor(userId: string): (typeof AVATAR_TOKENS)[number] {
  let hash = 0;
  for (let i = 0; i < userId.length; i++) {
    hash = (hash * 31 + userId.charCodeAt(i)) >>> 0;
  }
  return AVATAR_TOKENS[hash % AVATAR_TOKENS.length]!;
}

/** "Ola Audyt" → "OA"; single-word names yield a single initial. */
function initialsOf(displayName: string): string {
  const words = displayName.trim().split(/\s+/).filter(Boolean);
  const first = words[0]?.[0] ?? "?";
  const second = words.length > 1 ? (words[words.length - 1]?.[0] ?? "") : "";
  return (first + second).toUpperCase();
}

/**
 * Catalog avatar (T017, FR-105): Google photo with an initials fallback in a colored
 * circle. The fallback background is chosen DETERMINISTICALLY from the `--avatar-*`
 * AA-safe token palette (white initials ≥4.5:1 in every palette).
 */
export function Avatar({ userId, displayName, avatarUrl, size = "md", className }: AvatarProps) {
  const [failed, setFailed] = useState(false);
  const showPhoto = Boolean(avatarUrl) && !failed;
  const classes = [styles.avatar, styles[size], className].filter(Boolean).join(" ");

  if (showPhoto) {
    return (
      // eslint-disable-next-line @next/next/no-img-element -- external Google photo, sized tiny
      <img className={classes} src={avatarUrl!} alt={displayName} onError={() => setFailed(true)} />
    );
  }

  return (
    <span
      className={classes}
      style={{ background: `var(--avatar-${tokenFor(userId)})` }}
      role="img"
      aria-label={displayName}
    >
      <span aria-hidden="true">{initialsOf(displayName)}</span>
    </span>
  );
}
