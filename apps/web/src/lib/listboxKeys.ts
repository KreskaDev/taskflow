/**
 * Shared composite-widget keyboard operability for the task listboxes (slice 019, T035; D5,
 * UIT-090..093). AFTER the shortcut-system removal (FR-111) these keys are NOT shortcuts —
 * they are WAI-ARIA listbox behavior, living INSIDE the widget (no document-level listener):
 * ↑/↓ move the active option, Space toggles it, Enter opens it. Both listbox renderers
 * (TaskList and DailyView) attach this to their `role="listbox"` container, which holds DOM
 * focus while `aria-activedescendant` points at the selected option.
 */

export interface ListboxKeyOptions {
  count: number;
  selectedIndex: number;
  onSelectedIndexChange: (index: number) => void;
  /** Space — toggle the selected option (done↔backlog). */
  onToggleSelected?: () => void;
  /** Enter — open/activate the selected option (task details). */
  onActivateSelected?: () => void;
}

/**
 * Builds the listbox `onKeyDown`. Only handles keys arriving ON the container itself —
 * a key bubbling out of an inner text field (inline rename) or button is left alone, so
 * typing and native button activation are never hijacked.
 */
export function listboxKeyDown(options: ListboxKeyOptions) {
  return (event: React.KeyboardEvent<HTMLElement>): void => {
    if (event.target !== event.currentTarget) {
      return;
    }
    const { count, selectedIndex, onSelectedIndexChange } = options;

    switch (event.key) {
      case "ArrowDown":
        event.preventDefault();
        onSelectedIndexChange(Math.min(count - 1, selectedIndex + 1));
        return;
      case "ArrowUp":
        event.preventDefault();
        onSelectedIndexChange(Math.max(0, selectedIndex - 1));
        return;
      case "Home":
        event.preventDefault();
        if (count > 0) onSelectedIndexChange(0);
        return;
      case "End":
        event.preventDefault();
        if (count > 0) onSelectedIndexChange(count - 1);
        return;
      case " ":
        event.preventDefault();
        options.onToggleSelected?.();
        return;
      case "Enter":
        event.preventDefault();
        options.onActivateSelected?.();
        return;
      default:
        return;
    }
  };
}
