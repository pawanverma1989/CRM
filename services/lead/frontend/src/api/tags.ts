import apiClient from './client';

/** GET /tags/suggestions?q= — suggestions from tags already in use. */
export async function getTagSuggestions(prefix: string): Promise<string[]> {
  const { data } = await apiClient.get<string[]>(`/tags/suggestions?q=${encodeURIComponent(prefix)}`);
  return data;
}
