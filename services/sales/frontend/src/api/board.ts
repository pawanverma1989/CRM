import apiClient from './client';
import type { BoardResponse } from '../types';

export async function getBoard(pipelineId: string): Promise<BoardResponse> {
  const { data } = await apiClient.get<BoardResponse>(`/board?pipelineId=${encodeURIComponent(pipelineId)}`);
  return data;
}
