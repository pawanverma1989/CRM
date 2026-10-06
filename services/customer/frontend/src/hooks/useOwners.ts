import { useQuery } from '@tanstack/react-query';
import { getOwners } from '../api/owners';

interface UseOwnersOptions {
  /**
   * Use on create/edit forms and assign modals. New users reach this service
   * through events (eventually consistent), so refetch on every mount instead
   * of trusting the cache.
   */
  fresh?: boolean;
  enabled?: boolean;
}

export function useOwners({ fresh = false, enabled = true }: UseOwnersOptions = {}) {
  const query = useQuery({
    queryKey: ['owners'],
    queryFn: getOwners,
    staleTime: fresh ? 0 : 60_000,
    refetchOnMount: fresh ? 'always' : true,
    refetchOnWindowFocus: true,
    enabled,
  });
  return {
    owners: query.data ?? [],
    isLoading: query.isLoading,
    isFetching: query.isFetching,
    refetch: query.refetch,
  };
}
