import apiClient from './client';
import type { BulkResultDto, EntityType } from '../types';

/** OWN-2: change the owner of up to 500 records at once. */
export interface ReassignPayload {
  recordType: EntityType;
  recordIds: string[];
  /** null removes the owner — unowned records are visible to everyone (§2). */
  newOwnerId: string | null;
}

export async function reassignRecords(payload: ReassignPayload): Promise<BulkResultDto> {
  const { data } = await apiClient.post<BulkResultDto>('/reassign', payload);
  return data;
}

/** DEL-4: soft-delete up to 500 records at once. */
export interface BulkDeletePayload {
  recordType: EntityType;
  recordIds: string[];
}

export async function bulkDeleteRecords(payload: BulkDeletePayload): Promise<BulkResultDto> {
  const { data } = await apiClient.post<BulkResultDto>('/bulk-delete', payload);
  return data;
}

/** DUP-4/DUP-5: merge two records, choosing the surviving value per field. */
export interface MergePayload {
  survivorId: string;
  loserId: string;
  /** field key -> which record's value to keep. */
  fieldChoices: Record<string, 'survivor' | 'loser'>;
}

export async function mergeContacts(payload: MergePayload): Promise<{ id: string }> {
  const { data } = await apiClient.post<{ id: string }>('/contacts/merge', payload);
  return data;
}

export async function mergeCompanies(payload: MergePayload): Promise<{ id: string }> {
  const { data } = await apiClient.post<{ id: string }>('/companies/merge', payload);
  return data;
}
