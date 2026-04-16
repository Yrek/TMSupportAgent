import axios, { type AxiosError } from "axios";
import { env } from "@/lib/env";

// In-memory token store - never localStorage/sessionStorage
let accessToken: string | null = null;
let silentRefreshFn: (() => Promise<string | null>) | null = null;

export function setAccessToken(token: string | null) {
  accessToken = token;
}

export function hasAccessToken() {
  return !!accessToken;
}

export function registerSilentRefresh(fn: () => Promise<string | null>) {
  silentRefreshFn = fn;
}

export const apiClient = axios.create({
  baseURL: env.VITE_API_BASE_URL,
  headers: { "Content-Type": "application/json" },
});

// Attach Bearer token on every request
apiClient.interceptors.request.use((config) => {
  // Let the browser set multipart boundaries for FormData uploads.
  // If JSON content-type is forced here, ASP.NET model binding for IFormFile fails.
  if (typeof FormData !== "undefined" && config.data instanceof FormData) {
    if (config.headers) {
      delete config.headers["Content-Type"];
    }
  }

  if (accessToken) {
    config.headers["Authorization"] = `Bearer ${accessToken}`;
  }
  return config;
});

let isRefreshing = false;

function isAuthSessionRequest(url?: string) {
  return !!url && url.includes("/auth/session");
}

function isAuthRoute(pathname?: string) {
  const path = pathname ?? "";
  return path === "/login" || path.startsWith("/auth/callback");
}

// On 401: attempt one silent refresh, then redirect to login
apiClient.interceptors.response.use(
  (res) => res,
  async (error: AxiosError) => {
    const originalRequest = error.config as typeof error.config & { _retry?: boolean };

    if (error.response?.status === 401) {
      // /auth/session often returns 401 when user has no API token yet.
      // Do not redirect here to avoid auth redirect loops.
      if (isAuthSessionRequest(originalRequest?.url)) {
        accessToken = null;
        return Promise.reject(error);
      }

      if (!originalRequest._retry) {
        if (isRefreshing) {
          if (!isAuthRoute(window.location.pathname)) {
            window.location.href = "/login";
          }
          return Promise.reject(error);
        }

        originalRequest._retry = true;
        isRefreshing = true;

        try {
          if (silentRefreshFn) {
            const newToken = await silentRefreshFn();
            if (newToken) {
              accessToken = newToken;
              if (originalRequest.headers) {
                originalRequest.headers["Authorization"] = `Bearer ${newToken}`;
              }
              isRefreshing = false;
              return apiClient(originalRequest);
            }
          }
        } catch {
          // silent refresh failed
        }

        isRefreshing = false;
        accessToken = null;
      }

      if (!isAuthRoute(window.location.pathname)) {
        window.location.href = "/login";
      }
    }

    return Promise.reject(error);
  },
);
