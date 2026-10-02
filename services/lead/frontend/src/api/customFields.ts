import apiClient from './client';
import type { CustomFieldDefinitionDto } from '../types';

/**
 * GET /custom-fields — definitions for forms and lists.
 * `includeInactive` is for the admin management screen, which has to show
 * deactivated fields so they can be switched back on.
 */
export async function getCustomFields(includeInactive = false): Promise<CustomFieldDefinitionDto[]> {
  const params = new URLSearchParams();
  if (includeInactive) params.set('includeInactive', 'true');
  const { data } = await apiClient.get<CustomFieldDefinitionDto[]>(`/custom-fields?${params}`);
  return data;
}

export async function createCustomField(body: Record<string, unknown>): Promise<CustomFieldDefinitionDto> {
  const { data } = await apiClient.post<CustomFieldDefinitionDto>('/custom-fields', body);
  return data;
}

export async function updateCustomField(id: string, body: Record<string, unknown>): Promise<CustomFieldDefinitionDto> {
  const { data } = await apiClient.patch<CustomFieldDefinitionDto>(`/custom-fields/${id}`, body);
  return data;
}
