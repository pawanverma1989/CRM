import apiClient from './client';
import type { DuplicateCheckResult } from '../types';

export async function checkDuplicates(params: { excludeId?: string; email?: string; phone?: string }): Promise<DuplicateCheckResult> {
  const { data } = await apiClient.post<DuplicateCheckResult>('/leads/duplicates/check', params);
  return data;
}
