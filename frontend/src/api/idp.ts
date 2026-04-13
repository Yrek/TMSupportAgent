import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "./client";

export interface IdpConfig {
  providerType: string;
  domainHints: string[];
  createdAt: string;
  updatedAt?: string;
}

export interface UpsertIdpConfigRequest {
  providerType: string;
  domainHints: string[];
  workosConnectionId: string;
}

export function useIdpConfig(orgId: string) {
  return useQuery<IdpConfig | null>({
    queryKey: ["idp", orgId],
    queryFn: async () => {
      try {
        const res = await apiClient.get<IdpConfig>(`/orgs/${orgId}/idp`);
        return res.data;
      } catch (err: unknown) {
        if ((err as { response?: { status: number } }).response?.status === 404) return null;
        throw err;
      }
    },
    enabled: !!orgId,
  });
}

export function useUpsertIdpConfig(orgId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (req: UpsertIdpConfigRequest) => {
      const res = await apiClient.put<IdpConfig>(`/orgs/${orgId}/idp`, req);
      return res.data;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["idp", orgId] });
    },
  });
}

export function useDeleteIdpConfig(orgId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      await apiClient.delete(`/orgs/${orgId}/idp`);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["idp", orgId] });
    },
  });
}
