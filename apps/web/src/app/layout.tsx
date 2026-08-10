import type { Metadata } from "next";
import type { ReactNode } from "react";
import { Geist, Instrument_Serif, JetBrains_Mono } from "next/font/google";
import { LiveRegion } from "@/components/ui/LiveRegion";
import { Providers } from "./providers";
import "./globals.css";

/**
 * Typography (design-brief, D3): Geist (UI sans), Instrument Serif (brand wordmark ONLY),
 * JetBrains Mono (task identifiers / inline code). `next/font/google` downloads at BUILD
 * time and self-hosts the woff2 with the app — no runtime CDN request, `font-src 'self'`
 * stays intact (Constitution V / CSP).
 */
const geist = Geist({
  subsets: ["latin", "latin-ext"],
  variable: "--font-sans",
  display: "swap",
});

const instrumentSerif = Instrument_Serif({
  subsets: ["latin", "latin-ext"],
  weight: "400",
  variable: "--font-display",
  display: "swap",
});

const jetbrainsMono = JetBrains_Mono({
  subsets: ["latin", "latin-ext"],
  variable: "--font-mono",
  display: "swap",
});

export const metadata: Metadata = {
  title: "TaskFlow",
  description: "Szybkie, ciche, wspólne zarządzanie zadaniami.",
};

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    // Server-rendered hard default palette (D1, S1.2): the composite class is in the SSR
    // payload, so the dark-cool tokens apply before hydration — no FOUC. Palette switching
    // UI/persistence is slice 018; tests re-theme by swapping this class only.
    <html
      lang="pl"
      className={`dark-cool ${geist.variable} ${instrumentSerif.variable} ${jetbrainsMono.variable}`}
    >
      <body>
        <Providers>{children}</Providers>
        {/* App-level polite live region for server-initiated announcements (Constitution II). */}
        <LiveRegion />
      </body>
    </html>
  );
}
