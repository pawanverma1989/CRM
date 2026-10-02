import apiClient from './client';
import type { LeadSourceDto } from '../types';

export async function getLeadSources(): Promise<LeadSourceDto[]> {
  const { data } = await apiClient.get<LeadSourceDto[]>('/lead-sources');
  return data;
}

export async function createLeadSource(name: string): Promise<LeadSourceDto> {
  const { data } = await apiClient.post<LeadSourceDto>('/lead-sources', { name });
  return data;
}

export async function updateLeadSource(id: string, body: Partial<LeadSourceDto>): Promise<LeadSourceDto> {
  const { data } = await apiClient.patch<LeadSourceDto>(`/lead-sources/${id}`, body);
  return data;
}
