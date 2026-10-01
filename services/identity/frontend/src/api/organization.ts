import apiClient from './client';
import type { OrganizationDto } from '../types';

export async function getOrganization(): Promise<OrganizationDto> {
  const { data } = await apiClient.get<OrganizationDto>('/organization/me');
  return data;
}

export interface UpdateOrganizationPayload {
  name?: string;
  defaultCurrency?: string;
  timezone?: string;
}

export async function updateOrganization(payload: UpdateOrganizationPayload): Promise<OrganizationDto> {
  const { data } = await apiClient.put<OrganizationDto>('/organization/me', payload);
  return data;
}
