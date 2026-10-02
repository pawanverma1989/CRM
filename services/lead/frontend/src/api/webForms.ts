import apiClient from './client';
import type { WebFormDto, WebFormEmbedDto } from '../types';

export async function getWebForms(): Promise<WebFormDto[]> {
  const { data } = await apiClient.get<WebFormDto[]>('/web-forms');
  return data;
}

export async function getWebForm(id: string): Promise<WebFormDto> {
  const { data } = await apiClient.get<WebFormDto>(`/web-forms/${id}`);
  return data;
}

export async function createWebForm(body: Record<string, unknown>): Promise<WebFormDto> {
  const { data } = await apiClient.post<WebFormDto>('/web-forms', body);
  return data;
}

export async function updateWebForm(id: string, body: Record<string, unknown>): Promise<WebFormDto> {
  const { data } = await apiClient.patch<WebFormDto>(`/web-forms/${id}`, body);
  return data;
}

export async function getWebFormEmbed(id: string): Promise<WebFormEmbedDto> {
  const { data } = await apiClient.get<WebFormEmbedDto>(`/web-forms/${id}/embed`);
  return data;
}
