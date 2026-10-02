import apiClient from './client';
import type { OwnerDto } from '../types';

/**
 * GET /owners — assignable users with display names, served from the service's
 * own `user_refs` copy of Identity users. Used for owner columns in lists,
 * the owner filter, and the owner / "user" custom-field pickers.
 */
export async function getOwners(): Promise<OwnerDto[]> {
  const { data } = await apiClient.get<OwnerDto[]>('/owners');
  return data;
}
