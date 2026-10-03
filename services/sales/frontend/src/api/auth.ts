import { identityClient } from './client';
import type { LoginResponse } from '../types';

export async function refreshTokens(refreshToken: string): Promise<LoginResponse> {
  const { data } = await identityClient.post<LoginResponse>('/auth/refresh', { refreshToken });
  return data;
}
