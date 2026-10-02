import apiClient from './client';
import type { LeadDto, PagedResult } from '../types';
import { DEFAULT_PAGE_SIZE } from '../lib/validation';

export type LeadSortField = 'created_at' | 'updated_at' | 'name' | 'status' | 'owner';
export type SortDirection = 'asc' | 'desc';

export interface LeadListParams {
  q?: string;
  status?: string;
  ownerId?: string;
  unassigned?: boolean;
  leadSourceId?: string;
  utmCampaign?: string;
  tags?: string[];
  createdFrom?: string;
  createdTo?: string;
  sort?: LeadSortField;
  direction?: SortDirection;
  page?: number;
  pageSize?: number;
}

export async function getLeads(params: LeadListParams = {}): Promise<PagedResult<LeadDto>> {
  const search = new URLSearchParams();
  if (params.q) search.set('q', params.q);
  if (params.status) search.set('status', params.status);
  if (params.ownerId) search.set('ownerId', params.ownerId);
  if (params.unassigned) search.set('unassigned', 'true');
  if (params.leadSourceId) search.set('leadSourceId', params.leadSourceId);
  if (params.utmCampaign) search.set('utmCampaign', params.utmCampaign);
  if (params.tags?.length) params.tags.forEach((t) => search.append('tags', t));
  if (params.createdFrom) search.set('createdFrom', params.createdFrom);
  if (params.createdTo) search.set('createdTo', params.createdTo);
  search.set('sortBy', params.sort ?? 'updated_at');
  search.set('sortDir', params.direction ?? 'desc');
  search.set('page', String(params.page ?? 1));
  search.set('pageSize', String(params.pageSize ?? DEFAULT_PAGE_SIZE));
  const { data } = await apiClient.get<PagedResult<LeadDto>>(`/leads?${search}`);
  return data;
}

export async function getLead(id: string): Promise<LeadDto> {
  const { data } = await apiClient.get<LeadDto>(`/leads/${id}`);
  return data;
}

export async function createLead(body: Record<string, unknown>): Promise<LeadDto> {
  const { data } = await apiClient.post<LeadDto>('/leads', body);
  return data;
}

export async function updateLead(id: string, body: Record<string, unknown>): Promise<LeadDto> {
  const { data } = await apiClient.patch<LeadDto>(`/leads/${id}`, body);
  return data;
}

export async function deleteLead(id: string): Promise<void> {
  await apiClient.delete(`/leads/${id}`);
}

export async function bulkAssign(leadIds: string[], ownerId: string | null): Promise<{ assigned: number; skipped: number }> {
  const { data } = await apiClient.post('/leads/assign', { leadIds, ownerId });
  return data;
}
