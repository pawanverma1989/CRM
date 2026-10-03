import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { listRecycleBin, restoreDeal } from '../api/recycleBin';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { Pagination } from '../components/Pagination';
import { LoadingBlock, ErrorBlock, EmptyBlock } from '../components/StateBlocks';
import { useToast } from '../contexts/ToastContext';
import { formatDate, formatMoney, getApiErrorMessage } from '../lib/utils';
import { DEFAULT_PAGE_SIZE } from '../lib/validation';

export function RecycleBinPage() {
  const queryClient = useQueryClient();
  const { showToast } = useToast();

  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE);
  const [restoringId, setRestoringId] = useState<string | null>(null);

  const { data, isLoading, error, refetch } = useQuery({
    queryKey: ['recycle-bin', page, pageSize],
    queryFn: () => listRecycleBin(page, pageSize),
    staleTime: 30_000,
  });

  const restoreMutation = useMutation({
    mutationFn: (id: string) => restoreDeal(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['recycle-bin'] });
      queryClient.invalidateQueries({ queryKey: ['deals'] });
      queryClient.invalidateQueries({ queryKey: ['board'] });
      setRestoringId(null);
      showToast('Deal restored', 'success');
    },
    onError: (err) => {
      setRestoringId(null);
      showToast(getApiErrorMessage(err), 'error');
    },
  });

  if (isLoading) return <LoadingBlock label="Loading recycle bin…" />;
  if (error) return <ErrorBlock error={error} title="Could not load recycle bin" onRetry={() => refetch()} />;

  const deals = data?.data ?? [];
  const restoring = deals.find((d) => d.id === restoringId);

  return (
    <div>
      <div className="mb-6">
        <h1 className="text-2xl font-bold text-gray-900">Recycle Bin</h1>
        <p className="text-sm text-gray-500 mt-0.5">Soft-deleted deals. Restore within 30 days before they are permanently removed.</p>
      </div>

      {deals.length === 0 ? (
        <EmptyBlock title="Recycle bin is empty" message="Deleted deals will appear here." />
      ) : (
        <div className="bg-white rounded-lg border border-gray-200 overflow-hidden">
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50">
                <tr>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Deal</th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Pipeline / Stage</th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Amount</th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Deleted</th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Deleted by</th>
                  <th className="px-6 py-3" />
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-200">
                {deals.map((deal) => (
                  <tr key={deal.id} className="hover:bg-gray-50">
                    <td className="px-6 py-3">
                      <p className="text-sm font-medium text-gray-900">{deal.name}</p>
                      {deal.companyName && <p className="text-xs text-gray-400">{deal.companyName}</p>}
                    </td>
                    <td className="px-6 py-3 text-sm text-gray-600">
                      {deal.pipelineName ?? '—'}
                      {deal.stageName && <span className="text-gray-400"> / {deal.stageName}</span>}
                    </td>
                    <td className="px-6 py-3 text-sm text-gray-800 font-medium">{formatMoney(deal.amount, deal.currency)}</td>
                    <td className="px-6 py-3 text-sm text-gray-500">{deal.deletedAt ? formatDate(deal.deletedAt) : '—'}</td>
                    <td className="px-6 py-3 text-sm text-gray-500">{deal.deletedByName ?? '—'}</td>
                    <td className="px-6 py-3 text-right">
                      <button
                        type="button"
                        onClick={() => setRestoringId(deal.id)}
                        className="text-sm text-primary-600 hover:text-primary-800 font-medium"
                      >
                        Restore
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pagination
            page={page}
            pageSize={pageSize}
            total={data?.total ?? 0}
            onPageChange={setPage}
            onPageSizeChange={(s) => { setPageSize(s); setPage(1); }}
            noun="deleted deals"
          />
        </div>
      )}

      <ConfirmDialog
        isOpen={!!restoringId}
        title={`Restore "${restoring?.name ?? ''}"?`}
        message="The deal will be moved back to its pipeline and become visible to its owner."
        confirmLabel="Restore"
        isLoading={restoreMutation.isPending}
        onConfirm={() => restoringId && restoreMutation.mutate(restoringId)}
        onCancel={() => setRestoringId(null)}
      />
    </div>
  );
}
