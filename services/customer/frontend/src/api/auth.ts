import { identityClient } from './client';
import type { LoginResponse } from '../types';

/**
 * Single-origin SSO: the Identity app owns sign-in and leaves its refresh token
 * in localStorage under `crm_refresh_token`. This app exchanges that token for
 * an access token it keeps in memory only, and writes the rotated refresh token
 * back under the same key. There is no login screen here.
 */
export async function refreshTokens(refreshToken: string): Promise<LoginResponse> {
  const { data } = await identityClient.post<LoginResponse>('/auth/refresh', { refreshToken });
  return data;
}
