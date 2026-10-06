import apiClient from './client';
import type { DealDto, PagedResult, BulkReassignResult } from '../types';

export type SortField =
  | 'name'
  | 'amount'
  | 'created_at'
  | 'updated_at'
  | 'expected_close_date'
  | 'stage_entered_at'
  | 'last_activity_at';

export type SortDirection = 'asc' | 'desc';

export interface DealListParams {
  page?: number;
  pageSize?: number;
  pipelineId?: string;
  stageId?: string;
  ownerId?: string;
  status?: string;
  unassigned?: boolean;
  closeDateFrom?: string;
  closeDateTo?: string;
  createdFrom?: string;
  createdTo?: string;
  tags?: string[];
  q?: string;
  sortBy?: SortField;
  sortDir?: SortDirection;
  isStale?: boolean;
}

export async function listDeals(params: DealListParams = {}): Promise<PagedResult<DealDto>> {
  const query = new URLSearchParams();
  if (params.page) query.set('page', String(params.page));
  if (params.pageSize) query.set('pageSize', String(params.pageSize));
  if (params.pipelineId) query.set('pipelineId', params.pipelineId);
  if (params.stageId) query.set('stageId', params.stageId);
  if (params.ownerId) query.set('ownerId', params.ownerId);
  if (params.status) query.set('status', params.status);
  if (params.unassigned) query.set('unassigned', 'true');
  if (params.closeDateFrom) query.set('closeDateFrom', params.closeDateFrom);
  if (params.closeDateTo) query.set('closeDateTo', params.closeDateTo);
  if (params.createdFrom) query.set('createdFrom', params.createdFrom);
  if (params.createdTo) query.set('createdTo', params.createdTo);
  if (params.tags?.length) params.tags.forEach((t) => query.append('tags', t));
  if (params.q) query.set('q', params.q);
  if (params.sortBy) query.set('sortBy', params.sortBy);
  if (params.sortDir) query.set('sortDir', params.sortDir);
  if (params.isStale) query.set('isStale', 'true');

  const { data } = await apiClient.get<PagedResult<DealDto>>(`/deals?${query.toString()}`);
  return data;
}

export async function getDeal(id: string): Promise<DealDto> {
  const { data } = await apiClient.get<DealDto>(`/deals/${id}`);
  return data;
}

export interface CreateDealRequest {
  pipelineId: string;
  stageId: string;
  name: string;
  amount?: number;
  currency?: string;
  ownerId?: string | null;
  companyId?: string | null;
  primaryContactId?: string | null;
  probability?: number | null;
  expectedCloseDate?: string | null;
  tags?: string[];
  customFields?: Record<string, unknown>;
  sourceLeadId?: string | null;
}

export interface UpdateDealRequest {
  version: number;
  name?: string;
  amount?: number;
  currency?: string;
  ownerId?: string | null;
  companyId?: string | null;
  primaryContactId?: string | null;
  probability?: number | null;
  expectedCloseDate?: string | null;
  tags?: string[];
  customFields?: Record<string, unknown>;
  lossNotes?: string;
}

export async function createDeal(req: CreateDealRequest): Promise<DealDto> {
  const { data } = await apiClient.post<DealDto>('/deals', req);
  return data;
}

export async function updateDeal(id: string, req: UpdateDealRequest): Promise<DealDto> {
  const { data } = await apiClient.patch<DealDto>(`/deals/${id}`, req);
  return data;
}

export async function moveDeal(id: string, stageId: string): Promise<DealDto> {
  const { data } = await apiClient.post<DealDto>(`/deals/${id}/move`, { stageId });
  return data;
}

export async function markWon(id: string, closeDate?: string): Promise<DealDto> {
  const { data } = await apiClient.post<DealDto>(`/deals/${id}/won`, { closeDate: closeDate ?? null });
  return data;
}

export async function markLost(
  id: string,
  lossReasonId: string,
  lossNotes?: string,
  closeDate?: string
): Promise<DealDto> {
  const { data } = await apiClient.post<DealDto>(`/deals/${id}/lost`, {
    lossReasonId,
    lossNotes: lossNotes ?? null,
    closeDate: closeDate ?? null,
  });
  return data;
}

export async function reopenDeal(id: string, stageId: string): Promise<DealDto> {
  const { data } = await apiClient.post<DealDto>(`/deals/${id}/reopen`, { stageId });
  return data;
}

export interface DealContactEntry {
  contactId: string;
  role?: string | null;
  isPrimary?: boolean;
}

export async function replaceContacts(id: string, contacts: DealContactEntry[]): Promise<DealDto> {
  const { data } = await apiClient.put<DealDto>(`/deals/${id}/contacts`, { contacts });
  return data;
}

export async function deleteDeal(id: string): Promise<void> {
  await apiClient.delete(`/deals/${id}`);
}

export async function reassignDeals(
  dealIds: string[],
  newOwnerId: string | null
): Promise<BulkReassignResult> {
  const { data } = await apiClient.post<BulkReassignResult>('/deals/reassign', { dealIds, newOwnerId });
  return data;
}
