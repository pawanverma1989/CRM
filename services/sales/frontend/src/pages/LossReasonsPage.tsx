import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { listLossReasons, createLossReason, updateLossReason } from '../api/lossReasons';
import { Modal } from '../components/Modal';
import { FormField } from '../components/FormField';
import { LoadingBlock, ErrorBlock, EmptyBlock } from '../components/StateBlocks';
import { useToast } from '../contexts/ToastContext';
import { getApiErrorMessage } from '../lib/utils';
import type { LossReasonDto } from '../types';

export function LossReasonsPage() {
  const queryClient = useQueryClient();
  const { showToast } = useToast();

  const [showCreate, setShowCreate] = useState(false);
  const [editing, setEditing] = useState<LossReasonDto | null>(null);
  const [formName, setFormName] = useState('');
  const [formActive, setFormActive] = useState(true);

  const { data: reasons = [], isLoading, error, refetch } = useQuery({
    queryKey: ['loss-reasons'],
    queryFn: listLossReasons,
  });

  const createMutation = useMutation({
    mutationFn: () => createLossReason({ name: formName }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['loss-reasons'] });
      setShowCreate(false);
      showToast('Loss reason created', 'success');
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const updateMutation = useMutation({
    mutationFn: () => updateLossReason(editing!.id, { name: formName, isActive: formActive }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['loss-reasons'] });
      setEditing(null);
      showToast('Loss reason updated', 'success');
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const openCreate = () => {
    setFormName('');
    setFormActive(true);
    setShowCreate(true);
  };

  const openEdit = (r: LossReasonDto) => {
    setEditing(r);
    setFormName(r.name);
    setFormActive(r.isActive);
  };

  if (isLoading) return <LoadingBlock label="Loading loss reasons…" />;
  if (error) return <ErrorBlock error={error} title="Could not load loss reasons" onRetry={() => refetch()} />;

  return (
    <div>
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Loss Reasons</h1>
          <p className="text-sm text-gray-500 mt-0.5">Reasons presented when marking a deal as lost.</p>
        </div>
        <button
          type="button"
          onClick={openCreate}
          className="px-4 py-2 text-sm font-medium text-white bg-primary-600 rounded-md hover:bg-primary-700"
        >
          New reason
        </button>
      </div>

      {reasons.length === 0 ? (
        <EmptyBlock title="No loss reasons" message="Create loss reasons to track why deals are lost." />
      ) : (
        <div className="bg-white rounded-lg border border-gray-200 overflow-hidden">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Name</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Status</th>
                <th className="px-6 py-3" />
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-200">
              {reasons.map((r) => (
                <tr key={r.id} className="hover:bg-gray-50">
                  <td className="px-6 py-3 text-sm font-medium text-gray-900">{r.name}</td>
                  <td className="px-6 py-3">
                    <span className={`inline-flex px-2 py-0.5 text-xs font-medium rounded-full ${r.isActive ? 'bg-green-100 text-green-800' : 'bg-gray-100 text-gray-500'}`}>
                      {r.isActive ? 'Active' : 'Inactive'}
                    </span>
                  </td>
                  <td className="px-6 py-3 text-right">
                    <button
                      type="button"
                      onClick={() => openEdit(r)}
                      className="text-sm text-primary-600 hover:text-primary-800"
                    >
                      Edit
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <Modal isOpen={showCreate} title="New loss reason" onClose={() => setShowCreate(false)}>
        <div className="space-y-4">
          <FormField
            label="Name"
            required
            value={formName}
            onChange={(e) => setFormName(e.target.value)}
            placeholder="e.g. Price too high"
          />
          <div className="flex justify-end gap-3">
            <button type="button" onClick={() => setShowCreate(false)} className="px-4 py-2 text-sm font-medium text-gray-700 border border-gray-300 rounded-md hover:bg-gray-50">Cancel</button>
            <button
              type="button"
              onClick={() => createMutation.mutate()}
              disabled={!formName || createMutation.isPending}
              className="px-4 py-2 text-sm font-medium text-white bg-primary-600 rounded-md hover:bg-primary-700 disabled:opacity-50 flex items-center gap-2"
            >
              {createMutation.isPending && <span className="animate-spin h-3 w-3 border-2 border-white/30 border-t-white rounded-full" />}
              Create
            </button>
          </div>
        </div>
      </Modal>

      <Modal isOpen={!!editing} title={`Edit: ${editing?.name ?? ''}`} onClose={() => setEditing(null)}>
        <div className="space-y-4">
          <FormField
            label="Name"
            required
            value={formName}
            onChange={(e) => setFormName(e.target.value)}
          />
          <label className="flex items-center gap-2 text-sm text-gray-700 cursor-pointer">
            <input
              type="checkbox"
              checked={formActive}
              onChange={(e) => setFormActive(e.target.checked)}
              className="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
            />
            Active
          </label>
          <div className="flex justify-end gap-3">
            <button type="button" onClick={() => setEditing(null)} className="px-4 py-2 text-sm font-medium text-gray-700 border border-gray-300 rounded-md hover:bg-gray-50">Cancel</button>
            <button
              type="button"
              onClick={() => updateMutation.mutate()}
              disabled={!formName || updateMutation.isPending}
              className="px-4 py-2 text-sm font-medium text-white bg-primary-600 rounded-md hover:bg-primary-700 disabled:opacity-50 flex items-center gap-2"
            >
              {updateMutation.isPending && <span className="animate-spin h-3 w-3 border-2 border-white/30 border-t-white rounded-full" />}
              Save
            </button>
          </div>
        </div>
      </Modal>
    </div>
  );
}
