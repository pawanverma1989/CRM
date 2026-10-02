import apiClient from './client';
import type { DeletedRecordDto, EntityType, PagedResponse } from '../types';
import { DEFAULT_PAGE_SIZE } from '../lib/validation';

/** GET /recycle-bin — records deleted in the last 30 days (DEL-2). */
export interface RecycleBinParams {
  recordType?: EntityType;
  q?: string;
  page?: number;
  pageSize?: number;
}

export async function getRecycleBin(
  params: RecycleBinParams = {}
): Promise<PagedResponse<DeletedRecordDto>> {
  const search = new URLSearchParams();
  if (params.recordType) search.set('type', params.recordType);
  if (params.q) search.set('q', params.q);
  search.set('page', String(params.page ?? 1));
  search.set('pageSize', String(params.pageSize ?? DEFAULT_PAGE_SIZE));
  const { data } = await apiClient.get<PagedResponse<DeletedRecordDto>>(`/recycle-bin?${search}`);
  return data;
}

/**
 * POST /recycle-bin/{type}/{id}/restore — refused when it would break DUP-1,
 * and the error then names the conflicting record (DEL-2 / AC-14).
 */
export async function restoreRecord(recordType: EntityType, id: string): Promise<void> {
  await apiClient.post(`/recycle-bin/${recordType}/${id}/restore`);
}
