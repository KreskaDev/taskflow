// [C] Avatar catalog spec (T017, slice 019) — Google photo with initials fallback,
// deterministic AA-safe background palette from --avatar-* tokens (FR-105).
import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { Avatar } from "@/components/ui/Avatar";

describe("Avatar (catalog)", () => {
  it("renders the photo when avatarUrl is provided", () => {
    render(
      <Avatar userId="u1" displayName="Ola Audyt" avatarUrl="https://lh3.example/photo.jpg" />,
    );
    const img = screen.getByRole("img", { name: "Ola Audyt" });
    expect(img).toHaveAttribute("src", "https://lh3.example/photo.jpg");
  });

  it("falls back to initials when there is no photo", () => {
    const { container } = render(<Avatar userId="u1" displayName="Ola Audyt" />);
    expect(screen.getByText("OA")).toBeInTheDocument();
    expect(container.querySelector("img")).toBeNull();
  });

  it("falls back to initials when the photo fails to load", () => {
    render(<Avatar userId="u1" displayName="Bartek Widz" avatarUrl="https://broken.example/x" />);
    fireEvent.error(screen.getByRole("img", { name: "Bartek Widz" }));
    expect(screen.getByText("BW")).toBeInTheDocument();
  });

  it("single-word names yield a single initial", () => {
    render(<Avatar userId="u2" displayName="Ola" />);
    expect(screen.getByText("O")).toBeInTheDocument();
  });

  it("background color is deterministic per userId and drawn ONLY from the AA-safe token palette", () => {
    const { container: first } = render(<Avatar userId="stable-id" displayName="A B" />);
    const { container: second } = render(<Avatar userId="stable-id" displayName="A B" />);
    const a = (first.firstElementChild as HTMLElement).style.background;
    const b = (second.firstElementChild as HTMLElement).style.background;
    expect(a).toBe(b);
    expect(a).toMatch(/var\(--avatar-[abc]\)/);
  });

  it("initials fallback carries the accessible name", () => {
    render(<Avatar userId="u3" displayName="Celina Nowak" />);
    expect(screen.getByLabelText("Celina Nowak")).toBeInTheDocument();
  });
});
