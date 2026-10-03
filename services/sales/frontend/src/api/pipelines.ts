import apiClient from './client';
import type { PipelineDto, PipelineStageDto } from '../types';

export async function listPipelines(): Promise<PipelineDto[]> {
  const { data } = await apiClient.get<PipelineDto[]>('/pipelines');
  return data;
}

export async function getPipeline(id: string): Promise<PipelineDto> {
  const { data } = await apiClient.get<PipelineDto>(`/pipelines/${id}`);
  return data;
}

export async function createPipeline(req: {
  name: string;
  description?: string | null;
  isDefault?: boolean;
  staleDays?: number | null;
}): Promise<PipelineDto> {
  const { data } = await apiClient.post<PipelineDto>('/pipelines', req);
  return data;
}

export async function updatePipeline(id: string, req: {
  name?: string;
  description?: string | null;
  isDefault?: boolean;
  staleDays?: number | null;
}): Promise<PipelineDto> {
  const { data } = await apiClient.patch<PipelineDto>(`/pipelines/${id}`, req);
  return data;
}

export async function addStage(
  pipelineId: string,
  req: { name: string; stageType: string; probability: number }
): Promise<PipelineStageDto> {
  const { data } = await apiClient.post<PipelineStageDto>(`/pipelines/${pipelineId}/stages`, req);
  return data;
}

export async function reorderStages(pipelineId: string, stageIds: string[]): Promise<void> {
  await apiClient.post(`/pipelines/${pipelineId}/stages/reorder`, { stageIds });
}

export async function updateStage(
  id: string,
  req: { name?: string; stageType?: string; probability?: number; isActive?: boolean }
): Promise<PipelineStageDto> {
  const { data } = await apiClient.patch<PipelineStageDto>(`/stages/${id}`, req);
  return data;
}
