import type { Metadata } from "next";
import type { ReactNode } from "react";
import { LiveRegion } from "@/components/ui/LiveRegion";
import { Providers } from "./providers";
import "./globals.css";

export const metadata: Metadata = {
  title: "TaskFlow",
  description: "Fast, quiet, keyboard-driven collaborative task management.",
};

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    <html lang="en">
      <body>
        <Providers>{children}</Providers>
        {/* App-level polite live region for server-initiated announcements (Constitution II). */}
        <LiveRegion />
      </body>
    </html>
  );
}
