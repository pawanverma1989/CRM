import apiClient from './client';

/** GET /tags?prefix= — suggestions from tags already in use (TAG-3). */
export async function getTagSuggestions(prefix: string): Promise<string[]> {
  const params = new URLSearchParams();
  if (prefix) params.set('prefix', prefix);
  const { data } = await apiClient.get<string[]>(`/tags?${params}`);
  return data;
}
