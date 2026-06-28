// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import {
  createGlobalShortcutsListener,
  type GlobalShortcutHandlers,
} from "@/hooks/useGlobalShortcuts";

/**
 * App-shell keydown gate (T053, RED — covers T054; FR-031/EC-08/AS-09, research R11/R18).
 *
 * The single document-level keydown listener that turns bare keys into task commands and,
 * crucially, SUPPRESSES bare keys while the user is typing in a text field so a `C`/`E`/`?`
 * etc. is never hijacked mid-word (FR-031). The Alt+↑/↓ reorder CHORD fires ONLY while the
 * role=listbox (not a text input) has focus (R18), so it never reorders the background task
 * out from under a user typing in the capture / inline-rename input.
 *
 * PUBLIC SHAPE this test pins for the (not-yet-existing) `@/hooks/useGlobalShortcuts`:
 *   - `createGlobalShortcutsListener(handlers): (event: KeyboardEvent) => void` — a PURE listener
 *     factory (the tested surface; no React render needed, mirroring `createTaskMutationOptions`).
 *     It reads `document.activeElement` (NOT `event.target`), so the test calls the returned
 *     listener directly with a synthetic `KeyboardEvent` while a real element holds focus.
 *   - `useGlobalShortcuts(handlers): void` — the "use client" hook wrapper (NOT tested here; it
 *     installs exactly ONE `document` keydown listener via `useEffect` and removes it on cleanup,
 *     mirroring the `useEffect` add/remove pattern already in `TaskCapture.tsx`).
 *   - `GlobalShortcutHandlers` — the handlers object. Every field is OPTIONAL so a surface that
 *     only wires a subset (e.g. capture-only) type-checks; an unwired key is a no-op, never a throw.
 *
 * THE GATE PREDICATE this test pins:
 *   - A text field is `document.activeElement` matching {input, textarea, [contenteditable],
 *     role=textbox}. Detection is by tag / `[contenteditable]` ATTRIBUTE / `role="textbox"`
 *     (NOT the flaky `isContentEditable` property, which jsdom does not compute reliably).
 *   - The BYPASS modifiers are Ctrl/Meta/Alt ONLY. Shift is part of normal typing, so `?`
 *     (which is Shift+/) is a BARE key and IS suppressed inside a text field — pinned explicitly
 *     below so a wrong "any modifier bypasses" impl cannot go green.
 */

type Handler = keyof GlobalShortcutHandlers;

/** All nine handler names, each a fresh spy, so any accidental cross-dispatch is caught. */
function makeHandlers(): { [K in Handler]-?: ReturnType<typeof vi.fn> } & GlobalShortcutHandlers {
  return {
    onCapture: vi.fn(),
    onMoveUp: vi.fn(),
    onMoveDown: vi.fn(),
    onToggle: vi.fn(),
    onRename: vi.fn(),
    onMove: vi.fn(),
    onDelete: vi.fn(),
    onHelp: vi.fn(),
    onReorderUp: vi.fn(),
    onReorderDown: vi.fn(),
    onSetPriority: vi.fn(),
    onReschedule: vi.fn(),
    onGoInbox: vi.fn(),
    onGoToday: vi.fn(),
    onGoUpcoming: vi.fn(),
  };
}

/** Build a synthetic keydown the listener can inspect (cancelable so preventDefault is observable). */
function keydown(init: KeyboardEventInit): KeyboardEvent {
  return new KeyboardEvent("keydown", { bubbles: true, cancelable: true, ...init });
}

/** Append, focus, and ASSERT focus actually took — a silent jsdom focus failure must not pass as "suppressed". */
function focusEl(el: HTMLElement): HTMLElement {
  document.body.appendChild(el);
  el.focus();
  expect(document.activeElement).toBe(el);
  return el;
}

function textInput(): HTMLInputElement {
  const el = document.createElement("input");
  el.type = "text";
  return el;
}

function textarea(): HTMLTextAreaElement {
  return document.createElement("textarea");
}

/** A `[contenteditable]` div — `tabindex=0` so jsdom lets it become `activeElement`. */
function contentEditableDiv(): HTMLDivElement {
  const el = document.createElement("div");
  el.setAttribute("contenteditable", "true");
  el.setAttribute("tabindex", "0");
  return el;
}

/** A `role="textbox"` div — `tabindex=0` so jsdom lets it become `activeElement`. */
function roleTextboxDiv(): HTMLDivElement {
  const el = document.createElement("div");
  el.setAttribute("role", "textbox");
  el.setAttribute("tabindex", "0");
  return el;
}

