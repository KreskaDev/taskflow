import { createElement, type ReactElement } from "react";
import ReactMarkdown from "react-markdown";
import rehypeSanitize, { defaultSchema } from "rehype-sanitize";

/**
 * The render-boundary sanitizer (slice 009, T035; FR-098, Constitution XII, research R8) — the ONE new
 * web dependency this slice ships (`react-markdown` + `rehype-sanitize`), isolated to this module.
 *
 * Comments are the product's first free-form user-content surface and a stored-XSS target. The stored
 * `body` is RAW text; every render passes through here (so historical rows are covered too):
 *
 * - `react-markdown` NEVER dangerously-injects HTML — it builds a React element tree, and with
 *   `skipHtml` raw HTML nodes in the source are DROPPED (no passthrough, no `<script>`/`<iframe>`/event
 *   handlers can ever become elements).
 * - `rehype-sanitize` (the GitHub-style schema, restricted further below) is the defense-in-depth
 *   allowlist over the produced tree.
 * - `react-markdown`'s default URL transform allows only http/https/mailto/etc., so a `javascript:`
 *   link renders with its href neutralized.
 * - React's default text escaping is the backstop; the `next.config.ts` CSP sits behind all of it.
 *
 * @mentions are NEVER scraped from this prose (R6) — they ride the typed `mentionedUserIds` token set
 * and render as chips in `CommentItem`; a literal `@` here is inert text.
 */

/**
 * The constrained safe subset (FR-098: "plain text + safe markdown"): emphasis, inline code + code
 * blocks, lists, blockquotes, links, headings, paragraphs/breaks. Derived from the vetted
 * `rehype-sanitize` default (GitHub) schema with images and raw-ish embeds REMOVED — a comment thread
 * needs no `<img>` (an attacker-controlled remote-image beacon), and `id`/`className` stay stripped.
 */
const commentSchema: typeof defaultSchema = {
  ...defaultSchema,
  tagNames: (defaultSchema.tagNames ?? []).filter((tag) => tag !== "img"),
  attributes: {
    ...defaultSchema.attributes,
    "*": (defaultSchema.attributes?.["*"] ?? []).filter(
      (attr) => attr !== "className" && attr !== "id",
    ),
  },
};

export interface SafeMarkdownProps {
  /** The RAW stored comment body (untrusted user content). */
  body: string;
}

/**
 * Renders an untrusted comment body as the constrained safe subset. The single choke point every
 * comment body render must go through (the stored-XSS regression `comment-render.test.ts` gates it).
 */
export function SafeMarkdown({ body }: SafeMarkdownProps): ReactElement {
  return createElement(
    ReactMarkdown,
    {
      skipHtml: true,
      rehypePlugins: [[rehypeSanitize, commentSchema]],
    },
    body,
  );
}
