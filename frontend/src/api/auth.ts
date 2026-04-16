import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useAuth } from "@workos-inc/authkit-react";
import { useLocation } from "react-router-dom";
import { apiClient, hasAccessToken, setAccessToken } from "./client";

export interface OrgSummary {
  id: string;
  name: string;
  slug: string;
  role: "owner" | "member";
  workosOrgId: string | null;
}

export interface SessionResponse {
  userId: string | null;
  orgs: OrgSummary[];
  isPlatformAdmin: boolean;
}

export function useSession() {
  const location = useLocation();
  const { user, isLoading } = useAuth();
  const isAuthPage = location.pathname === "/login" || location.pathname.startsWith("/auth/callback");

  return useQuery<SessionResponse>({
    queryKey: ["session"],
    queryFn: async () => {
      const res = await apiClient.get<SessionResponse>("/auth/session");
      return res.data;
    },
    // Prevent noisy 401 loops before token is available and on auth pages.
    enabled: !isLoading && !!user && hasAccessToken() && !isAuthPage,
    retry: false,
    refetchOnWindowFocus: false,
    refetchOnReconnect: false,
    staleTime: 5 * 60 * 1000,
  });
}

export function useSignOut() {
  const { signOut } = useAuth();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      // Server-side signout endpoint is best-effort for stateless JWT mode.
      // Always complete IdP/browser logout so local auth state is fully cleared.
      try {
        await apiClient.delete("/auth/session");
      } catch {
        // ignore and continue with identity-provider logout
      }

      queryClient.clear();
      setAccessToken(null);
      signOut({ returnTo: `${window.location.origin}/login` });
    },
  });
}
