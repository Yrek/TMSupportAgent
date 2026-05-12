import { z } from "zod";

const optionalUrl = z.preprocess(
  (value) => (typeof value === "string" && value.trim() === "" ? undefined : value),
  z.string().url().optional(),
);

const optionalString = z.preprocess(
  (value) => (typeof value === "string" && value.trim() === "" ? undefined : value),
  z.string().optional(),
);

const devAuth = import.meta.env.VITE_DEV_AUTH === "true";
const authMode = import.meta.env.VITE_AUTH_MODE ?? "workos"; // "workos" | "entra"
const entraAuth = !devAuth && authMode === "entra";

const envSchema = z.object({
  VITE_API_BASE_URL: z.string().url("VITE_API_BASE_URL must be a valid URL"),
  // WorkOS vars are optional when VITE_DEV_AUTH=true or VITE_AUTH_MODE=entra
  VITE_WORKOS_CLIENT_ID: devAuth || entraAuth
    ? optionalString
    : z.string().min(1, "VITE_WORKOS_CLIENT_ID is required"),
  VITE_WORKOS_REDIRECT_URI: devAuth || entraAuth
    ? optionalUrl
    : z.string().url("VITE_WORKOS_REDIRECT_URI must be a valid URL"),
  VITE_DEV_AUTH: z.string().optional(),
  VITE_AUTH_MODE: z.string().optional(),
  // Entra ID vars — required when VITE_AUTH_MODE=entra
  VITE_ENTRA_TENANT_ID: entraAuth
    ? z.string().min(1, "VITE_ENTRA_TENANT_ID is required when VITE_AUTH_MODE=entra")
    : optionalString,
  VITE_ENTRA_CLIENT_ID: entraAuth
    ? z.string().min(1, "VITE_ENTRA_CLIENT_ID is required when VITE_AUTH_MODE=entra")
    : optionalString,
  // Optional — when set, errors are forwarded to Sentry with full stack traces
  VITE_SENTRY_DSN: optionalUrl,
  // Optional — when set, telemetry is sent to Azure Application Insights
  VITE_APPINSIGHTS_CONNECTION_STRING: z.string().optional(),
  // Optional — injected by CI at build time for release tracking in both Sentry and App Insights
  VITE_APP_VERSION: z.string().optional(),
});

const parsed = envSchema.safeParse(import.meta.env);

if (!parsed.success) {
  const issues = parsed.error.issues.map((i) => `  ${i.path.join(".")}: ${i.message}`).join("\n");
  throw new Error(`Missing or invalid environment variables:\n${issues}`);
}

export const env = parsed.data;

// Convenience flags
export const isDevAuth = devAuth;
export const isEntraAuth = entraAuth;
