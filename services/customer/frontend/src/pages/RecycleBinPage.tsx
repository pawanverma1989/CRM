import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { getRecycleBin, restoreRecord } from '../api/recycleBin';
import { useDebouncedValue } from '../hooks/useDebouncedValue';
import { useToast } from '../contexts/ToastContext';
import { Pagination } from '../components/Pagination';
import { SelectField } from '../components/SelectField';
import { ConflictBanner } from '../components/DuplicateWarning';
import { LoadingBlock, ErrorBlock, EmptyBlock } from '../components/StateBlocks';
import { DEFAULT_PAGE_SIZE } from '../lib/validation';
import { formatDateTime, getApiErrorMessage, getConflictRef } from '../lib/utils';
import type { ConflictRef, EntityType } from '../types';

/** DEL-2/DEL-3: records deleted in the last 30 days, with restore. */
export function RecycleBinPage() {
  const { showToast } = useToast();
  const queryClient = useQueryClient();

  const [recordType, setRecordType] = useState<EntityType | ''>('');
  const [search, setSearch] = useState('');
  const debouncedSearch = useDebouncedValue(search.trim(), 300);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE);
  const [restoreConflict, setRestoreConflict] = useState<{ message: string; ref?: ConflictRef } | null>(null);

  const { data, isLoading, isFetching, isError, error, refetch } = useQuery({
    queryKey: ['recycle-bin', recordType, debouncedSearch, page, pageSize],
    queryFn: () =>
      getRecycleBin({
        recordType: recordType || undefined,
        q: debouncedSearch || undefined,
        page,
        pageSize,
      }),
    placeholderData: (prev) => prev,
  });

  const restoreMutation = useMutation({
    mutationFn: ({ type, id }: { type: EntityType; id: string }) => restoreRecord(type, id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['recycle-bin'] });
      queryClient.invalidateQueries({ queryKey: ['contacts'] });
      queryClient.invalidateQueries({ queryKey: ['companies'] });
      showToast('Record restored', 'success');
      setRestoreConflict(null);
    },
    onError: (err) => {
      const ref = getConflictRef(err);
      setRestoreConflict({ message: getApiErrorMessage(err), ref });
    },
  });

  const records = data?.data ?? [];

  return (
    <div>
      <h1 className="text-2xl font-bold text-gray-900 mb-2">Recycle bin</h1>
      <p className="text-sm text-gray-600 mb-6">
        Deleted records stay here for 30 days, then a nightly job removes them permanently.
      </p>

      {restoreConflict && (
        <div className="mb-4">
          <ConflictBanner message={restoreConflict.message} conflict={restoreConflict.ref} />
        </div>
      )}

      <div className="bg-white rounded-lg border border-gray-200 p-4 mb-4 flex flex-wrap gap-3">
        <div className="flex-1 min-w-[220px]">
          <label htmlFor="recycle-search" className="block text-sm font-medium text-gray-700 mb-1">
            Search
          </label>
          <input
            id="recycle-search"
            type="search"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setPage(1);
            }}
            className="w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500"
          />
        </div>
        <div className="w-44">
          <SelectField
            label="Type"
            value={recordType}
            placeholder="All types"
            options={[
              { value: 'contact', label: 'Contacts' },
              { value: 'company', label: 'Companies' },
            ]}
            onChange={(e) => {
              setRecordType(e.target.value as EntityType | '');
              setPage(1);
            }}
          />
        </div>
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
                  <th scope="col" className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Record</th>
                  <th scope="col" className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Type</th>
                  <th scope="col" className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Owner</th>
                  <th scope="col" className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Deleted</th>
                  <th scope="col" className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Purged on</th>
                  <th scope="col" className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-200">
                {records.map((record) => (
                  <tr key={`${record.recordType}-${record.id}`}>
                    <td className="px-4 py-3 text-sm font-medium text-gray-900">
                      {record.label}
                      {record.secondaryLabel && <span className="text-gray-500 font-normal"> — {record.secondaryLabel}</span>}
                    </td>
                    <td className="px-4 py-3 text-sm text-gray-600 capitalize">{record.recordType}</td>
                    <td className="px-4 py-3 text-sm text-gray-600">{record.ownerName ?? '—'}</td>
                    <td className="px-4 py-3 text-sm text-gray-600">{formatDateTime(record.deletedAt)}</td>
                    <td className="px-4 py-3 text-sm text-gray-600">{formatDateTime(record.purgeAt)}</td>
                    <td className="px-4 py-3 text-right text-sm">
                      <button
                        onClick={() => restoreMutation.mutate({ type: record.recordType, id: record.id })}
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
              noun="deleted records"
            />
          </>
        )}
      </div>
    </div>
  );
}
