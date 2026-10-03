import apiClient from './client';
import type { OwnerDto } from '../types';

export async function getOwners(): Promise<OwnerDto[]> {
  const { data } = await apiClient.get<OwnerDto[]>('/owners');
  return data;
}
