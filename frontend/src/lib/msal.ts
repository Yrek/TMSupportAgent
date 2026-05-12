import { PublicClientApplication, type Configuration } from "@azure/msal-browser";
import { env } from "./env";

const msalConfig: Configuration = {
  auth: {
    clientId: env.VITE_ENTRA_CLIENT_ID ?? "",
    authority: `https://login.microsoftonline.com/${env.VITE_ENTRA_TENANT_ID ?? "common"}`,
    redirectUri: window.location.origin + "/auth/callback",
    postLogoutRedirectUri: window.location.origin + "/login",
  },
  cache: {
    cacheLocation: "sessionStorage", // never localStorage — aligns with in-memory token model
  },
};

// Singleton — shared across all components. Initialized before the app renders.
export const msalInstance = new PublicClientApplication(msalConfig);

export const entraLoginRequest = {
  scopes: [`api://${env.VITE_ENTRA_CLIENT_ID}/access_as_user`],
};
