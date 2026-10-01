import apiClient from './client';
import type { UserDto, SessionDto } from '../types';

export interface UpdateMePayload {
  firstName?: string;
  lastName?: string;
  phone?: string;
}

export interface ChangePasswordPayload {
  currentPassword: string;
  newPassword: string;
}

export async function updateMe(payload: UpdateMePayload): Promise<UserDto> {
  const { data } = await apiClient.patch<UserDto>('/me', payload);
  return data;
}

export async function changePassword(payload: ChangePasswordPayload): Promise<void> {
  await apiClient.post('/me/password', payload);
}

export async function getSessions(): Promise<SessionDto[]> {
  const { data } = await apiClient.get<SessionDto[]>('/me/sessions');
  return data;
}

export async function revokeSession(sessionId: string): Promise<void> {
  await apiClient.delete(`/me/sessions/${sessionId}`);
}
