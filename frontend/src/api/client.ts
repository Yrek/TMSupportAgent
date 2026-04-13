import axios, { type AxiosError } from "axios";
import { env } from "@/lib/env";

// In-memory token store — never localStorage/sessionStorage
let accessToken: string | null = null;
let silentRefreshFn: (() => Promise<string | null>) | null = null;

export function setAccessToken(token: string | null) {
  accessToken = token;
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
  if (accessToken) {
    config.headers["Authorization"] = `Bearer ${accessToken}`;
  }
  return config;
});

let isRefreshing = false;

// On 401: attempt one silent refresh, then redirect to login
apiClient.interceptors.response.use(
  (res) => res,
  async (error: AxiosError) => {
    const originalRequest = error.config as typeof error.config & { _retry?: boolean };

    if (error.response?.status === 401) {
      if (!originalRequest._retry) {
        if (isRefreshing) {
          // Already refreshing — redirect to avoid loop
          window.location.href = "/login";
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

      // Retry also failed (or _retry was already set) — redirect to login
      window.location.href = "/login";
    }

    return Promise.reject(error);
  },
);
