import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { getLeads, deleteLead, bulkAssign, type LeadSortField, type SortDirection } from '../api/leads';
import { useOwners } from '../hooks/useOwners';
import { getLeadSources } from '../api/leadSources';
import { useAuth } from '../contexts/AuthContext';
import { useDebouncedValue } from '../hooks/useDebouncedValue';
import { usePermissions } from '../hooks/usePermissions';
import { useToast } from '../contexts/ToastContext';
import { Pagination } from '../components/Pagination';
import { SortableHeader } from '../components/SortableHeader';
import { BulkActionBar } from '../components/BulkActionBar';
import { AssignModal } from '../components/AssignModal';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { StatusBadge } from '../components/StatusBadge';
import { TagChips, TagInput } from '../components/TagInput';
import { SelectField } from '../components/SelectField';
import { LoadingBlock, ErrorBlock, EmptyBlock } from '../components/StateBlocks';
import { DEFAULT_PAGE_SIZE } from '../lib/validation';
import { leadName, formatDate, getApiErrorMessage } from '../lib/utils';
import { subDays, format } from 'date-fns';

type QuickFilter = 'my_open' | 'unassigned' | 'new_this_week' | 'disqualified' | 'none';

const STATUS_OPTIONS = [
  { value: '', label: 'All statuses' },
  { value: 'new', label: 'New' },
  { value: 'contacted', label: 'Contacted' },
  { value: 'qualified', label: 'Qualified' },
  { value: 'disqualified', label: 'Disqualified' },
  { value: 'converted', label: 'Converted' },
];

