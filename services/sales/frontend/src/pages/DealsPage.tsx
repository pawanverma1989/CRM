import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate } from 'react-router-dom';
import { listDeals, deleteDeal, reassignDeals, type SortField, type SortDirection } from '../api/deals';
import { listPipelines } from '../api/pipelines';
import { getOwners } from '../api/owners';
import { Pagination } from '../components/Pagination';
import { StatusBadge } from '../components/StatusBadge';
import { SortableHeader } from '../components/SortableHeader';
import { BulkActionBar } from '../components/BulkActionBar';
import { AssignModal } from '../components/AssignModal';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { LoadingBlock, ErrorBlock, EmptyBlock } from '../components/StateBlocks';
import { useToast } from '../contexts/ToastContext';
import { usePermissions } from '../hooks/usePermissions';
import { useDebouncedValue } from '../hooks/useDebouncedValue';
import { formatMoney, formatDate, getApiErrorMessage } from '../lib/utils';
import { DEFAULT_PAGE_SIZE } from '../lib/validation';

export function DealsPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { showToast } = useToast();
  const { canAssign } = usePermissions();

  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE);
  const [sortBy, setSortBy] = useState<SortField>('created_at');
  const [sortDir, setSortDir] = useState<SortDirection>('desc');
  const [search, setSearch] = useState('');
  const [pipelineFilter, setPipelineFilter] = useState('');
  const [stageFilter, setStageFilter] = useState('');
  const [ownerFilter, setOwnerFilter] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  const [isStaleFilter, setIsStaleFilter] = useState(false);

  const debouncedSearch = useDebouncedValue(search, 300);

  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [showAssign, setShowAssign] = useState(false);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);

  const { data: pipelines = [] } = useQuery({ queryKey: ['pipelines'], queryFn: listPipelines, staleTime: 60_000 });
  const { data: owners = [] } = useQuery({ queryKey: ['owners'], queryFn: getOwners, staleTime: 60_000 });

  const selectedPipeline = pipelines.find((p) => p.id === pipelineFilter);
  const openStages = selectedPipeline?.stages.filter((s) => s.isActive) ?? [];

  const params = {
    page,
    pageSize,
    sortBy,
    sortDir,
    q: debouncedSearch || undefined,
    pipelineId: pipelineFilter || undefined,
    stageId: stageFilter || undefined,
    ownerId: ownerFilter || undefined,
    status: statusFilter || undefined,
    isStale: isStaleFilter || undefined,
  };

  const { data, isLoading, error, isFetching, refetch } = useQuery({
    queryKey: ['deals', params],
    queryFn: () => listDeals(params),
    staleTime: 30_000,
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteDeal(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['deals'] });
      showToast('Deal deleted', 'success');
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const reassignMutation = useMutation({
    mutationFn: ({ dealIds, newOwnerId }: { dealIds: string[]; newOwnerId: string | null }) =>
      reassignDeals(dealIds, newOwnerId),
    onSuccess: (result) => {
      queryClient.invalidateQueries({ queryKey: ['deals'] });
      setSelected(new Set());
      setShowAssign(false);
      showToast(`${result.reassigned} deal${result.reassigned === 1 ? '' : 's'} reassigned`, 'success');
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const handleSort = (field: SortField) => {
    if (field === sortBy) {
      setSortDir((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      setSortBy(field);
      setSortDir('asc');
    }
    setPage(1);
  };

  const handlePageChange = (p: number) => { setPage(p); setSelected(new Set()); };
  const handlePageSizeChange = (s: number) => { setPageSize(s); setPage(1); setSelected(new Set()); };

  const toggleSelect = (id: string) => {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
  };

  const toggleSelectAll = () => {
    if (!data) return;
    const allIds = data.data.map((d) => d.id);
    if (allIds.every((id) => selected.has(id))) {
      setSelected((prev) => { const next = new Set(prev); allIds.forEach((id) => next.delete(id)); return next; });
    } else {
      setSelected((prev) => { const next = new Set(prev); allIds.forEach((id) => next.add(id)); return next; });
    }
  };

  const handleBulkDelete = () => setShowDeleteConfirm(true);

  const confirmBulkDelete = async () => {
    const ids = Array.from(selected);
    setShowDeleteConfirm(false);
    let failed = 0;
    for (const id of ids) {
      try { await deleteDeal(id); } catch { failed++; }
    }
    queryClient.invalidateQueries({ queryKey: ['deals'] });
    setSelected(new Set());
    if (failed === 0) showToast(`${ids.length} deal${ids.length === 1 ? '' : 's'} deleted`, 'success');
    else showToast(`${ids.length - failed} deleted, ${failed} failed`, 'warning');
  };

  if (isLoading) return <LoadingBlock label="Loading deals…" />;
  if (error) return <ErrorBlock error={error} title="Could not load deals" onRetry={() => refetch()} />;

  const deals = data?.data ?? [];
  const allOnPageSelected = deals.length > 0 && deals.every((d) => selected.has(d.id));

  return (
    <div>
      <div className="flex flex-wrap items-center justify-between gap-4 mb-6">
        <h1 className="text-2xl font-bold text-gray-900">Deals</h1>
        <button
          type="button"
          onClick={() => navigate('/deals/new')}
          className="px-4 py-2 text-sm font-medium text-white bg-primary-600 rounded-md hover:bg-primary-700"
        >
          New deal
        </button>
      </div>

      {/* Filters */}
      <div className="bg-white rounded-lg border border-gray-200 p-4 mb-4">
        <div className="flex flex-wrap gap-3">
          <input
            type="search"
            placeholder="Search deals…"
            value={search}
            onChange={(e) => { setSearch(e.target.value); setPage(1); }}
            className="px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500 min-w-[200px]"
          />
          <select
            value={pipelineFilter}
            onChange={(e) => { setPipelineFilter(e.target.value); setStageFilter(''); setPage(1); }}
            className="px-3 py-2 border border-gray-300 rounded-md text-sm bg-white focus:outline-none focus:ring-2 focus:ring-primary-500"
            aria-label="Filter by pipeline"
          >
            <option value="">All pipelines</option>
            {pipelines.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
          </select>
          {pipelineFilter && openStages.length > 0 && (
            <select
              value={stageFilter}
              onChange={(e) => { setStageFilter(e.target.value); setPage(1); }}
              className="px-3 py-2 border border-gray-300 rounded-md text-sm bg-white focus:outline-none focus:ring-2 focus:ring-primary-500"
              aria-label="Filter by stage"
            >
              <option value="">All stages</option>
              {openStages.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
            </select>
          )}
          <select
            value={ownerFilter}
            onChange={(e) => { setOwnerFilter(e.target.value); setPage(1); }}
            className="px-3 py-2 border border-gray-300 rounded-md text-sm bg-white focus:outline-none focus:ring-2 focus:ring-primary-500"
            aria-label="Filter by owner"
          >
            <option value="">All owners</option>
            {owners.map((o) => <option key={o.id} value={o.id}>{o.displayName}</option>)}
          </select>
          <select
            value={statusFilter}
            onChange={(e) => { setStatusFilter(e.target.value); setPage(1); }}
            className="px-3 py-2 border border-gray-300 rounded-md text-sm bg-white focus:outline-none focus:ring-2 focus:ring-primary-500"
            aria-label="Filter by status"
          >
            <option value="">All statuses</option>
            <option value="open">Open</option>
            <option value="won">Won</option>
            <option value="lost">Lost</option>
          </select>
          <label className="flex items-center gap-2 text-sm text-gray-700 cursor-pointer">
            <input
              type="checkbox"
              checked={isStaleFilter}
              onChange={(e) => { setIsStaleFilter(e.target.checked); setPage(1); }}
              className="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
            />
            Stale only
          </label>
        </div>
      </div>

      <BulkActionBar
        selectedCount={selected.size}
        canAssign={canAssign}
        onClear={() => setSelected(new Set())}
        onDelete={handleBulkDelete}
        onAssign={() => setShowAssign(true)}
      />

      <div className="bg-white rounded-lg border border-gray-200 overflow-hidden">
        <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-4 py-3 w-10">
                  <input
                    type="checkbox"
                    checked={allOnPageSelected}
                    onChange={toggleSelectAll}
                    className="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                    aria-label="Select all on page"
                  />
                </th>
                <SortableHeader<SortField> field="name" label="Name" activeField={sortBy} direction={sortDir} onSort={handleSort} />
                <th className="px-4 sm:px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Company</th>
                <SortableHeader<SortField> field="amount" label="Amount" activeField={sortBy} direction={sortDir} onSort={handleSort} />
                <th className="px-4 sm:px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Stage</th>
                <th className="px-4 sm:px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Owner</th>
                <th className="px-4 sm:px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Status</th>
                <SortableHeader<SortField> field="expected_close_date" label="Close Date" activeField={sortBy} direction={sortDir} onSort={handleSort} />
                <SortableHeader<SortField> field="created_at" label="Created" activeField={sortBy} direction={sortDir} onSort={handleSort} />
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
              {deals.length === 0 ? (
                <tr>
                  <td colSpan={9}>
                    <EmptyBlock title="No deals found" message="Try adjusting your filters or create a new deal." />
                  </td>
                </tr>
              ) : (
                deals.map((deal) => (
                  <tr key={deal.id} className={`hover:bg-gray-50 ${selected.has(deal.id) ? 'bg-primary-50' : ''}`}>
                    <td className="px-4 py-3 w-10">
                      <input
                        type="checkbox"
                        checked={selected.has(deal.id)}
                        onChange={() => toggleSelect(deal.id)}
                        className="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                        aria-label={`Select ${deal.name}`}
                      />
                    </td>
                    <td className="px-4 sm:px-6 py-3">
                      <div className="flex items-center gap-2">
                        <Link to={`/deals/${deal.id}`} className="text-sm font-medium text-primary-700 hover:text-primary-900">
                          {deal.name}
                        </Link>
                        {deal.isStale && (
                          <span className="inline-flex px-1.5 py-0.5 text-xs font-medium bg-yellow-100 text-yellow-800 rounded">Stale</span>
                        )}
                        {deal.isOverdue && (
                          <span className="inline-flex px-1.5 py-0.5 text-xs font-medium bg-red-100 text-red-800 rounded">Overdue</span>
                        )}
                      </div>
                    </td>
                    <td className="px-4 sm:px-6 py-3 text-sm text-gray-600">{deal.companyName ?? '—'}</td>
                    <td className="px-4 sm:px-6 py-3 text-sm text-gray-800 font-medium">{formatMoney(deal.amount, deal.currency)}</td>
                    <td className="px-4 sm:px-6 py-3 text-sm text-gray-600">{deal.stageName ?? '—'}</td>
                    <td className="px-4 sm:px-6 py-3 text-sm text-gray-600">{deal.ownerName ?? '—'}</td>
                    <td className="px-4 sm:px-6 py-3"><StatusBadge status={deal.status} /></td>
                    <td className="px-4 sm:px-6 py-3 text-sm text-gray-600">{deal.expectedCloseDate ? formatDate(deal.expectedCloseDate) : '—'}</td>
                    <td className="px-4 sm:px-6 py-3 text-sm text-gray-500">{formatDate(deal.createdAt)}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
        <Pagination
          page={page}
          pageSize={pageSize}
          total={data?.total ?? 0}
          isFetching={isFetching}
          onPageChange={handlePageChange}
          onPageSizeChange={handlePageSizeChange}
          noun="deals"
        />
      </div>

      <AssignModal
        isOpen={showAssign}
        onClose={() => setShowAssign(false)}
        owners={owners}
        dealCount={selected.size}
        isLoading={reassignMutation.isPending}
        onConfirm={(ownerId) => reassignMutation.mutate({ dealIds: Array.from(selected), newOwnerId: ownerId })}
      />

      <ConfirmDialog
        isOpen={showDeleteConfirm}
        title={`Delete ${selected.size} deal${selected.size === 1 ? '' : 's'}?`}
        message={`This will move ${selected.size === 1 ? 'the deal' : `all ${selected.size} deals`} to the recycle bin.`}
        warning="Deals can be restored by an admin within 30 days."
        confirmLabel="Delete"
        danger
        isLoading={deleteMutation.isPending}
        onConfirm={confirmBulkDelete}
        onCancel={() => setShowDeleteConfirm(false)}
      />
    </div>
  );
}
