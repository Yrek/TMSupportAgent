/**
 * F-T06 — client.ts interceptor tests
 *
 * Uses per-request adapter overrides to control HTTP responses without
 * a real server. Module-level state (accessToken, silentRefreshFn) is
 * reset before each test via the exported setter/register functions.
 */
import { describe, it, expect, vi, beforeEach } from "vitest";
import axios from "axios";
import { setAccessToken, registerSilentRefresh, apiClient } from "@/api/client";

// Replace window.location with a plain object so the setter can be observed
// without jsdom throwing a navigation error.
const locationStub = { href: "" };
vi.stubGlobal("location", locationStub);

// Helper — build an AxiosError for a given HTTP status
function axiosError(status: number, config: Record<string, unknown>) {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const axiosCfg = config as any;
  return new axios.AxiosError(
    "Request failed",
    `ERR_${status}`,
    axiosCfg,
    null,
    {
      status,
      statusText: status === 401 ? "Unauthorized" : "Error",
      data: {},
      headers: {},
      config: axiosCfg,
    },
  );
}

beforeEach(() => {
  setAccessToken(null);
  registerSilentRefresh(async () => null);
  locationStub.href = "";
});

// ────────────────────────────────────────────────────────────────────────────
// Request interceptor
// ────────────────────────────────────────────────────────────────────────────

describe("request interceptor", () => {
  it("attaches Authorization header when an access token is set", async () => {
    setAccessToken("tok-abc-123");

    let captured: Record<string, string> = {};

    await apiClient.get("/test", {
      adapter: (config) => {
        captured = config.headers as Record<string, string>;
        return Promise.resolve({ data: {}, status: 200, statusText: "OK", headers: {}, config });
      },
    });

    expect(captured["Authorization"]).toBe("Bearer tok-abc-123");
  });

  it("does not attach Authorization header when no token is set", async () => {
    setAccessToken(null);

    let captured: Record<string, string> = {};

    await apiClient.get("/test", {
      adapter: (config) => {
        captured = config.headers as Record<string, string>;
        return Promise.resolve({ data: {}, status: 200, statusText: "OK", headers: {}, config });
      },
    });

    expect(captured["Authorization"]).toBeUndefined();
  });
});

// ────────────────────────────────────────────────────────────────────────────
// Response interceptor — 401 handling
// ────────────────────────────────────────────────────────────────────────────

describe("response interceptor", () => {
  it("calls silentRefreshFn and retries with new token on first 401", async () => {
    setAccessToken("old-token");
    const refreshFn = vi.fn().mockResolvedValue("new-token");
    registerSilentRefresh(refreshFn);

    let callCount = 0;

    const result = await apiClient.get("/protected", {
      adapter: (config) => {
        callCount++;
        if (callCount === 1) {
          // First attempt: 401
          return Promise.reject(axiosError(401, config as unknown as Record<string, unknown>));
        }
        // Retry: success with the updated Authorization header
        return Promise.resolve({ data: { ok: true }, status: 200, statusText: "OK", headers: {}, config });
      },
    });

    expect(refreshFn).toHaveBeenCalledOnce();
    expect(callCount).toBe(2);
    expect(result.data).toEqual({ ok: true });
  });

  it("redirects to /login when silentRefreshFn returns null", async () => {
    setAccessToken("old-token");
    registerSilentRefresh(async () => null);

    await expect(
      apiClient.get("/protected", {
        adapter: (config) =>
          Promise.reject(axiosError(401, config as unknown as Record<string, unknown>)),
      }),
    ).rejects.toBeDefined();

    expect(locationStub.href).toBe("/login");
  });

  it("redirects to /login when silentRefreshFn throws", async () => {
    setAccessToken("old-token");
    registerSilentRefresh(async () => {
      throw new Error("WorkOS unavailable");
    });

    await expect(
      apiClient.get("/protected", {
        adapter: (config) =>
          Promise.reject(axiosError(401, config as unknown as Record<string, unknown>)),
      }),
    ).rejects.toBeDefined();

    expect(locationStub.href).toBe("/login");
  });

  it("does not retry when _retry flag is already set (prevents infinite loop)", async () => {
    setAccessToken("old-token");
    const refreshFn = vi.fn().mockResolvedValue("new-token");
    registerSilentRefresh(refreshFn);

    let callCount = 0;

    // Both attempts return 401 — second one has _retry=true so interceptor skips retry
    await expect(
      apiClient.get("/protected", {
        adapter: (config) => {
          callCount++;
          return Promise.reject(axiosError(401, config as unknown as Record<string, unknown>));
        },
      }),
    ).rejects.toBeDefined();

    // refreshFn called once (for first 401), second 401 goes straight to /login
    expect(refreshFn).toHaveBeenCalledOnce();
    expect(callCount).toBe(2);
    expect(locationStub.href).toBe("/login");
  });

  it("passes non-401 errors through without retrying", async () => {
    setAccessToken("tok");
    const refreshFn = vi.fn();
    registerSilentRefresh(refreshFn);

    await expect(
      apiClient.get("/protected", {
        adapter: (config) =>
          Promise.reject(axiosError(403, config as unknown as Record<string, unknown>)),
      }),
    ).rejects.toBeDefined();

    expect(refreshFn).not.toHaveBeenCalled();
    expect(locationStub.href).toBe(""); // no redirect
  });
});
