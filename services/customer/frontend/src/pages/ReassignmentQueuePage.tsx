import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { getReassignmentQueue } from '../api/reassignmentQueue';
import { getOwners } from '../api/owners';
import { reassignRecords } from '../api/bulk';
import { useToast } from '../contexts/ToastContext';
import { Pagination } from '../components/Pagination';
import { ReassignModal } from '../components/ReassignModal';
import { LoadingBlock, ErrorBlock, EmptyBlock } from '../components/StateBlocks';
import { DEFAULT_PAGE_SIZE } from '../lib/validation';
import { detailPath, formatDateTime, getApiErrorMessage } from '../lib/utils';
import type { ReassignmentQueueItemDto } from '../types';

/** OWN-3: records of deactivated users, with single and bulk reassignment. */
export function ReassignmentQueuePage() {
  const { showToast } = useToast();
  const queryClient = useQueryClient();

  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE);
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [reassignTarget, setReassignTarget] = useState<'single' | 'bulk' | null>(null);
  const [singleItem, setSingleItem] = useState<ReassignmentQueueItemDto | null>(null);

  const { data, isLoading, isFetching, isError, error, refetch } = useQuery({
    queryKey: ['reassignment-queue', page, pageSize],
    queryFn: () => getReassignmentQueue({ page, pageSize }),
    placeholderData: (prev) => prev,
  });

  const { data: owners = [] } = useQuery({ queryKey: ['owners'], queryFn: getOwners });

  const items = data?.data ?? [];
  const allOnPageSelected = items.length > 0 && items.every((i) => selectedIds.has(i.id));

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: ['reassignment-queue'] });
    queryClient.invalidateQueries({ queryKey: ['contacts'] });
    queryClient.invalidateQueries({ queryKey: ['companies'] });
  };

  const reassignMutation = useMutation({
    mutationFn: async (newOwnerId: string | null) => {
      const targets = reassignTarget === 'single' && singleItem ? [singleItem] : items.filter((i) => selectedIds.has(i.id));
      const byType = {
        contact: targets.filter((t) => t.recordType === 'contact').map((t) => t.recordId),
        company: targets.filter((t) => t.recordType === 'company').map((t) => t.recordId),
      };
      const results = await Promise.all(
        (Object.keys(byType) as Array<'contact' | 'company'>)
          .filter((type) => byType[type].length > 0)
          .map((type) => reassignRecords({ recordType: type, recordIds: byType[type], newOwnerId }))
      );
      return results.reduce((sum, r) => sum + r.succeeded, 0);
    },
    onSuccess: (succeeded) => {
      invalidate();
      showToast(`Reassigned ${succeeded} record${succeeded === 1 ? '' : 's'}`, 'success');
      setReassignTarget(null);
      setSingleItem(null);
      setSelectedIds(new Set());
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const toggleRow = (id: string) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const toggleAllOnPage = () => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (allOnPageSelected) items.forEach((i) => next.delete(i.id));
      else items.forEach((i) => next.add(i.id));
      return next;
    });
  };

  const modalRecordCount = useMemo(
    () => (reassignTarget === 'single' ? 1 : selectedIds.size),
    [reassignTarget, selectedIds]
  );

  return (
    <div>
      <h1 className="text-2xl font-bold text-gray-900 mb-2">Reassignment queue</h1>
      <p className="text-sm text-gray-600 mb-6">
        Records of deactivated users. They stay visible and editable until you reassign them.
      </p>

      {selectedIds.size > 0 && (
        <div className="mb-4 rounded-lg border border-primary-200 bg-primary-50 px-4 py-3 flex flex-wrap items-center gap-3">
          <p className="text-sm font-medium text-primary-900">{selectedIds.size} selected</p>
          <button
            type="button"
            onClick={() => setReassignTarget('bulk')}
            className="px-3 py-1.5 text-sm font-medium text-primary-800 bg-white border border-primary-300 rounded-md hover:bg-primary-100"
          >
            Reassign selected
          </button>
          <button
            type="button"
            onClick={() => setSelectedIds(new Set())}
            className="ml-auto text-sm text-primary-700 underline hover:text-primary-900"
          >
            Clear selection
          </button>
        </div>
      )}

      <div className="bg-white rounded-lg border border-gray-200 overflow-hidden">
        {isLoading ? (
          <LoadingBlock label="Loading the reassignment queue…" />
        ) : isError ? (
          <ErrorBlock error={error} title="Could not load the reassignment queue" onRetry={refetch} />
        ) : items.length === 0 ? (
          <EmptyBlock title="Nothing waiting to be reassigned" />
        ) : (
          <>
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50">
                <tr>
                  <th scope="col" className="px-4 py-3 w-10">
                    <input
                      type="checkbox"
                      aria-label="Select all records on this page"
                      checked={allOnPageSelected}
                      onChange={toggleAllOnPage}
                      className="h-4 w-4 rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                    />
                  </th>
                  <th scope="col" className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Record</th>
                  <th scope="col" className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Type</th>
                  <th scope="col" className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Previous owner</th>
                  <th scope="col" className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Queued</th>
                  <th scope="col" className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-200">
                {items.map((item) => (
                  <tr key={item.id}>
                    <td className="px-4 py-3">
                      <input
                        type="checkbox"
                        aria-label={`Select ${item.label}`}
                        checked={selectedIds.has(item.id)}
                        onChange={() => toggleRow(item.id)}
                        className="h-4 w-4 rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                      />
                    </td>
                    <td className="px-4 py-3 text-sm font-medium text-gray-900">
                      <Link to={detailPath(item.recordType, item.recordId)} className="text-primary-700 hover:underline">
                        {item.label}
                      </Link>
                    </td>
                    <td className="px-4 py-3 text-sm text-gray-600 capitalize">{item.recordType}</td>
                    <td className="px-4 py-3 text-sm text-gray-600">{item.previousOwnerName ?? '—'}</td>
                    <td className="px-4 py-3 text-sm text-gray-600">{formatDateTime(item.queuedAt)}</td>
                    <td className="px-4 py-3 text-right text-sm">
                      <button
                        onClick={() => {
                          setSingleItem(item);
                          setReassignTarget('single');
                        }}
                        className="text-primary-600 hover:text-primary-700 font-medium"
                      >
                        Reassign
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
              noun="records"
            />
          </>
        )}
      </div>

      <ReassignModal
        isOpen={reassignTarget !== null}
        onClose={() => {
          setReassignTarget(null);
          setSingleItem(null);
        }}
        owners={owners}
        recordCount={modalRecordCount}
        isLoading={reassignMutation.isPending}
        onConfirm={(ownerId) => reassignMutation.mutate(ownerId)}
      />
    </div>
  );
}
