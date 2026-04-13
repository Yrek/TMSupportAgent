import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "./client";

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
  return useQuery<SessionResponse>({
    queryKey: ["session"],
    queryFn: async () => {
      const res = await apiClient.get<SessionResponse>("/auth/session");
      return res.data;
    },
    retry: false,
    staleTime: 5 * 60 * 1000,
  });
}

export function useSignOut() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      await apiClient.delete("/auth/session");
    },
    onSuccess: () => {
      queryClient.clear();
      window.location.href = "/login";
    },
  });
}
