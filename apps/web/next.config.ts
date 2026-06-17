import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  reactStrictMode: true,
  // Emit a self-contained server bundle for the multi-stage Docker runtime image.
  output: "standalone",
  // `pg` is a Node-only library (uses `fs`/`net`); keep it external so webpack never tries to
  // bundle it for the instrumentation/edge analysis pass. It is require()'d at runtime in Node.
  serverExternalPackages: ["pg"],
  // Security response headers (FR-099) baseline. CSP is tightened in the API/edge layer;
  // these apply to BFF-served responses.
  async headers() {
    return [
      {
        source: "/:path*",
        headers: [
          { key: "X-Content-Type-Options", value: "nosniff" },
          { key: "X-Frame-Options", value: "DENY" },
          { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
        ],
      },
    ];
  },
};

export default nextConfig;
