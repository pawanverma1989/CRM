import apiClient from './client';
import type { PagedResponse, ReassignmentQueueItemDto } from '../types';
import { DEFAULT_PAGE_SIZE } from '../lib/validation';

/**
 * GET /reassignment-queue — records whose owner was deactivated in Identity
 * (OWN-3). They stay visible and editable until someone reassigns them.
 */
export interface ReassignmentQueueParams {
  previousOwnerId?: string;
  page?: number;
  pageSize?: number;
}

export async function getReassignmentQueue(
  params: ReassignmentQueueParams = {}
): Promise<PagedResponse<ReassignmentQueueItemDto>> {
  const search = new URLSearchParams();
  if (params.previousOwnerId) search.set('previousOwnerId', params.previousOwnerId);
  search.set('page', String(params.page ?? 1));
  search.set('pageSize', String(params.pageSize ?? DEFAULT_PAGE_SIZE));
  const { data } = await apiClient.get<PagedResponse<ReassignmentQueueItemDto>>(
    `/reassignment-queue?${search}`
  );
  return data;
}
