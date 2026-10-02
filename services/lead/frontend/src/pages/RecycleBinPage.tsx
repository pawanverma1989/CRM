import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { getRecycleBin, restoreLead } from '../api/recycleBin';
import { useDebouncedValue } from '../hooks/useDebouncedValue';
import { useToast } from '../contexts/ToastContext';
import { Pagination } from '../components/Pagination';
import { LoadingBlock, ErrorBlock, EmptyBlock } from '../components/StateBlocks';
import { DEFAULT_PAGE_SIZE } from '../lib/validation';
import { formatDateTime, getApiErrorMessage, leadName } from '../lib/utils';

/** Leads deleted in the last 30 days, with restore. */
export function RecycleBinPage() {
  const { showToast } = useToast();
  const queryClient = useQueryClient();

  const [search, setSearch] = useState('');
  const debouncedSearch = useDebouncedValue(search.trim(), 300);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE);

  const { data, isLoading, isFetching, isError, error, refetch } = useQuery({
    queryKey: ['recycle-bin', debouncedSearch, page, pageSize],
    queryFn: () =>
      getRecycleBin({
        q: debouncedSearch || undefined,
        page,
        pageSize,
      }),
    placeholderData: (prev) => prev,
  });

  const restoreMutation = useMutation({
    mutationFn: (id: string) => restoreLead(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['recycle-bin'] });
      queryClient.invalidateQueries({ queryKey: ['leads'] });
      showToast('Lead restored', 'success');
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const records = data?.data ?? [];

  return (
    <div>
      <h1 className="text-2xl font-bold text-gray-900 mb-2">Recycle bin</h1>
      <p className="text-sm text-gray-600 mb-6">
        Deleted leads stay here for 30 days, then a nightly job removes them permanently.
      </p>

      <div className="bg-white rounded-lg border border-gray-200 p-4 mb-4">
        <label htmlFor="recycle-search" className="block text-sm font-medium text-gray-700 mb-1">Search</label>
        <input
          id="recycle-search"
          type="search"
          value={search}
          onChange={(e) => {
            setSearch(e.target.value);
            setPage(1);
          }}
          className="w-full max-w-sm px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500"
        />
      </div>

      <div className="bg-white rounded-lg border border-gray-200 overflow-hidden">
        {isLoading ? (
          <LoadingBlock label="Loading the recycle bin…" />
        ) : isError ? (
          <ErrorBlock error={error} title="Could not load the recycle bin" onRetry={refetch} />
        ) : records.length === 0 ? (
          <EmptyBlock title="The recycle bin is empty" />
        ) : (
          <>
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50">
                <tr>
                  <th scope="col" className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Lead</th>
                  <th scope="col" className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Deleted</th>
                  <th scope="col" className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Purged on</th>
                  <th scope="col" className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-200">
                {records.map((record) => (
                  <tr key={record.id}>
                    <td className="px-4 py-3 text-sm font-medium text-gray-900">
                      {leadName(record)}
                      {record.email && !record.firstName && !record.lastName && (
                        <span className="text-gray-500 font-normal"> — {record.email}</span>
                      )}
                    </td>
                    <td className="px-4 py-3 text-sm text-gray-600">{formatDateTime(record.deletedAt)}</td>
                    <td className="px-4 py-3 text-sm text-gray-600">{formatDateTime(record.purgeAfter)}</td>
                    <td className="px-4 py-3 text-right text-sm">
                      <button
                        type="button"
                        onClick={() => restoreMutation.mutate(record.id)}
                        disabled={restoreMutation.isPending}
                        className="text-primary-600 hover:text-primary-700 font-medium disabled:opacity-50"
                      >
                        Restore
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            <Pagination
              page={page}
              pageSize={pageSize}
              total={data?.total ?? 0}
              isFetching={isFetching}
              onPageChange={setPage}
              onPageSizeChange={(size) => {
                setPageSize(size);
                setPage(1);
              }}
              noun="deleted leads"
            />
          </>
        )}
      </div>
    </div>
  );
}
