import { useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { getCompanies, type CompanySortField } from '../api/companies';
import type { SortDirection } from '../api/contacts';
import { useOwners } from '../hooks/useOwners';
import { getCustomFields } from '../api/customFields';
import { getPicklist } from '../api/picklists';
import { reassignRecords, bulkDeleteRecords } from '../api/bulk';
import { useDebouncedValue } from '../hooks/useDebouncedValue';
import { usePermissions } from '../hooks/usePermissions';
import { useToast } from '../contexts/ToastContext';
import { Pagination } from '../components/Pagination';
import { SortableHeader } from '../components/SortableHeader';
import { BulkActionBar } from '../components/BulkActionBar';
import { TagChips, TagInput } from '../components/TagInput';
import { SelectField } from '../components/SelectField';
import { CustomFieldFilterInput } from '../components/CustomFieldFilterInput';
import { ReassignModal } from '../components/ReassignModal';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { LoadingBlock, ErrorBlock, EmptyBlock } from '../components/StateBlocks';
import { DEFAULT_PAGE_SIZE } from '../lib/validation';
import { formatDate, getApiErrorMessage } from '../lib/utils';

const UNOWNED = '__unowned__';

export function CompaniesPage() {
  const navigate = useNavigate();
  const { showToast } = useToast();
  const queryClient = useQueryClient();
  const { canMerge, canReassign } = usePermissions();

  const [search, setSearch] = useState('');
  const debouncedSearch = useDebouncedValue(search.trim(), 300);

  const [ownerFilter, setOwnerFilter] = useState('');
  const [tagFilter, setTagFilter] = useState<string[]>([]);
  const [industryId, setIndustryId] = useState('');
  const [city, setCity] = useState('');
  const [state, setState] = useState('');
  const [country, setCountry] = useState('');
  const [createdFrom, setCreatedFrom] = useState('');
  const [createdTo, setCreatedTo] = useState('');
  const [updatedFrom, setUpdatedFrom] = useState('');
  const [updatedTo, setUpdatedTo] = useState('');
  const [customFieldFilters, setCustomFieldFilters] = useState<Record<string, string>>({});

  const [sort, setSort] = useState<CompanySortField>('updated_at');
  const [direction, setDirection] = useState<SortDirection>('desc');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE);

  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [reassignOpen, setReassignOpen] = useState(false);
  const [deleteOpen, setDeleteOpen] = useState(false);

  const resetToFirstPage = () => setPage(1);

  const { owners } = useOwners();
  const { data: industries = [] } = useQuery({
    queryKey: ['picklist', 'company_industry'],
    queryFn: () => getPicklist('company_industry'),
  });
  const { data: customFieldDefs = [] } = useQuery({
    queryKey: ['custom-fields', 'company'],
    queryFn: () => getCustomFields('company'),
  });

  const listParams = useMemo(
    () => ({
      q: debouncedSearch || undefined,
      ownerId: ownerFilter && ownerFilter !== UNOWNED ? ownerFilter : undefined,
      unowned: ownerFilter === UNOWNED ? true : undefined,
      tags: tagFilter.length ? tagFilter : undefined,
      industryId: industryId || undefined,
      city: city || undefined,
      state: state || undefined,
      country: country || undefined,
      createdFrom: createdFrom || undefined,
      createdTo: createdTo || undefined,
      updatedFrom: updatedFrom || undefined,
      updatedTo: updatedTo || undefined,
      customFields: Object.keys(customFieldFilters).length ? customFieldFilters : undefined,
      sort,
      direction,
      page,
      pageSize,
    }),
    [
      debouncedSearch,
      ownerFilter,
      tagFilter,
      industryId,
      city,
      state,
      country,
      createdFrom,
      createdTo,
      updatedFrom,
      updatedTo,
      customFieldFilters,
      sort,
      direction,
      page,
      pageSize,
    ]
  );

  const { data, isLoading, isFetching, isError, error, refetch } = useQuery({
    queryKey: ['companies', listParams],
    queryFn: () => getCompanies(listParams),
    placeholderData: (prev) => prev,
  });

  const companies = data?.data ?? [];
  const allOnPageSelected = companies.length > 0 && companies.every((c) => selectedIds.has(c.id));

  const toggleSort = (field: CompanySortField) => {
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
        companies.forEach((c) => next.delete(c.id));
      } else {
        companies.forEach((c) => next.add(c.id));
      }
      return next;
    });
  };

  const reassignMutation = useMutation({
    mutationFn: (newOwnerId: string | null) =>
      reassignRecords({ recordType: 'company', recordIds: Array.from(selectedIds), newOwnerId }),
    onSuccess: (result) => {
      queryClient.invalidateQueries({ queryKey: ['companies'] });
      showToast(`Reassigned ${result.succeeded} compan${result.succeeded === 1 ? 'y' : 'ies'}`, 'success');
      if (result.failed.length > 0) {
        showToast(`${result.failed.length} could not be reassigned`, 'warning');
      }
      setReassignOpen(false);
      setSelectedIds(new Set());
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const deleteMutation = useMutation({
    mutationFn: () => bulkDeleteRecords({ recordType: 'company', recordIds: Array.from(selectedIds) }),
    onSuccess: (result) => {
      queryClient.invalidateQueries({ queryKey: ['companies'] });
      showToast(`Deleted ${result.succeeded} compan${result.succeeded === 1 ? 'y' : 'ies'}`, 'success');
      if (result.failed.length > 0) {
        showToast(`${result.failed.length} could not be deleted`, 'warning');
      }
      setDeleteOpen(false);
      setSelectedIds(new Set());
    },
    onError: (err) => {
      showToast(getApiErrorMessage(err), 'error');
      setDeleteOpen(false);
    },
  });

  const onMerge = () => {
    const ids = Array.from(selectedIds);
    navigate(`/companies/merge?ids=${ids.map(encodeURIComponent).join(',')}`);
  };

  return (
    <div>
      <div className="flex items-center justify-between mb-6">
        <h1 className="text-2xl font-bold text-gray-900">Companies</h1>
        <Link
          to="/companies/new"
          className="px-4 py-2 bg-primary-600 text-white text-sm font-medium rounded-md hover:bg-primary-700"
        >
          Add company
        </Link>
      </div>

      <div className="bg-white rounded-lg border border-gray-200 p-4 mb-4 space-y-3">
        <div className="flex flex-wrap gap-3">
          <div className="flex-1 min-w-[220px]">
            <label htmlFor="company-search" className="block text-sm font-medium text-gray-700 mb-1">
              Quick search
            </label>
            <input
              id="company-search"
              type="search"
              placeholder="Search by name or domain…"
              value={search}
              onChange={(e) => {
                setSearch(e.target.value);
                resetToFirstPage();
              }}
              className="w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500"
            />
          </div>
          <div className="w-48">
            <SelectField
              label="Owner"
              value={ownerFilter}
              placeholder="All owners"
              options={[
                { value: UNOWNED, label: 'Unowned' },
                ...owners.map((o) => ({ value: o.id, label: o.displayName })),
              ]}
              onChange={(e) => {
                setOwnerFilter(e.target.value);
                resetToFirstPage();
              }}
            />
          </div>
          <div className="w-48">
            <SelectField
              label="Industry"
              value={industryId}
              placeholder="All industries"
              options={industries.filter((i) => i.isActive).map((i) => ({ value: i.id, label: i.value }))}
              onChange={(e) => {
                setIndustryId(e.target.value);
                resetToFirstPage();
              }}
            />
          </div>
          <div className="w-40">
            <label htmlFor="company-city" className="block text-sm font-medium text-gray-700 mb-1">
              City
            </label>
            <input
              id="company-city"
              value={city}
              onChange={(e) => {
                setCity(e.target.value);
                resetToFirstPage();
              }}
              className="w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500"
            />
          </div>
          <div className="w-40">
            <label htmlFor="company-state" className="block text-sm font-medium text-gray-700 mb-1">
              State
            </label>
            <input
              id="company-state"
              value={state}
              onChange={(e) => {
                setState(e.target.value);
                resetToFirstPage();
              }}
              className="w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500"
            />
          </div>
          <div className="w-28">
            <label htmlFor="company-country" className="block text-sm font-medium text-gray-700 mb-1">
              Country
            </label>
            <input
              id="company-country"
              placeholder="IN"
              maxLength={2}
              value={country}
              onChange={(e) => {
                setCountry(e.target.value.toUpperCase());
                resetToFirstPage();
              }}
              className="w-full px-3 py-2 border border-gray-300 rounded-md text-sm uppercase focus:outline-none focus:ring-2 focus:ring-primary-500"
            />
          </div>
        </div>

        <div className="flex flex-wrap gap-3 items-end">
          <div className="w-40">
            <label htmlFor="company-created-from" className="block text-sm font-medium text-gray-700 mb-1">
              Created from
            </label>
            <input
              id="company-created-from"
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
            <label htmlFor="company-created-to" className="block text-sm font-medium text-gray-700 mb-1">
              Created to
            </label>
            <input
              id="company-created-to"
              type="date"
              value={createdTo}
              onChange={(e) => {
                setCreatedTo(e.target.value);
                resetToFirstPage();
              }}
              className="w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500"
            />
          </div>
          <div className="w-40">
            <label htmlFor="company-updated-from" className="block text-sm font-medium text-gray-700 mb-1">
              Updated from
            </label>
            <input
              id="company-updated-from"
              type="date"
              value={updatedFrom}
              onChange={(e) => {
                setUpdatedFrom(e.target.value);
                resetToFirstPage();
              }}
              className="w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500"
            />
          </div>
          <div className="w-40">
            <label htmlFor="company-updated-to" className="block text-sm font-medium text-gray-700 mb-1">
              Updated to
            </label>
            <input
              id="company-updated-to"
              type="date"
              value={updatedTo}
              onChange={(e) => {
                setUpdatedTo(e.target.value);
                resetToFirstPage();
              }}
              className="w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500"
            />
          </div>
          <div className="flex-1 min-w-[260px]">
            <TagInput value={tagFilter} onChange={(tags) => { setTagFilter(tags); resetToFirstPage(); }} label="Tags" />
          </div>
        </div>

        {customFieldDefs.length > 0 && (
          <div className="flex flex-wrap gap-3 pt-2 border-t border-gray-100">
            {customFieldDefs.map((def) => (
              <div key={def.id} className="w-48">
                <CustomFieldFilterInput
                  definition={def}
                  owners={owners}
                  value={customFieldFilters[def.fieldKey] ?? ''}
                  onChange={(value) => {
                    setCustomFieldFilters((prev) => {
                      const next = { ...prev };
                      if (value === '') delete next[def.fieldKey];
                      else next[def.fieldKey] = value;
                      return next;
                    });
                    resetToFirstPage();
                  }}
                />
              </div>
            ))}
          </div>
        )}
      </div>

      <BulkActionBar
        recordType="company"
        selectedCount={selectedIds.size}
        canMerge={canMerge}
        canReassign={canReassign}
        onClear={() => setSelectedIds(new Set())}
        onDelete={() => setDeleteOpen(true)}
        onReassign={() => setReassignOpen(true)}
        onMerge={onMerge}
      />

      <div className="bg-white rounded-lg border border-gray-200 overflow-hidden">
        {isLoading ? (
          <LoadingBlock label="Loading companies…" />
        ) : isError ? (
          <ErrorBlock error={error} title="Could not load companies" onRetry={refetch} />
        ) : companies.length === 0 ? (
          <EmptyBlock
            title="No companies found"
            message="Try changing your filters, or add the first company."
            action={
              <Link to="/companies/new" className="text-primary-600 hover:underline text-sm font-medium">
                Add company
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
                        aria-label="Select all companies on this page"
                        checked={allOnPageSelected}
                        onChange={toggleAllOnPage}
                        className="h-4 w-4 rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                      />
                    </th>
                    <SortableHeader field="name" label="Name" activeField={sort} direction={direction} onSort={toggleSort} />
                    <th scope="col" className="px-4 sm:px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Domain
                    </th>
                    <th scope="col" className="px-4 sm:px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Industry
                    </th>
                    <SortableHeader field="owner" label="Owner" activeField={sort} direction={direction} onSort={toggleSort} />
                    <th scope="col" className="px-4 sm:px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Tags
                    </th>
                    <SortableHeader field="updated_at" label="Updated" activeField={sort} direction={direction} onSort={toggleSort} />
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-200">
                  {companies.map((company) => (
                    <tr key={company.id} className="hover:bg-gray-50">
                      <td className="px-4 py-4">
                        <input
                          type="checkbox"
                          aria-label={`Select ${company.name}`}
                          checked={selectedIds.has(company.id)}
                          onChange={() => toggleRow(company.id)}
                          className="h-4 w-4 rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                        />
                      </td>
                      <td className="px-4 sm:px-6 py-4 whitespace-nowrap">
                        <Link to={`/companies/${company.id}`} className="text-sm font-medium text-primary-700 hover:underline">
                          {company.name}
                        </Link>
                      </td>
                      <td className="px-4 sm:px-6 py-4 whitespace-nowrap text-sm text-gray-600">{company.domain ?? '—'}</td>
                      <td className="px-4 sm:px-6 py-4 whitespace-nowrap text-sm text-gray-600">{company.industryValue ?? '—'}</td>
                      <td className="px-4 sm:px-6 py-4 whitespace-nowrap text-sm text-gray-600">{company.ownerName ?? '—'}</td>
                      <td className="px-4 sm:px-6 py-4">
                        <TagChips tags={company.tags} />
                      </td>
                      <td className="px-4 sm:px-6 py-4 whitespace-nowrap text-sm text-gray-600">
                        {formatDate(company.updatedAt)}
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
              noun="companies"
            />
          </>
        )}
      </div>

      <ReassignModal
        isOpen={reassignOpen}
        onClose={() => setReassignOpen(false)}
        recordCount={selectedIds.size}
        isLoading={reassignMutation.isPending}
        onConfirm={(ownerId) => reassignMutation.mutate(ownerId)}
      />

      <ConfirmDialog
        isOpen={deleteOpen}
        title="Delete companies"
        message={`Move ${selectedIds.size} compan${selectedIds.size === 1 ? 'y' : 'ies'} to the recycle bin? Their contacts are kept but unlinked. Admins can restore within 30 days.`}
        confirmLabel="Delete"
        danger
        isLoading={deleteMutation.isPending}
        onConfirm={() => deleteMutation.mutate()}
        onCancel={() => setDeleteOpen(false)}
      />
    </div>
  );
}
