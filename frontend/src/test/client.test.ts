/**
 * client.ts interceptor tests
 */
import { describe, it, expect, vi, beforeEach } from "vitest";
import axios from "axios";
import { setAccessToken, registerSilentRefresh, apiClient } from "@/api/client";

const locationStub = { href: "", pathname: "/orgs" };
vi.stubGlobal("location", locationStub);

function axiosError(status: number, config: Record<string, unknown>) {
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
  locationStub.pathname = "/orgs";
});

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
          return Promise.reject(axiosError(401, config as unknown as Record<string, unknown>));
        }
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

  it("does not redirect for /auth/session 401", async () => {
    setAccessToken("old-token");

    await expect(
      apiClient.get("/auth/session", {
        adapter: (config) =>
          Promise.reject(axiosError(401, config as unknown as Record<string, unknown>)),
      }),
    ).rejects.toBeDefined();

    expect(locationStub.href).toBe("");
  });

  it("does not retry when _retry flag is already set", async () => {
    setAccessToken("old-token");
    const refreshFn = vi.fn().mockResolvedValue("new-token");
    registerSilentRefresh(refreshFn);

    let callCount = 0;

    await expect(
      apiClient.get("/protected", {
        adapter: (config) => {
          callCount++;
          return Promise.reject(axiosError(401, config as unknown as Record<string, unknown>));
        },
      }),
    ).rejects.toBeDefined();

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
    expect(locationStub.href).toBe("");
  });
});
