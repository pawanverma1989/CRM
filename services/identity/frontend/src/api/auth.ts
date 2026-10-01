import apiClient from './client';
import type { LoginResponse, UserDto } from '../types';

export interface LoginPayload {
  email: string;
  password: string;
}

export async function login(payload: LoginPayload): Promise<LoginResponse> {
  const { data } = await apiClient.post<LoginResponse>('/auth/login', payload);
  return data;
}

export async function refreshTokens(refreshToken: string): Promise<LoginResponse> {
  const { data } = await apiClient.post<LoginResponse>('/auth/refresh', { refreshToken });
  return data;
}

export async function logout(): Promise<void> {
  await apiClient.post('/auth/logout');
}

export async function logoutAll(): Promise<void> {
  await apiClient.post('/auth/logout-all');
}

export async function forgotPassword(email: string): Promise<{ message: string }> {
  const { data } = await apiClient.post<{ message: string }>('/password/forgot', { email });
  return data;
}

export async function resetPassword(token: string, password: string): Promise<{ message: string }> {
  const { data } = await apiClient.post<{ message: string }>('/password/reset', { token, password });
  return data;
}

export async function acceptInvitation(payload: {
  token: string;
  firstName: string;
  lastName: string;
  password: string;
}): Promise<{ message: string }> {
  const { data } = await apiClient.post<{ message: string }>('/invitations/accept', payload);
  return data;
}

export async function getMe(): Promise<UserDto> {
  const { data } = await apiClient.get<UserDto>('/me');
  return data;
}
