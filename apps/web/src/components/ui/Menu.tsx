"use client";

import {
  useCallback,
  useEffect,
  useId,
  useLayoutEffect,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { createPortal } from "react-dom";
import styles from "./Menu.module.css";

export interface MenuItemSpec {
  id: string;
  label: ReactNode;
  onSelect: () => void;
  /** Destructive actions (delete) get the danger styling hook. */
  destructive?: boolean;
  disabled?: boolean;
}

interface MenuProps {
  items: MenuItemSpec[];
  /** Accessible name of the trigger (icon-only "⋯" needs one — FR-043). */
  triggerLabel: string;
  triggerContent: ReactNode;
  /** Extra class for the trigger button (row action-zone styling). */
  triggerClassName?: string;
  /** Accessible name of the menu itself (defaults to the trigger label). */
  menuLabel?: string;
}

interface MenuPosition {
  top: number;
  left: number;
}

/**
 * Catalog context/overflow menu (T012): `role="menu"` with full keyboard navigation
 * (↑/↓ wrap, Home/End), `aria-expanded` on the trigger, Esc/Tab/click-outside/activation
 * close with focus returning to the trigger, and viewport-edge repositioning measured
 * AFTER the menu shows (spec Edge Cases). The popup renders in a PORTAL at the token
 * z-layer 60 so virtualized `translateY` rows can never clip or stack over it (the
 * documented slice-001 stacking trap).
 */
export function Menu({
  items,
  triggerLabel,
  triggerContent,
  triggerClassName,
  menuLabel,
}: MenuProps) {
  const [open, setOpen] = useState(false);
  const [position, setPosition] = useState<MenuPosition | null>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const menuId = useId();

  const close = useCallback((returnFocus: boolean) => {
    setOpen(false);
    setPosition(null);
    if (returnFocus) triggerRef.current?.focus();
  }, []);

  // Position AFTER showing: measure the real menu box, flip up / align right when it
  // would escape the viewport (dimensions are only trustworthy once rendered).
  useLayoutEffect(() => {
    if (!open) return;
    const trigger = triggerRef.current;
    const menu = menuRef.current;
    if (!trigger || !menu) return;

    const t = trigger.getBoundingClientRect();
    const m = menu.getBoundingClientRect();
    let top = t.bottom + 4;
    let left = t.left;
    if (m.height > 0 && top + m.height > window.innerHeight) {
      top = Math.max(4, t.top - m.height - 4);
    }
    if (m.width > 0 && left + m.width > window.innerWidth) {
      left = Math.max(4, t.right - m.width);
    }
    setPosition({ top, left });
  }, [open]);

  // Initial focus: first enabled item.
  useEffect(() => {
    if (!open) return;
    const first = menuRef.current?.querySelector<HTMLElement>('[role="menuitem"]:not([aria-disabled="true"])');
    first?.focus();
  }, [open]);

  // Click outside closes (without stealing focus back to the trigger).
  useEffect(() => {
    if (!open) return;
    const onPointerDown = (event: PointerEvent) => {
      const target = event.target as Node;
      if (menuRef.current?.contains(target) || triggerRef.current?.contains(target)) return;
      close(false);
    };
    document.addEventListener("pointerdown", onPointerDown);
    return () => document.removeEventListener("pointerdown", onPointerDown);
  }, [open, close]);

  const focusables = (): HTMLElement[] =>
    Array.from(
      menuRef.current?.querySelectorAll<HTMLElement>('[role="menuitem"]:not([aria-disabled="true"])') ?? [],
    );

  const onMenuKeyDown = (event: React.KeyboardEvent<HTMLDivElement>) => {
    const elements = focusables();
    const current = elements.indexOf(document.activeElement as HTMLElement);
    switch (event.key) {
      case "ArrowDown": {
        event.preventDefault();
        elements[(current + 1) % elements.length]?.focus();
        return;
      }
      case "ArrowUp": {
        event.preventDefault();
        elements[(current - 1 + elements.length) % elements.length]?.focus();
        return;
      }
      case "Home": {
        event.preventDefault();
        elements[0]?.focus();
        return;
      }
      case "End": {
        event.preventDefault();
        elements[elements.length - 1]?.focus();
        return;
      }
      case "Escape": {
        event.preventDefault();
        event.stopPropagation();
        close(true);
        return;
      }
      case "Tab": {
        // A menu is not a tab stop sequence — Tab dismisses (WAI-ARIA menu pattern).
        close(true);
        event.preventDefault();
        return;
      }
      default:
        return;
    }
  };

  const activate = (item: MenuItemSpec) => {
    if (item.disabled) return;
    close(true);
    item.onSelect();
  };

  return (
    <>
      <button
        ref={triggerRef}
        type="button"
        className={[styles.trigger, triggerClassName].filter(Boolean).join(" ")}
        aria-label={triggerLabel}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? menuId : undefined}
        onClick={() => (open ? close(true) : setOpen(true))}
        onKeyDown={(event) => {
          if (event.key === "ArrowDown" && !open) {
            event.preventDefault();
            setOpen(true);
          }
        }}
      >
        {triggerContent}
      </button>
      {open
        ? createPortal(
            <div
              ref={menuRef}
              id={menuId}
              role="menu"
              aria-label={menuLabel ?? triggerLabel}
              className={styles.menu}
              style={position ? { top: position.top, left: position.left } : { visibility: "hidden" }}
              onKeyDown={onMenuKeyDown}
            >
              {items.map((item) => (
                <button
                  key={item.id}
                  type="button"
                  role="menuitem"
                  tabIndex={-1}
                  className={[styles.item, item.destructive ? styles.destructive : null]
                    .filter(Boolean)
                    .join(" ")}
                  aria-disabled={item.disabled ? true : undefined}
                  onClick={() => activate(item)}
                >
                  {item.label}
                </button>
              ))}
            </div>,
            document.body,
          )
        : null}
    </>
  );
}
