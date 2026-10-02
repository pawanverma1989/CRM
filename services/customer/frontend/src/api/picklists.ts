import apiClient from './client';
import type { PicklistItemDto, PicklistType } from '../types';

/**
 * GET /picklists?type=… — the admin-managed company-industry and contact-source
 * lists (§7 change 3). `companies.industry_id` and `contacts.source_id` are FK
 * columns, so writes send the id while reads carry the resolved label too.
 */
export async function getPicklist(
  listType: PicklistType,
  includeInactive = false
): Promise<PicklistItemDto[]> {
  const params = new URLSearchParams({ type: listType });
  if (includeInactive) params.set('includeInactive', 'true');
  const { data } = await apiClient.get<PicklistItemDto[]>(`/picklists?${params}`);
  return data;
}

export interface CreatePicklistItemPayload {
  listType: PicklistType;
  value: string;
  sortOrder: number;
}

export async function createPicklistItem(
  payload: CreatePicklistItemPayload
): Promise<PicklistItemDto> {
  const { data } = await apiClient.post<PicklistItemDto>('/picklists', payload);
  return data;
}

export interface UpdatePicklistItemPayload {
  value?: string;
  sortOrder?: number;
  isActive?: boolean;
}

export async function updatePicklistItem(
  id: string,
  payload: UpdatePicklistItemPayload
): Promise<PicklistItemDto> {
  const { data } = await apiClient.patch<PicklistItemDto>(`/picklists/${id}`, payload);
  return data;
}
