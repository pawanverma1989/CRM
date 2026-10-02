import { useEffect, useRef, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { getLead, updateLead, deleteLead } from '../api/leads';
import { getDisqualifyReasons } from '../api/disqualifyReasons';
import { getOwners } from '../api/owners';
import { getCustomFields } from '../api/customFields';
import { startConversion, getConversion } from '../api/conversions';
import { usePermissions } from '../hooks/usePermissions';
import { useToast } from '../contexts/ToastContext';
import { TagInput, TagChips } from '../components/TagInput';
import { StatusBadge } from '../components/StatusBadge';
import { Modal } from '../components/Modal';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { LoadingBlock, ErrorBlock } from '../components/StateBlocks';
import { formatCustomFieldValue } from '../lib/customFields';
import { leadName, formatDateTime, getApiErrorMessage, isVersionConflict, orDash } from '../lib/utils';
import type { ConversionDto } from '../types';

export function LeadDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { showToast } = useToast();
  const queryClient = useQueryClient();
  const { canAssign, canConvert } = usePermissions();

  const [deleteOpen, setDeleteOpen] = useState(false);
  const [disqualifyOpen, setDisqualifyOpen] = useState(false);
  const [convertOpen, setConvertOpen] = useState(false);
  const [versionConflict, setVersionConflict] = useState(false);

  const [disqualifyReasonId, setDisqualifyReasonId] = useState('');
  const [disqualifyNote, setDisqualifyNote] = useState('');

  const [convertCompanyName, setConvertCompanyName] = useState('');
  const [convertDealName, setConvertDealName] = useState('');
  const [createCompany, setCreateCompany] = useState(true);
  const [createDeal, setCreateDeal] = useState(false);

  const [conversion, setConversion] = useState<ConversionDto | null>(null);
  const pollRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const {
    data: lead,
    isLoading,
    isError,
    error,
    refetch,
  } = useQuery({
    queryKey: ['lead', id],
    queryFn: () => getLead(id!),
    enabled: !!id,
  });

  const { data: owners = [] } = useQuery({ queryKey: ['owners'], queryFn: getOwners });
  const { data: disqualifyReasons = [] } = useQuery({
    queryKey: ['disqualify-reasons'],
    queryFn: getDisqualifyReasons,
  });
  const { data: customFieldDefs = [] } = useQuery({
    queryKey: ['custom-fields'],
    queryFn: () => getCustomFields(),
  });

  // Poll conversion status
  useEffect(() => {
    if (!conversion) return;
    if (conversion.status === 'completed' || conversion.status === 'failed') return;

    pollRef.current = setInterval(async () => {
      try {
        const updated = await getConversion(conversion.id);
        setConversion(updated);
        if (updated.status === 'completed') {
          clearInterval(pollRef.current!);
          queryClient.invalidateQueries({ queryKey: ['lead', id] });
          showToast('Lead converted successfully', 'success');
        } else if (updated.status === 'failed') {
          clearInterval(pollRef.current!);
          showToast(`Conversion failed: ${updated.lastError ?? 'unknown error'}`, 'error');
        }
      } catch {
        clearInterval(pollRef.current!);
      }
    }, 2000);

    return () => {
      if (pollRef.current) clearInterval(pollRef.current);
    };
  }, [conversion, id, queryClient, showToast]);

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: ['lead', id] });
    queryClient.invalidateQueries({ queryKey: ['leads'] });
  };

  const statusMutation = useMutation({
    mutationFn: (newStatus: string) =>
      updateLead(id!, { status: newStatus, version: lead!.version }),
    onSuccess: () => {
      invalidate();
      showToast('Status updated', 'success');
    },
    onError: (err) => {
      if (isVersionConflict(err)) setVersionConflict(true);
      else showToast(getApiErrorMessage(err), 'error');
    },
  });

  const disqualifyMutation = useMutation({
    mutationFn: () =>
      updateLead(id!, {
        status: 'disqualified',
        disqualifyReasonId: disqualifyReasonId || undefined,
        disqualifyReason: disqualifyNote.trim() || undefined,
        version: lead!.version,
      }),
    onSuccess: () => {
      invalidate();
      showToast('Lead disqualified', 'success');
      setDisqualifyOpen(false);
      setDisqualifyReasonId('');
      setDisqualifyNote('');
    },
    onError: (err) => {
      if (isVersionConflict(err)) setVersionConflict(true);
      else showToast(getApiErrorMessage(err), 'error');
    },
  });

  const tagsMutation = useMutation({
    mutationFn: (tags: string[]) =>
      updateLead(id!, { tags, version: lead!.version }),
    onSuccess: () => {
      invalidate();
      showToast('Tags updated', 'success');
    },
    onError: (err) => {
      if (isVersionConflict(err)) setVersionConflict(true);
      else showToast(getApiErrorMessage(err), 'error');
    },
  });

  const deleteMutation = useMutation({
    mutationFn: () => deleteLead(id!),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['leads'] });
      showToast('Lead moved to the recycle bin', 'success');
      navigate('/');
    },
    onError: (err) => {
      showToast(getApiErrorMessage(err), 'error');
      setDeleteOpen(false);
    },
  });

  const convertMutation = useMutation({
    mutationFn: () =>
      startConversion(id!, {
        companyName: convertCompanyName.trim() || undefined,
        dealName: convertDealName.trim() || undefined,
        createCompany,
        createDeal,
      }),
    onSuccess: (conv) => {
      setConversion(conv);
      setConvertOpen(false);
      showToast('Conversion started', 'info');
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  if (isLoading) return <LoadingBlock label="Loading lead…" />;
  if (isError) return <ErrorBlock error={error} title="Could not load this lead" onRetry={refetch} />;
  if (!lead) return <ErrorBlock error="Lead not found" title="Not found" />;

  const isConverted = lead.status === 'converted';
  const isDisqualified = lead.status === 'disqualified';
  const canEdit = !isConverted;
  const ownerName = owners.find((o) => o.id === lead.ownerId)?.displayName;

  return (
    <div className="max-w-4xl">
      <Link to="/" className="text-sm text-gray-500 hover:text-gray-700 mb-6 inline-flex items-center gap-1">
        ← Back to leads
      </Link>

      {versionConflict && (
        <div className="mt-4 mb-2 rounded-lg border border-yellow-200 bg-yellow-50 px-4 py-3 flex items-center justify-between gap-4">
          <p className="text-sm text-yellow-800">
            This lead was updated by someone else. Reload to see the latest version.
          </p>
          <button
            type="button"
            onClick={() => { setVersionConflict(false); refetch(); }}
            className="px-3 py-1.5 text-sm font-medium text-yellow-900 border border-yellow-400 rounded-md hover:bg-yellow-100 shrink-0"
          >
            Reload
          </button>
        </div>
      )}

      {conversion && (conversion.status === 'pending' || conversion.status === 'in_progress') && (
        <div className="mt-4 mb-2 rounded-lg border border-blue-200 bg-blue-50 px-4 py-3 flex items-center gap-3">
          <span className="animate-spin h-4 w-4 border-2 border-blue-300 border-t-blue-700 rounded-full" />
          <p className="text-sm text-blue-800">Converting lead… this may take a moment.</p>
        </div>
      )}

      {conversion?.status === 'failed' && (
        <div className="mt-4 mb-2 rounded-lg border border-red-200 bg-red-50 px-4 py-3">
          <p className="text-sm text-red-800">Conversion failed: {conversion.lastError ?? 'unknown error'}</p>
        </div>
      )}

      <div className="flex items-start justify-between mt-2 mb-6">
        <div className="flex items-center gap-3 flex-wrap">
          <h1 className="text-2xl font-bold text-gray-900">{leadName(lead)}</h1>
          <StatusBadge status={lead.status} />
        </div>
        <div className="flex gap-2 flex-wrap justify-end">
          {canEdit && (
            <>
              <Link
                to={`/${lead.id}/edit`}
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
            </>
          )}
        </div>
      </div>

      {/* Status management */}
      {canEdit && !isConverted && (
        <div className="bg-white rounded-lg border border-gray-200 p-4 mb-6">
          <h2 className="text-sm font-semibold text-gray-900 mb-3">Update status</h2>
          <div className="flex flex-wrap gap-2">
            {lead.status !== 'new' && lead.status !== 'disqualified' && (
              <button
                type="button"
                onClick={() => statusMutation.mutate('new')}
                disabled={statusMutation.isPending}
                className="px-3 py-1.5 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50 disabled:opacity-50"
              >
                Mark as New
              </button>
            )}
            {lead.status !== 'contacted' && !isDisqualified && (
              <button
                type="button"
                onClick={() => statusMutation.mutate('contacted')}
                disabled={statusMutation.isPending}
                className="px-3 py-1.5 text-sm font-medium text-yellow-800 bg-yellow-50 border border-yellow-300 rounded-md hover:bg-yellow-100 disabled:opacity-50"
              >
                Mark as Contacted
              </button>
            )}
            {lead.status !== 'qualified' && !isDisqualified && (
              <button
                type="button"
                onClick={() => statusMutation.mutate('qualified')}
                disabled={statusMutation.isPending}
                className="px-3 py-1.5 text-sm font-medium text-green-800 bg-green-50 border border-green-300 rounded-md hover:bg-green-100 disabled:opacity-50"
              >
                Mark as Qualified
              </button>
            )}
            {!isDisqualified && (
              <button
                type="button"
                onClick={() => setDisqualifyOpen(true)}
                disabled={statusMutation.isPending}
                className="px-3 py-1.5 text-sm font-medium text-red-700 bg-red-50 border border-red-300 rounded-md hover:bg-red-100 disabled:opacity-50"
              >
                Disqualify
              </button>
            )}
            {isDisqualified && (
              <button
                type="button"
                onClick={() => statusMutation.mutate('new')}
                disabled={statusMutation.isPending}
                className="px-3 py-1.5 text-sm font-medium text-primary-700 bg-primary-50 border border-primary-300 rounded-md hover:bg-primary-100 disabled:opacity-50"
              >
                Reopen
              </button>
            )}
            {canConvert && !isDisqualified && (
              <button
                type="button"
                onClick={() => setConvertOpen(true)}
                disabled={statusMutation.isPending || !!conversion}
                className="px-3 py-1.5 text-sm font-medium text-purple-800 bg-purple-50 border border-purple-300 rounded-md hover:bg-purple-100 disabled:opacity-50"
              >
                Convert
              </button>
            )}
          </div>
        </div>
      )}

      <div className="bg-white rounded-lg border border-gray-200 p-6 mb-6">
        <dl className="grid grid-cols-1 sm:grid-cols-2 gap-x-6 gap-y-4 text-sm">
          <div>
            <dt className="text-gray-500">Email</dt>
            <dd className="text-gray-900 font-medium">{orDash(lead.email)}</dd>
          </div>
          <div>
            <dt className="text-gray-500">Phone</dt>
            <dd className="text-gray-900 font-medium">{orDash(lead.phone)}</dd>
          </div>
          <div>
            <dt className="text-gray-500">Company</dt>
            <dd className="text-gray-900 font-medium">{orDash(lead.companyName)}</dd>
          </div>
          <div>
            <dt className="text-gray-500">Job title</dt>
            <dd className="text-gray-900 font-medium">{orDash(lead.jobTitle)}</dd>
          </div>
          <div>
            <dt className="text-gray-500">Lead source</dt>
            <dd className="text-gray-900 font-medium">{orDash(lead.leadSourceName)}</dd>
          </div>
          <div>
            <dt className="text-gray-500">Owner</dt>
            <dd className="text-gray-900 font-medium flex items-center gap-2">
              {orDash(ownerName)}
            </dd>
          </div>
          {lead.utmSource && (
            <div>
              <dt className="text-gray-500">UTM source</dt>
              <dd className="text-gray-900 font-medium">{lead.utmSource}</dd>
            </div>
          )}
          {lead.utmMedium && (
            <div>
              <dt className="text-gray-500">UTM medium</dt>
              <dd className="text-gray-900 font-medium">{lead.utmMedium}</dd>
            </div>
          )}
          {lead.utmCampaign && (
            <div>
              <dt className="text-gray-500">UTM campaign</dt>
              <dd className="text-gray-900 font-medium">{lead.utmCampaign}</dd>
            </div>
          )}
          {isDisqualified && (
            <div>
              <dt className="text-gray-500">Disqualify reason</dt>
              <dd className="text-gray-900 font-medium">{orDash(lead.disqualifyReason)}</dd>
            </div>
          )}
          {lead.notes && (
            <div className="sm:col-span-2">
              <dt className="text-gray-500">Notes</dt>
              <dd className="text-gray-900 whitespace-pre-line">{lead.notes}</dd>
            </div>
          )}
          <div>
            <dt className="text-gray-500">Created</dt>
            <dd className="text-gray-900">{formatDateTime(lead.createdAt)}</dd>
          </div>
          <div>
            <dt className="text-gray-500">Last updated</dt>
            <dd className="text-gray-900">{formatDateTime(lead.updatedAt)}</dd>
          </div>
          {isConverted && (
            <>
              <div>
                <dt className="text-gray-500">Converted at</dt>
                <dd className="text-gray-900">{lead.convertedAt ? formatDateTime(lead.convertedAt) : '—'}</dd>
              </div>
              {lead.convertedContactId && (
                <div>
                  <dt className="text-gray-500">Contact ID</dt>
                  <dd className="text-gray-900 font-mono text-xs">{lead.convertedContactId}</dd>
                </div>
              )}
              {lead.convertedCompanyId && (
                <div>
                  <dt className="text-gray-500">Company ID</dt>
                  <dd className="text-gray-900 font-mono text-xs">{lead.convertedCompanyId}</dd>
                </div>
              )}
              {lead.convertedDealId && (
                <div>
                  <dt className="text-gray-500">Deal ID</dt>
                  <dd className="text-gray-900 font-mono text-xs">{lead.convertedDealId}</dd>
                </div>
              )}
            </>
          )}
        </dl>
      </div>

      <div className="bg-white rounded-lg border border-gray-200 p-6 mb-6">
        <h2 className="text-sm font-semibold text-gray-900 mb-3">Tags</h2>
        {isConverted ? (
          <TagChips tags={lead.tags} />
        ) : (
          <TagInput value={lead.tags} onChange={(tags) => tagsMutation.mutate(tags)} />
        )}
      </div>

      {customFieldDefs.filter((d) => d.isActive).length > 0 && (
        <div className="bg-white rounded-lg border border-gray-200 p-6 mb-6">
          <h2 className="text-sm font-semibold text-gray-900 mb-3">Custom fields</h2>
          <dl className="grid grid-cols-1 sm:grid-cols-2 gap-x-6 gap-y-4 text-sm">
            {customFieldDefs.filter((d) => d.isActive).map((def) => (
              <div key={def.id}>
                <dt className="text-gray-500">{def.label}</dt>
                <dd className="text-gray-900 font-medium">
                  {formatCustomFieldValue(def, lead.customFields[def.fieldKey] ?? null, owners)}
                </dd>
              </div>
            ))}
          </dl>
        </div>
      )}

      {/* Disqualify modal */}
      <Modal isOpen={disqualifyOpen} title="Disqualify lead" onClose={() => setDisqualifyOpen(false)}>
        <div className="space-y-4">
          <div>
            <label htmlFor="disqualify-reason" className="block text-sm font-medium text-gray-700 mb-1">Reason</label>
            <select
              id="disqualify-reason"
              value={disqualifyReasonId}
              onChange={(e) => setDisqualifyReasonId(e.target.value)}
              className="block w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500"
            >
              <option value="">— Select a reason —</option>
              {disqualifyReasons.filter((r) => r.isActive).map((r) => (
                <option key={r.id} value={r.id}>{r.name}</option>
              ))}
            </select>
          </div>
          <div>
            <label htmlFor="disqualify-note" className="block text-sm font-medium text-gray-700 mb-1">Note (optional)</label>
            <textarea
              id="disqualify-note"
              rows={3}
              value={disqualifyNote}
              onChange={(e) => setDisqualifyNote(e.target.value)}
              className="block w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500"
            />
          </div>
          <div className="flex justify-end gap-3">
            <button type="button" onClick={() => setDisqualifyOpen(false)} className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50">
              Cancel
            </button>
            <button
              type="button"
              onClick={() => disqualifyMutation.mutate()}
              disabled={disqualifyMutation.isPending}
              className="flex items-center gap-2 px-4 py-2 text-sm font-medium text-white bg-red-600 rounded-md hover:bg-red-700 disabled:opacity-50"
            >
              {disqualifyMutation.isPending && <span className="animate-spin h-3 w-3 border-2 border-white/30 border-t-white rounded-full" />}
              Disqualify
            </button>
          </div>
        </div>
      </Modal>

      {/* Convert modal */}
      <Modal isOpen={convertOpen} title="Convert lead" onClose={() => setConvertOpen(false)}>
        <div className="space-y-4">
          <p className="text-sm text-gray-600">Converting this lead will create a contact and optionally a company and deal in the Sales service.</p>
          <label className="flex items-center gap-2 text-sm text-gray-700">
            <input
              type="checkbox"
              checked={createCompany}
              onChange={(e) => setCreateCompany(e.target.checked)}
              className="h-4 w-4 rounded border-gray-300 text-primary-600 focus:ring-primary-500"
            />
            Create a company
          </label>
          {createCompany && (
            <div>
              <label htmlFor="convert-company" className="block text-sm font-medium text-gray-700 mb-1">Company name</label>
              <input
                id="convert-company"
                type="text"
                value={convertCompanyName}
                onChange={(e) => setConvertCompanyName(e.target.value)}
                placeholder={lead.companyName ?? ''}
                className="block w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500"
              />
            </div>
          )}
          <label className="flex items-center gap-2 text-sm text-gray-700">
            <input
              type="checkbox"
              checked={createDeal}
              onChange={(e) => setCreateDeal(e.target.checked)}
              className="h-4 w-4 rounded border-gray-300 text-primary-600 focus:ring-primary-500"
            />
            Create a deal
          </label>
          {createDeal && (
            <div>
              <label htmlFor="convert-deal" className="block text-sm font-medium text-gray-700 mb-1">Deal name</label>
              <input
                id="convert-deal"
                type="text"
                value={convertDealName}
                onChange={(e) => setConvertDealName(e.target.value)}
                className="block w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500"
              />
            </div>
          )}
          {canAssign && (
            <p className="text-xs text-gray-500">The contact and deal will be assigned to the same owner as this lead.</p>
          )}
          <div className="flex justify-end gap-3">
            <button type="button" onClick={() => setConvertOpen(false)} className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50">
              Cancel
            </button>
            <button
              type="button"
              onClick={() => convertMutation.mutate()}
              disabled={convertMutation.isPending}
              className="flex items-center gap-2 px-4 py-2 text-sm font-medium text-white bg-purple-600 rounded-md hover:bg-purple-700 disabled:opacity-50"
            >
              {convertMutation.isPending && <span className="animate-spin h-3 w-3 border-2 border-white/30 border-t-white rounded-full" />}
              Convert
            </button>
          </div>
        </div>
      </Modal>

      <ConfirmDialog
        isOpen={deleteOpen}
        title="Delete lead"
        message={`Move ${leadName(lead)} to the recycle bin? Admins can restore within 30 days.`}
        confirmLabel="Delete"
        danger
        isLoading={deleteMutation.isPending}
        onConfirm={() => deleteMutation.mutate()}
        onCancel={() => setDeleteOpen(false)}
      />
    </div>
  );
}
