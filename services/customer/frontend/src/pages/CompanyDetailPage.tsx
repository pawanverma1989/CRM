import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { getCompany, deleteCompany, updateCompany } from '../api/companies';
import { getCustomFields } from '../api/customFields';
import { useOwners } from '../hooks/useOwners';
import { reassignRecords } from '../api/bulk';
import { usePermissions } from '../hooks/usePermissions';
import { useToast } from '../contexts/ToastContext';
import { TagInput } from '../components/TagInput';
import { ReassignModal } from '../components/ReassignModal';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { LoadingBlock, ErrorBlock, EmptyBlock, NotAvailablePanel } from '../components/StateBlocks';
import { VersionConflictBanner } from '../components/DuplicateWarning';
import { formatCustomFieldValue } from '../lib/customFields';
import { contactName, formatDateTime, formatMoney, formatNumber, getApiErrorMessage, isVersionConflict, orDash } from '../lib/utils';

export function CompanyDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { showToast } = useToast();
  const queryClient = useQueryClient();
  const { canReassign } = usePermissions();

  const [reassignOpen, setReassignOpen] = useState(false);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [versionConflict, setVersionConflict] = useState(false);

  const {
    data: company,
    isLoading,
    isError,
    error,
    refetch,
  } = useQuery({
    queryKey: ['company', id],
    queryFn: () => getCompany(id!),
    enabled: !!id,
  });

  const { owners } = useOwners();
  const { data: customFieldDefs = [] } = useQuery({
    queryKey: ['custom-fields', 'company'],
    queryFn: () => getCustomFields('company'),
  });

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: ['company', id] });
    queryClient.invalidateQueries({ queryKey: ['companies'] });
  };

  const tagsMutation = useMutation({
    mutationFn: (tags: string[]) => updateCompany(id!, { tags, version: company!.version }),
    onSuccess: () => {
      invalidate();
      showToast('Tags updated', 'success');
    },
    onError: (err) => {
      if (isVersionConflict(err)) setVersionConflict(true);
      else showToast(getApiErrorMessage(err), 'error');
    },
  });

  const reassignMutation = useMutation({
    mutationFn: (newOwnerId: string | null) =>
      reassignRecords({ recordType: 'company', recordIds: [id!], newOwnerId }),
    onSuccess: () => {
      invalidate();
      showToast('Owner updated', 'success');
      setReassignOpen(false);
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const deleteMutation = useMutation({
    mutationFn: () => deleteCompany(id!),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['companies'] });
      showToast('Company moved to the recycle bin', 'success');
      navigate('/companies');
    },
    onError: (err) => {
      showToast(getApiErrorMessage(err), 'error');
      setDeleteOpen(false);
    },
  });

  if (isLoading) return <LoadingBlock label="Loading company…" />;
  if (isError) return <ErrorBlock error={error} title="Could not load this company" onRetry={refetch} />;
  if (!company) return <ErrorBlock error="Company not found" title="Not found" />;

  return (
    <div className="max-w-4xl">
      <Link to="/companies" className="text-sm text-gray-500 hover:text-gray-700 mb-6 inline-flex items-center gap-1">
        ← Back to companies
      </Link>

      {versionConflict && (
        <div className="mt-4 mb-2">
          <VersionConflictBanner onReload={() => { setVersionConflict(false); refetch(); }} />
        </div>
      )}

      <div className="flex items-start justify-between mt-2 mb-6">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">{company.name}</h1>
          {company.domain && <p className="text-sm text-gray-500">{company.domain}</p>}
        </div>
        <div className="flex gap-2">
          <Link
            to={`/companies/${company.id}/edit`}
            className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50"
          >
            Edit
          </Link>
          <button
            onClick={() => setDeleteOpen(true)}
            className="px-4 py-2 text-sm font-medium text-red-700 bg-white border border-red-300 rounded-md hover:bg-red-50"
          >
            Delete
          </button>
        </div>
      </div>

      <div className="bg-white rounded-lg border border-gray-200 p-6 mb-6">
        <dl className="grid grid-cols-1 sm:grid-cols-2 gap-x-6 gap-y-4 text-sm">
          <div>
            <dt className="text-gray-500">Industry</dt>
            <dd className="text-gray-900 font-medium">{orDash(company.industryValue)}</dd>
          </div>
          <div>
            <dt className="text-gray-500">Phone</dt>
            <dd className="text-gray-900 font-medium">{orDash(company.phone)}</dd>
          </div>
          <div>
            <dt className="text-gray-500">Website</dt>
            <dd className="text-gray-900 font-medium">
              {company.website ? (
                <a href={company.website} target="_blank" rel="noreferrer" className="text-primary-700 hover:underline">
                  {company.website}
                </a>
              ) : (
                '—'
              )}
            </dd>
          </div>
          <div>
            <dt className="text-gray-500">Employee count</dt>
            <dd className="text-gray-900 font-medium">{formatNumber(company.employeeCount)}</dd>
          </div>
          <div>
            <dt className="text-gray-500">Annual revenue</dt>
            <dd className="text-gray-900 font-medium">{formatMoney(company.annualRevenue, company.currency ?? 'INR')}</dd>
          </div>
          <div>
            <dt className="text-gray-500">GSTIN</dt>
            <dd className="text-gray-900 font-medium">{orDash(company.gstin)}</dd>
          </div>
          <div>
            <dt className="text-gray-500">Owner</dt>
            <dd className="text-gray-900 font-medium flex items-center gap-2">
              {orDash(company.ownerName)}
              {canReassign && (
                <button
                  type="button"
                  onClick={() => setReassignOpen(true)}
                  className="text-xs text-primary-700 hover:underline font-normal"
                >
                  Reassign
                </button>
              )}
            </dd>
          </div>
          <div>
            <dt className="text-gray-500">Address</dt>
            <dd className="text-gray-900">
              {[company.addressLine1, company.addressLine2, company.city, company.state, company.postalCode, company.country]
                .filter(Boolean)
                .join(', ') || '—'}
            </dd>
          </div>
          <div>
            <dt className="text-gray-500">Created</dt>
            <dd className="text-gray-900">{formatDateTime(company.createdAt)}</dd>
          </div>
          <div>
            <dt className="text-gray-500">Last updated</dt>
            <dd className="text-gray-900">{formatDateTime(company.updatedAt)}</dd>
          </div>
        </dl>
      </div>

      <div className="bg-white rounded-lg border border-gray-200 p-6 mb-6">
        <h2 className="text-sm font-semibold text-gray-900 mb-3">Tags</h2>
        <TagInput value={company.tags} onChange={(tags) => tagsMutation.mutate(tags)} />
      </div>

      {customFieldDefs.length > 0 && (
        <div className="bg-white rounded-lg border border-gray-200 p-6 mb-6">
          <h2 className="text-sm font-semibold text-gray-900 mb-3">Custom fields</h2>
          <dl className="grid grid-cols-1 sm:grid-cols-2 gap-x-6 gap-y-4 text-sm">
            {customFieldDefs.map((def) => (
              <div key={def.id}>
                <dt className="text-gray-500">{def.label}</dt>
                <dd className="text-gray-900 font-medium">
                  {formatCustomFieldValue(def, company.customFields[def.fieldKey] ?? null, owners)}
                </dd>
              </div>
            ))}
          </dl>
        </div>
      )}

      <div className="bg-white rounded-lg border border-gray-200 p-6 mb-6">
        <h2 className="text-sm font-semibold text-gray-900 mb-3">Contacts ({company.contacts.length})</h2>
        {company.contacts.length === 0 ? (
          <EmptyBlock title="No contacts linked to this company yet" />
        ) : (
          <ul className="divide-y divide-gray-200">
            {company.contacts.map((c) => (
              <li key={c.id} className="py-2 flex items-center justify-between">
                <Link to={`/contacts/${c.id}`} className="text-sm font-medium text-primary-700 hover:underline">
                  {contactName(c)}
                </Link>
                <span className="text-sm text-gray-500">{c.email ?? c.phone ?? '—'}</span>
              </li>
            ))}
          </ul>
        )}
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <NotAvailablePanel title="Deals" service="Sales" />
        <NotAvailablePanel title="Timeline" service="Activity" />
      </div>

      <ReassignModal
        isOpen={reassignOpen}
        onClose={() => setReassignOpen(false)}
        currentOwnerId={company.ownerId}
        isLoading={reassignMutation.isPending}
        onConfirm={(ownerId) => reassignMutation.mutate(ownerId)}
      />

      <ConfirmDialog
        isOpen={deleteOpen}
        title="Delete company"
        message={`Move ${company.name} to the recycle bin? Its contacts are kept but unlinked. Admins can restore within 30 days.`}
        confirmLabel="Delete"
        danger
        isLoading={deleteMutation.isPending}
        onConfirm={() => deleteMutation.mutate()}
        onCancel={() => setDeleteOpen(false)}
      />
    </div>
  );
}
