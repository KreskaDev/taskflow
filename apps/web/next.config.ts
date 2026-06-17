import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  reactStrictMode: true,
  // Emit a self-contained server bundle for the multi-stage Docker runtime image.
  output: "standalone",
  // `pg` is a Node-only library (uses `fs`/`net`); keep it external so webpack never tries to
  // bundle it for the instrumentation/edge analysis pass. It is require()'d at runtime in Node.
  serverExternalPackages: ["pg"],
  // Security response headers (FR-099, Constitution XII) baseline for BFF-served HTML — the surface a
  // browser actually renders. The API CSP (`default-src 'none'`) only guards its JSON; this is the CSP
  // that protects rendered output (e.g. the Google profile fields on /settings).
  //
  // `img-src` MUST allow the Google avatar hosts because /settings renders `<img src={avatarUrl}>`
  // with a `lh3.googleusercontent.com` URL. `script-src`/`style-src` carry `'unsafe-inline'` because
  // Next.js 15 (App Router) emits an inline bootstrap/hydration script and inline styles; without it,
  // client components (the delete Dialog) never hydrate. Tightening this to a nonce-based CSP via
  // middleware is a deliberate later-slice hardening (the FR-099 baseline requirement is a CSP being
  // *present* on the rendered surface, which this establishes).
  //
  // `next dev` evaluates client modules via `eval()` (webpack/React Refresh/HMR), so `'unsafe-eval'`
  // is added to `script-src` ONLY in development. The production CSP (what ships, and what FR-099
  // governs) never carries `'unsafe-eval'` — the App Router prod bootstrap is inline, not eval.
  async headers() {
    const isDev = process.env.NODE_ENV !== "production";
    const scriptSrc = isDev
      ? "script-src 'self' 'unsafe-inline' 'unsafe-eval'"
      : "script-src 'self' 'unsafe-inline'";

    const csp = [
      "default-src 'self'",
      "img-src 'self' https://lh3.googleusercontent.com https://*.googleusercontent.com data:",
      "style-src 'self' 'unsafe-inline'",
      scriptSrc,
      "connect-src 'self'",
      "font-src 'self'",
      "frame-ancestors 'none'",
      "base-uri 'self'",
      "form-action 'self'",
    ].join("; ");

    return [
      {
        source: "/:path*",
        headers: [
          { key: "Content-Security-Policy", value: csp },
          { key: "X-Content-Type-Options", value: "nosniff" },
          { key: "X-Frame-Options", value: "DENY" },
          { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
        ],
      },
    ];
  },
};

export default nextConfig;
