import apiClient from './client';

export async function getTagSuggestions(prefix: string): Promise<string[]> {
  const { data } = await apiClient.get<string[]>(`/tags/suggestions?q=${encodeURIComponent(prefix)}`);
  return data;
}