const TEXT_FIELDS: ReadonlyArray<readonly [string, () => HTMLElement]> = [
  ["input", textInput],
  ["textarea", textarea],
  ["[contenteditable]", contentEditableDiv],
  ['role="textbox"', roleTextboxDiv],
];

/** Bare keys that must NEVER fire while a text field is focused (the char is typed instead). */
const SUPPRESSED_BARE_KEYS: ReadonlyArray<readonly [string, KeyboardEventInit]> = [
  ["c", { key: "c" }],
  ["C", { key: "C" }],
  ["e", { key: "e" }],
  ["E", { key: "E" }],
  ["m", { key: "m" }],
  ["M", { key: "M" }],
  ["Space", { key: " " }],
  ["Delete", { key: "Delete" }],
  ["? (Shift+/)", { key: "?", shiftKey: true }], // Shift is NOT a bypass modifier — `?` is bare.
  ["ArrowUp", { key: "ArrowUp" }],
  ["ArrowDown", { key: "ArrowDown" }],
];

beforeEach(() => {
  document.body.innerHTML = "";
});

afterEach(() => {
  document.body.innerHTML = "";
  vi.restoreAllMocks();
});

describe("createGlobalShortcutsListener — gate (a): suppression inside text fields", () => {
  for (const [fieldName, makeField] of TEXT_FIELDS) {
    for (const [keyName, init] of SUPPRESSED_BARE_KEYS) {
      it(`suppresses bare ${keyName} while a ${fieldName} is focused (no handler fires)`, () => {
        focusEl(makeField());
        const handlers = makeHandlers();
        const listener = createGlobalShortcutsListener(handlers);

        listener(keydown(init));

        // NOTHING fires — the keystroke is left to be typed as a character.
        for (const name of Object.keys(handlers) as Handler[]) {
          expect(handlers[name]).not.toHaveBeenCalled();
        }
      });
    }

    it(`SUPPRESSES Alt+ArrowUp/Down reorder chords from inside a ${fieldName} (R18: the chord fires only while the role=listbox has focus)`, () => {
      focusEl(makeField());
      const handlers = makeHandlers();
      const listener = createGlobalShortcutsListener(handlers);

      listener(keydown({ key: "ArrowUp", altKey: true }));
      listener(keydown({ key: "ArrowDown", altKey: true }));

      // The chord must NOT reorder the background task out from under a typing user (R18) —
      // it is left to native behaviour (e.g. macOS Option+Arrow paragraph navigation).
      expect(handlers.onReorderUp).not.toHaveBeenCalled();
      expect(handlers.onReorderDown).not.toHaveBeenCalled();
      // The bare-arrow navigation handlers are NOT triggered either.
      expect(handlers.onMoveUp).not.toHaveBeenCalled();
      expect(handlers.onMoveDown).not.toHaveBeenCalled();
    });
  }
});

describe("createGlobalShortcutsListener — gate (b): bare keys dispatch outside text inputs", () => {
  // Focus stays on document.body (not a text field), so the gate lets bare keys through.
  const CASES: ReadonlyArray<readonly [string, KeyboardEventInit, Handler]> = [
    ["C -> onCapture", { key: "c" }, "onCapture"],
    ["uppercase C -> onCapture", { key: "C" }, "onCapture"],
    ["ArrowUp -> onMoveUp", { key: "ArrowUp" }, "onMoveUp"],
    ["ArrowDown -> onMoveDown", { key: "ArrowDown" }, "onMoveDown"],
    ["Space -> onToggle", { key: " " }, "onToggle"],
    ["E -> onRename", { key: "e" }, "onRename"],
    ["uppercase E -> onRename", { key: "E" }, "onRename"],
    ["M -> onMove", { key: "m" }, "onMove"],
    ["uppercase M -> onMove", { key: "M" }, "onMove"],
    ["Delete -> onDelete", { key: "Delete" }, "onDelete"],
    ["? -> onHelp", { key: "?", shiftKey: true }, "onHelp"],
  ];

  for (const [name, init, expected] of CASES) {
    it(`dispatches ${name} when no text field is focused`, () => {
      const handlers = makeHandlers();
      const listener = createGlobalShortcutsListener(handlers);

      listener(keydown(init));

      expect(handlers[expected]).toHaveBeenCalledTimes(1);
      // Exactly one handler fires — no key maps to two commands.
      for (const other of Object.keys(handlers) as Handler[]) {
        if (other !== expected) expect(handlers[other]).not.toHaveBeenCalled();
      }
    });
  }
});

