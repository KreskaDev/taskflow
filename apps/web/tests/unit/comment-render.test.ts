import { cleanup, render } from "@testing-library/react";
import { createElement } from "react";
import { afterEach, describe, expect, it } from "vitest";

import { SafeMarkdown } from "@/lib/markdown/safeMarkdown";

/**
 * THE stored-XSS regression (T034, RED — covers T035; slice 009, FR-098/FR-099, Constitution XII).
 * Comments are the product's first free-form user-content surface; `body` is stored RAW and sanitized to
 * the constrained safe subset (plain text + safe markdown) at THIS render boundary. These cases pin:
 * a `<script>`, an event-handler attribute, a `javascript:` URL, and raw-HTML passthrough all render
 * INERT (no element, no handler, no executable URL — at most escaped text), while the safe subset
 * (emphasis, lists, inline code) still renders; a literal `@` in prose is inert text (mentions ride the
 * TYPED token, never prose scraping — R6). React's default escaping is the backstop; the `next.config.ts`
 * CSP is the defense-in-depth layer behind this module.
 */

declare global {
  interface Window {
    __xssProbe?: boolean;
  }
}

function renderBody(body: string) {
  return render(createElement(SafeMarkdown, { body }));
}

afterEach(() => {
  cleanup();
  delete window.__xssProbe;
});

describe("SafeMarkdown — the render-boundary sanitizer [INV-117]", () => {
  it("renders the safe markdown subset (emphasis, list, inline code)", () => {
    const { container } = renderBody("This is **bold**, a `code span`, and:\n\n- item one\n- item two");
    expect(container.querySelector("strong")?.textContent).toBe("bold");
    expect(container.querySelector("code")?.textContent).toBe("code span");
    expect(container.querySelectorAll("li")).toHaveLength(2);
  });

  it("a <script> payload renders inert — no script element, nothing executes", () => {
    const { container } = renderBody('<script>window.__xssProbe = true;</script>');
    expect(container.querySelector("script")).toBeNull();
    expect(window.__xssProbe).toBeUndefined();
  });

  it("an <img onerror> payload renders inert — no element with an event-handler attribute survives", () => {
    const { container } = renderBody('<img src="x" onerror="window.__xssProbe = true" />');
    expect(container.querySelector("img")).toBeNull();
    expect(container.querySelector("[onerror]")).toBeNull();
    expect(window.__xssProbe).toBeUndefined();
  });

  it("a javascript: URL is neutralized — the link carries no executable href", () => {
    const { container } = renderBody("[click me](javascript:window.__xssProbe=true)");
    const anchor = container.querySelector("a");
    // Either the anchor is dropped entirely or its href is stripped/neutralized — never javascript:.
    expect(anchor?.getAttribute("href") ?? "").not.toMatch(/^javascript:/i);
    expect(window.__xssProbe).toBeUndefined();
  });

  it("a safe https link keeps its href", () => {
    const { container } = renderBody("[docs](https://example.com/docs)");
    expect(container.querySelector("a")?.getAttribute("href")).toBe("https://example.com/docs");
  });

  it("raw inline HTML does not become elements (no raw-HTML passthrough)", () => {
    const { container } = renderBody('Hello <b class="sneaky">world</b> <iframe src="https://evil.example"></iframe>');
    expect(container.querySelector("b.sneaky")).toBeNull();
    expect(container.querySelector("iframe")).toBeNull();
  });

  it("a literal @ in prose stays inert text — mentions ride the typed token, never prose scraping", () => {
    const { container } = renderBody("Ping @viewer about this");
    expect(container.textContent).toContain("@viewer");
    expect(container.querySelector("a")).toBeNull();
  });
});
