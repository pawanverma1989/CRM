import apiClient from './client';
import type { CustomFieldDefinitionDto, CustomFieldType, EntityType } from '../types';

/**
 * GET /custom-fields?entity=contact|company — definitions for forms and lists
 * (CF-1). `includeInactive` is for the admin management screen, which has to
 * show deactivated fields so they can be switched back on (CF-5).
 */
export async function getCustomFields(
  entity: EntityType,
  includeInactive = false
): Promise<CustomFieldDefinitionDto[]> {
  const params = new URLSearchParams({ entity });
  if (includeInactive) params.set('includeInactive', 'true');
  const { data } = await apiClient.get<CustomFieldDefinitionDto[]>(`/custom-fields?${params}`);
  return data;
}

export interface CreateCustomFieldPayload {
  entityType: EntityType;
  fieldKey: string;
  label: string;
  fieldType: CustomFieldType;
  options?: string[] | null;
  isRequired: boolean;
  sortOrder: number;
}

export async function createCustomField(
  payload: CreateCustomFieldPayload
): Promise<CustomFieldDefinitionDto> {
  const { data } = await apiClient.post<CustomFieldDefinitionDto>('/custom-fields', payload);
  return data;
}

/** CF-6: the key and the type are not in this payload — they cannot change. */
export interface UpdateCustomFieldPayload {
  label?: string;
  options?: string[] | null;
  isRequired?: boolean;
  sortOrder?: number;
  isActive?: boolean;
}

export async function updateCustomField(
  id: string,
  payload: UpdateCustomFieldPayload
): Promise<CustomFieldDefinitionDto> {
  const { data } = await apiClient.patch<CustomFieldDefinitionDto>(`/custom-fields/${id}`, payload);
  return data;
}