describe("createGlobalShortcutsListener — gate (c): Alt+Arrow reorder chord", () => {
  it("Alt+ArrowUp dispatches onReorderUp and calls preventDefault", () => {
    const handlers = makeHandlers();
    const listener = createGlobalShortcutsListener(handlers);
    const event = keydown({ key: "ArrowUp", altKey: true });
    const preventDefault = vi.spyOn(event, "preventDefault");

    listener(event);

    expect(handlers.onReorderUp).toHaveBeenCalledTimes(1);
    expect(preventDefault).toHaveBeenCalled();
    // Plain ArrowUp navigation is NOT also fired.
    expect(handlers.onMoveUp).not.toHaveBeenCalled();
  });

  it("Alt+ArrowDown dispatches onReorderDown and calls preventDefault", () => {
    const handlers = makeHandlers();
    const listener = createGlobalShortcutsListener(handlers);
    const event = keydown({ key: "ArrowDown", altKey: true });
    const preventDefault = vi.spyOn(event, "preventDefault");

    listener(event);

    expect(handlers.onReorderDown).toHaveBeenCalledTimes(1);
    expect(preventDefault).toHaveBeenCalled();
    expect(handlers.onMoveDown).not.toHaveBeenCalled();
  });
});

describe("createGlobalShortcutsListener — unmapped / no-op safety", () => {
  it("an unmapped key dispatches nothing", () => {
    const handlers = makeHandlers();
    const listener = createGlobalShortcutsListener(handlers);

    listener(keydown({ key: "x" }));

    for (const name of Object.keys(handlers) as Handler[]) {
      expect(handlers[name]).not.toHaveBeenCalled();
    }
  });

  it("a missing handler for a mapped key is a silent no-op (does not throw)", () => {
    // Only onCapture wired — pressing Space (onToggle unwired) must not throw.
    const listener = createGlobalShortcutsListener({ onCapture: vi.fn() });

    expect(() => listener(keydown({ key: " " }))).not.toThrow();
  });
});

describe("createGlobalShortcutsListener — slice 005 keys (priority / reschedule / G-chord nav)", () => {
  it("1-4 set P0-P3 on the selected task (AS-04)", () => {
    const handlers = makeHandlers();
    const listener = createGlobalShortcutsListener(handlers);
    listener(keydown({ key: "1" }));
    listener(keydown({ key: "4" }));
    expect(handlers.onSetPriority).toHaveBeenNthCalledWith(1, "P0");
    expect(handlers.onSetPriority).toHaveBeenNthCalledWith(2, "P3");
  });

  it("bare T reschedules; it is suppressed in a text field (FR-031)", () => {
    const handlers = makeHandlers();
    const listener = createGlobalShortcutsListener(handlers);
    listener(keydown({ key: "T" }));
    expect(handlers.onReschedule).toHaveBeenCalledTimes(1);

    focusEl(textInput());
    listener(keydown({ key: "t" }));
    expect(handlers.onReschedule).toHaveBeenCalledTimes(1); // still 1 — suppressed while typing
  });

  it("G I / G T / G U navigate; the chord disambiguates bare T from G T", () => {
    const handlers = makeHandlers();
    const listener = createGlobalShortcutsListener(handlers);

    listener(keydown({ key: "g" }));
    listener(keydown({ key: "i" }));
    expect(handlers.onGoInbox).toHaveBeenCalledTimes(1);

    listener(keydown({ key: "g" }));
    listener(keydown({ key: "t" }));
    expect(handlers.onGoToday).toHaveBeenCalledTimes(1);
    expect(handlers.onReschedule).not.toHaveBeenCalled(); // G T is nav, not a bare-T reschedule

    listener(keydown({ key: "g" }));
    listener(keydown({ key: "u" }));
    expect(handlers.onGoUpcoming).toHaveBeenCalledTimes(1);
  });

  it("an aborted G-chord swallows the stray second key", () => {
    const handlers = makeHandlers();
    const listener = createGlobalShortcutsListener(handlers);
    listener(keydown({ key: "g" }));
    listener(keydown({ key: "x" })); // not a nav key → chord aborts, nothing fires
    expect(handlers.onGoInbox).not.toHaveBeenCalled();
    // The chord is reset: a subsequent bare Space still toggles.
    listener(keydown({ key: " " }));
    expect(handlers.onToggle).toHaveBeenCalledTimes(1);
  });
});
