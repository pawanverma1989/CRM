import apiClient from './client';
import type { PagedResult, RecycleBinDealDto } from '../types';

export async function listRecycleBin(page = 1, pageSize = 50): Promise<PagedResult<RecycleBinDealDto>> {
  const { data } = await apiClient.get<PagedResult<RecycleBinDealDto>>(
    `/recycle-bin?page=${page}&pageSize=${pageSize}`
  );
  return data;
}

export async function restoreDeal(id: string): Promise<void> {
  await apiClient.post(`/recycle-bin/${id}/restore`);
}
