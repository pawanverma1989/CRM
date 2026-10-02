import apiClient from './client';
import type { DisqualifyReasonDto } from '../types';

export async function getDisqualifyReasons(): Promise<DisqualifyReasonDto[]> {
  const { data } = await apiClient.get<DisqualifyReasonDto[]>('/disqualify-reasons');
  return data;
}

export async function createDisqualifyReason(name: string): Promise<DisqualifyReasonDto> {
  const { data } = await apiClient.post<DisqualifyReasonDto>('/disqualify-reasons', { name });
  return data;
}

export async function updateDisqualifyReason(id: string, body: Partial<DisqualifyReasonDto>): Promise<DisqualifyReasonDto> {
  const { data } = await apiClient.patch<DisqualifyReasonDto>(`/disqualify-reasons/${id}`, body);
  return data;
}