export function LeadsPage() {
  const { user } = useAuth();
  const { showToast } = useToast();
  const queryClient = useQueryClient();
  const { canAssign } = usePermissions();

  const [quickFilter, setQuickFilter] = useState<QuickFilter>('my_open');
  const [search, setSearch] = useState('');
  const debouncedSearch = useDebouncedValue(search.trim(), 300);

  const [statusFilter, setStatusFilter] = useState('');
  const [ownerFilter, setOwnerFilter] = useState('');
  const [leadSourceFilter, setLeadSourceFilter] = useState('');
  const [utmCampaign, setUtmCampaign] = useState('');
  const [tagFilter, setTagFilter] = useState<string[]>([]);
  const [createdFrom, setCreatedFrom] = useState('');
  const [createdTo, setCreatedTo] = useState('');

  const [sort, setSort] = useState<LeadSortField>('updated_at');
  const [direction, setDirection] = useState<SortDirection>('desc');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE);

  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [assignOpen, setAssignOpen] = useState(false);
  const [deleteOpen, setDeleteOpen] = useState(false);

  const resetToFirstPage = () => setPage(1);

  const { owners } = useOwners();
  const { data: leadSources = [] } = useQuery({ queryKey: ['lead-sources'], queryFn: getLeadSources });

  // Build params based on quick filter + explicit filters
  const listParams = useMemo(() => {
    const base: Parameters<typeof getLeads>[0] = {
      q: debouncedSearch || undefined,
      sort,
      direction,
      page,
      pageSize,
    };

    // Quick filters take precedence; explicit filters are applied on top when
    // quick filter is 'none'.
    if (quickFilter === 'my_open' && user) {
      base.ownerId = user.id;
      // Exclude terminal statuses
      base.status = statusFilter || undefined;
    } else if (quickFilter === 'unassigned') {
      base.unassigned = true;
    } else if (quickFilter === 'new_this_week') {
      base.status = 'new';
      base.createdFrom = format(subDays(new Date(), 7), 'yyyy-MM-dd');
    } else if (quickFilter === 'disqualified') {
      base.status = 'disqualified';
    } else {
      // 'none' — use explicit filters
      if (statusFilter) base.status = statusFilter;
      if (ownerFilter) base.ownerId = ownerFilter;
      if (leadSourceFilter) base.leadSourceId = leadSourceFilter;
      if (utmCampaign) base.utmCampaign = utmCampaign;
      if (tagFilter.length) base.tags = tagFilter;
      if (createdFrom) base.createdFrom = createdFrom;
      if (createdTo) base.createdTo = createdTo;
    }

    return base;
  }, [
    debouncedSearch,
    quickFilter,
    statusFilter,
    ownerFilter,
    leadSourceFilter,
    utmCampaign,
    tagFilter,
    createdFrom,
    createdTo,
    sort,
    direction,
    page,
    pageSize,
    user,
  ]);

  const { data, isLoading, isFetching, isError, error, refetch } = useQuery({
    queryKey: ['leads', listParams],
    queryFn: () => getLeads(listParams),
    placeholderData: (prev) => prev,
  });

  const leads = data?.data ?? [];
  const allOnPageSelected = leads.length > 0 && leads.every((l) => selectedIds.has(l.id));

  const toggleSort = (field: LeadSortField) => {
    if (field === sort) {
      setDirection((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      setSort(field);
      setDirection('asc');
    }
  };

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
      if (allOnPageSelected) {
        leads.forEach((l) => next.delete(l.id));
      } else {
        leads.forEach((l) => next.add(l.id));
      }
      return next;
    });
  };

  const assignMutation = useMutation({
    mutationFn: (ownerId: string | null) =>
      bulkAssign(Array.from(selectedIds), ownerId),
    onSuccess: (result) => {
      queryClient.invalidateQueries({ queryKey: ['leads'] });
      showToast(`Assigned ${result.assigned} lead${result.assigned === 1 ? '' : 's'}`, 'success');
      if (result.skipped > 0) {
        showToast(`${result.skipped} could not be assigned`, 'warning');
      }
      setAssignOpen(false);
      setSelectedIds(new Set());
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const deleteMutation = useMutation({
    mutationFn: async () => {
      const ids = Array.from(selectedIds);
      await Promise.all(ids.map((id) => deleteLead(id)));
      return ids.length;
    },
    onSuccess: (count) => {
      queryClient.invalidateQueries({ queryKey: ['leads'] });
      showToast(`Deleted ${count} lead${count === 1 ? '' : 's'}`, 'success');
      setDeleteOpen(false);
      setSelectedIds(new Set());
    },
    onError: (err) => {
      showToast(getApiErrorMessage(err), 'error');
      setDeleteOpen(false);
    },
  });

  const quickFilterButtons: { key: QuickFilter; label: string; adminOnly?: boolean }[] = [
    { key: 'my_open', label: 'My open leads' },
    { key: 'unassigned', label: 'Unassigned', adminOnly: true },
    { key: 'new_this_week', label: 'New this week' },
    { key: 'disqualified', label: 'Disqualified' },
    { key: 'none', label: 'All leads' },
  ];

  return (
    <div>
      <div className="flex items-center justify-between mb-6">
        <h1 className="text-2xl font-bold text-gray-900">Leads</h1>
        <Link
          to="/new"
          className="px-4 py-2 bg-primary-600 text-white text-sm font-medium rounded-md hover:bg-primary-700"
        >
          Add lead
        </Link>
      </div>

      {/* Quick filters */}
      <div className="flex flex-wrap gap-2 mb-4" role="group" aria-label="Quick filters">
        {quickFilterButtons
          .filter((btn) => !btn.adminOnly || canAssign)
          .map((btn) => (
            <button
              key={btn.key}
              type="button"
              onClick={() => {
                setQuickFilter(btn.key);
                resetToFirstPage();
              }}
              className={`px-3 py-1.5 rounded-md text-sm font-medium border transition-colors ${
                quickFilter === btn.key
                  ? 'bg-primary-600 text-white border-primary-600'
                  : 'bg-white text-gray-700 border-gray-300 hover:bg-gray-50'
              }`}
            >
              {btn.label}
            </button>
          ))}
      </div>

      <div className="bg-white rounded-lg border border-gray-200 p-4 mb-4 space-y-3">
        <div className="flex flex-wrap gap-3">
          <div className="flex-1 min-w-[220px]">
            <label htmlFor="lead-search" className="block text-sm font-medium text-gray-700 mb-1">
              Quick search
            </label>
            <input
              id="lead-search"
              type="search"
              placeholder="Search by name, email or phone…"
              value={search}
              onChange={(e) => {
                setSearch(e.target.value);
                resetToFirstPage();
              }}
              className="w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500"
            />
          </div>
          {quickFilter === 'none' && (
            <>
              <div className="w-44">
                <SelectField
                  label="Status"
                  value={statusFilter}
                  placeholder="All statuses"
                  options={STATUS_OPTIONS.filter((o) => o.value !== '')}
                  onChange={(e) => {
                    setStatusFilter(e.target.value);
                    resetToFirstPage();
                  }}
                />
              </div>
              <div className="w-48">
                <SelectField
                  label="Owner"
                  value={ownerFilter}
                  placeholder="All owners"
                  options={owners.map((o) => ({ value: o.id, label: o.displayName }))}
                  onChange={(e) => {
                    setOwnerFilter(e.target.value);
                    resetToFirstPage();
                  }}
                />
              </div>
              <div className="w-48">
                <SelectField
                  label="Lead source"
                  value={leadSourceFilter}
                  placeholder="All sources"
                  options={leadSources.filter((s) => s.isActive).map((s) => ({ value: s.id, label: s.name }))}
                  onChange={(e) => {
                    setLeadSourceFilter(e.target.value);
                    resetToFirstPage();
                  }}
                />
              </div>
              <div className="w-44">
                <label htmlFor="lead-utm" className="block text-sm font-medium text-gray-700 mb-1">UTM campaign</label>
                <input
                  id="lead-utm"
                  value={utmCampaign}
                  onChange={(e) => {
                    setUtmCampaign(e.target.value);
                    resetToFirstPage();
                  }}
                  className="w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500"
                />
              </div>
            </>
          )}
        </div>

        {quickFilter === 'none' && (
          <div className="flex flex-wrap gap-3 items-end">
            <div className="w-40">
              <label htmlFor="lead-created-from" className="block text-sm font-medium text-gray-700 mb-1">Created from</label>
              <input
                id="lead-created-from"
                type="date"
                value={createdFrom}
                onChange={(e) => {
                  setCreatedFrom(e.target.value);
                  resetToFirstPage();
                }}
                className="w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500"
              />
            </div>
            <div className="w-40">
              <label htmlFor="lead-created-to" className="block text-sm font-medium text-gray-700 mb-1">Created to</label>
              <input
                id="lead-created-to"
                type="date"
                value={createdTo}
                onChange={(e) => {
                  setCreatedTo(e.target.value);
                  resetToFirstPage();
                }}
                className="w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500"
              />
            </div>
            <div className="flex-1 min-w-[260px]">
              <TagInput
                value={tagFilter}
                onChange={(tags) => {
                  setTagFilter(tags);
                  resetToFirstPage();
                }}
                label="Tags"
              />
            </div>
          </div>
        )}
      </div>

      <BulkActionBar
        selectedCount={selectedIds.size}
        canAssign={canAssign}
        onClear={() => setSelectedIds(new Set())}
        onDelete={() => setDeleteOpen(true)}
        onAssign={() => setAssignOpen(true)}
      />

      <div className="bg-white rounded-lg border border-gray-200 overflow-hidden">
        {isLoading ? (
          <LoadingBlock label="Loading leads…" />
        ) : isError ? (
          <ErrorBlock error={error} title="Could not load leads" onRetry={refetch} />
        ) : leads.length === 0 ? (
          <EmptyBlock
            title="No leads found"
            message="Try changing your filters, or add the first lead."
            action={
              <Link to="/new" className="text-primary-600 hover:underline text-sm font-medium">
                Add lead
              </Link>
            }
          />
        ) : (
          <>
            <div className="overflow-x-auto">
              <table className="min-w-full divide-y divide-gray-200">
                <thead className="bg-gray-50">
                  <tr>
                    <th scope="col" className="px-4 py-3 w-10">
                      <input
                        type="checkbox"
                        aria-label="Select all leads on this page"
                        checked={allOnPageSelected}
                        onChange={toggleAllOnPage}
                        className="h-4 w-4 rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                      />
                    </th>
                    <SortableHeader field="name" label="Name" activeField={sort} direction={direction} onSort={toggleSort} />
                    <th scope="col" className="px-4 sm:px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Email</th>
                    <th scope="col" className="px-4 sm:px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Phone</th>
                    <SortableHeader field="status" label="Status" activeField={sort} direction={direction} onSort={toggleSort} />
                    <th scope="col" className="px-4 sm:px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Source</th>
                    <SortableHeader field="owner" label="Owner" activeField={sort} direction={direction} onSort={toggleSort} />
                    <th scope="col" className="px-4 sm:px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Tags</th>
                    <SortableHeader field="created_at" label="Created" activeField={sort} direction={direction} onSort={toggleSort} />
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-200">
                  {leads.map((lead) => (
                    <tr key={lead.id} className="hover:bg-gray-50">
                      <td className="px-4 py-4">
                        <input
                          type="checkbox"
                          aria-label={`Select ${leadName(lead)}`}
                          checked={selectedIds.has(lead.id)}
                          onChange={() => toggleRow(lead.id)}
                          className="h-4 w-4 rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                        />
                      </td>
                      <td className="px-4 sm:px-6 py-4 whitespace-nowrap">
                        <Link to={`/${lead.id}`} className="text-sm font-medium text-primary-700 hover:underline">
                          {leadName(lead)}
                        </Link>
                      </td>
                      <td className="px-4 sm:px-6 py-4 whitespace-nowrap text-sm text-gray-600">{lead.email ?? '—'}</td>
                      <td className="px-4 sm:px-6 py-4 whitespace-nowrap text-sm text-gray-600">{lead.phone ?? '—'}</td>
                      <td className="px-4 sm:px-6 py-4 whitespace-nowrap">
                        <StatusBadge status={lead.status} />
                      </td>
                      <td className="px-4 sm:px-6 py-4 whitespace-nowrap text-sm text-gray-600">{lead.leadSourceName ?? '—'}</td>
                      <td className="px-4 sm:px-6 py-4 whitespace-nowrap text-sm text-gray-600">
                        {owners.find((o) => o.id === lead.ownerId)?.displayName ?? '—'}
                      </td>
                      <td className="px-4 sm:px-6 py-4">
                        <TagChips tags={lead.tags} />
                      </td>
                      <td className="px-4 sm:px-6 py-4 whitespace-nowrap text-sm text-gray-600">
                        {formatDate(lead.createdAt)}
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
              isFetching={isFetching}
              onPageChange={setPage}
              onPageSizeChange={(size) => {
                setPageSize(size);
                resetToFirstPage();
              }}
              noun="leads"
            />
          </>
        )}
      </div>

      <AssignModal
        isOpen={assignOpen}
        onClose={() => setAssignOpen(false)}
        leadCount={selectedIds.size}
        isLoading={assignMutation.isPending}
        onConfirm={(ownerId) => assignMutation.mutate(ownerId)}
      />

      <ConfirmDialog
        isOpen={deleteOpen}
        title="Delete leads"
        message={`Move ${selectedIds.size} lead${selectedIds.size === 1 ? '' : 's'} to the recycle bin? Admins can restore within 30 days.`}
        confirmLabel="Delete"
        danger
        isLoading={deleteMutation.isPending}
        onConfirm={() => deleteMutation.mutate()}
        onCancel={() => setDeleteOpen(false)}
      />
    </div>
  );
}
