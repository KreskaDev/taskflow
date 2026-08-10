// [C] Skeleton catalog spec (T015, slice 019) — decorative (aria-hidden), variants,
// reduced-motion-safe shimmer (the animation is CSS-only, guarded globally).
import { render } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { Skeleton } from "@/components/ui/Skeleton";

describe("Skeleton (catalog)", () => {
  it("is hidden from assistive technology (decorative placeholder)", () => {
    const { container } = render(<Skeleton variant="row" />);
    expect(container.firstElementChild).toHaveAttribute("aria-hidden", "true");
  });

  it("renders row and block variants", () => {
    const { container: row } = render(<Skeleton variant="row" />);
    const { container: block } = render(<Skeleton variant="block" />);
    expect(row.firstElementChild?.className).toContain("row");
    expect(block.firstElementChild?.className).toContain("block");
  });

  it("row variant repeats for a given count", () => {
    const { container } = render(<Skeleton variant="row" count={3} />);
    expect(container.querySelectorAll("[data-skeleton-row]")).toHaveLength(3);
  });
});
