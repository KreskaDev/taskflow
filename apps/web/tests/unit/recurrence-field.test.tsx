import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import {
  RECURRENCE_INTERVAL_ERROR,
  RECURRENCE_NO_DUE_HINT,
  RECURRENCE_WEEKDAYS_ERROR,
  RecurrenceField,
  type RecurrenceFieldChange,
} from "@/components/tasks/RecurrenceField";
import { fromReferenceZone } from "@/lib/timezone";

/**
 * The shared „Powtarzanie" field (slice 012, T016 — contracts/ui-recurrence.md): the five options, each
 * type's inputs and validity, the preview text, and the disabled hint without a due date.
 * [INV-176] [INV-177] [INV-178] [INV-179] [INV-180]
 */

afterEach(cleanup);

/** 2026-06-10 00:00 Europe/Warsaw (date-only due). */
const DUE = fromReferenceZone(new Date(2026, 5, 10, 0, 0, 0, 0));

function renderField(props: Partial<Parameters<typeof RecurrenceField>[0]> = {}) {
  const onChange = vi.fn<(change: RecurrenceFieldChange) => void>();
  render(<RecurrenceField rule={null} dueDate={DUE} dueHasTime={false} onChange={onChange} {...props} />);
  return { onChange, last: () => onChange.mock.calls.at(-1)?.[0] };
}

describe("RecurrenceField [INV-176]", () => {
  it("renders the labelled select with the five options, defaulting to „Nie powtarza się”", () => {
    renderField();
    const select = screen.getByRole("combobox", { name: "Powtarzanie" }) as HTMLSelectElement;
    expect(select.value).toBe("none");
    expect(Array.from(select.options).map((o) => o.textContent)).toEqual([
      "Nie powtarza się",
      "Codziennie",
      "Co N dni",
      "W wybrane dni tygodnia",
      "Co miesiąc",
    ]);
    expect(screen.queryByText(RECURRENCE_NO_DUE_HINT)).toBeNull();
  });

  it("is disabled with the visible hint „Ustaw najpierw termin” when the task has no due date", () => {
    renderField({ dueDate: null });
    const select = screen.getByRole("combobox", { name: "Powtarzanie" });
    expect(select).toBeDisabled();
    expect(screen.getByText(RECURRENCE_NO_DUE_HINT)).toBeTruthy();
  });

  it("„Codziennie” reports a daily rule and previews tomorrow [INV-180]", () => {
    const { last } = renderField();
    fireEvent.change(screen.getByRole("combobox", { name: "Powtarzanie" }), { target: { value: "daily" } });
    expect(last()).toEqual({ rule: { type: "daily" }, error: null });
    expect(screen.getByText("Następne wystąpienie: 11.06.2026")).toBeTruthy();
  });

  it("seeds from an existing rule", () => {
    renderField({ rule: { type: "every_n_days", interval: 5 } });
    expect((screen.getByRole("combobox", { name: "Powtarzanie" }) as HTMLSelectElement).value).toBe("every_n_days");
    expect((screen.getByRole("spinbutton", { name: "Co ile dni" }) as HTMLInputElement).value).toBe("5");
  });
});

describe("RecurrenceField — „Co N dni” [INV-177]", () => {
  it("shows the labelled number input (2..365) and reports the interval", () => {
    const { last } = renderField();
    fireEvent.change(screen.getByRole("combobox", { name: "Powtarzanie" }), { target: { value: "every_n_days" } });
    const input = screen.getByRole("spinbutton", { name: "Co ile dni" }) as HTMLInputElement;
    expect(input.min).toBe("2");
    expect(input.max).toBe("365");
    expect(last()).toEqual({ rule: { type: "every_n_days", interval: 2 }, error: null });

    fireEvent.change(input, { target: { value: "3" } });
    expect(last()).toEqual({ rule: { type: "every_n_days", interval: 3 }, error: null });
    expect(screen.getByText("Następne wystąpienie: 13.06.2026")).toBeTruthy();
  });

  it("blocks an out-of-range interval with an inline error", () => {
    const { last } = renderField();
    fireEvent.change(screen.getByRole("combobox", { name: "Powtarzanie" }), { target: { value: "every_n_days" } });
    fireEvent.change(screen.getByRole("spinbutton", { name: "Co ile dni" }), { target: { value: "1" } });
    expect(last()).toEqual({ rule: null, error: RECURRENCE_INTERVAL_ERROR });
    expect(screen.getByRole("alert")).toHaveTextContent(RECURRENCE_INTERVAL_ERROR);
  });
});

describe("RecurrenceField — „W wybrane dni tygodnia” [INV-178]", () => {
  it("renders Pn..Nd checkboxes in ISO order and blocks the save while none is checked", () => {
    const { last } = renderField();
    fireEvent.change(screen.getByRole("combobox", { name: "Powtarzanie" }), { target: { value: "weekdays" } });
    const boxes = screen.getAllByRole("checkbox");
    expect(boxes.map((b) => b.getAttribute("aria-label"))).toEqual([
      "poniedziałek",
      "wtorek",
      "środa",
      "czwartek",
      "piątek",
      "sobota",
      "niedziela",
    ]);
    expect(last()).toEqual({ rule: null, error: RECURRENCE_WEEKDAYS_ERROR });
    expect(screen.getByRole("alert")).toHaveTextContent(RECURRENCE_WEEKDAYS_ERROR);

    fireEvent.click(screen.getByRole("checkbox", { name: "środa" }));
    fireEvent.click(screen.getByRole("checkbox", { name: "poniedziałek" }));
    expect(last()).toEqual({ rule: { type: "weekdays", weekdays: [1, 3] }, error: null });
    // 2026-06-10 is a Wednesday → the next listed day is Monday 15.06.
    expect(screen.getByText("Następne wystąpienie: 15.06.2026")).toBeTruthy();
  });
});

describe("RecurrenceField — „Co miesiąc” [INV-179]", () => {
  it("anchors on the due date's Warsaw day-of-month and shows it as text", () => {
    const { last } = renderField();
    fireEvent.change(screen.getByRole("combobox", { name: "Powtarzanie" }), { target: { value: "monthly" } });
    expect(last()).toEqual({ rule: { type: "monthly", dayOfMonth: 10 }, error: null });
    expect(screen.getByText("co miesiąc, 10. dnia")).toBeTruthy();
    expect(screen.getByText("Następne wystąpienie: 10.07.2026")).toBeTruthy();
  });

  it("shows the stored anchor of an existing monthly rule, not the due's day", () => {
    renderField({ rule: { type: "monthly", dayOfMonth: 31 }, dueDate: fromReferenceZone(new Date(2026, 1, 28)) });
    expect(screen.getByText("co miesiąc, 31. dnia")).toBeTruthy();
    expect(screen.getByText("Następne wystąpienie: 31.03.2026")).toBeTruthy();
  });
});
