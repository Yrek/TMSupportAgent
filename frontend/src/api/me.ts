import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "./client";

export interface MeResponse {
  id: string;
  workosUserId: string;
  createdAt: string;
}

export function useMe() {
  return useQuery<MeResponse>({
    queryKey: ["me"],
    queryFn: async () => {
      const res = await apiClient.get<MeResponse>("/me");
      return res.data;
    },
  });
}

export function useDeleteAccount() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      await apiClient.delete("/me");
    },
    onSuccess: () => {
      queryClient.clear();
      window.location.href = "/login";
    },
  });
}
