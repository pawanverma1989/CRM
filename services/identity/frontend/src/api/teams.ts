import apiClient from './client';
import type { TeamDto } from '../types';

export async function getTeams(): Promise<TeamDto[]> {
  const { data } = await apiClient.get<TeamDto[]>('/teams');
  return data;
}

export interface CreateTeamPayload {
  name: string;
  managerId?: string;
}

export async function createTeam(payload: CreateTeamPayload): Promise<TeamDto> {
  const { data } = await apiClient.post<TeamDto>('/teams', payload);
  return data;
}

export interface UpdateTeamPayload {
  name?: string;
  managerId?: string | null;
}

export async function updateTeam(id: string, payload: UpdateTeamPayload): Promise<TeamDto> {
  const { data } = await apiClient.patch<TeamDto>(`/teams/${id}`, payload);
  return data;
}

export async function deleteTeam(id: string): Promise<void> {
  await apiClient.delete(`/teams/${id}`);
}
