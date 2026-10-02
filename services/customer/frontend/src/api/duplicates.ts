import apiClient from './client';
import type { DuplicateCheckResponse, EntityType } from '../types';

/**
 * POST /duplicates/check — possible duplicates for a draft record before it is
 * saved (DUP-2, DUP-3, AC-5). `excludeId` keeps a record from matching itself
 * while it is being edited.
 */
export interface DuplicateCheckPayload {
  entityType: EntityType;
  excludeId?: string;
  /** Contacts */
  firstName?: string;
  lastName?: string;
  email?: string;
  phone?: string;
  companyId?: string;
  /** Companies */
  name?: string;
  domain?: string;
}

export async function checkDuplicates(
  payload: DuplicateCheckPayload
): Promise<DuplicateCheckResponse> {
  const { data } = await apiClient.post<DuplicateCheckResponse>('/duplicates/check', payload);
  return data;
}
