import { z } from "zod";

const optionalUrl = z.preprocess(
  (value) => (typeof value === "string" && value.trim() === "" ? undefined : value),
  z.string().url().optional(),
);

const envSchema = z.object({
  VITE_API_BASE_URL: z.string().url("VITE_API_BASE_URL must be a valid URL"),
  VITE_WORKOS_CLIENT_ID: z.string().min(1, "VITE_WORKOS_CLIENT_ID is required"),
  VITE_WORKOS_REDIRECT_URI: z.string().url("VITE_WORKOS_REDIRECT_URI must be a valid URL"),
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
