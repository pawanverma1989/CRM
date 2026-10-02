import apiClient from './client';
import type { RecycleBinLeadDto, PagedResult } from '../types';
import { DEFAULT_PAGE_SIZE } from '../lib/validation';

export async function getRecycleBin(params: { q?: string; page?: number; pageSize?: number } = {}): Promise<PagedResult<RecycleBinLeadDto>> {
  const search = new URLSearchParams();
  if (params.q) search.set('q', params.q);
  search.set('page', String(params.page ?? 1));
  search.set('pageSize', String(params.pageSize ?? DEFAULT_PAGE_SIZE));
  const { data } = await apiClient.get<PagedResult<RecycleBinLeadDto>>(`/recycle-bin?${search}`);
  return data;
}

export async function restoreLead(id: string): Promise<void> {
  await apiClient.post(`/recycle-bin/${id}/restore`);
}
