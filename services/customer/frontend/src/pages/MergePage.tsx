import { useMemo, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { useMutation, useQueries, useQueryClient } from '@tanstack/react-query';
import { getContact } from '../api/contacts';
import { getCompany } from '../api/companies';
import { mergeContacts, mergeCompanies } from '../api/bulk';
import { useToast } from '../contexts/ToastContext';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { LoadingBlock, ErrorBlock } from '../components/StateBlocks';
import { contactName, formatDate, getApiErrorMessage, orDash } from '../lib/utils';
import type { CompanyDetailDto, ContactDto, EntityType } from '../types';

interface MergePageProps {
  entityType: EntityType;
}

type MergeRecord = ContactDto | CompanyDetailDto;

interface FieldRow {
  key: string;
  label: string;
  format: (record: MergeRecord) => string;
}

const contactFields: FieldRow[] = [
  { key: 'firstName', label: 'First name', format: (r) => (r as ContactDto).firstName },
  { key: 'lastName', label: 'Last name', format: (r) => orDash((r as ContactDto).lastName) },
  { key: 'email', label: 'Email', format: (r) => orDash((r as ContactDto).email) },
  { key: 'phone', label: 'Phone', format: (r) => orDash((r as ContactDto).phoneNormalized ?? (r as ContactDto).phone) },
  { key: 'jobTitle', label: 'Job title', format: (r) => orDash((r as ContactDto).jobTitle) },
  { key: 'companyId', label: 'Company', format: (r) => orDash((r as ContactDto).companyName) },
  { key: 'ownerId', label: 'Owner', format: (r) => orDash(r.ownerName) },
  { key: 'city', label: 'City', format: (r) => orDash(r.city) },
  { key: 'country', label: 'Country', format: (r) => orDash(r.country) },
];

const companyFields: FieldRow[] = [
  { key: 'name', label: 'Name', format: (r) => (r as CompanyDetailDto).name },
  { key: 'domain', label: 'Domain', format: (r) => orDash((r as CompanyDetailDto).domain) },
  { key: 'industryId', label: 'Industry', format: (r) => orDash((r as CompanyDetailDto).industryValue) },
  { key: 'phone', label: 'Phone', format: (r) => orDash((r as CompanyDetailDto).phone) },
  { key: 'website', label: 'Website', format: (r) => orDash((r as CompanyDetailDto).website) },
  { key: 'ownerId', label: 'Owner', format: (r) => orDash(r.ownerName) },
  { key: 'city', label: 'City', format: (r) => orDash(r.city) },
  { key: 'country', label: 'Country', format: (r) => orDash(r.country) },
];

/** DUP-4..6: an admin or manager merges two contacts or two companies. */
export function MergePage({ entityType }: MergePageProps) {
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const { showToast } = useToast();
  const queryClient = useQueryClient();

  const ids = useMemo(
    () => (params.get('ids') ?? '').split(',').map((s) => decodeURIComponent(s)).filter(Boolean),
    [params]
  );

  const [survivorId, setSurvivorId] = useState<string>(ids[0] ?? '');
  const [fieldChoices, setFieldChoices] = useState<Record<string, 'survivor' | 'loser'>>({});
  const [confirmOpen, setConfirmOpen] = useState(false);

  const results = useQueries({
    queries: ids.map((id) => ({
      queryKey: [entityType, id],
      queryFn: () => (entityType === 'contact' ? getContact(id) : getCompany(id)),
      enabled: ids.length === 2,
    })),
  });

  const isLoading = results.some((r) => r.isLoading);
  const isError = results.some((r) => r.isError);
  const records = results.map((r) => r.data).filter((d): d is MergeRecord => !!d);

  const mergeMutation = useMutation({
    mutationFn: (loserId: string) => {
      const payload = { survivorId, loserId, fieldChoices };
      return entityType === 'contact' ? mergeContacts(payload) : mergeCompanies(payload);
    },
    onSuccess: (result) => {
      queryClient.invalidateQueries({ queryKey: [entityType === 'contact' ? 'contacts' : 'companies'] });
      showToast('Records merged', 'success');
      navigate(entityType === 'contact' ? `/contacts/${result.id}` : `/companies/${result.id}`);
    },
    onError: (err) => {
      showToast(getApiErrorMessage(err), 'error');
      setConfirmOpen(false);
    },
  });

  const listPath = entityType === 'contact' ? '/contacts' : '/companies';
  const noun = entityType === 'contact' ? 'contacts' : 'companies';

  if (ids.length !== 2) {
    return (
      <div className="max-w-2xl">
        <ErrorBlock
          error="Select exactly two records from the list to merge."
          title="Nothing to merge"
        />
        <Link to={listPath} className="text-primary-600 hover:underline text-sm">
          Back to {noun}
        </Link>
      </div>
    );
  }

  if (isLoading) return <LoadingBlock label="Loading records…" />;
  if (isError || records.length !== 2) {
    return <ErrorBlock error="Could not load one or both records." title="Could not load records" />;
  }

  const [recordA, recordB] = records as [MergeRecord, MergeRecord];
  const survivor = survivorId === recordA.id ? recordA : recordB;
  const loser = survivorId === recordA.id ? recordB : recordA;
  const fields = entityType === 'contact' ? contactFields : companyFields;

  const label = (r: MergeRecord) =>
    entityType === 'contact' ? contactName(r as ContactDto) : (r as CompanyDetailDto).name;

  return (
    <div className="max-w-4xl">
      <Link to={listPath} className="text-sm text-gray-500 hover:text-gray-700 mb-6 inline-flex items-center gap-1">
        ← Back to {noun}
      </Link>
      <h1 className="text-2xl font-bold text-gray-900 mt-2 mb-2">Merge {noun}</h1>
      <p className="text-sm text-gray-600 mb-6">
        Choose the record to keep, then choose which value to keep for each field that differs.
      </p>

      <div className="bg-white rounded-lg border border-gray-200 p-6 mb-6">
        <fieldset>
          <legend className="text-sm font-semibold text-gray-900 mb-3">Surviving record</legend>
          <div className="flex gap-6">
            {[recordA, recordB].map((r) => (
              <label key={r.id} className="flex items-center gap-2 text-sm text-gray-700">
                <input
                  type="radio"
                  name="survivor"
                  checked={survivorId === r.id}
                  onChange={() => setSurvivorId(r.id)}
                  className="h-4 w-4 text-primary-600 focus:ring-primary-500"
                />
                {label(r)} <span className="text-gray-400">({formatDate(r.createdAt)})</span>
              </label>
            ))}
          </div>
        </fieldset>
      </div>

      <div className="bg-white rounded-lg border border-gray-200 overflow-hidden mb-6">
        <table className="min-w-full divide-y divide-gray-200">
          <thead className="bg-gray-50">
            <tr>
              <th scope="col" className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                Field
              </th>
              <th scope="col" className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                {label(survivor)} (survivor)
              </th>
              <th scope="col" className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                {label(loser)}
              </th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-200">
            {fields.map((field) => {
              const survivorValue = field.format(survivor);
              const loserValue = field.format(loser);
              const differs = survivorValue !== loserValue;
              const choice = fieldChoices[field.key] ?? 'survivor';
              return (
                <tr key={field.key}>
                  <td className="px-4 py-3 text-sm font-medium text-gray-700">{field.label}</td>
                  <td className="px-4 py-3 text-sm text-gray-900">
                    <label className="flex items-center gap-2">
                      {differs && (
                        <input
                          type="radio"
                          name={`field-${field.key}`}
                          checked={choice === 'survivor'}
                          onChange={() => setFieldChoices((prev) => ({ ...prev, [field.key]: 'survivor' }))}
                          className="h-4 w-4 text-primary-600 focus:ring-primary-500"
                          aria-label={`Keep ${field.label} from ${label(survivor)}`}
                        />
                      )}
                      {survivorValue}
                    </label>
                  </td>
                  <td className="px-4 py-3 text-sm text-gray-900">
                    {differs ? (
                      <label className="flex items-center gap-2">
                        <input
                          type="radio"
                          name={`field-${field.key}`}
                          checked={choice === 'loser'}
                          onChange={() => setFieldChoices((prev) => ({ ...prev, [field.key]: 'loser' }))}
                          className="h-4 w-4 text-primary-600 focus:ring-primary-500"
                          aria-label={`Keep ${field.label} from ${label(loser)}`}
                        />
                        {loserValue}
                      </label>
                    ) : (
                      <span className="text-gray-400">Same value</span>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      <div className="flex justify-end">
        <button
          type="button"
          onClick={() => setConfirmOpen(true)}
          className="px-4 py-2 bg-red-600 text-white text-sm font-medium rounded-md hover:bg-red-700"
        >
          Merge records
        </button>
      </div>

      <ConfirmDialog
        isOpen={confirmOpen}
        title="Merge these records?"
        message={`${label(loser)} will be marked merged and deleted. Its tags${
          entityType === 'contact' ? '' : ' and contacts'
        } move to ${label(survivor)}.`}
        warning="A merge cannot be undone."
        confirmLabel="Merge"
        danger
        isLoading={mergeMutation.isPending}
        onConfirm={() => mergeMutation.mutate(loser.id)}
        onCancel={() => setConfirmOpen(false)}
      />
    </div>
  );
}
