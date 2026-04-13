import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "./client";

export interface Member {
  userId: string;
  role: "owner" | "member";
  joinedAt: string;
}

export interface InviteMemberRequest {
  email: string;
  role: "owner" | "member";
}

export function useMembers(orgId: string) {
  return useQuery<Member[]>({
    queryKey: ["members", orgId],
    queryFn: async () => {
      const res = await apiClient.get<{ data: Member[] }>(`/orgs/${orgId}/members`);
      return res.data.data;
    },
    enabled: !!orgId,
  });
}

export function useInviteMember(orgId: string) {
  return useMutation({
    mutationFn: async (req: InviteMemberRequest) => {
      await apiClient.post(`/orgs/${orgId}/members`, req);
    },
  });
}

export function useUpdateMemberRole(orgId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ userId, role }: { userId: string; role: "owner" | "member" }) => {
      const res = await apiClient.patch<Member>(`/orgs/${orgId}/members/${userId}`, { role });
      return res.data;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["members", orgId] });
    },
  });
}

export function useRemoveMember(orgId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (userId: string) => {
      await apiClient.delete(`/orgs/${orgId}/members/${userId}`);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["members", orgId] });
    },
  });
}
