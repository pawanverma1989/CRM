import apiClient from './client';
import type { CustomFieldDefinitionDto } from '../types';

export async function listCustomFields(entityType = 'deal'): Promise<CustomFieldDefinitionDto[]> {
  const { data } = await apiClient.get<CustomFieldDefinitionDto[]>(`/custom-fields?entityType=${encodeURIComponent(entityType)}`);
  return data;
}

export async function createCustomField(req: {
  entityType: string;
  fieldKey: string;
  label: string;
  fieldType: string;
  options?: string[];
  isRequired?: boolean;
  sortOrder?: number;
}): Promise<CustomFieldDefinitionDto> {
  const { data } = await apiClient.post<CustomFieldDefinitionDto>('/custom-fields', req);
  return data;
}

export async function updateCustomField(
  id: string,
  req: { label?: string; options?: string[]; isRequired?: boolean; sortOrder?: number; isActive?: boolean }
): Promise<CustomFieldDefinitionDto> {
  const { data } = await apiClient.patch<CustomFieldDefinitionDto>(`/custom-fields/${id}`, req);
  return data;
}
