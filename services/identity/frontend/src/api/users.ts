import apiClient from './client';
import type { UserDto, UserRole } from '../types';

export interface UsersFilter {
  role?: UserRole;
  teamId?: string;
  status?: string;
  search?: string;
  cursor?: string;
  limit?: number;
}

export interface UsersResponse {
  data: UserDto[];
  nextCursor?: string;
  hasMore: boolean;
}

export async function getUsers(filter: UsersFilter = {}): Promise<UsersResponse> {
  const params = new URLSearchParams();
  if (filter.role) params.set('role', filter.role);
  if (filter.teamId) params.set('teamId', filter.teamId);
  if (filter.status) params.set('status', filter.status);
  if (filter.search) params.set('search', filter.search);
  if (filter.cursor) params.set('cursor', filter.cursor);
  params.set('limit', String(filter.limit ?? 50));

  const { data } = await apiClient.get<UsersResponse>(`/users?${params}`);
  return data;
}

export async function getUser(id: string): Promise<UserDto> {
  const { data } = await apiClient.get<UserDto>(`/users/${id}`);
  return data;
}

export interface UpdateUserPayload {
  role?: UserRole;
  teamId?: string | null;
}

export async function updateUser(id: string, payload: UpdateUserPayload): Promise<UserDto> {
  const { data } = await apiClient.patch<UserDto>(`/users/${id}`, payload);
  return data;
}

export interface InviteUserPayload {
  email: string;
  role: UserRole;
  teamId?: string;
}

export async function inviteUser(payload: InviteUserPayload): Promise<UserDto> {
  const { data } = await apiClient.post<UserDto>('/users/invitations', payload);
  return data;
}

export interface CreateUserPayload {
  email: string;
  password: string;
  firstName: string;
  lastName?: string;
  phone?: string;
  role: UserRole;
  teamId?: string;
}

export async function createUser(payload: CreateUserPayload): Promise<UserDto> {
  const { data } = await apiClient.post<UserDto>('/users', payload);
  return data;
}

export async function resendInvite(invitationId: string): Promise<void> {
  await apiClient.post(`/users/invitations/${invitationId}/resend`);
}

export async function cancelInvite(invitationId: string): Promise<void> {
  await apiClient.delete(`/users/invitations/${invitationId}`);
}

export async function deactivateUser(id: string): Promise<void> {
  await apiClient.post(`/users/${id}/deactivate`);
}

export async function reactivateUser(id: string): Promise<void> {
  await apiClient.post(`/users/${id}/reactivate`);
}

export async function logoutUserAll(id: string): Promise<void> {
  await apiClient.post(`/users/${id}/logout-all`);
}
