import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { getContact, deleteContact, updateContact } from '../api/contacts';
import { getCustomFields } from '../api/customFields';
import { getOwners } from '../api/owners';
import { reassignRecords } from '../api/bulk';
import { usePermissions } from '../hooks/usePermissions';
import { useToast } from '../contexts/ToastContext';
import { TagInput } from '../components/TagInput';
import { ReassignModal } from '../components/ReassignModal';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { LoadingBlock, ErrorBlock, NotAvailablePanel } from '../components/StateBlocks';
import { VersionConflictBanner } from '../components/DuplicateWarning';
import { formatCustomFieldValue } from '../lib/customFields';
import { contactName, formatDateTime, getApiErrorMessage, isVersionConflict, orDash } from '../lib/utils';

export function ContactDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { showToast } = useToast();
  const queryClient = useQueryClient();
  const { canReassign } = usePermissions();

  const [reassignOpen, setReassignOpen] = useState(false);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [versionConflict, setVersionConflict] = useState(false);

  const {
    data: contact,
    isLoading,
    isError,
    error,
    refetch,
  } = useQuery({
    queryKey: ['contact', id],
    queryFn: () => getContact(id!),
    enabled: !!id,
  });

  const { data: owners = [] } = useQuery({ queryKey: ['owners'], queryFn: getOwners });
  const { data: customFieldDefs = [] } = useQuery({
    queryKey: ['custom-fields', 'contact'],
    queryFn: () => getCustomFields('contact'),
  });

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: ['contact', id] });
    queryClient.invalidateQueries({ queryKey: ['contacts'] });
  };

  const tagsMutation = useMutation({
    mutationFn: (tags: string[]) => updateContact(id!, { tags, version: contact!.version }),
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
      reassignRecords({ recordType: 'contact', recordIds: [id!], newOwnerId }),
    onSuccess: () => {
      invalidate();
      showToast('Owner updated', 'success');
      setReassignOpen(false);
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const deleteMutation = useMutation({
    mutationFn: () => deleteContact(id!),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['contacts'] });
      showToast('Contact moved to the recycle bin', 'success');
      navigate('/contacts');
    },
    onError: (err) => {
      showToast(getApiErrorMessage(err), 'error');
      setDeleteOpen(false);
    },
  });

  if (isLoading) return <LoadingBlock label="Loading contact…" />;
  if (isError) return <ErrorBlock error={error} title="Could not load this contact" onRetry={refetch} />;
  if (!contact) return <ErrorBlock error="Contact not found" title="Not found" />;

  return (
    <div className="max-w-4xl">
      <Link to="/contacts" className="text-sm text-gray-500 hover:text-gray-700 mb-6 inline-flex items-center gap-1">
        ← Back to contacts
      </Link>

      {versionConflict && (
        <div className="mt-4 mb-2">
          <VersionConflictBanner onReload={() => { setVersionConflict(false); refetch(); }} />
        </div>
      )}

      <div className="flex items-start justify-between mt-2 mb-6">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">{contactName(contact)}</h1>
          {contact.jobTitle && <p className="text-sm text-gray-500">{contact.jobTitle}</p>}
        </div>
        <div className="flex gap-2">
          <Link
            to={`/contacts/${contact.id}/edit`}
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
            <dt className="text-gray-500">Email</dt>
            <dd className="text-gray-900 font-medium">{orDash(contact.email)}</dd>
          </div>
          <div>
            <dt className="text-gray-500">Phone</dt>
            <dd className="text-gray-900 font-medium">{orDash(contact.phoneNormalized ?? contact.phone)}</dd>
          </div>
          <div>
            <dt className="text-gray-500">Mobile</dt>
            <dd className="text-gray-900 font-medium">{orDash(contact.mobile)}</dd>
          </div>
          <div>
            <dt className="text-gray-500">Source</dt>
            <dd className="text-gray-900 font-medium">{orDash(contact.sourceValue)}</dd>
          </div>
          <div>
            <dt className="text-gray-500">Company</dt>
            <dd className="text-gray-900 font-medium">
              {contact.companyId ? (
                <Link to={`/companies/${contact.companyId}`} className="text-primary-700 hover:underline">
                  {contact.companyName ?? 'View company'}
                </Link>
              ) : (
                '—'
              )}
            </dd>
          </div>
          <div>
            <dt className="text-gray-500">Owner</dt>
            <dd className="text-gray-900 font-medium flex items-center gap-2">
              {orDash(contact.ownerName)}
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
              {[contact.addressLine1, contact.addressLine2, contact.city, contact.state, contact.postalCode, contact.country]
                .filter(Boolean)
                .join(', ') || '—'}
            </dd>
          </div>
          <div>
            <dt className="text-gray-500">Created</dt>
            <dd className="text-gray-900">{formatDateTime(contact.createdAt)}</dd>
          </div>
          <div>
            <dt className="text-gray-500">Last updated</dt>
            <dd className="text-gray-900">{formatDateTime(contact.updatedAt)}</dd>
          </div>
        </dl>
      </div>

      <div className="bg-white rounded-lg border border-gray-200 p-6 mb-6">
        <h2 className="text-sm font-semibold text-gray-900 mb-3">Tags</h2>
        <TagInput value={contact.tags} onChange={(tags) => tagsMutation.mutate(tags)} />
      </div>

      {customFieldDefs.length > 0 && (
        <div className="bg-white rounded-lg border border-gray-200 p-6 mb-6">
          <h2 className="text-sm font-semibold text-gray-900 mb-3">Custom fields</h2>
          <dl className="grid grid-cols-1 sm:grid-cols-2 gap-x-6 gap-y-4 text-sm">
            {customFieldDefs.map((def) => (
              <div key={def.id}>
                <dt className="text-gray-500">{def.label}</dt>
                <dd className="text-gray-900 font-medium">
                  {formatCustomFieldValue(def, contact.customFields[def.fieldKey] ?? null, owners)}
                </dd>
              </div>
            ))}
          </dl>
        </div>
      )}

      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <NotAvailablePanel title="Deals" service="Sales" />
        <NotAvailablePanel title="Timeline" service="Activity" />
      </div>

      <ReassignModal
        isOpen={reassignOpen}
        onClose={() => setReassignOpen(false)}
        owners={owners}
        currentOwnerId={contact.ownerId}
        isLoading={reassignMutation.isPending}
        onConfirm={(ownerId) => reassignMutation.mutate(ownerId)}
      />

      <ConfirmDialog
        isOpen={deleteOpen}
        title="Delete contact"
        message={`Move ${contactName(contact)} to the recycle bin? Admins can restore within 30 days.`}
        confirmLabel="Delete"
        danger
        isLoading={deleteMutation.isPending}
        onConfirm={() => deleteMutation.mutate()}
        onCancel={() => setDeleteOpen(false)}
      />
    </div>
  );
}
