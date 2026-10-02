import apiClient from './client';
import type { ConversionDto } from '../types';

export async function startConversion(leadId: string, body: Record<string, unknown>): Promise<ConversionDto> {
  const { data } = await apiClient.post<ConversionDto>(`/leads/${leadId}/convert`, body);
  return data;
}

export async function getConversion(conversionId: string): Promise<ConversionDto> {
  const { data } = await apiClient.get<ConversionDto>(`/conversions/${conversionId}`);
  return data;
}
