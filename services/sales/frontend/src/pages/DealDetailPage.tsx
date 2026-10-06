import { useState } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getDeal, moveDeal, markWon, markLost, reopenDeal, deleteDeal } from '../api/deals';
import { listPipelines } from '../api/pipelines';
import { listLossReasons } from '../api/lossReasons';
import { Modal } from '../components/Modal';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { StatusBadge } from '../components/StatusBadge';
import { TagChips } from '../components/TagInput';
import { LoadingBlock, ErrorBlock } from '../components/StateBlocks';
import { useToast } from '../contexts/ToastContext';
import { formatMoney, formatDate, formatDateTime, orDash, getApiErrorMessage } from '../lib/utils';

export function DealDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { showToast } = useToast();

  const [showMoveModal, setShowMoveModal] = useState(false);
  const [showWonModal, setShowWonModal] = useState(false);
  const [showLostModal, setShowLostModal] = useState(false);
  const [showReopenModal, setShowReopenModal] = useState(false);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);

  const [moveStageId, setMoveStageId] = useState('');
  const [wonCloseDate, setWonCloseDate] = useState('');
  const [lostReasonId, setLostReasonId] = useState('');
  const [lostNotes, setLostNotes] = useState('');
  const [lostCloseDate, setLostCloseDate] = useState('');
  const [reopenStageId, setReopenStageId] = useState('');

  const { data: deal, isLoading, error, refetch } = useQuery({
    queryKey: ['deal', id],
    queryFn: () => getDeal(id!),
    enabled: !!id,
  });

  const { data: pipelines = [] } = useQuery({ queryKey: ['pipelines'], queryFn: listPipelines, staleTime: 60_000 });
  const { data: lossReasons = [] } = useQuery({ queryKey: ['loss-reasons'], queryFn: listLossReasons, staleTime: 60_000 });

  const currentPipeline = pipelines.find((p) => p.id === deal?.pipelineId);
  const openStages = currentPipeline?.stages.filter((s) => s.isActive && s.stageType === 'open') ?? [];

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: ['deal', id] });
    queryClient.invalidateQueries({ queryKey: ['deals'] });
    queryClient.invalidateQueries({ queryKey: ['board'] });
  };

  const moveMutation = useMutation({
    mutationFn: () => moveDeal(id!, moveStageId),
    onSuccess: () => { invalidate(); setShowMoveModal(false); showToast('Deal moved', 'success'); },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const wonMutation = useMutation({
    mutationFn: () => markWon(id!, wonCloseDate || undefined),
    onSuccess: () => { invalidate(); setShowWonModal(false); showToast('Deal marked as won', 'success'); },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const lostMutation = useMutation({
    mutationFn: () => markLost(id!, lostReasonId, lostNotes || undefined, lostCloseDate || undefined),
    onSuccess: () => { invalidate(); setShowLostModal(false); showToast('Deal marked as lost', 'success'); },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const reopenMutation = useMutation({
    mutationFn: () => reopenDeal(id!, reopenStageId),
    onSuccess: () => { invalidate(); setShowReopenModal(false); showToast('Deal reopened', 'success'); },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const deleteMutation = useMutation({
    mutationFn: () => deleteDeal(id!),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['deals'] });
      showToast('Deal deleted', 'success');
      navigate('/deals');
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  if (isLoading) return <LoadingBlock label="Loading deal…" />;
  if (error) return <ErrorBlock error={error} title="Could not load deal" onRetry={() => refetch()} />;
  if (!deal) return null;

  return (
    <div className="max-w-4xl mx-auto">
      {/* Header */}
      <div className="mb-6">
        <nav className="text-sm text-gray-500 mb-2" aria-label="Breadcrumb">
          <Link to="/deals" className="hover:text-primary-600">Deals</Link>
          <span className="mx-2">/</span>
          <span className="text-gray-900">{deal.name}</span>
        </nav>
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div>
            <div className="flex items-center gap-3 mb-1">
              <h1 className="text-2xl font-bold text-gray-900">{deal.name}</h1>
              <StatusBadge status={deal.status} />
              {deal.isStale && <span className="inline-flex px-2 py-0.5 text-xs font-medium bg-yellow-100 text-yellow-800 rounded-full">Stale</span>}
              {deal.isOverdue && <span className="inline-flex px-2 py-0.5 text-xs font-medium bg-red-100 text-red-800 rounded-full">Overdue</span>}
            </div>
            <p className="text-sm text-gray-500">{deal.pipelineName ?? '—'} → {deal.stageName ?? '—'}</p>
          </div>
          <div className="flex flex-wrap gap-2">
            {deal.status === 'open' && (
              <>
                <button type="button" onClick={() => { setMoveStageId(''); setShowMoveModal(true); }} className="px-3 py-1.5 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50">Move stage</button>
                <button type="button" onClick={() => { setWonCloseDate(''); setShowWonModal(true); }} className="px-3 py-1.5 text-sm font-medium text-white bg-green-600 rounded-md hover:bg-green-700">Mark won</button>
                <button type="button" onClick={() => { setLostReasonId(''); setLostNotes(''); setLostCloseDate(''); setShowLostModal(true); }} className="px-3 py-1.5 text-sm font-medium text-white bg-red-600 rounded-md hover:bg-red-700">Mark lost</button>
              </>
            )}
            {(deal.status === 'won' || deal.status === 'lost') && (
              <button type="button" onClick={() => { setReopenStageId(''); setShowReopenModal(true); }} className="px-3 py-1.5 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50">Reopen</button>
            )}
            <Link to={`/deals/${deal.id}/edit`} className="px-3 py-1.5 text-sm font-medium text-primary-700 bg-white border border-primary-300 rounded-md hover:bg-primary-50">Edit</Link>
            <button type="button" onClick={() => setShowDeleteConfirm(true)} className="px-3 py-1.5 text-sm font-medium text-red-700 bg-white border border-red-300 rounded-md hover:bg-red-50">Delete</button>
          </div>
        </div>
      </div>

      {/* Detail Grid */}
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-6 mb-8">
        <div className="bg-white rounded-lg border border-gray-200 p-5">
          <h2 className="text-sm font-semibold text-gray-900 mb-4 uppercase tracking-wider">Deal Details</h2>
          <dl className="space-y-3">
            <DetailRow label="Amount" value={`${formatMoney(deal.amount, deal.currency)} (${deal.currency})`} />
            <DetailRow label="Pipeline" value={orDash(deal.pipelineName)} />
            <DetailRow label="Stage" value={orDash(deal.stageName)} />
            <DetailRow label="Status" value={<StatusBadge status={deal.status} />} />
            {deal.probability !== null && deal.probability !== undefined && (
              <DetailRow label="Probability" value={`${deal.probability}%`} />
            )}
            <DetailRow label="Expected close" value={deal.expectedCloseDate ? formatDate(deal.expectedCloseDate) : '—'} />
            {deal.closedAt && <DetailRow label="Closed at" value={formatDateTime(deal.closedAt)} />}
          </dl>
        </div>

        <div className="bg-white rounded-lg border border-gray-200 p-5">
          <h2 className="text-sm font-semibold text-gray-900 mb-4 uppercase tracking-wider">Relationships</h2>
          <dl className="space-y-3">
            <DetailRow label="Owner" value={orDash(deal.ownerName)} />
            <DetailRow label="Company" value={orDash(deal.companyName)} />
            <DetailRow label="Primary contact" value={orDash(deal.primaryContactName)} />
            {deal.lossReasonName && <DetailRow label="Loss reason" value={deal.lossReasonName} />}
            {deal.lossNotes && <DetailRow label="Loss notes" value={deal.lossNotes} />}
            {deal.sourceLeadId && <DetailRow label="Source lead" value={deal.sourceLeadId} />}
          </dl>
        </div>

        <div className="bg-white rounded-lg border border-gray-200 p-5">
          <h2 className="text-sm font-semibold text-gray-900 mb-4 uppercase tracking-wider">Tags</h2>
          <TagChips tags={deal.tags} />
        </div>

        <div className="bg-white rounded-lg border border-gray-200 p-5">
          <h2 className="text-sm font-semibold text-gray-900 mb-4 uppercase tracking-wider">Timestamps</h2>
          <dl className="space-y-3">
            <DetailRow label="Created" value={formatDateTime(deal.createdAt)} />
            <DetailRow label="Updated" value={formatDateTime(deal.updatedAt)} />
            <DetailRow label="Stage entered" value={formatDateTime(deal.stageEnteredAt)} />
            {deal.lastActivityAt && <DetailRow label="Last activity" value={formatDateTime(deal.lastActivityAt)} />}
          </dl>
        </div>
      </div>

      {/* Contacts */}
      {deal.contacts && deal.contacts.length > 0 && (
        <div className="bg-white rounded-lg border border-gray-200 p-5 mb-6">
          <h2 className="text-sm font-semibold text-gray-900 mb-4 uppercase tracking-wider">Contacts</h2>
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-gray-200">
              <thead>
                <tr>
                  <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Name</th>
                  <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Role</th>
                  <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Primary</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {deal.contacts.map((c) => (
                  <tr key={c.contactId}>
                    <td className="px-4 py-2 text-sm text-gray-800">{orDash(c.contactName)}</td>
                    <td className="px-4 py-2 text-sm text-gray-600">{orDash(c.role)}</td>
                    <td className="px-4 py-2 text-sm">{c.isPrimary ? <span className="text-green-600 font-medium">Yes</span> : '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* Stage History */}
      {deal.history && deal.history.length > 0 && (
        <div className="bg-white rounded-lg border border-gray-200 p-5 mb-6">
          <h2 className="text-sm font-semibold text-gray-900 mb-4 uppercase tracking-wider">Stage History</h2>
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-gray-200">
              <thead>
                <tr>
                  <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">From</th>
                  <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">To</th>
                  <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Amount</th>
                  <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Date</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {deal.history.map((h) => (
                  <tr key={h.id}>
                    <td className="px-4 py-2 text-sm text-gray-600">{h.fromStageName ?? '—'}</td>
                    <td className="px-4 py-2 text-sm font-medium text-gray-800">{h.toStageName}</td>
                    <td className="px-4 py-2 text-sm text-gray-600">{h.amountAtChange != null ? formatMoney(h.amountAtChange) : '—'}</td>
                    <td className="px-4 py-2 text-sm text-gray-500">{formatDateTime(h.changedAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* Move Stage Modal */}
      <Modal isOpen={showMoveModal} title="Move to stage" onClose={() => setShowMoveModal(false)}>
        <div className="space-y-4">
          <div>
            <label htmlFor="move-stage" className="block text-sm font-medium text-gray-700 mb-1">New stage</label>
            <select id="move-stage" value={moveStageId} onChange={(e) => setMoveStageId(e.target.value)}
              className="block w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500">
              <option value="">Select a stage…</option>
              {openStages.filter((s) => s.id !== deal.stageId).map((s) => (
                <option key={s.id} value={s.id}>{s.name}</option>
              ))}
            </select>
          </div>
          <div className="flex justify-end gap-3">
            <button type="button" onClick={() => setShowMoveModal(false)} className="px-4 py-2 text-sm text-gray-700 border border-gray-300 rounded-md hover:bg-gray-50">Cancel</button>
            <button type="button" onClick={() => moveMutation.mutate()} disabled={!moveStageId || moveMutation.isPending}
              className="px-4 py-2 text-sm font-medium text-white bg-primary-600 rounded-md hover:bg-primary-700 disabled:opacity-50 flex items-center gap-2">
              {moveMutation.isPending && <span className="animate-spin h-3 w-3 border-2 border-white/30 border-t-white rounded-full" />}
              Move
            </button>
          </div>
        </div>
      </Modal>

      {/* Mark Won Modal */}
      <Modal isOpen={showWonModal} title="Mark as won" onClose={() => setShowWonModal(false)}>
        <div className="space-y-4">
          <div>
            <label htmlFor="won-close-date" className="block text-sm font-medium text-gray-700 mb-1">Close date (optional)</label>
            <input id="won-close-date" type="date" value={wonCloseDate} onChange={(e) => setWonCloseDate(e.target.value)}
              className="block w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500" />
          </div>
          <div className="flex justify-end gap-3">
            <button type="button" onClick={() => setShowWonModal(false)} className="px-4 py-2 text-sm text-gray-700 border border-gray-300 rounded-md hover:bg-gray-50">Cancel</button>
            <button type="button" onClick={() => wonMutation.mutate()} disabled={wonMutation.isPending}
              className="px-4 py-2 text-sm font-medium text-white bg-green-600 rounded-md hover:bg-green-700 disabled:opacity-50 flex items-center gap-2">
              {wonMutation.isPending && <span className="animate-spin h-3 w-3 border-2 border-white/30 border-t-white rounded-full" />}
              Mark won
            </button>
          </div>
        </div>
      </Modal>

      {/* Mark Lost Modal */}
      <Modal isOpen={showLostModal} title="Mark as lost" onClose={() => setShowLostModal(false)}>
        <div className="space-y-4">
          <div>
            <label htmlFor="lost-reason" className="block text-sm font-medium text-gray-700 mb-1">Loss reason <span className="text-red-600">*</span></label>
            <select id="lost-reason" value={lostReasonId} onChange={(e) => setLostReasonId(e.target.value)}
              className="block w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500">
              <option value="">Select a reason…</option>
              {lossReasons.filter((r) => r.isActive).map((r) => (
                <option key={r.id} value={r.id}>{r.name}</option>
              ))}
            </select>
          </div>
          <div>
            <label htmlFor="lost-notes" className="block text-sm font-medium text-gray-700 mb-1">Notes (optional)</label>
            <textarea id="lost-notes" value={lostNotes} onChange={(e) => setLostNotes(e.target.value)} rows={3}
              className="block w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500" />
          </div>
          <div>
            <label htmlFor="lost-close-date" className="block text-sm font-medium text-gray-700 mb-1">Close date (optional)</label>
            <input id="lost-close-date" type="date" value={lostCloseDate} onChange={(e) => setLostCloseDate(e.target.value)}
              className="block w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500" />
          </div>
          <div className="flex justify-end gap-3">
            <button type="button" onClick={() => setShowLostModal(false)} className="px-4 py-2 text-sm text-gray-700 border border-gray-300 rounded-md hover:bg-gray-50">Cancel</button>
            <button type="button" onClick={() => lostMutation.mutate()} disabled={!lostReasonId || lostMutation.isPending}
              className="px-4 py-2 text-sm font-medium text-white bg-red-600 rounded-md hover:bg-red-700 disabled:opacity-50 flex items-center gap-2">
              {lostMutation.isPending && <span className="animate-spin h-3 w-3 border-2 border-white/30 border-t-white rounded-full" />}
              Mark lost
            </button>
          </div>
        </div>
      </Modal>

      {/* Reopen Modal */}
      <Modal isOpen={showReopenModal} title="Reopen deal" onClose={() => setShowReopenModal(false)}>
        <div className="space-y-4">
          <div>
            <label htmlFor="reopen-stage" className="block text-sm font-medium text-gray-700 mb-1">Move to stage <span className="text-red-600">*</span></label>
            <select id="reopen-stage" value={reopenStageId} onChange={(e) => setReopenStageId(e.target.value)}
              className="block w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500">
              <option value="">Select a stage…</option>
              {openStages.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
            </select>
          </div>
          <div className="flex justify-end gap-3">
            <button type="button" onClick={() => setShowReopenModal(false)} className="px-4 py-2 text-sm text-gray-700 border border-gray-300 rounded-md hover:bg-gray-50">Cancel</button>
            <button type="button" onClick={() => reopenMutation.mutate()} disabled={!reopenStageId || reopenMutation.isPending}
              className="px-4 py-2 text-sm font-medium text-white bg-primary-600 rounded-md hover:bg-primary-700 disabled:opacity-50 flex items-center gap-2">
              {reopenMutation.isPending && <span className="animate-spin h-3 w-3 border-2 border-white/30 border-t-white rounded-full" />}
              Reopen
            </button>
          </div>
        </div>
      </Modal>

      <ConfirmDialog
        isOpen={showDeleteConfirm}
        title="Delete deal?"
        message={`"${deal.name}" will be moved to the recycle bin.`}
        warning="An admin can restore it within 30 days."
        confirmLabel="Delete"
        danger
        isLoading={deleteMutation.isPending}
        onConfirm={() => deleteMutation.mutate()}
        onCancel={() => setShowDeleteConfirm(false)}
      />
    </div>
  );
}

function DetailRow({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="flex items-start gap-2">
      <dt className="text-sm text-gray-500 min-w-[130px]">{label}</dt>
      <dd className="text-sm text-gray-900">{value}</dd>
    </div>
  );
}
