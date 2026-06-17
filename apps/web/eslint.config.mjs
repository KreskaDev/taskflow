import { dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { FlatCompat } from "@eslint/eslintrc";

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);
const compat = new FlatCompat({ baseDirectory: __dirname });

const eslintConfig = [
  ...compat.extends("next/core-web-vitals", "next/typescript"),
  {
    rules: {
      // Constitution VI: `any` is forbidden project-wide.
      "@typescript-eslint/no-explicit-any": "error",
    },
  },
  {
    ignores: [".next/**", "node_modules/**", "src/lib/api/generated/**"],
  },
];

export default eslintConfig;
