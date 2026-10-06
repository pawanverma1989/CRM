import apiClient from './client';
import type { LossReasonDto } from '../types';

export async function listLossReasons(): Promise<LossReasonDto[]> {
  const { data } = await apiClient.get<LossReasonDto[]>('/loss-reasons');
  return data;
}

export async function createLossReason(req: { name: string }): Promise<LossReasonDto> {
  const { data } = await apiClient.post<LossReasonDto>('/loss-reasons', req);
  return data;
}

export async function updateLossReason(
  id: string,
  req: { name?: string; isActive?: boolean }
): Promise<LossReasonDto> {
  const { data } = await apiClient.patch<LossReasonDto>(`/loss-reasons/${id}`, req);
  return data;
}
